namespace CircuitRF.Design.Revision;

/// <summary>
/// How a restore point came about (<c>docs/design/revision-control.md</c> §5.5, R-rc5-9a).
///
/// <para><b>The distinction between <see cref="SavePoint"/> and <see cref="WorkspaceClosed"/> is the
/// point of the enum</b>, and it is drawn in the restore-point list where the two appear together: a
/// designer scanning it must be able to tell <i>"I decided this was worth keeping"</i> from
/// <i>"circuitRF kept this because I shut the lid"</i> without opening either. The automatic one is
/// not lesser — it is frequently the one that saves them — but it means something different, and a
/// list that renders them identically is lying by omission.</para>
/// </summary>
public enum CheckpointOrigin
{
    /// <summary>The user asked for one. Carries their label when they gave one, and is KEPT
    /// (§5.6 rule 6) — the user's judgement about what matters beats any heuristic.</summary>
    SavePoint,

    /// <summary>The workspace was closed. The designer did not choose this moment and the entry must
    /// not imply they did.</summary>
    WorkspaceClosed,

    /// <summary>Taken before an agent's batch modified anything. Labelled with the batch's own stated
    /// intent (R-rc5-6b) — this is §1.2's governing motive.</summary>
    BeforeBatch,

    /// <summary>
    /// Taken before a restore replaced the working tree (R-rc5-12a).
    ///
    /// <para><b>It is an ordinary entry in the list and is not named after the operation that caused
    /// it</b> (R-rc5-13a). "The state before you went back" is a sentence with a git shape — it names
    /// a place to return to rather than a moment in the design — and this list has no such
    /// vocabulary in it.</para>
    /// </summary>
    BeforeRestore,
}
