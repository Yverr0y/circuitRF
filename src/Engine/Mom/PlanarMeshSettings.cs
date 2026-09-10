namespace CircuitRF.Engine.Mom;

/// <summary>
/// <b>Whether a boundary cell follows the GRID or the METAL.</b>
///
/// <para><b>This is the fourth user control, and D3 said there were exactly three.</b> The reversal
/// is on the owner's explicit instruction (brief-conformal-boundary-cells.md §5) and is recorded
/// rather than slipped in; D3's reasoning still stands for everything else and is not generally
/// relaxed. What earns this one is that it is not the same KIND of control as the three: cells per
/// wavelength and edge cells change how finely the same structure is discretised, while this changes
/// <i>which structure is discretised at all</i> — a staircased disc and a conformal disc are
/// different geometry, not two resolutions of one. That is a modelling decision, and modelling
/// decisions belong to the user.</para>
///
/// <para>It also needs an off switch on evidence rather than on taste: <b>every L8/L9 measurement in
/// this repository was taken on the staircase</b>, and a user reproducing one of them must be able
/// to.</para>
/// </summary>
public enum PlanarBoundaryCells
{
    /// <summary>L8b's rule as shipped: a cell is a whole grid rectangle, and a diagonal or curved
    /// outline is approximated by a staircase (D2). <b>The default</b>, and the model every measured
    /// number in this directory's <c>CLAUDE.md</c> was taken on.</summary>
    Staircase,

    /// <summary>A boundary cell is the grid rectangle intersected with the metal, so the union of the
    /// cells is the drawn polygon to round-off (R-cut-1). The interior of the mesh is unchanged and
    /// a Manhattan mesh is bit-identical (R-cut-2).</summary>
    Conformal,
}

