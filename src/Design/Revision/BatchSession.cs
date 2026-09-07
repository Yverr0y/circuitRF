using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What opening a batch produced.</summary>
/// <param name="Ok">Whether the batch is open. False means <b>nothing was modified</b>.</param>
/// <param name="AlreadyOpen">True when this open joined the batch that was already running
/// (R-rc5-6c) — no second entry was taken and none should have been.</param>
/// <param name="Point">The entry taken before anything changed, when one was.</param>
/// <param name="Refusal">Why not, in a sentence that names what to do instead (R-rc5-6i).</param>
public sealed record BatchOpenResult(
    bool          Ok,
    bool          AlreadyOpen,
    RestorePoint? Point,
    Diagnostic?   Refusal);

/// <summary>What closing a batch produced.</summary>
/// <param name="Modified">Workspace-relative paths the batch changed — what the window is told to
/// reload (R-rc5-7b). Computed by comparing states, so an agent that forgot to say cannot be wrong
/// about it.</param>
public sealed record BatchCloseResult(bool WasOpen, IReadOnlyList<string> Modified);

/// <summary>
/// <b>The AI-batch boundary</b> (<c>docs/design/revision-control.md</c> §5.3a, §5.3c;
/// R-rc5-6a … R-rc5-6f, R-rc5-7a, R-rc5-7b).
///
/// <para>rev 2 of the architecture named the most important checkpoint in the design and gave it no
/// mechanism. This is the mechanism, and <b>it cannot be deferred to the AI work</b>, because §12 Q5
/// says the floor ships before the thing it is a floor under.</para>
///
/// <para><b>The agent declares the batch; circuitRF does not infer it</b> (R-rc5-6a). A batch is
/// opened and closed explicitly and the entry is taken WHEN IT OPENS, before the first modification.
/// Inferring one from a run of write operations requires a timeout, and a timeout is §5.3's rejected
/// idle trigger under another name.</para>
///
/// <para><b>Batches do not nest</b> (R-rc5-6c): a second open inside an open batch is the same batch.
/// Two entries around one logical action is the unreadable log §5.3 rejects, and the designer's
/// question — what did this look like before the agent touched it — has exactly one answer. <b>A second
/// open on a DIFFERENT workspace is refused instead</b>, because it is not that action: replacing the
/// running batch would abandon it, and an abandoned batch never closes, so the window holding the first
/// workspace is never told which documents changed (R-rc5-7b) and its next save discards them.</para>
///
/// <para><b>An unclosed batch is not a failure and needs no repair</b> (R-rc5-6d). The entry was
/// taken before anything was modified, which is the whole of what §1.2 asks; if the agent dies
/// mid-batch the fallback is exactly where it should be and the next boundary closes it. <b>The safety
/// net must have no failure mode that depends on an agent behaving well</b> — that is the population
/// it exists to protect against.</para>
///
/// <para><b>Reverting a batch is going back to its entry</b> (R-rc5-6e), offered to the DESIGNER and
/// not to the agent. §1.2 asks for revertible in one action; it does not ask for a tool call an agent
/// can use to erase what it did — which is why there is no such method here.</para>
///
/// <para><b>Five refusals, resolved at the same moment and for the same reason</b> (R-rc5-6f,
/// R-rc5-7a) — off, held, nowhere to record, a window holding unsaved changes, and a batch already
/// running on another workspace. All are answered BEFORE anything is modified, because <b>a floor
/// announced after the fall is not a floor</b>, and each names what to do instead, because
/// §5.3b rule 9 tells an agent to stop and say so and a vague refusal is what makes one
/// improvise.</para>
/// </summary>
public sealed class BatchSession
{
    /// <summary>
    /// R-rc5-7a's question, asked over R-rc5-7c's channel: does a running window hold unsaved changes
    /// to this workspace? <b>Null means nobody is listening, which means no window to protect</b> —
    /// not "assume the worst", because refusing every batch on a machine with no window open would
    /// refuse exactly the headless case §1.2 is written for.
    /// </summary>
    public Func<string, bool>? WindowHasUnsavedChanges { get; set; }

