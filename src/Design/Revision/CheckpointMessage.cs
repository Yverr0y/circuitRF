using System.Text;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>A restore point's own metadata, carried in the message of the object that holds it</b>
/// (<c>docs/design/revision-control.md</c> §5.2b, §5.5; R-rc5-1e, R-rc5-9a).
///
/// <para><b>Why the message and not a side file.</b> The metadata has to survive everything the object
/// survives — an archive that copies the directory, a machine that crashed between two writes, a
/// reference somebody deleted by hand. A side file is a second thing to keep in step, and the one
/// state it cannot represent is the one that matters: an entry whose metadata went missing is
/// indistinguishable from an entry that never carried any.</para>
///
/// <para><b>Trailers, in git's own trailer shape</b> — <c>Key: value</c> lines in a final block — so
/// the escape hatch of §4.1 can read them with <c>git log</c> and nothing here needs a parser
/// anybody has to learn. Unrecognised keys are ignored on the way in, so a later stage may add one
/// without this reader changing.</para>
///
/// <para><b>No git vocabulary reaches this text</b> (R-rc0-6, R-rc5-2). What a designer might read in
/// an escape hatch is the same wording the restore-point list shows them.</para>
/// </summary>
public static class CheckpointMessage
{
    /// <summary>The trailer key prefix. Namespaced so a designer's own trailers cannot collide.</summary>
    public const string KeyPrefix = "CircuitRF-";

    public const string SequenceKey = KeyPrefix + "Sequence";
    public const string OriginKey   = KeyPrefix + "Origin";
    public const string IntentKey   = KeyPrefix + "Intent";
    public const string KeptKey     = KeyPrefix + "Kept";

    /// <summary>Repeatable: one line per workspace-relative path left out under R-rc5-15a.</summary>
    public const string LeftOutKey  = KeyPrefix + "Left-Out";

    /// <summary>
    /// What the list shows when the user asked for a save-point and typed nothing (R-rc5-5a).
    /// <b>Never a bare time</b>: a time is what every entry already has, so an entry labelled only
    /// with one says nothing at all.
    /// </summary>
    public const string UnnamedSavePoint = "save-point";

    /// <summary>What an agent's batch is labelled when it declared no intent (R-rc5-6b).</summary>
    public const string UnnamedBatch = "an unnamed change";

    /// <summary>The subject line for each origin, given whatever label the caller has.</summary>
    public static string SubjectFor(CheckpointOrigin origin, string? label)
    {
        string trimmed = label?.Trim().ReplaceLineEndings(" ").Trim() ?? "";

        return origin switch
        {
            CheckpointOrigin.SavePoint       => trimmed.Length > 0 ? trimmed : UnnamedSavePoint,
            CheckpointOrigin.WorkspaceClosed => "workspace closed",
            CheckpointOrigin.BeforeBatch     => "before: " + (trimmed.Length > 0 ? trimmed : UnnamedBatch),
            CheckpointOrigin.BeforeRestore   => "before going back",
            _                                => UnnamedSavePoint,
        };
    }

    /// <summary>The wire spelling of an origin. A file format — do not rename one; add one.</summary>
    public static string Spell(CheckpointOrigin origin) => origin switch
    {
        CheckpointOrigin.SavePoint       => "save-point",
        CheckpointOrigin.WorkspaceClosed => "workspace-closed",
        CheckpointOrigin.BeforeBatch     => "before-batch",
        CheckpointOrigin.BeforeRestore   => "before-restore",
        _                                => "save-point",
    };

    private static CheckpointOrigin Parse(string? spelled) => spelled?.Trim() switch
    {
        "workspace-closed" => CheckpointOrigin.WorkspaceClosed,
        "before-batch"     => CheckpointOrigin.BeforeBatch,
        "before-restore"   => CheckpointOrigin.BeforeRestore,
        _                  => CheckpointOrigin.SavePoint,
    };