/// <summary>
/// D3 asked for <b>exactly three user controls</b> — <c>Auto</c>, <c>Cells per wavelength</c>,
/// <c>Edge mesh on/off + cell count</c> — and that is §10.5's own list, verbatim. <b>There are six
/// now, and every addition past the third was an explicit owner decision recorded at the parameter
/// it added</b> (<see cref="PlanarBoundaryCells"/>, <see cref="MeshFrequencyHz"/>,
/// <see cref="MinCellsAcrossConductor"/>). D3's REASONING still governs what may be added: a control
/// earns its place by being a modelling or responsibility decision that is the user's to make, never
/// by being a number that happens to exist in the mesher.
///
/// <para><b>Kernel A's <see cref="EmMeshSettings"/> has six, and the temptation is to mirror it. Do
/// not.</b> Its six exist because a boundary mesher over infinite dielectric interfaces has a
/// TRUNCATION problem (R-mom-10: truncation half-extent, tail cells) that a surface mesher over a
/// bounded piece of artwork does not have at all, and because a cross-section's edge cell is a
/// fraction of the metal THICKNESS, which a zero-thickness sheet does not have either. Everything
/// the user does not need to think about is auto-derived from the analysis — §10.5's own
/// instruction, and what makes §10.10's 30-second target reachable.</para>
/// </summary>
/// <param name="Auto">
/// When true the other three are ignored and the defaults below are used. This is the control the
/// user actually sees first, and leaving it on is meant to be the normal way to run.
/// </param>
/// <param name="CellsPerWavelength">§10.5's "20" in <c>max cell ≤ λ_g/20</c>.</param>
/// <param name="EdgeMesh">Graded cells at every conductor edge (R-msh-5).</param>
/// <param name="EdgeCells">How many, when on. §10.5 asks for 2–4.</param>
/// <param name="BoundaryCells">
/// <b>Conformal (cut) boundary cells — the fourth control, added as a TRAILING parameter</b> so every
/// existing positional construction is unchanged. <b>It ships OFF</b>; see
/// <see cref="PlanarBoundaryCells"/> for why it is a control at all, and this directory's
/// <c>CLAUDE.md</c> for the measurement that decides whether the default ever flips.
/// </param>
/// <param name="MeshFrequencyHz">
/// <b>The frequency the λ_g/N cell-size cap is sized at — the FIFTH control, and the only one that
/// is a PERFORMANCE knob rather than a resolution or a modelling choice.</b> <c>null</c> (the
/// default) means <i>the sweep's own top frequency</i>, which is exactly what the mesher did before
/// this parameter existed — so an unset value reproduces the shipped behaviour bit for bit.
///
/// <para><b>It is NOT <see cref="PlanarProblem.MaxFrequencyHz"/>, and the distinction is
/// load-bearing.</b> That property answers <i>how high does this sweep go</i> and has three
/// consumers that must keep reading it: <c>PlanarKernel.CanSolve</c>'s electrical via bound (a
/// physics refusal, which a performance knob must never be able to widen), the ρ/λ validated-range
/// note, and the geometry hash. This one answers only <i>what frequency was the mesh sized at</i>.
/// Conflating them would let a user silently relax a refusal by turning down a mesh setting.</para>
///
/// <para><b>The saving is AXIAL ONLY.</b> The transverse pitch is normally set by
/// <see cref="MinCellsAcrossConductor"/> rather than by λ, so halving the mesh frequency does not
/// halve the unknown count in both directions — do not describe it as quadratic. See this
/// directory's <c>CLAUDE.md</c> for the measured table.</para>
/// </param>
public sealed record PlanarMeshSettings(
    bool Auto               = true,
    int  CellsPerWavelength = 20,      // = DefaultCellsPerWavelength (a record's own const cannot
    bool EdgeMesh           = true,    //   be referenced from its primary-constructor defaults;
    int  EdgeCells          = 3,       //   the Default* consts below pin the two together)
    PlanarBoundaryCells BoundaryCells = PlanarBoundaryCells.Staircase,
    double? MeshFrequencyHz = null,
    int  MinCellsAcrossConductor = 4)
{
    public const int  DefaultCellsPerWavelength = 20;
    public const bool DefaultEdgeMesh           = true;
    public const int  DefaultEdgeCells          = 3;
    public const PlanarBoundaryCells DefaultBoundaryCells = PlanarBoundaryCells.Staircase;

    /// <summary>
    /// R-msh-4 — §10.5's "at least 3–5 cells across any conductor width". 4 sits in the middle of
    /// the range the design note asks for, and it is still the default.
    ///
    /// <para><b>It stopped being a constant on 2026-09-09, on the owner's explicit instruction:
    /// the user is responsible for mesh density, and a tool that will not give them a bad mesh when
    /// they ask for one is deciding on their behalf.</b> So
    /// <see cref="PlanarMeshSettings.MinCellsAcrossConductor"/> is now an ordinary setting and 1 is
    /// reachable. The concern that was raised and OVERRULED is recorded rather than dropped, because
    /// it decides how the mesher must REPORT the result: <b>the cell count is not monotonic in this
    /// number today.</b> Measured on a plain 200 µm × 10 mm FR-4 line at cells/λ = 20 — 8 → 220
    /// cells, 6 → 200, 4 → 180, 3 → 160, 2 → 180, <b>1 → 200</b>. <b>Accuracy is not what 4 was
    /// buying</b> — de-embedded S21 moved under 0.001 dB from 1 to 8 across.
    ///
    /// <para><b>That non-monotonicity is a DEFECT elsewhere, not a property of this control, and the
    /// first version of this comment got the cause wrong.</b> It is not the edge fan legitimately
    /// spending back what the bulk saved. It is <c>BuildGridLines</c>' grading marcher: the half-step
    /// look-ahead in <c>BoundaryMesher.PartitionFractions</c> is clamped to the interval end, where
    /// the size field is <c>c0</c> by definition, so the approach to a conductor's far edge collapses
    /// into a uniform run at the FINEST cell size instead of grading down — and coarsening the bulk
    /// pitch makes that run longer. With a Lipschitz-consistent marcher the same ladder is monotonic
    /// and cheaper at every rung: 264 / 220 / 198 / 176 / 154 / 154. Verified, but it moves
    /// de-embedded S21 by 0.063 dB, so it needs a convergence study first —
    /// <c>docs/sonnet-briefs/brief-em-transmission-line-mesh.md</c> M0.</para>
    ///
    /// <para>Consequence for the mesher until that lands: turning this down can RAISE the count, so
    /// <see cref="SurfaceMesher"/> says so when it did and names the edge mesh, which removes the
    /// fan entirely. <b>A warning, never a refusal.</b></para>
    /// </summary>
    public const int DefaultMinCellsAcrossConductor = 4;

    /// <summary>
    /// R-msh-5 — the outermost edge cell as a fraction of the reference length, §10.5's own 2–5%.
    /// <b>Which length that fraction is OF is the measured question</b>; see
    /// <see cref="SurfaceMesher.EdgeReferenceLength"/>.
    /// </summary>
    public const double EdgeFractionOfReference = 0.03;

    /// <summary>Geometric growth ratio inward from an edge — §10.5's "~1.5–2".</summary>
    public const double EdgeGrowthRatio = 1.7;

    public static readonly PlanarMeshSettings Default = new();

    /// <summary>
    /// The settings actually used: <see cref="Auto"/> collapses to the defaults, so there is exactly
    /// one place the "what does Auto mean" question is answered and no code path downstream has to
    /// ask it again.
    ///
    /// <para><b><see cref="BoundaryCells"/> SURVIVES Auto, and that is a decision rather than an
    /// oversight.</b> <c>Auto = true</c> throwing the whole record away is the shape that already
    /// cost this area once — a fixture that sets a control and leaves Auto on then silently meshes
    /// the other way. The rule that resolves it is the same one that earned this control a place at
    /// all: Auto means "choose the RESOLUTION for me", and boundary cells are not a resolution. A
    /// user who asks for the metal to be followed is asking about the structure, and Auto has no
    /// opinion about the structure. Gated by <c>SurfaceMesherConformalTests</c>.</para>
    ///
    /// <para><b><see cref="MeshFrequencyHz"/> SURVIVES Auto too, for the same reason.</b> Auto decides
    /// cells/λ and edge cells — a RESOLUTION. Which frequency that resolution is applied AT is a
    /// different question, and Auto has no opinion about it. Throwing it away here would mean a user
    /// who set a mesh frequency and left Auto on silently got the sweep's top instead — the exact
    /// shape of failure the boundary-cell control above already had to be protected from.</para>
    ///
    /// <para><b><see cref="MinCellsAcrossConductor"/> SURVIVES Auto as well, and here the argument is
    /// the OWNER'S rather than the taxonomy's.</b> It is a resolution, so the taxonomy would have Auto
    /// reset it; but the whole reason it is a control is that the user is responsible for mesh density,
    /// and a number they typed being silently discarded because a checkbox is ticked is exactly the
    /// failure the two paragraphs above exist to prevent. It carries.</para>
    /// </summary>
    public PlanarMeshSettings Resolved => Auto
        ? new PlanarMeshSettings(Auto: false, BoundaryCells: BoundaryCells,
                                 MeshFrequencyHz: MeshFrequencyHz,
                                 MinCellsAcrossConductor: MinCellsAcrossConductor)
        : this with
        {
            CellsPerWavelength      = Math.Max(2, CellsPerWavelength),
            EdgeCells               = Math.Max(0, EdgeCells),
            MinCellsAcrossConductor = Math.Max(1, MinCellsAcrossConductor),
        };
}