    /// <summary>The per-user preference, read by whoever constructs this. Defaults to the documented
    /// default, which is what a headless run with no preferences file must see.</summary>
    public bool KeepHistoryPreference { get; set; } = RevisionArming.KeepHistoryDefault;

    /// <summary>The workspace the open batch belongs to, or null when none is open.</summary>
    public string? OpenWorkspace { get; private set; }

    /// <summary>The intent the open batch declared, for the surface to report (R-rc5-6g).</summary>
    public string? OpenIntent { get; private set; }

    /// <summary>The state the workspace was in when the batch opened — what the close compares
    /// against to say which documents changed.</summary>
    private string? _openTreeId;

    /// <summary>Whether a skipped batch has already been reported this session (R-rc5-11): once is
    /// unmissable, more is noise a designer learns to scroll past.</summary>
    public bool SkipAlreadyReported { get; private set; }

    /// <summary>
    /// Opens a batch, taking the entry first.
    /// </summary>
    /// <param name="workspaceRoot">The workspace folder, or null for one with no folder yet.</param>
    public BatchOpenResult Open(string? workspaceRoot, string? intent)
    {
        // R-rc5-6c, both halves.
        //
        // SAME workspace: the same batch, and it takes no second entry. Two entries around one logical
        // action is the unreadable log §5.3 rejects, and the designer's question — what did this look
        // like before the agent touched it — has exactly one answer.
        //
        // DIFFERENT workspace: REFUSED, and refused FIRST, before this workspace's own state is even
        // asked about. It is not the same batch and cannot be folded into one, and the alternative to
        // refusing is not untidiness — it is abandoning the open batch. An abandoned batch never
        // closes, so the window holding the first workspace is never told which documents changed
        // (R-rc5-7b) and never reloads them: it goes on showing the old content over an undo stack
        // describing edits its files no longer contain, and its next save discards everything that
        // batch did. §5.3b rule 9 already tells the agent what to do with a refusal, and the refusal
        // names the open batch so it can.
        if (OpenWorkspace is { } running)
        {
            if (SameWorkspace(workspaceRoot, running)) return new BatchOpenResult(true, true, null, null);
            return Refuse(RestorePointMessages.AnotherBatchIsOpen(
                RevisionSwitch.WorkspaceName(running), OpenIntent));
        }

        var setting = workspaceRoot is { Length: > 0 }
            ? WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(workspaceRoot))
            : null;

        var state = AgentContract.StateOf(workspaceRoot, KeepHistoryPreference, setting);

        if (state != RevisionAvailability.On)
            return Refuse(RefusalFor(state, workspaceRoot));

        string root = Path.GetFullPath(workspaceRoot!);

        // R-rc5-7a, the fourth member. Asked BEFORE anything is modified; a window with unsaved
        // changes to this workspace would discard everything the batch did on its next save, which is
        // the two-editors-one-file failure the architecture already calls worse than divergence.
        if (WindowHasUnsavedChanges?.Invoke(root) == true)
            return Refuse(RestorePointMessages.WindowHasUnsavedChanges());

        var armed = WorkspaceArming.Arm(root, CheckpointOrigin.BeforeBatch,
                                        KeepHistoryPreference, setting,
                                        circuitRfWroteAFileThisSession: true);

        if (!armed.Armed || armed.Git is null)
            return Refuse(armed.Refusal ?? RestorePointMessages.NoGitHere());

        var taken = WorkspaceCheckpoints.Take(armed.Git, CheckpointOrigin.BeforeBatch, intent,
                                              attended: false);

        // R-rc5-5a. An unchanged tree records nothing, and that is a SUCCESS: the state is already in
        // the history, which is all the batch needed. A failure is a different thing entirely, and
        // R-rc5-6f says the batch does not run.
        var failure = taken.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (failure is not null) return Refuse(failure);

