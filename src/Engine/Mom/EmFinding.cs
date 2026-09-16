// EM-SEV — a run's findings carry a CLASS, and the class answers one question: did this change what
// was solved? (docs/sonnet-briefs/brief-em-run-severity-and-check.md R-emsev-1.)
//
// WHY THIS EXISTS AT ALL. A user drew a spiral in series with a MIM capacitor, ran it, and got a
// flat open. Three separate mechanisms had removed the capacitor from the solve, and circuitRF
// reported all three — correctly, in good English, as three of thirty-five notes at identical
// weight. The prose was not the defect. The defect was that nothing downstream could RANK them, so
// the reader was asked to, on the one day they are least able to.
//
// WHY NOT A PREFIX. `BuildVias` already wrote "WARNING: " into one sentence by hand, which is the
// shape of the answer and also the reason it does not work: nothing downstream can read a prefix it
// was not told about, and every consumer would have to agree on the spelling. The class is data.
//
// WHY IT LIVES IN Engine/Mom RATHER THAN BESIDE THE EXTRACTOR. Both halves of a run produce
// findings — `PlanarExtractor` in src/Design and `PlanarSolve.LevelSeparationNotes` here — and
// src/Design references src/Engine, never the reverse. One type both can name has to be on this
// side of that arrow.

namespace CircuitRF.Engine.Mom;

/// <summary>
/// <b>The three classes, defined by one question: did this change what was solved?</b>
///
/// <para>A REFUSAL is not a member and deliberately so: nothing was written, so it is not a finding
/// about a result — it is the absence of one, and every path that produces one already carries it as
/// its own return (<c>PlanarExtractionResult.Refusal</c>, <c>EmRunStatus.Refused</c>). Adding it
/// here would give two spellings of one outcome.</para>
/// </summary>
public enum EmSeverity
{
    /// <summary>The run explaining itself — mesh sizing, the equal-area via substitution, the
    /// quasi-static γ crossover, the core count. Nothing about the structure differs from what was
    /// drawn.</summary>
    Note,

    /// <summary>An answer was produced and something in it is NOT what was drawn: a level carrying
    /// artwork that was dropped, a drawn via discarded, a quantity past the range it was measured
    /// over. This is the class the reader must be able to find first.</summary>
    Warning,
}

/// <summary>
/// One line of a run's report, with its class attached.
///
/// <para><b>A string converts implicitly, to <see cref="EmSeverity.Note"/>.</b> That is not
/// laziness: the overwhelming majority of the sentences in this area are the run explaining itself,
/// and the conversion is what lets the class be introduced without touching hundreds of correct
/// <c>notes.Add("…")</c> calls — every one of which would otherwise have to be re-read and
/// re-classified in the same change that introduces the mechanism. A warning is spelled out
/// (<see cref="Warn"/>), which is the right way round: the exceptional case is the one that should
/// be visible in the source.</para>
/// </summary>
/// <param name="Severity">Which of the two classes this is.</param>
/// <param name="Text">The sentence, exactly as the producer wrote it. Consumers surface it VERBATIM
/// (R-em-16) — the class is carried BESIDE the words, never spliced into them.</param>
public readonly record struct EmFinding(EmSeverity Severity, string Text)
{
    public static EmFinding Note(string text) => new(EmSeverity.Note, text);
    public static EmFinding Warn(string text) => new(EmSeverity.Warning, text);

    public bool IsWarning => Severity == EmSeverity.Warning;

    public static implicit operator EmFinding(string text) => new(EmSeverity.Note, text);

    /// <summary>The sentence alone. Findings are interpolated into report lines all over this area
    /// and every one of them wants the words, not the pair.</summary>
    public override string ToString() => Text;
}

/// <summary>Conversions between a finding list and the plain-string lists that predate it. Kept in
/// one place so "a list of strings is a list of notes" is stated once rather than at every
/// boundary.</summary>
public static class EmFindings
{
    /// <summary>Every finding's text, class discarded — the pre-EM-SEV spelling of a report, kept
    /// because dozens of call sites and gates read it. <b>Derived, never stored:</b> a second stored
    /// list is a list that drifts.</summary>
    public static IReadOnlyList<string> Texts(IEnumerable<EmFinding> findings)
        => [.. findings.Select(f => f.Text)];

    /// <summary>The texts of the warnings only, in order.</summary>
    public static IReadOnlyList<string> WarningTexts(IEnumerable<EmFinding> findings)
        => [.. findings.Where(f => f.IsWarning).Select(f => f.Text)];

    /// <summary>The texts of the notes only, in order.</summary>
    public static IReadOnlyList<string> NoteTexts(IEnumerable<EmFinding> findings)
        => [.. findings.Where(f => !f.IsWarning).Select(f => f.Text)];

    /// <summary>Plain sentences, all of them notes. The explicit spelling of the implicit
    /// conversion, for the places that carry a whole list across.</summary>
    public static IEnumerable<EmFinding> AsNotes(IEnumerable<string> texts)
        => texts.Select(EmFinding.Note);
}