    /// <summary>
    /// Builds the whole message: a subject, a plain sentence saying how it came about, then the
    /// trailer block.
    /// </summary>
    /// <param name="sequence">R-rc5-8's monotonic sequence. The wall clock supplies the label a human
    /// reads; it never decides what is oldest.</param>
    /// <param name="kept">Whether retention may never thin this one (R-rc5-1f).</param>
    /// <param name="leftOut">Workspace-relative paths left out at an unattended boundary
    /// (R-rc5-15a). Recorded here because the entry has to be able to say it is incomplete.</param>
    public static string Build(
        CheckpointOrigin       origin,
        string?                label,
        long                   sequence,
        bool                   kept        = false,
        IReadOnlyList<string>? leftOut     = null)
    {
        var text = new StringBuilder();
        text.Append(SubjectFor(origin, label)).Append('\n').Append('\n');
        text.Append(Explanation(origin)).Append('\n').Append('\n');

        text.Append(SequenceKey).Append(": ").Append(sequence).Append('\n');
        text.Append(OriginKey).Append(": ").Append(Spell(origin)).Append('\n');

        if (label?.Trim() is { Length: > 0 } intent)
            text.Append(IntentKey).Append(": ").Append(intent.ReplaceLineEndings(" ").Trim()).Append('\n');

        if (kept) text.Append(KeptKey).Append(": yes").Append('\n');

        foreach (string path in leftOut ?? [])
            if (path.Trim() is { Length: > 0 } p)
                text.Append(LeftOutKey).Append(": ").Append(p.ReplaceLineEndings(" ").Trim()).Append('\n');

        return text.ToString();
    }

    /// <summary>The one-line explanation under the subject — §5.5's "a line recording how it came
    /// about", in circuitRF's words rather than git's.</summary>
    private static string Explanation(CheckpointOrigin origin) => origin switch
    {
        CheckpointOrigin.SavePoint       => "You asked circuitRF to keep this state.",
        CheckpointOrigin.WorkspaceClosed => "circuitRF kept this state because the workspace was closed.",
        CheckpointOrigin.BeforeBatch     => "circuitRF kept this state before an assistant changed anything.",
        CheckpointOrigin.BeforeRestore   => "circuitRF kept this state before replacing it with an earlier one.",
        _                                => "circuitRF kept this state.",
    };

    /// <summary>
    /// Reads back what <see cref="Build"/> wrote. <b>Never throws and never refuses</b>: a message
    /// this cannot understand still yields an entry, because a restore point whose metadata is
    /// unreadable is still a restore point and hiding it would be the silent loss §1.4 forbids.
    /// </summary>
    public static CheckpointMetadata Read(string message)
    {
        string   subject  = "";
        long?    sequence = null;
        string?  origin   = null;
        string?  intent   = null;
        bool     kept     = false;
        List<string> leftOut = [];

        var lines = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (subject.Length == 0 && line.Trim().Length > 0 && !IsTrailer(line))
                subject = line.Trim();

            if (!IsTrailer(line)) continue;

            int    colon = line.IndexOf(':');
            string key   = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case SequenceKey when long.TryParse(value, out long parsed): sequence = parsed; break;
                case OriginKey:  origin = value; break;
                case IntentKey:  intent = value; break;
                case KeptKey:    kept   = value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                                       || value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case LeftOutKey when value.Length > 0: leftOut.Add(value); break;
            }
        }

        var parsedOrigin = Parse(origin);

        // A save-point carries the mark by construction (§5.6 rule 6) — an old entry written before
        // the trailer existed is still one, so the mark is derived rather than only read.
        if (parsedOrigin == CheckpointOrigin.SavePoint) kept = true;

        return new CheckpointMetadata(subject, sequence, parsedOrigin, intent, kept, leftOut);
    }

    private static bool IsTrailer(string line)
        => line.StartsWith(KeyPrefix, StringComparison.Ordinal) && line.Contains(':');
}

/// <summary>What <see cref="CheckpointMessage.Read"/> found.</summary>
/// <param name="Subject">The line a designer reads — already free of git vocabulary.</param>
/// <param name="Sequence">R-rc5-8's ordering, or null on an entry that carried none.</param>
/// <param name="Origin">How it came about (§5.5).</param>
/// <param name="Intent">The label the user or the agent supplied, when there was one.</param>
/// <param name="Kept">Whether retention may thin it (R-rc5-1f).</param>
/// <param name="LeftOut">Paths left out at an unattended boundary (R-rc5-15a).</param>
public sealed record CheckpointMetadata(
    string                Subject,
    long?                 Sequence,
    CheckpointOrigin      Origin,
    string?               Intent,
    bool                  Kept,
    IReadOnlyList<string> LeftOut);
