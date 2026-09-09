// The generic FR-4 board an artwork-only import falls back to when the files state nothing.
//
// WHY THIS EXISTS, AND WHAT IT REVERSES. A Gerber set with no job file states the artwork and the
// order of the copper and nothing whatever about the substrate, so GI2's skeleton wrote structure with
// every value at ZERO and refused to invent one — deliberately, and with a test saying in capitals not
// to "correct" Epsr to 1.0. That refusal was right about 1.0: air is a valid, entirely simulatable
// substrate, so a stack silently modelled in air RUNS and answers a different question, with nothing
// downstream to question it.
//
// The owner's decision (2026-09-08) is that a NAMED default is not that failure, and that the zeros
// were being paid for on every single Gerber import by the person who then had to type eleven rows of
// numbers by hand. So the structure is now completed with one ordinary FR-4 board — the same numbers
// this repository already ships in pcb-2layer_FR-4_70mil_1oz.ctech — and the import SAYS, in its own
// message, exactly which values it supplied and that they are guesses about a board it has never seen.
//
// The two things that make that safe are both elsewhere and both required:
//   * the import's message names every defaulted quantity (GerberStackupMapping);
//   * the Technology editor marks a field an EM run cannot use (StackupLayerRowViewModel's
//     NeedsAttention flags), which is what catches the values this file could not supply either.
//
// WHAT MAY AND MAY NOT GO IN HERE. ONE generic board, and never a table. "FR-4" is a flammability
// grade, not a product — it is already the name in five shipped .ctech files — and a lookup of laminate
// TRADE names keyed on the material strings a job file carries is refused permanently, for the reason
// GerberStackupMapping's own header and StackupDefaults' both give: it is third-party product names in
// this repository (root CLAUDE.md §"Commercial Vendor References"), and it is out of scope. A second
// grade added here would be the first row of that table. Whoever knows the board types the real
// numbers into the Stackup tab; this only spares them the ones that are the same on nearly every board.

namespace CircuitRF.Design.Layout;

/// <summary>
/// The substrate values an artwork-only import completes a stackup with, and the one pass that
/// applies them.
/// </summary>
public static class SubstrateDefaults
{
    /// <summary>Relative permittivity of ordinary FR-4 — the value every shipped FR-4 technology in
    /// this repository carries.</summary>
    public const double Epsr = 4.4;

    /// <summary>Loss tangent to match.</summary>
    public const double TanD = 0.02;

    /// <summary>Relative permeability. Non-magnetic, like every dielectric circuitRF ships.</summary>
    public const double Mur = 1.0;

    /// <summary>Outer-layer copper, in microns — 1 oz, as the shipped two-layer technology has it
    /// (owner, 2026-09-08: 35 µm rather than that file's own value, which is the same number).</summary>
    public const decimal OuterConductorUm = 35m;

    /// <summary>Inner-layer copper, in microns (owner, 2026-09-08). The shipped four-layer technology
    /// says 17.5 µm — half-ounce foil — and 18 is the rounded figure asked for.</summary>
    public const decimal InnerConductorUm = 18m;

    /// <summary>
    /// The total DIELECTRIC height a board is assumed to have when nothing states an overall board
    /// thickness — 1.778 mm, the 70 mil core of the shipped two-layer technology.
    ///
    /// <para>Split evenly across however many dielectrics the artwork implies, so a two-layer set
    /// reproduces that technology exactly and a six-layer one comes out as an ordinary 1.8 mm board
    /// rather than a 9 mm one. When a job file DOES state a board thickness, that is used instead and
    /// the conductors are subtracted from it — a stated number always beats this one.</para>
    /// </summary>
    public const decimal DielectricBudgetUm = 1778m;

    /// <summary>The floor a derived dielectric thickness is held at, in microns. A stated board
    /// thickness thinner than the copper it carries is arithmetic nobody can honour; rather than
    /// writing zero (which is what this whole pass exists to stop) the layer takes this and the
    /// disagreement shows up on the editor's own board-thickness comparison.</summary>
    public const decimal MinimumDielectricUm = 25m;

    /// <summary>What <see cref="Fill"/> actually supplied — each count is the number of ENTRIES it
    /// wrote that quantity onto, so a caller can say what it defaulted and stay silent about the rest.
    /// </summary>
    /// <param name="ConductorThickness">Conductor entries given a thickness.</param>
    /// <param name="DielectricThickness">Dielectric entries given a thickness.</param>
    /// <param name="Epsr">Dielectrics given a relative permittivity.</param>
    /// <param name="TanD">Dielectrics given a loss tangent (only where εr was also unset — a board
    /// stating a permittivity and a genuine zero loss tangent is describing a lossless dielectric and
    /// is left alone).</param>
    /// <param name="Mur">Dielectrics given a relative permeability.</param>
    /// <param name="DielectricUm">The per-dielectric thickness used, in microns, or null when no
    /// dielectric needed one — what the message quotes.</param>
    /// <param name="FromStatedBoardThickness">Whether <paramref name="DielectricUm"/> was derived from
    /// a board thickness the files stated, rather than from <see cref="DielectricBudgetUm"/>.</param>
    public sealed record Filled(
        int      ConductorThickness,
        int      DielectricThickness,
        int      Epsr,
        int      TanD,
        int      Mur,
        decimal? DielectricUm,
        bool     FromStatedBoardThickness)
    {
        /// <summary>Whether anything at all was supplied — what decides whether the caller says a word.
        /// </summary>
        public bool Any => ConductorThickness > 0 || DielectricThickness > 0
                        || Epsr > 0 || TanD > 0 || Mur > 0;
    }

