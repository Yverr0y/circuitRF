using System;
using System.Threading;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Messages;

namespace CircuitRF.Ui.Archive;

/// <summary>
/// <b>What has to happen before the dialog can honestly say what including the history would add</b>
/// (RC-8 R-rc8-9, <c>docs/design/revision-control.md</c> §9A.3).
///
/// <para><b>Packing runs in front of the user here, and this is the ONE place that is correct.</b>
/// R-rc3-14 otherwise keeps packing behind a workspace close, because an unasked-for pause is a
/// defect. Here the user has asked for an archive; RC-3's measured overhang means an unpacked
/// repository can be ten times its real size, and an archive is exactly where that surprises someone.
/// It also makes R-rc8-7's size figure honest — a figure taken before the pack would describe an
/// archive nobody is going to get.</para>
///
/// <para><b>So it is reported as progress and never allowed to read as a stall</b>: the Messages panel
/// gets a live row for the duration, and the dialog says what it is doing while the figures are
/// missing. R-rc3-1b's rule that packing yields to another process holding the workspace still
/// applies, and a yielded pack is not a failure — the figures are still computed, from a repository
/// that is merely larger than it needs to be.</para>
///
/// <para>Framework-free, like everything else in this folder, so the whole sequence is testable
/// without a window.</para>
/// </summary>
public static class ArchiveHistoryPreparation
{
    /// <summary>
    /// Packs the workspace's repository and measures what including it would add, writing the answer
    /// onto <paramref name="plan"/>.
    ///
    /// <para>Returns null — and clears <see cref="WorkspaceArchivePlan.HistoryAvailable"/> — when there
    /// is no usable git on this machine. That is R-rc3-3's silence rather than a failure: a designer
    /// who never had this feature is not told about it, and the offer simply is not there.</para>
    /// </summary>
    public static HistoryArchiveSummary? Prepare(
        WorkspaceArchivePlan plan, IMessageSink? messages = null, CancellationToken ct = default)
    {
        if (!plan.HistoryAvailable) return null;

        if (GitCommand.For(plan.WorkspaceDir) is not { } git || !git.IsRepositoryRoot())
        {
            plan.HistoryAvailable = false;
            plan.IncludeHistory   = false;
            return null;
        }

        var live = messages?.BeginProgress("Archive — preparing this workspace's history");
        live?.Update("Archive — preparing this workspace's history", indeterminate: true);

        try
        {
            var (outcome, summary) = HistoryArchive.Prepare(git, ct);
            plan.History = summary;

            live?.Finish(MessageLevel.Info, outcome switch
            {
                // Named rather than hidden: the figure the dialog shows is then larger than the
                // history really needs to be, and the reason is somebody else being in the workspace.
                PackOutcome.Yielded   => "— left as it is, because this workspace is open somewhere else",
                PackOutcome.Cancelled => "— stopped",
                PackOutcome.Failed    => "— could not be tidied up, so it may be larger than it needs to be",
                _                     => $"— {HistoryArchiveSummary.FormatSize(summary.Bytes)}",
            }, keepBar: false);

            return summary;
        }
        catch (Exception e)
        {
            live?.Complete(MessageLevel.Warning, $"Archive — could not read this workspace's history: {e.Message}");
            return null;
        }
    }
}