        OpenWorkspace = root;
        OpenIntent    = intent;
        _openTreeId   = taken.TreeId;

        return new BatchOpenResult(true, false, taken.Point, null);
    }

    /// <summary>
    /// Closes the batch and reports what it changed.
    ///
    /// <para><b>The modified set is MEASURED, not declared</b> — the state now against the state the
    /// batch opened on. An agent that forgot to mention a file it wrote cannot leave that file's
    /// window showing the old content, which is the failure R-rc5-7b exists to prevent and the one an
    /// honour-system list would reintroduce.</para>
    /// </summary>
    public BatchCloseResult Close()
    {
        if (OpenWorkspace is not { } root) return new BatchCloseResult(false, []);

        List<string> modified = [];

        if (_openTreeId is { Length: > 0 } && GitCommand.For(root) is { } git)
        {
            git.Identity ??= RevisionIdentity.Resolve(git);

            // Compare the tree the batch opened on against what is on disk now. Through a private
            // index, so the designer's own staged state is untouched (R-rc5-1c).
            string index = GitCheckpoint.PrivateIndexPath(root) + "-batch";
            try
            {
                try { File.Delete(index); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

                var options = new GitRunOptions(IndexFile: index);
                var nested  = NestedRepositories.Find(root);

                List<string> add = ["add", "--all", "--", "."];
                add.AddRange(NestedRepositories.ExcludePathspecs(nested));

                if (git.Run(add, options).Ok)
                {
                    var diff = git.Run(["diff-index", "--cached", "--name-only", "-z", _openTreeId],
                                       options);
                    if (diff.Ok)
                        foreach (string path in diff.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                            if (path.Trim() is { Length: > 0 } p) modified.Add(p);
                }
            }
            finally
            {
                try { File.Delete(index); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        OpenWorkspace = null;
        OpenIntent    = null;
        _openTreeId   = null;

        return new BatchCloseResult(true, modified);
    }

    /// <summary>
    /// R-rc5-11. Whether this session has yet said that a batch went without an entry — and marks it
    /// said. Once per session, however many are skipped.
    /// </summary>
    public bool ShouldReportSkip()
    {
        if (SkipAlreadyReported) return false;
        SkipAlreadyReported = true;
        return true;
    }

    /// <summary>
    /// The refusal for a state that is not <see cref="RevisionAvailability.On"/>. <b>Each names what
    /// to do instead</b> (R-rc5-6i) — an agent given a vague refusal will improvise, which is what
    /// rule 9 forbids.
    /// </summary>
    private static Diagnostic RefusalFor(RevisionAvailability state, string? workspaceRoot) => state switch
    {
        RevisionAvailability.Off  => RestorePointMessages.RecordingOff(),
        RevisionAvailability.Held => RestorePointMessages.RecordingHeld(),
        _ => workspaceRoot is { Length: > 0 } && Directory.Exists(workspaceRoot)
                ? RestorePointMessages.NoGitHere()
                : RestorePointMessages.ScratchHasNowhereToRecord(),
    };

    private static BatchOpenResult Refuse(Diagnostic d) => new(false, false, null, d);

    /// <summary>
    /// Whether an asked-for workspace is the one a batch is already open on.
    ///
    /// <para>A workspace with no folder yet is never the same as one that has: <see cref="Open"/>
    /// refuses it in its own right, and answering "same batch" for it would hand back an
    /// <c>Ok</c> with no entry behind it.</para>
    ///
    /// <para><b>A path that cannot be resolved is not the same one</b>, rather than an exception out of
    /// the comparison: this runs before every batch, and a malformed argument must reach the refusal
    /// that names what is wrong with it instead of ending the call.</para>
    /// </summary>
    private static bool SameWorkspace(string? asked, string running)
    {
        if (asked is not { Length: > 0 }) return false;
        try { return string.Equals(Path.GetFullPath(asked), running, StringComparison.Ordinal); }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