    /// <summary>
    /// Completes every unset substrate value in <paramref name="stackup"/>, in place, and reports what
    /// it wrote.
    ///
    /// <para><b>Unset only — never a correction.</b> Zero thickness, zero εr and a non-positive µr are
    /// the spellings of "nobody said" that a fresh import produces; anything else is a number somebody
    /// or some file stated and is left exactly as it is, including values that are wrong. Correcting a
    /// stated value would make the import unpredictable and would hide a real mistake, which is what
    /// <c>TechValidation</c>'s own unset-versus-wrong rule already turns on.</para>
    ///
    /// <para>Via entries are skipped entirely: a via has no z band of its own and no dielectric
    /// properties, which is why the extractors and the validator both pass over them.</para>
    /// </summary>
    /// <param name="stackup">The stackup to complete, in top-to-bottom order.</param>
    /// <param name="boardThicknessUm">The board's overall thickness if some file stated one, in
    /// microns. Beats <see cref="DielectricBudgetUm"/> when present and positive.</param>
    /// <param name="dbuPerMicron">The destination technology's resolution.</param>
    public static Filled Fill(Stackup stackup, decimal? boardThicknessUm, int dbuPerMicron)
    {
        var rows = stackup.Layers.Where(l => l.Kind != StackupKind.Via).ToList();
        if (rows.Count == 0)
            return new Filled(0, 0, 0, 0, 0, null, false);

        var conductors  = rows.Where(l => l.Kind == StackupKind.Conductor).ToList();
        var dielectrics = rows.Where(l => l.Kind == StackupKind.Dielectric).ToList();

        // ── Conductors first, because the dielectric budget is what is left after them ────────────
        //
        // Outer is FIRST and LAST in the top-to-bottom order, which is what "outer" means on a board;
        // everything between them is inner foil. A single-conductor stack has one outer layer and no
        // inner, which falls out of the same test.
        int conductorFilled = 0;
        for (int i = 0; i < conductors.Count; i++)
        {
            if (conductors[i].ThicknessDbu != 0) continue;
            bool outer = i == 0 || i == conductors.Count - 1;
            conductors[i].ThicknessDbu =
                LayoutUnits.ToDbu(outer ? OuterConductorUm : InnerConductorUm, LayoutUnit.Um, dbuPerMicron);
            conductorFilled++;
        }

        // ── The dielectrics ──────────────────────────────────────────────────────────────────────
        int needThickness = dielectrics.Count(l => l.ThicknessDbu == 0);
        decimal? perDielectricUm = null;
        bool fromStated = false;

        if (needThickness > 0)
        {
            // What the copper (all of it, including entries that already had a thickness) and the
            // dielectrics that already have one take out of a stated board thickness. Anything left
            // is what the unset dielectrics share.
            decimal budget;
            if (boardThicknessUm is { } stated && stated > 0)
            {
                decimal taken = 0;
                foreach (var row in rows)
                    if (row.ThicknessDbu != 0 || row.Kind == StackupKind.Conductor)
                        taken += LayoutUnits.FromDbu(row.ThicknessDbu, LayoutUnit.Um, dbuPerMicron);
                budget = stated - taken;
                fromStated = true;
            }
            else
            {
                // No stated thickness: the whole default budget belongs to the dielectrics, shared
                // across ALL of them so the answer does not depend on how many were already filled in.
                budget = DielectricBudgetUm;
                foreach (var d in dielectrics)
                    if (d.ThicknessDbu != 0)
                        budget -= LayoutUnits.FromDbu(d.ThicknessDbu, LayoutUnit.Um, dbuPerMicron);
            }

            decimal each = budget / needThickness;
            if (each < MinimumDielectricUm) { each = MinimumDielectricUm; fromStated = false; }
            perDielectricUm = each;

            long dbu = LayoutUnits.ToDbu(each, LayoutUnit.Um, dbuPerMicron);
            foreach (var d in dielectrics)
                if (d.ThicknessDbu == 0)
                    d.ThicknessDbu = dbu;
        }

        int epsrFilled = 0, tandFilled = 0, murFilled = 0;
        foreach (var d in dielectrics)
        {
            // εr and tanδ move together. A dielectric whose permittivity somebody stated and whose
            // loss tangent is zero is a LOSSLESS dielectric, which is a legitimate thing to author;
            // overwriting that zero with 0.02 would be the correction this pass does not make.
            if (d.Epsr <= 0)
            {
                d.Epsr = Epsr;
                epsrFilled++;
                if (d.TanD == 0) { d.TanD = TanD; tandFilled++; }
            }
            if (d.Mur <= 0) { d.Mur = Mur; murFilled++; }
        }

        return new Filled(
            conductorFilled, needThickness, epsrFilled, tandFilled, murFilled,
            perDielectricUm, fromStated);
    }
}
