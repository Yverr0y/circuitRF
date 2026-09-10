// L8d — the port: what one IS, how it resolves onto L8b's mesh, and the refusals.
//
// D2 — AN EDGE PORT'S CUT IS THE OUTERMOST ROOFTOP ROW OF THE FEED, AND NOTHING IS USER-POSITIONABLE.
// An edge port names an end of a conductor; the reference plane is then the shared edge of the two
// outermost cells there, one cell in from the drawn metal, and the half-cell beyond it is part of
// the error box. There is no port-offset setting, no reference-plane coordinate and no de-embedding
// distance to choose, because ALL of that is what the calibration removes (PlanarCalibration) — and
// offering a knob for it would offer a way to get a different answer for the same structure.
//
// THE OTHER TWO PORT TYPES ARE THE SAME OBJECT CUT SOMEWHERE ELSE.
//
// PlanarPortKind.Internal cuts at the foot of a via instead, between the metal and the ground
// plane, and drives that via's ground-attachment bases. It is the port a grounded component or a
// ground-returning device terminal needs, and it is the same incidence matrix and the same
// Y = BᵀZ⁻¹B one dimension over — a delta gap across the shared FOOTPRINT rather than a shared
// edge. Its polarity is not asked for: + is the metal and − is the plane, because one of its two
// terminals IS the ground reference.
// THE SECOND PORT TYPE — AN INTERNAL DELTA GAP — IS THE SAME OBJECT CUT SOMEWHERE ELSE.
// PlanarPortKind.InternalDeltaGap names a POINT ON the metal rather than an end of it, and the gap
// is the mesh gridline NEAREST that point with metal on both sides. Everything downstream of the
// cut is unchanged: the same rooftop row, the same incidence matrix, the same Y = BᵀZ⁻¹B (D1).
//
// What DOES change is that there is nothing outside the gap. An edge port has a feed, so it has an
// error box and the two-line calibration removes it; an internal port has metal on both sides, so
// there is no feed to remove, no Z_c to reference to, and no calibration standard that could even
// be built for it. Its s-parameters are therefore referenced to its own declared Z0 at the gap
// itself. That is what an internal port IS — see PlanarSolve's IdentityBox for how the two kinds
// share one de-embedding path without either pretending to be the other.
//
// D3 — THE GROUND REFERENCE IS THE STACKUP'S GROUND PLANE, ALWAYS. The return path is the ground
// plane by construction and there is nothing for the user to declare. A port naming any other
// reference — a coplanar ground, a second conductor, a differential pair, or (L9d) a via between two
// levels — is refused by name (see PlanarPortReference and PlanarPorts.ViaPortRefusal).
//
// L9d NOTE: through L8 those refusals pointed at "L9". L9 has now arrived and none of them is built,
// which is exactly why a refusal must name WHERE THE CAPABILITY ARRIVES rather than a phase number:
// §10.6 lists coplanar, differential, multi-mode and co-simulation ports as later work, and L9's own
// out-of-scope list keeps them there.
//
// ── RP-2a — D3 IS NOW HALF TRUE, AND THE HALF THAT CHANGED IS *TWO CUTS*, NOT A SECOND PLANE ─────
//
// A port referenced to DRAWN metal is not a second ground plane and never could be: a LayerStack has
// exactly two terminations and no interior PEC, so the medium cannot represent one (RP-2's R-rp2-1).
// What it is instead is TWO CUTS AT ONE STATION — an ordinary delta gap in the signal conductor and a
// second ordinary delta gap in the RETURN conductor, driven against each other. The incidence column
// then carries a signed block on each row set rather than one signed block, and NOTHING else moves:
// same mesh, same basis set, same Z, same factorisation, same Y = BᵀZ⁻¹B.
//
// IT IS TWO CUTS BECAUSE IT CANNOT BE ONE. RP-2's own first measurement (CoplanarSlotMeshTests, and
// src/Engine/Mom/RESOLVED.md) asked whether the surface mesher produces a basis spanning the SLOT
// between two conductors, so that a single delta gap could be cut across it. The answer is no, and
// structurally so: every polygon edge is a hard gridline, a cell exists only where a grid row's
// centre is inside metal, and a basis is a pair of grid-adjacent cells — so a slot of nonzero width
// always owns at least one metal-free grid row and refining ADDS rows to it. A basis over the slot
// would be a new basis family (a slot/magnetic-frill unknown) with its own singular treatment, which
// is a far larger question than a per-port reference.
//
// ONLY AN INTERNAL DELTA GAP. An edge port has a feed outside its cut, therefore an error box,
// therefore a calibration standard — and a coplanar port's standard is a COPLANAR line, with its own
// Z_c, its own β and its own static capacitance. PlanarCalibration builds uniform lines over the
// plane and PlanarPortResolution's cross-section fields (WidthM, TransverseLines, …) describe one
// conductor, so calibrating a coplanar edge port against those standards would produce s-parameters
// that are plausible and referenced to nothing. That is strictly worse than a refusal, so a
// conductor-referenced EDGE port is still refused, and the refusal names the capability it is
// waiting for rather than a phase (R-mom-17).
//
// R-msh-2 is honoured throughout: resolution INDEXES by L8b's cell and basis order and never
// re-sorts it. The one dictionary here is a pure LOOKUP built by a single forward pass over
// mesh.Bases and never iterated, so nothing on this path depends on hash order.

using System.Numerics;

namespace CircuitRF.Engine.Mom;

/// <summary>Which end of a conductor the port sits on — and therefore which way current flows in.</summary>
public enum PlanarPortSide
{
    /// <summary>The low-x end; current enters flowing +x̂.</summary>
    MinX,
    /// <summary>The high-x end; current enters flowing −x̂.</summary>
    MaxX,
    /// <summary>The low-y end; current enters flowing +ŷ.</summary>
    MinY,
    /// <summary>The high-y end; current enters flowing −ŷ.</summary>
    MaxY,
}

/// <summary>
/// <b>WHERE the delta gap is cut, which is the one thing that distinguishes the two port types this
/// kernel builds.</b> Both are the same object — a delta gap across the shared edge of two adjacent
/// cells, driving the rooftop row that spans it (D1) — and they differ only in which shared edge,
/// and therefore in whether there is a feed outside the gap to calibrate against.
/// </summary>
public enum PlanarPortKind
{
    /// <summary>
    /// The gap is one cell in from a conductor's own END FACE, so everything outside it is the port
    /// discontinuity and the two-line calibration removes it (<see cref="PlanarDeembed"/>). This is
    /// the port every measured or probed structure has.
    /// </summary>
    Edge,

    /// <summary>
    /// <b>The gap is an INTERIOR cut of a conductor</b> — metal on both sides, nothing outside it.
    /// A lumped element embedded in metal, or a device terminal in the middle of a structure.
    ///
    /// <para>There is no feed beyond the cut, therefore no error box, therefore <b>no de-embedding
    /// and no Z_c to reference to</b>: the reference plane IS the gap, and the published
    /// s-parameters are referenced to the port's own declared Z₀ directly. That is a property of
    /// what an internal port IS, not a missing feature — a two-line calibration removes a feed, and
    /// there is no feed here to remove.</para>
    /// </summary>
    InternalDeltaGap,

    /// <summary>
    /// <b>The gap is at the foot of a via, between the metal and the GROUND PLANE.</b> It does not
    /// cut the trace: the current it drives leaves the conductor vertically, down the via and into
    /// the plane, which is the path a grounded R, L or C — or a device terminal that returns to
    /// ground — needs.
    ///
    /// <para>Its + terminal is the metal and its − terminal is the ground plane — the only reading
    /// of a port whose second terminal is the ground reference. That polarity is fixed by what the
    /// port IS and is never asked for, unlike an <see cref="InternalDeltaGap"/>, whose two lips are
    /// both metal and whose direction therefore has to be stated.</para>
    ///
    /// <para>It drives the ground-attachment bases of the via under it — every cell of that via's
    /// footprint, since they are one conductor at one potential — and, like an
    /// <see cref="InternalDeltaGap"/>, <b>it is not de-embedded</b>: there is nothing outside the cut
    /// to remove, so its s-parameters are reported at the foot of the via in its own declared Z₀.</para>
    /// </summary>
    Internal,
}

/// <summary>
/// D3 — the only ground reference v1 has. The enum exists so the refusal can be worded against a
/// named alternative rather than against "not implemented", which is R-mom-17's whole point.
/// </summary>
public enum PlanarPortReference
{
    /// <summary>The stackup's ground plane. The only reference the MEDIUM can represent — see D3.</summary>
    GroundPlane,
    /// <summary>
    /// <b>RP-2a — the negative terminal is a drawn coplanar ground conductor.</b> A second cut, in
    /// that conductor, at the same station as the signal's, driven against it.
    /// <para>Identical to <see cref="SecondConductor"/> in what the kernel does; the two members
    /// differ in what the USER means — a ground strip against a second signal line — and that
    /// difference is what the layout note and a future coplanar calibration read differently.
    /// Building two code paths for one object would be the way to make them disagree.</para>
    /// </summary>
    CoplanarGround,
    /// <summary>
    /// <b>RP-2a — the negative terminal is a second drawn signal conductor.</b> The same two-cut
    /// object <see cref="CoplanarGround"/> is; see its remark for why both members exist.
    /// <para><b>This is not a differential-mode decomposition.</b> It is one port driven between two
    /// conductors. Multi-mode ports stay §10.6's later work.</para>
    /// </summary>
    SecondConductor,
    /// <summary>
    /// <b>L9d — a port driven BETWEEN the two levels a via joins</b>, i.e. §0.2 item 2's option (b).
    /// Named so the refusal can be worded against it; see <see cref="PlanarPorts"/> for the
    /// argument and for where the capability actually arrives (§10.6's co-simulation ports, which
    /// are not L9's).
    /// </summary>
    ViaBetweenLevels,
}

/// <summary>
/// A port on the planar structure: an end of a conductor, a reference impedance, and nothing else.
/// Neutral in the R-mom-1 sense — metres and ohms, no DBU, no <c>.clay</c>, no layer table.
/// </summary>
/// <param name="Number">1-based, and the order the s-parameter matrix is indexed in.</param>
/// <param name="Location">A point on (or just inside) the conductor.
/// <para>For an <see cref="PlanarPortKind.Edge"/> port only its TRANSVERSE coordinate is used to pick
/// the conductor run; the longitudinal one is not, because D2 fixes the cut at the outermost row.
/// For an <see cref="PlanarPortKind.InternalDeltaGap"/> port <b>both</b> coordinates are used — the
/// transverse one picks the run and the longitudinal one picks the cut — and the resolution reports
/// how far the chosen gridline landed from the point that was asked for.</para></param>
/// <param name="Side">Which end of the conductor this is — and, for an
/// <see cref="PlanarPortKind.InternalDeltaGap"/> port where there is no "end", which way positive
/// port current flows across the cut. Either way it is the sign of
/// <see cref="IncidenceSign"/> and getting it wrong is a hard π in the transmission phase.</param>
/// <param name="Z0">Reference impedance. Complex is allowed because
/// <c>RFNetwork.SToS</c> already handles it and refusing it here would be a gratuitous narrowing.</param>
/// <param name="LayerIndex">
/// <b>L9d/D2 — a port's LEVEL is part of its identity, and null means "infer it".</b>
///
/// <para>Through L9c this was a plain <c>int</c> defaulting to 0 and had never been given a non-zero
/// value. With more than one level a cut at a given (x, y) can intersect metal on several of them,
/// and picking one silently is exactly the shape of failure R-mom-17 exists to prevent — so the
/// default is now <b>null = infer</b>: exactly one candidate level resolves, or the port is refused
/// by name listing every level it could have meant. An explicit index is honoured as given (the
/// caller has disambiguated) and refused by name if that level carries no conductor there.</para>
///
/// <para>Every pre-L9d construction site passes nothing and every one-level mesh has exactly one
/// candidate, so inference reproduces the old behaviour exactly.</para>
/// </param>
/// <param name="GroundPathWidthM">
/// <b><see cref="PlanarPortKind.Internal"/> ports only: the side of the square path down to the
/// ground plane this port may GROW for itself, when the artwork has no via under it.</b> Metres;
/// null means grow nothing and refuse instead.
///
/// <para>A port to ground needs a conductor to ground: the current it drives has to leave the metal
/// somehow, and in this kernel only a via basis carries vertical current. Requiring the user to draw
/// one is honest but is a chore for what is, geometrically, one cell — so the solver grows it
/// (<see cref="PlanarGroundPath"/>), by the same rule R-fed-1's calibration feed follows: created
/// before meshing, REPORTED by name, and never at the expense of metal the user drew. A drawn via
/// always wins; this only fills in where there is none.</para>
///
/// <para><b>It is a real conductor and its inductance is in the answer</b>, which is why the width is
/// stated rather than assumed: the caller passes the technology's own default via size, so what gets
/// built is the via that board would actually have. Null keeps the strict behaviour — a port with no
/// via under it is refused by name — which is what a headless caller that has no technology to ask
/// gets.</para>
/// </param>
/// <param name="Kind">
/// <b>Edge (the default) or an interior delta gap.</b> An edge port names an END of a conductor and
/// its cut is fixed at the outermost cell pair (D2); an internal one names a POINT ON the metal and
/// its cut is the mesh gridline nearest that point, with metal on both sides. Every construction
/// site that predates this parameter passes nothing and gets exactly the port it always got.
/// </param>
/// <param name="NegativeLocation">
/// <b>RP-2a — a point on the RETURN conductor, for a <see cref="PlanarPortReference.CoplanarGround"/>
/// or <see cref="PlanarPortReference.SecondConductor"/> port. Null for every ground-referenced
/// port, which is every port that predates RP-2a.</b>
///
/// <para>It is resolved exactly the way <see cref="Location"/> is — the same
/// <c>TryResolveOnLayer</c>, the same nearest-gridline snap, the same worded refusals — because a
/// second resolution rule is a second chance for the two terminals to land somewhere nobody
/// intended (RP-2's R-rp2-2). The two cuts must then be at the same station, on the same level and
/// in different conductors, and each of those is refused by name rather than adjusted.</para>
/// </param>
/// <param name="NegativeLayerIndex">
/// The return terminal's level, on <see cref="LayerIndex"/>'s terms: null = infer, an explicit index
/// is honoured as given. <b>Stating BOTH levels is what permits a port whose two cuts are on
/// different levels</b>; inferring them and landing on two levels is refused, because two terminals
/// is two chances to do that silently (RP-2a's R-rp2a-3, L9d/D2's reason one level up).
/// </param>
public sealed record PlanarPort(
    int                 Number,
    EmPoint             Location,
    PlanarPortSide      Side,
    Complex             Z0,
    int?                LayerIndex = null,
    PlanarPortReference Reference  = PlanarPortReference.GroundPlane,
    PlanarPortKind      Kind       = PlanarPortKind.Edge,
    double?             GroundPathWidthM = null,
    EmPoint?            NegativeLocation = null,
    int?                NegativeLayerIndex = null)
{
    /// <summary>RP-2a — whether this port's negative terminal is a drawn conductor rather than the
    /// stackup's ground plane. The two members that say so are one object here (R-rp2a-12).</summary>
    public bool IsConductorReferenced =>
        Reference is PlanarPortReference.CoplanarGround or PlanarPortReference.SecondConductor;

    /// <summary>The basis direction the port's row is drawn from. <b>Z for an
    /// <see cref="PlanarPortKind.Internal"/> port</b>, whose current leaves the plane down a
    /// via rather than running along the metal — which is why <see cref="Side"/> means nothing to
    /// one and is not read.</summary>
    public PlanarBasisDirection Direction =>
        Kind == PlanarPortKind.Internal ? PlanarBasisDirection.Z
        : Side is PlanarPortSide.MinX or PlanarPortSide.MaxX
            ? PlanarBasisDirection.X
            : PlanarBasisDirection.Y;

    /// <summary>
    /// D1's ±1. A rooftop's current is positive along +x̂ (or +ŷ); current flowing INTO the structure
    /// is +x̂ at a MinX port and −x̂ at a MaxX one. The SAME sign is used for the excitation and for
    /// reading the current back — that is what makes <c>Y = BᵀZ⁻¹B</c> the actual admittance matrix
    /// rather than a sign-scrambled relative of one.
    /// </summary>
    /// <para><b>An internal port's sign is +1 and is not the user's to set</b> — it is the same "current
    /// flows INTO the structure" convention every other port here uses, one dimension over. Every
    /// vertical basis's current flows +z, from the plane up to the metal (see
    /// <c>PlanarBasisFunctions</c>' header), so a positive port current enters the metal from the
    /// plane, which puts + on the METAL and − on the ground plane.</para>
    ///
    /// <para><b>Measured rather than reasoned into place.</b> A short line with a via to ground at
    /// its centre is three 50 Ω ports meeting at one node above the plane, whose s-matrix is
    /// S_ii = −1/3, S_ij = +2/3 — and it comes back that way at +1. The other sign turns every term
    /// through this port by π and changes nothing else, which is a complete and plausible s-matrix
    /// that no magnitude plot would question; the derivation of which lip is "+" is exactly the step
    /// that is easy to write down backwards, so it is gated
    /// (<c>InternalPortTests.ASmallStructureAtLowFrequencyIsATHREEWAYNODE…</c>).</para>
    public double IncidenceSign =>
        Kind == PlanarPortKind.Internal ? +1.0
        : Side is PlanarPortSide.MinX or PlanarPortSide.MinY ? +1.0 : -1.0;
}

/// <summary>
/// <b>RP-2a — the RETURN terminal of a conductor-referenced port: the second cut, field for field.</b>
///
/// <para><see cref="PlanarPortResolution"/>'s cross-section fields are all singular — one conductor,
/// one cut, one width — and R-rp2a-10 says that record GROWS rather than forks. So this carries the
/// negative terminal's counterparts of exactly those fields and hangs off the resolution as a
/// nullable companion: a ground-referenced resolution is the record it has always been, with a null
/// here, and every consumer that never asks reads the same bits it always read.</para>
///
/// <para>There is deliberately no <c>Side</c>, <c>Direction</c>, <c>Z0</c> or <c>IncidenceSign</c>
/// here. A port has ONE polarity and ONE reference impedance; the negative terminal's sign is the
/// positive one's negated, which is a property of the port rather than of the terminal, and putting
/// a second copy of it here is how the two would drift apart.</para>
/// </summary>
public sealed record PlanarPortTerminal(
    IReadOnlyList<int>    BasisIndices,
    double                WidthM,
    double                ReferencePlaneM,
    double                OuterEdgeM,
    IReadOnlyList<double> TransverseLines,
    IReadOnlyList<double> LongitudinalRunM,
    int                   LayerIndex,
    int                   CutCellCount   = 0,
    double                GridWidthM     = 0,
    double                UndrivenMetalM = 0,
    double                GapOffsetM     = 0)
{
    public int BasisCount => BasisIndices.Count;

    /// <summary>The negative terminal is resolved by the SAME routine the positive one is, so it
    /// arrives as a full resolution and is narrowed here. Reusing that routine is R-rp2-2.</summary>
    internal static PlanarPortTerminal From(PlanarPortResolution r) =>
        new(r.BasisIndices, r.WidthM, r.ReferencePlaneM, r.OuterEdgeM, r.TransverseLines,
            r.LongitudinalRunM, r.LayerIndex, r.CutCellCount, r.GridWidthM, r.UndrivenMetalM,
            r.GapOffsetM);
}

/// <summary>
/// What a port resolved to on a particular mesh — R-prt-2's report. Everything a user (or L8e's
/// panel) needs in order to see where the reference plane actually landed, and everything
/// <see cref="PlanarCalibration"/> needs in order to rebuild the port's neighbourhood exactly (D4).
/// </summary>
/// <param name="BasisIndices">Indices into <see cref="PlanarMesh.Bases"/>, in transverse order.</param>
/// <param name="WidthM">The resolved conductor width at the cut — <b>the metal actually on the
/// reference plane</b>, not the drawn width, so a staircased edge reports what was meshed and a
/// CONFORMAL boundary cell reports the metal rather than its grid rectangle. With no cut cell in the
/// port's run the two are the same subtraction and the number is bit-identical to L8d's.</param>
/// <param name="ReferencePlaneM">The coordinate of the shared edge the current crosses. D2.</param>
/// <param name="OuterEdgeM">The metal's own outer edge — one cell further out.</param>
/// <param name="TransverseLines">The port's own cross-section, <c>BasisIndices.Count + 1</c> lines.
/// D4 copies these into the calibration standard verbatim, which is why they are the METAL's extents
/// rather than the grid's whenever a boundary cell is cut: the standard is a uniform rectangle and it
/// has to be a rectangle of the DUT's own cross-section or the error box is not the same object.
/// Where nothing is cut they are the gridlines, copied verbatim as before.</param>
/// <param name="LongitudinalRunM">Cell sizes marching INWARD from the outer edge along the port's
/// own axis, as far as the conductor runs. D4 copies the first K of these.</param>
/// <param name="LayerIndex">L9d — the conductor level the cut actually landed on, inferred or
/// explicit. The de-embedding needs it (a standard is a single-level line on THIS level, D3) and so
/// does anything that has to say which level a reported quantity belongs to.</param>
/// <param name="CutCellCount">How many of the port's own cells the conformal mesher cut. Zero under
/// the staircase and on any Manhattan feed; non-zero says the width below is a metal width rather
/// than a grid one, and that the residual named in <see cref="PlanarPortResolution.Describe"/>
/// applies.</param>
/// <param name="GridWidthM">What the width WOULD have been on the grid — carried only so the report
/// can state the deficit as a number instead of a caveat. Equal to <c>WidthM</c> when nothing is
/// cut.</param>
/// <param name="Kind">Which kind of cut this is. An <see cref="PlanarPortKind.Edge"/> resolution
/// carries a feed outside the plane and is de-embedded; an <see cref="PlanarPortKind.InternalDeltaGap"/>
/// one has metal on both sides, is not de-embedded, and reports its s-parameters at the gap in its
/// own declared Z₀. Everything else in this record means the same thing for both.</param>
/// <param name="GapOffsetM">Internal ports only: <b>how far the gap actually landed from the point
/// that was asked for</b>, along the port's own axis. The cut can only be a mesh gridline, so a
/// requested position between two gridlines snaps to the nearer — and the distance it moved is
/// reported rather than left to be discovered, because it is bounded by half a cell and half a cell
/// is a quantity the user sets. Zero for an edge port, whose cut is fixed by D2 and is not asked
/// for at all.</param>
/// <param name="FootprintAreaM2"><b>Internal ports only: the meshed area of the via this port drives.</b>
/// An internal port's transverse dimension is an AREA rather than a width — its current crosses the via's
/// footprint, not a line across the metal — so <see cref="WidthM"/>, <see cref="ReferencePlaneM"/>
/// and <see cref="OuterEdgeM"/>, which are all in-plane lengths along a port's own axis, mean nothing
/// for one and are zero. This is the number that says how much via the port actually got: it is the
/// sum of the cell areas that carry a ground-attachment basis, so a footprint the mesh resolved into
/// fewer cells than the artwork drew reports the smaller number rather than the drawn one.</param>
/// <param name="UndrivenMetalM">Metal on the reference plane, adjacent to the port's own run, that
/// carries NO rooftop and is therefore not driven — R-cut-4 declining the outermost cell pair of a
/// conformal feed. Zero under the staircase and on any Manhattan feed. It is reported rather than
/// silently absorbed because it is the one thing a conformal port does WORSE than a staircased one,
/// and refining the transverse mesh is what shrinks it.</param>
public sealed record PlanarPortResolution(
    int                    Number,
    PlanarPortSide         Side,
    PlanarBasisDirection   Direction,
    Complex                Z0,
    IReadOnlyList<int>     BasisIndices,
    double                 IncidenceSign,
    double                 WidthM,
    double                 ReferencePlaneM,
    double                 OuterEdgeM,
    IReadOnlyList<double>  TransverseLines,
    IReadOnlyList<double>  LongitudinalRunM,
    int                    LayerIndex     = 0,
    int                    CutCellCount   = 0,
    double                 GridWidthM     = 0,
    double                 UndrivenMetalM = 0,
    PlanarPortKind         Kind           = PlanarPortKind.Edge,
    double                 GapOffsetM     = 0,
    double                 FootprintAreaM2 = 0,
    PlanarPortReference    Reference       = PlanarPortReference.GroundPlane,
    PlanarPortTerminal?    Negative        = null)
{
    public int BasisCount => BasisIndices.Count;

    // ── RP-2a — THE NORMALISATION, AND THE MEASUREMENT THAT CHOSE IT ─────────────────────────────
    //
    // A two-cut port impresses a gap voltage on EACH of its two cuts, and the two add around the
    // loop: with ±w on the blocks the loop EMF is 2w, and the port current read back through the
    // same B is w·(I⁺ − I⁻) = 2w·I_line when the return conductor carries the whole return. So
    //
    //     Z₁₁ = v / i = Z_true / (4 w²)
    //
    // and only w = ½ makes the port report the impedance it is actually looking into. w = 1 gives a
    // complete, plausible s-matrix a QUARTER of the right impedance — no symptom, no warning, and
    // reciprocity and passivity both still hold. (The brief costed that error at a factor of two; it
    // is four, because w scales the excitation and the read-back alike.)
    //
    // The single-cut ground-referenced port is the same formula with ONE gap: EMF = w, i = w·I_line,
    // Z₁₁ = Z_true/w², so its w = 1 — which is why nothing about it moves and why the two are not
    // "the same constant" by coincidence.
    //
    // **MEASURED, not reasoned into place** (R-rp2a-5). The reasoning above is exactly the shape of
    // step this file already records getting backwards once (the internal port's SIGN, derived in
    // prose and caught only by a structure with a known answer). The gate is a coplanar-strips line
    // against the conformal-mapping closed form Z = η₀·K(k)/K(k′), which is exact for the ideal
    // structure and independent of everything here:
    // TwoCutPortTests.Gate2_ACoplanarStripLine_MatchesTheConformalMappingClosedForm.
    /// <summary>RP-2a — each cut of a two-cut port carries HALF the port's voltage. See the note
    /// above for the derivation and for the measurement that selected it.</summary>
    public const double TwoCutTerminalWeight = 0.5;

    /// <summary>
    /// <b>The incidence weight on this port's POSITIVE block.</b> For every ground-referenced port
    /// this is <see cref="IncidenceSign"/> itself, bit for bit, which is what makes RP-2a's
    /// bit-identity gate a statement about arithmetic rather than about tolerance.
    /// </summary>
    public double PositiveWeight => Negative is null ? IncidenceSign : IncidenceSign * TwoCutTerminalWeight;

    /// <summary>The weight on the NEGATIVE block — the positive one negated, so the two gaps add
    /// around the loop. Meaningless (and unread) when <see cref="Negative"/> is null.</summary>
    public double NegativeWeight => -PositiveWeight;

    /// <summary>RP-2a — whether this port drives a second cut in a drawn return conductor.</summary>
    public bool IsConductorReferenced => Negative is not null;

    /// <summary>
    /// <b>Whether the two-line calibration means anything for this port.</b> An edge port has a feed
    /// outside its cut, so it has an error box and a Z_c; an internal delta gap has metal on both
    /// sides and neither. Asked in exactly one place (<c>PlanarSolve</c>) so the two kinds cannot
    /// drift apart, and named rather than written as an enum comparison at each site.
    /// </summary>
    public bool IsDeembeddable => Kind == PlanarPortKind.Edge;

    /// <summary>The bulk (largest) cell along the port's axis, which is what D4 fills a standard's
    /// middle with so the standard's line and the DUT's feed are discretised identically.</summary>
    public double BulkCellM
    {
        get
        {
            double m = 0;
            foreach (double d in LongitudinalRunM) m = Math.Max(m, d);
            return m;
        }
    }

    /// <summary>R-prt-2's one-line summary, for the notes.</summary>
    public string Describe() =>
        (Kind switch
        {
            PlanarPortKind.InternalDeltaGap => DescribeDeltaGap(),
            PlanarPortKind.Internal    => DescribeInternal(),
            _                               => DescribeEdge(),
        }) +
        (CutCellCount == 0 && UndrivenMetalM <= 0 ? "" : ConformalNote()) +
        (Negative is null ? "" : DescribeReturn());

    /// <summary>
    /// <b>RP-2a — what this port RETURNS through, said per port.</b>
    ///
    /// <para>The kernel's standing sentence is that the stackup's ground plane is the negative
    /// terminal of every port in a run and is not selectable per port. That is false of this one,
    /// and a run can now mix the two, so the fact has to travel with the port rather than with the
    /// run — a reader of a mixed s-matrix otherwise has no way to tell which reference each column
    /// is in.</para>
    /// </summary>
    private string DescribeReturn()
    {
        var n = Negative!;
        return
            $" Its negative terminal is NOT the ground plane: it is a second cut, of " +
            $"{n.BasisCount} basis function(s) spanning {SurfaceMesher.Eng(n.WidthM)}m, in the " +
            (Reference == PlanarPortReference.CoplanarGround
                ? "coplanar ground conductor "
                : "second signal conductor ") +
            $"you named, at the same station ({(Direction == PlanarBasisDirection.X ? "x" : "y")} = " +
            $"{SurfaceMesher.Eng(n.ReferencePlaneM)}m) on level {n.LayerIndex}. The two cuts are " +
            "driven against each other, each carrying half the port's voltage, so the current this " +
            "port measures is the loop current between those two conductors and the return path is " +
            "the metal you drew rather than the plane underneath it.";
    }

    /// <summary>
    /// The internal port's own report. Like the gap's it has to say that nothing is de-embedded here
    /// and why, but the two facts specific to THIS port are that its current leaves the metal
    /// vertically — so the answer is at the foot of a via and not at a plane across the trace — and
    /// how much of the via's footprint the mesh actually resolved, which is the quantity a coarse
    /// mesh silently shrinks.
    /// </summary>
    private string DescribeInternal() =>
        $"Port {Number} is an internal port: a gap at the foot of the via under it, between " +
        $"the metal and the ground plane. It drives {BasisCount} ground-attachment basis " +
        $"function(s) over {FootprintAreaM2 * 1e6:0.###} mm² of via footprint. Its + terminal is the " +
        "metal and its − terminal is the plane, so positive port current flows UP the via into the " +
        "conductor, the same way into the structure as every other port here. It is NOT de-embedded: the " +
        "gap has the ground plane on one side and the via on the other, so there is no feed line " +
        "outside it to remove and no line impedance to reference to. Its s-parameters are reported " +
        "at the foot of the via, in the reference impedance you declared for it.";

    private string DescribeEdge() =>
        $"Port {Number} resolved to {BasisCount} basis function(s) across " +
        $"{SurfaceMesher.Eng(WidthM)}m of conductor; reference plane at " +
        $"{(Direction == PlanarBasisDirection.X ? "x" : "y")} = {SurfaceMesher.Eng(ReferencePlaneM)}m, " +
        $"one cell in from the metal edge at {SurfaceMesher.Eng(OuterEdgeM)}m.";

    /// <summary>
    /// The internal port's own report. It says three things an edge port's does not, and every one
    /// of them is something a user would otherwise have to infer: that this is an interior cut with
    /// metal on both sides, WHERE the cut landed against where it was asked for, and that nothing is
    /// de-embedded here because there is no feed to remove.
    /// </summary>
    private string DescribeDeltaGap() =>
        $"Port {Number} is an internal delta gap across {BasisCount} basis function(s) spanning " +
        $"{SurfaceMesher.Eng(WidthM)}m of conductor; the gap is the interior cut at " +
        $"{(Direction == PlanarBasisDirection.X ? "x" : "y")} = {SurfaceMesher.Eng(ReferencePlaneM)}m, " +
        $"with metal on both sides" +
        (GapOffsetM > 0
            ? $" — {SurfaceMesher.Eng(GapOffsetM)}m from where it was placed, because a gap can only " +
              "be a mesh gridline and this was the nearest one. Refine the mesh there to move it closer."
            : ", exactly where it was placed.") +
        " It is NOT de-embedded: there is no feed outside an interior cut, so there is no port " +
        "discontinuity to remove and no line impedance to reference to. Its s-parameters are " +
        "reported at the gap itself, in the reference impedance you declared for it.";

    /// <summary>
    /// <b>What a CONFORMAL boundary cell at a port does and does not cost, as a number.</b>
    ///
    /// <para>This used to be a refusal. It is a note because the refusal's own premise — "a port
    /// belongs on a drawn feed, which is Manhattan, so this should never fire" — is false of the
    /// parts a user actually selects: a taper's flanks are oblique from its very first cell, so on
    /// MKlopf and MTaper the outermost cell of the port's transverse run is cut and the whole run
    /// was refused for it. What the cut actually changes is that the reference plane's own metal is
    /// shorter than the gridline, and that is now MEASURED and carried into the calibration standard
    /// (the standard is built from <see cref="TransverseLines"/>, which are the metal's extents) —
    /// so the error box is the same object again and the residual is the one every port already has:
    /// the feed is not perfectly uniform over the length the standard replaces.</para>
    /// </summary>
    private string ConformalNote()
    {
        string s = "";

        if (CutCellCount > 0)
            s += $" {CutCellCount} of its cell(s) follow the metal rather than the grid, so its " +
                 $"width is the metal on the reference plane ({SurfaceMesher.Eng(WidthM)}m) rather " +
                 $"than the grid extent ({SurfaceMesher.Eng(GridWidthM)}m) — and the calibration " +
                 "standard is built to that same cross-section, so the error box is the same object. " +
                 "What stays approximate is that a standard is a UNIFORM line while a cut feed is, " +
                 "by construction, tapering.";

        if (UndrivenMetalM > 0)
            s += $" A further {SurfaceMesher.Eng(UndrivenMetalM)}m of metal beside the port " +
                 $"({UndrivenMetalM / (UndrivenMetalM + WidthM):P1} of the feed at the plane) carries " +
                 "NO rooftop and is not driven: the outline crosses those cells obliquely and their " +
                 "shared edge does not sweep them, so a basis there would push current out through " +
                 "the metal's rim. This is the one thing a conformal port does WORSE than a " +
                 "staircased one, and raising Cells per wavelength is what shrinks it: the undriven " +
                 "cells are the outermost of the transverse run, so their share of the width falls as " +
                 "the run gets longer.";

        return s;
    }
}

/// <summary>
/// Port resolution: geometry in, a row of basis indices out — or a worded refusal (R-prt-2). Never a
/// silent snap to something nearby.
/// </summary>
public static class PlanarPorts
{
    /// <summary>Resolves, or throws with the refusal's own wording.</summary>
    public static PlanarPortResolution Resolve(PlanarMesh mesh, PlanarPort port)
    {
        if (!TryResolve(mesh, port, out var res, out string? refusal))
            throw new InvalidOperationException(refusal);
        return res!;
    }

    public static IReadOnlyList<PlanarPortResolution> ResolveAll(
        PlanarMesh mesh, IReadOnlyList<PlanarPort> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var list = new List<PlanarPortResolution>(ports.Count);
        foreach (var p in ports) list.Add(Resolve(mesh, p));
        return list;
    }

    public static bool TryResolve(PlanarMesh mesh, PlanarPort port,
                                  out PlanarPortResolution? resolution, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(port);
        resolution = null;

        // ── RP-2a — a port referenced to DRAWN metal is two cuts, and it has its own routine ─────
        if (port.IsConductorReferenced)
            return TryResolveTwoCut(mesh, port, out resolution, out refusal);

        // ── D3: the plane is still the only reference the MEDIUM can provide ─────────────────────
        if (port.Reference != PlanarPortReference.GroundPlane)
        {
            // L9d/§0.2 item 2 — the one genuinely new port question that phase had to answer.
            refusal = ViaPortRefusal(port.Number);
            return false;
        }

        return TryResolveAgainstPlane(mesh, port, out resolution, out refusal);
    }

    /// <summary>
    /// The ground-referenced resolution — L9d's level inference and everything under it, unchanged.
    /// <b>Split out of <see cref="TryResolve"/> so RP-2a's two terminals can each go through it</b>,
    /// which is R-rp2-2: one resolution rule, used twice, rather than a second one that gets a
    /// chance to disagree with it.
    /// </summary>
    private static bool TryResolveAgainstPlane(PlanarMesh mesh, PlanarPort port,
                                               out PlanarPortResolution? resolution, out string? refusal)
    {
        resolution = null;

        // ── L9d/D2 — WHICH LEVEL, and never silently ─────────────────────────────────────────────
        if (port.LayerIndex is { } explicitLayer)
            return TryResolveOnLayer(mesh, port, explicitLayer, out resolution, out refusal);

        var candidates = new List<int>();
        PlanarPortResolution? first = null;
        string? firstRefusal = null;
        for (int li = 0; li < mesh.LayerNames.Count; li++)
        {
            if (!TryResolveOnLayer(mesh, port, li, out var r, out string? why))
            {
                firstRefusal ??= why;
                continue;
            }
            candidates.Add(li);
            first ??= r;
        }

        if (candidates.Count == 1) { resolution = first; refusal = null; return true; }

        if (candidates.Count == 0)
        {
            // Every level said no. A one-level mesh has exactly one reason, and it is the useful one;
            // a multi-level mesh gets the first level's reason plus the fact that no level worked.
            refusal = mesh.LayerNames.Count <= 1
                ? firstRefusal
                : $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                  $"{SurfaceMesher.Eng(port.Location.Y)}m) does not resolve on ANY of this mesh's " +
                  $"{mesh.LayerNames.Count} conductor levels ({string.Join(", ", mesh.LayerNames)}). " +
                  $"The reason on level 0 was: {firstRefusal}";
            return false;
        }

        // D2 — ambiguous, refused BY NAME. The alternative is to take the lowest (or the topmost)
        // level, which is a coin flip that produces a complete, plausible s-parameter set for a
        // structure the user did not draw.
        refusal =
            $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
            $"{SurfaceMesher.Eng(port.Location.Y)}m) can be cut on " +
            $"{candidates.Count} conductor levels — " +
            string.Join(", ", candidates.Select(i => $"level {i} ('{mesh.LayerNames[i]}')")) +
            ". A port's LEVEL is part of its identity: driving the wrong one drives a different " +
            "conductor with the same footprint, which produces a complete and plausible answer for " +
            "a structure that was not drawn. Say which level this port is on.";
        return false;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // RP-2a — A PORT REFERENCED TO DRAWN METAL: TWO CUTS AT ONE STATION
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Resolves a conductor-referenced port: the signal cut, the return cut, and the four things
    /// that must be true of the pair.</b>
    ///
    /// <para>Both cuts go through <see cref="TryResolveAgainstPlane"/> — the SAME routine, including
    /// L9d's level inference and its ambiguity refusal (R-rp2-2/R-rp2a-3). What this adds is only
    /// the checks that are about the PAIR, and every one of them is a refusal rather than an
    /// adjustment: a skewed pair, a pair on two levels nobody stated, and a pair that is really one
    /// conductor all produce a complete and plausible s-matrix for a structure nobody drew.</para>
    /// </summary>
    private static bool TryResolveTwoCut(PlanarMesh mesh, PlanarPort port,
                                         out PlanarPortResolution? resolution, out string? refusal)
    {
        resolution = null;

        // ── R-rp2a-11 — ONLY AN INTERNAL DELTA GAP, AND THE REFUSAL NAMES WHAT IS MISSING ────────
        if (port.Kind != PlanarPortKind.InternalDeltaGap)
        {
            refusal = port.Kind == PlanarPortKind.Edge
                ? $"Port {port.Number} is an EDGE port asking to return through drawn metal, and " +
                  "that is refused rather than approximated. An edge port has a feed outside its cut, " +
                  "so it has an error box, and the two-line calibration that removes it needs a " +
                  "STANDARD in the port's own reference — a coplanar line, with its own Z_c, its own " +
                  "propagation constant and its own static capacitance. The standards this kernel " +
                  "builds are uniform single conductors over the ground plane, so calibrating this " +
                  "port against them would publish s-parameters that are plausible and referenced to " +
                  "nothing. What is missing is the coplanar calibration standard (§10.6's coplanar " +
                  "de-embedding); until it exists, cut this port as an internal delta gap instead — " +
                  "an interior cut has no feed, no error box and needs no standard, and it is " +
                  "referenced to its own declared Z0 at the gap."
                : $"Port {port.Number} is an internal (via-to-plane) port asking to return through " +
                  "drawn metal. Its negative terminal is the ground plane by construction — that is " +
                  "what the port IS, a gap at the foot of the via that reaches the plane — so there " +
                  "is nothing for a second cut to be. A port between two pieces of drawn metal is an " +
                  "internal delta gap with a return conductor named; a port from metal to the plane " +
                  "is this one, and it takes no reference.";
            return false;
        }

        if (port.NegativeLocation is not { } negPoint)
        {
            refusal =
                $"Port {port.Number} names a {(port.Reference == PlanarPortReference.CoplanarGround ? "coplanar ground" : "second-conductor")} " +
                "reference but does not say WHERE the return conductor is cut. A conductor-referenced " +
                "port is two cuts driven against each other, so it needs a point on the return metal " +
                "as well as one on the signal metal; there is no nearest-conductor search, because " +
                "picking the return silently is picking which loop the answer is about.";
            return false;
        }

        // ── The two terminals, each through the one resolution rule ──────────────────────────────
        if (!TryResolveAgainstPlane(mesh, port with { Reference = PlanarPortReference.GroundPlane },
                                    out var pos, out refusal))
            return false;

        var negProbe = port with
        {
            Reference  = PlanarPortReference.GroundPlane,
            Location   = negPoint,
            LayerIndex = port.NegativeLayerIndex,
        };
        if (!TryResolveAgainstPlane(mesh, negProbe, out var neg, out string? negWhy))
        {
            // The inner refusal is worded for a port, and here it is worded for a TERMINAL — without
            // saying which, a user reading "Port 3 is not on any conductor" has two places to look.
            refusal = $"Port {port.Number}'s RETURN terminal, at " +
                      $"({SurfaceMesher.Eng(negPoint.X)}m, {SurfaceMesher.Eng(negPoint.Y)}m), did not " +
                      $"resolve. {negWhy}";
            return false;
        }

        // ── R-rp2a-3 — ONE LEVEL, unless the caller stated both ──────────────────────────────────
        if (pos!.LayerIndex != neg!.LayerIndex &&
            !(port.LayerIndex.HasValue && port.NegativeLayerIndex.HasValue))
        {
            refusal =
                $"Port {port.Number}'s two cuts were INFERRED onto different levels: the signal cut " +
                $"on level {pos.LayerIndex}{LayerName(mesh, pos.LayerIndex)} and the return cut on " +
                $"level {neg.LayerIndex}{LayerName(mesh, neg.LayerIndex)}. A port's level is part of " +
                "its identity and two terminals is two chances to land on the wrong one silently, so " +
                "a port spanning levels has to be SAID rather than inferred: state the level of both " +
                "terminals if that is what you meant, or move one of the two points onto the level " +
                "the other resolved on.";
            return false;
        }

        // ── R-rp2a-4 — TWO DIFFERENT CONDUCTORS, and the two ways that fails are said apart ──────
        var posSet = new HashSet<int>(pos.BasisIndices);
        bool sharesRow = false;
        foreach (int b in neg.BasisIndices) if (posSet.Contains(b)) { sharesRow = true; break; }

        if (sharesRow)
        {
            refusal =
                $"Port {port.Number}'s two cuts landed on the SAME rooftop row — the same basis " +
                "functions on the same cut of the same metal. Driven against each other those two " +
                "blocks cancel exactly, which is a short across the port, not a port. The two points " +
                "are close enough that they resolved to one cut: move the return point onto the " +
                "return conductor, or refine the mesh if the two conductors are genuinely there and " +
                "the mesh merged them.";
            return false;
        }

        if (pos.LayerIndex == neg.LayerIndex &&
            SameConductor(mesh, pos.LayerIndex, pos.BasisIndices, neg.BasisIndices))
        {
            refusal =
                $"Port {port.Number}'s two cuts are in the SAME conductor — the metal under the " +
                $"return point at ({SurfaceMesher.Eng(negPoint.X)}m, {SurfaceMesher.Eng(negPoint.Y)}m) " +
                $"is continuous with the metal under the signal point at " +
                $"({SurfaceMesher.Eng(port.Location.X)}m, {SurfaceMesher.Eng(port.Location.Y)}m) on " +
                $"level {pos.LayerIndex}{LayerName(mesh, pos.LayerIndex)}. A port between two cuts of " +
                "one conductor is an ordinary internal delta gap wearing a costume: it is already " +
                "what this kernel builds with no reference at all. Name a genuinely separate " +
                "conductor as the return, or drop the reference and cut a single gap.";
            return false;
        }

        // ── R-rp2a-2 — ONE STATION. A skewed pair is a port plus a length of line ────────────────
        //
        // Both cuts snap to gridlines of the SAME grid along the same axis, so two cuts genuinely at
        // one station agree to the bit; the tolerance below is insurance against a coordinate that
        // arrived by arithmetic rather than off the grid, scaled to the mesh's own extent.
        bool alongX = pos.Direction == PlanarBasisDirection.X;
        var  gLong  = alongX ? mesh.GridX : mesh.GridY;
        double span = gLong[^1] - gLong[0];
        double skew = Math.Abs(pos.ReferencePlaneM - neg.ReferencePlaneM);

        if (skew > 1e-12 * Math.Max(span, 1e-12))
        {
            string ax = alongX ? "x" : "y";
            refusal =
                $"Port {port.Number}'s two cuts are at different stations: the signal cut at " +
                $"{ax} = {SurfaceMesher.Eng(pos.ReferencePlaneM)}m and the return cut at " +
                $"{ax} = {SurfaceMesher.Eng(neg.ReferencePlaneM)}m, {SurfaceMesher.Eng(skew)}m apart. " +
                "That is not one port: it is a port plus that length of line, and it solves to a " +
                "complete and plausible s-matrix for a structure nobody drew. Place both points at " +
                "the same station — each cut snaps to the nearest mesh gridline, so two points on " +
                $"one {ax} take the same gridline unless the mesh offered one conductor a cut there " +
                "and not the other, which refining the mesh fixes.";
            return false;
        }

        resolution = pos with
        {
            Reference = port.Reference,
            Negative  = PlanarPortTerminal.From(neg),
        };
        refusal = null;
        return true;
    }

    /// <summary>
    /// <b>Are these two cuts in one piece of metal?</b> A 4-connectivity walk over the cells of one
    /// level, from the positive cut's own cells, asking whether the negative cut's cells are reached.
    ///
    /// <para>It is the mesh's connectivity rather than the artwork's on purpose: what the solve
    /// drives is cells, and two polygons the mesher merged are one conductor as far as the answer is
    /// concerned. <b>It is also IN-PLANE only</b> — two conductors joined by a via somewhere else are
    /// not caught here, and are not claimed to be: that is a short in the structure, which the solve
    /// reports as one, rather than a port that resolved to the wrong thing.</para>
    /// </summary>
    private static bool SameConductor(PlanarMesh mesh, int layerIndex,
                                      IReadOnlyList<int> fromBases, IReadOnlyList<int> toBases)
    {
        int nx = mesh.GridX.Count - 1, ny = mesh.GridY.Count - 1;
        if (nx < 1 || ny < 1) return false;

        var at = new int[nx * ny];
        Array.Fill(at, -1);
        for (int c = 0; c < mesh.Cells.Count; c++)
        {
            var cell = mesh.Cells[c];
            if (cell.LayerIndex == layerIndex) at[cell.IY * nx + cell.IX] = c;
        }

        var target = new HashSet<int>();
        foreach (int b in toBases)
        {
            target.Add(mesh.Bases[b].CellA);
            target.Add(mesh.Bases[b].CellB);
        }

        var seen  = new bool[nx * ny];
        var stack = new Stack<int>();
        void Push(int cellIndex)
        {
            var c = mesh.Cells[cellIndex];
            if (c.LayerIndex != layerIndex) return;
            int k = c.IY * nx + c.IX;
            if (seen[k]) return;
            seen[k] = true;
            stack.Push(k);
        }

        foreach (int b in fromBases) { Push(mesh.Bases[b].CellA); Push(mesh.Bases[b].CellB); }

        while (stack.Count > 0)
        {
            int k  = stack.Pop();
            int cx = k % nx, cy = k / nx;
            if (at[k] >= 0 && target.Contains(at[k])) return true;

            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int qx = cx + dx, qy = cy + dy;
                if (qx < 0 || qx >= nx || qy < 0 || qy >= ny) continue;
                int q = qy * nx + qx;
                if (seen[q] || at[q] < 0) continue;
                seen[q] = true;
                stack.Push(q);
            }
        }
        return false;
    }

    /// <summary>
    /// <b>§0.2 item 2's answer, worded once.</b> A port ON A VIA is refused, and the argument is
    /// that it is a different OBJECT rather than an unimplemented case.
    ///
    /// <para>L8d's D1 makes a port an incidence matrix over a row of rooftops whose shared edge IS
    /// the reference plane, with the half-cell beyond it — the part the calibration removes — as the
    /// delta gap's other terminal. A vertical basis has no analogue of any of that: its unit current
    /// already crosses its shared footprint, its "cut" is the via itself, and there is no cell beyond
    /// the cut to reference against because a via has no end in the layout plane. Driving the
    /// horizontal rooftops that happen to sit at the same (x, y) instead is a perfectly good
    /// port — it is simply a different one, and taking it silently is the substitution this refuses.
    /// The vertical unknowns are the tail of the unknown vector (R-via-5) and are never in any
    /// port's row, which is asserted structurally rather than assumed.</para>
    ///
    /// <para>A port genuinely BETWEEN two levels is an INTERNAL port — §10.6 lists co-simulation
    /// ports as later work and they are explicitly not L9's.</para>
    /// </summary>
    internal static string ViaPortRefusal(int portNumber) =>
        $"Port {portNumber} asks to be driven BETWEEN two levels at a via. That is not the port this " +
        "kernel builds: a port is a delta gap across the shared edge of the two outermost cells of a " +
        "conductor END, and it drives the horizontal rooftops across that cut. A via basis has no " +
        "end in the layout plane, its unit current already crosses its shared footprint, and there " +
        "is no cell beyond the cut to reference against — so there is nothing for a delta gap to " +
        "act across. Driving the horizontal metal at the same (x, y) instead is a legitimate port " +
        "and is what a GroundPlane-referenced port there already does, but it is a DIFFERENT port " +
        "and is not substituted silently. A port truly between two levels is an internal " +
        "(co-simulation) port; §10.6 lists those as later work, and nothing in this repository provides one.";

    /// <summary>
    /// <b>AN INTERNAL VIA PORT: the gap is at the foot of the via under the label, not anywhere in the
    /// plane.</b> Everything the two in-plane kinds share — a transverse run of rooftops, a
    /// longitudinal cut, a reference plane coordinate — is about current that runs ALONG the metal,
    /// and none of it applies to current that leaves it. What this resolves to instead is the
    /// ground-attachment bases of one via footprint (<c>PlanarBasisFunctions</c>' header), which is
    /// the same incidence row and the same <c>Y = BᵀZ⁻¹B</c> one dimension over.
    ///
    /// <para><b>Every attachment cell of that via is driven, not just the one under the label.</b>
    /// A via's footprint is one conductor at one potential, exactly as a wide feed's transverse row
    /// is: driving a single cell of it would leave the rest of the footprint shorting the trace
    /// straight to the plane beside the port, which is a complete and plausible answer for a
    /// structure with a short across it. The footprint is walked by 4-connectivity from the cell the
    /// label is in, over cells that carry an attachment basis on this level.</para>
    /// </summary>
    private static bool TryResolveInternal(PlanarMesh mesh, PlanarPort port, int layerIndex,
                                        out PlanarPortResolution? resolution, out string? refusal)
    {
        resolution = null;

        int nx = mesh.GridX.Count - 1, ny = mesh.GridY.Count - 1;
        if (nx < 1 || ny < 1)
        {
            refusal = $"Port {port.Number} cannot be placed: the mesh has no cells to place it on.";
            return false;
        }

        // The attachment bases of THIS level, indexed by their one meshed foot cell. One forward
        // pass over Bases, queried and never iterated — R-msh-2 as everywhere else in this file.
        var attachAt = new int[nx * ny];
        Array.Fill(attachAt, -1);
        int attachments = 0;
        for (int b = 0; b < mesh.Bases.Count; b++)
        {
            var bs = mesh.Bases[b];
            if (!bs.AttachesToGround || bs.LayerIndex != layerIndex) continue;
            var cell = mesh.Cells[bs.CellB];
            attachAt[cell.IY * nx + cell.IX] = b;
            attachments++;
        }

        // ── IS THE LABEL ON THE ARTWORK AT ALL? ──────────────────────────────────────────────────
        //
        // Asked of both axes and of the grid's own extent, for the reason the internal gap asks it:
        // IndexOf CLAMPS, so a point metres away from the board would land on the outermost cell and
        // find whatever is there. An edge port may legitimately sit just off the end face it names;
        // an internal port names a via it is standing on.
        bool inside = port.Location.X >= mesh.GridX[0] - 1e-15 && port.Location.X <= mesh.GridX[^1] + 1e-15
                   && port.Location.Y >= mesh.GridY[0] - 1e-15 && port.Location.Y <= mesh.GridY[^1] + 1e-15;
        int ix = IndexOf(mesh.GridX, port.Location.X), iy = IndexOf(mesh.GridY, port.Location.Y);

        if (!inside || ix < 0 || iy < 0 || attachAt[iy * nx + ix] < 0)
        {
            // The two ways this fails are genuinely different and a user can act on only one of
            // them at a time, so they are worded apart: nothing on this level goes to ground at
            // all, versus a via that is there but is not under the label.
            refusal = attachments == 0
                ? $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                  $"{SurfaceMesher.Eng(port.Location.Y)}m) is an internal port, and nothing on layer " +
                  $"{layerIndex}{LayerName(mesh, layerIndex)} connects to the ground plane. An internal " +
                  "internal port drives the current that leaves the metal DOWNWARD, so its path is a via to " +
                  "the plane and the port is the gap at that via's foot — with no via there is no " +
                  "path and nothing to drive. Draw a via to the ground plane where the grounded element " +
                  "attaches and put the port on it, or make this an edge or internal delta-gap port, " +
                  "which drive current along the metal instead."
                : $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                  $"{SurfaceMesher.Eng(port.Location.Y)}m) is an internal port and there is no via to the " +
                  $"ground plane under it. This level does carry {attachments} meshed via cell(s), so " +
                  "the port is beside its via rather than on it — an internal port drives the via it " +
                  "stands on, and moving it to the nearest one silently would drive a path the user " +
                  "did not point at. Move the label onto the via's own footprint, or raise Cells per " +
                  "wavelength if the footprint is too small to have survived meshing where you placed it.";
            return false;
        }

        // ── The whole footprint, by 4-connectivity ───────────────────────────────────────────────
        var stack   = new Stack<(int X, int Y)>();
        var claimed = new bool[nx * ny];
        stack.Push((ix, iy));
        claimed[iy * nx + ix] = true;

        var indices = new List<int>();
        double area = 0;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            int bi = attachAt[cy * nx + cx];
            indices.Add(bi);
            area += mesh.Cells[mesh.Bases[bi].CellB].Area;

            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int qx = cx + dx, qy = cy + dy;
                if (qx < 0 || qx >= nx || qy < 0 || qy >= ny) continue;
                if (claimed[qy * nx + qx] || attachAt[qy * nx + qx] < 0) continue;
                claimed[qy * nx + qx] = true;
                stack.Push((qx, qy));
            }
        }

        // Ascending basis order, so the row is the mesh's own order regardless of which cell the
        // label happened to land in — the same determinism an edge port gets from marching its run.
        indices.Sort();

        resolution = new PlanarPortResolution(
            Number:            port.Number,
            Side:              port.Side,
            Direction:         PlanarBasisDirection.Z,
            Z0:                port.Z0,
            BasisIndices:      indices,
            IncidenceSign:     port.IncidenceSign,
            // An internal port has no in-plane width, reference plane or outer edge: its current crosses
            // the via's FOOTPRINT rather than a line across the metal, and the area that replaces
            // them is reported in its own field rather than smuggled into a length.
            WidthM:            0,
            ReferencePlaneM:   0,
            OuterEdgeM:        0,
            TransverseLines:   [],
            LongitudinalRunM:  [],
            LayerIndex:        layerIndex,
            Kind:              PlanarPortKind.Internal,
            FootprintAreaM2:   area);

        refusal = null;
        return true;
    }

    private static bool TryResolveOnLayer(PlanarMesh mesh, PlanarPort port, int layerIndex,
                                          out PlanarPortResolution? resolution, out string? refusal)
    {
        resolution = null;
        if (port.Kind == PlanarPortKind.Internal)
            return TryResolveInternal(mesh, port, layerIndex, out resolution, out refusal);


        bool alongX = port.Direction == PlanarBasisDirection.X;
        var  gLong  = alongX ? mesh.GridX : mesh.GridY;   // along the current
        var  gTran  = alongX ? mesh.GridY : mesh.GridX;   // across it
        int  nLong  = gLong.Count - 1, nTran = gTran.Count - 1;

        if (nLong < 1 || nTran < 1)
        {
            refusal = $"Port {port.Number} cannot be placed: the mesh has no cells to place it on.";
            return false;
        }

        // ── (ix, iy) → cell index, for this layer only. A plain int[], as R-msh-2 requires. ──────
        int nx = mesh.GridX.Count - 1, ny = mesh.GridY.Count - 1;
        var at = new int[nx * ny];
        Array.Fill(at, -1);
        for (int c = 0; c < mesh.Cells.Count; c++)
        {
            var cell = mesh.Cells[c];
            if (cell.LayerIndex == layerIndex) at[cell.IY * nx + cell.IX] = c;
        }

        int CellAt(int iLong, int iTran) =>
            alongX ? at[iTran * nx + iLong] : at[iLong * nx + iTran];

        // ── The transverse index the port's own point falls in ───────────────────────────────────
        double tCoord = alongX ? port.Location.Y : port.Location.X;
        int    seedT  = IndexOf(gTran, tCoord);
        if (seedT < 0)
        {
            refusal = $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                      $"{SurfaceMesher.Eng(port.Location.Y)}m) is outside the meshed region entirely.";
            return false;
        }

        // ── Basis lookup. Built by ONE forward pass over Bases; queried, never iterated. ─────────
        //
        // Hoisted above the column search (it used to sit below it) because an INTERNAL port's cut is
        // chosen by asking which candidate gridlines actually carry a rooftop — the search needs the
        // lookup, not the other way round. An edge port's search is unchanged and reaches exactly the
        // same columns it always did.
        var byPair = new Dictionary<(int A, int B, PlanarBasisDirection D), int>(mesh.Bases.Count);
        for (int b = 0; b < mesh.Bases.Count; b++)
        {
            var bs = mesh.Bases[b];
            byPair[(bs.CellA, bs.CellB, bs.Direction)] = b;
        }

        // Is the cell pair (lowC, lowC+1) at transverse index t paired into a rooftop?
        bool PairAt(int lowC, int t)
        {
            if (lowC < 0 || lowC + 1 >= nLong) return false;
            int a = CellAt(lowC, t), b = CellAt(lowC + 1, t);
            return a >= 0 && b >= 0 && byPair.ContainsKey((a, b, port.Direction));
        }

        bool fromLow = port.Side is PlanarPortSide.MinX or PlanarPortSide.MinY;
        bool internalGap = port.Kind == PlanarPortKind.InternalDeltaGap;

        int lowCol, highCol, outer;
        double gapOffset = 0;

        if (!internalGap)
        {
            // ── March in from the named side to the RUN OF METAL THE PORT IS ON (D2) ────────────
            //
            // This used to march from the mesh's own edge and stop at the FIRST metal it met,
            // reading only the port's transverse coordinate — "the transverse coordinate is all that
            // is read", as the note above still says for the clamp. That is right while the port's
            // row crosses exactly ONE run of metal, which is every uniform feed and every test
            // fixture, and it is why it stood so long.
            //
            // **It is silently wrong the moment the row crosses two.** Owner report, 2026-09-09: a
            // port placed on the wall of a NOTCH in a connector cutout resolved to
            // side = MaxX, plane = 109.6192 mm — the far edge of a polygon 5.7 mm away, and the
            // SAME plane the other port had already claimed. Both ports drove one edge: a complete,
            // plausible two-port answer for a structure nobody drew, with no refusal and nothing on
            // screen to say so. A row through a slot, a gap, or two separate conductors on one layer
            // is the same shape of error.
            //
            // The port's LONGITUDINAL coordinate is what disambiguates, and it costs nothing to read.
            // The run CONTAINING it wins; failing that the nearest one, which is what preserves the
            // documented allowance that "its label may legitimately sit just off the end face it
            // names" — a label beyond the metal still lands on the run it is beyond, exactly as the
            // old march did. Single-run rows resolve cell for cell as before.
            double lCoordEdge = alongX ? port.Location.X : port.Location.Y;

            outer = -1;
            double bestRunD = double.PositiveInfinity;
            for (int c = 0; c < nLong; )
            {
                if (CellAt(c, seedT) < 0) { c++; continue; }

                int runLo = c;
                while (c + 1 < nLong && CellAt(c + 1, seedT) >= 0) c++;
                int runHi = c++;

                double a = gLong[runLo], b = gLong[runHi + 1];
                double d = lCoordEdge < a ? a - lCoordEdge : lCoordEdge > b ? lCoordEdge - b : 0;
                if (d >= bestRunD) continue;

                bestRunD = d;
                outer    = fromLow ? runLo : runHi;
            }

            if (outer < 0)
            {
                refusal = $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                          $"{SurfaceMesher.Eng(port.Location.Y)}m) does not lie on any conductor on layer " +
                          $"{layerIndex}{LayerName(mesh, layerIndex)} — nothing was meshed along that " +
                          "line. Move the port onto the " +
                          "metal, or check that the conductor survived meshing at this cell size.";
                return false;
            }

            int inner = fromLow ? outer + 1 : outer - 1;
            if (inner < 0 || inner >= nLong || CellAt(inner, seedT) < 0)
            {
                refusal = $"Port {port.Number}'s conductor is only one cell long in the direction current " +
                          "would flow, so there is no rooftop basis function to drive — a port needs a pair " +
                          "of adjacent cells. Lengthen the feed line, or raise Cells per wavelength so the " +
                          "feed is meshed into more than one cell.";
                return false;
            }

            lowCol  = Math.Min(outer, inner);
            highCol = Math.Max(outer, inner);
        }
        else
        {
            // ── AN INTERNAL DELTA GAP: THE CUT IS THE GRIDLINE NEAREST THE PLACED POINT ──────────
            //
            // Both of the label's coordinates matter here, which is the one place this port differs
            // from an edge port before the solve. The transverse one picks the conductor run exactly
            // as it always did; the LONGITUDINAL one picks which of that run's interior gridlines the
            // gap is cut on — and the only cuts on offer are the mesh's own, so the nearest usable
            // one wins and how far it moved is reported (GapOffsetM). Snapping silently would hide a
            // displacement bounded by half a cell, and half a cell is a quantity the user sets.
            //
            // "Usable" means the pair either side is metal AND is paired into a rooftop, which is
            // what makes this an INTERIOR cut rather than an end: at a conductor's end face one side
            // is empty, so that gridline is never a candidate and the refusal below can say so
            // truthfully rather than guessing at the user's intent.
            double lCoord = alongX ? port.Location.X : port.Location.Y;

            // ── THE INDEX LOOKUP CLAMPS, SO "IS IT ON THE METAL?" NEEDS THE EXTENT TOO ──────────
            //
            // IndexOf returns the nearest cell for a coordinate outside the grid rather than a
            // miss, which is right for an EDGE port (its label may legitimately sit just off the end
            // face it names, and the transverse coordinate is all that is read). For an internal
            // port it is not: a point metres away from the artwork would clamp onto the outermost
            // row, find metal there, and cut a gap the user never asked for — a complete, plausible
            // answer for a structure nobody drew. Asked of both axes, because a gap is placed by
            // both of its coordinates.
            bool inside = tCoord >= gTran[0] - 1e-15 && tCoord <= gTran[^1] + 1e-15
                       && lCoord >= gLong[0] - 1e-15 && lCoord <= gLong[^1] + 1e-15;

            if (!inside || CellAt(IndexOf(gLong, lCoord), seedT) < 0)
            {
                refusal = $"Port {port.Number} at ({SurfaceMesher.Eng(port.Location.X)}m, " +
                          $"{SurfaceMesher.Eng(port.Location.Y)}m) is an internal delta-gap port, and " +
                          $"it is not ON any conductor on layer {layerIndex}{LayerName(mesh, layerIndex)}. " +
                          "An internal port cuts a gap in metal, so it has to be placed on the metal it " +
                          "cuts — unlike an edge port, whose label may sit just off the end face it names.";
                return false;
            }

            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int c = 0; c < nLong - 1; c++)          // the cut between cells c and c+1
            {
                if (!PairAt(c, seedT)) continue;
                double d = Math.Abs(gLong[c + 1] - lCoord);
                if (d < bestD) { bestD = d; best = c; }
            }

            if (best < 0)
            {
                refusal = $"Port {port.Number} is an internal delta-gap port, and the conductor under it " +
                          "has no interior cut to gap: along the direction its current flows, no two " +
                          "adjacent cells there are both metal and paired into a rooftop. An internal " +
                          "gap needs metal on BOTH sides — at a conductor's END there is metal on one " +
                          "side only, and that is an edge port, not this. Move the port into the middle " +
                          "of the conductor, raise Cells per wavelength so the run is meshed into more " +
                          "than one cell, or change this port to an edge port if the end is what you meant.";
                return false;
            }

            lowCol    = best;
            highCol   = best + 1;
            outer     = best;                 // no metal is outside an interior cut; see below
            gapOffset = bestD;
        }

        double sharedCoord = gLong[highCol];

        // ── §4 — A PORT ON A CUT CELL: THE RUN IS THE ROOFTOPS THAT EXIST, NOT THE METAL ─────────
        //
        // This used to be a blanket refusal, on the argument that "a port belongs on a drawn feed,
        // which is Manhattan, so this should never fire". **That premise is false of the parts a user
        // actually selects.** A taper's flanks are oblique from its very first cell, so on MKlopf and
        // MTaper the outermost cell of the port's transverse run is cut and the whole port was
        // refused for it — which made Boundary cells = Conformal unusable on the one part whose value
        // is a controlled ripple.
        //
        // What a cut at the port genuinely costs is TWO different things, and only the first is a
        // matter of bookkeeping:
        //
        //   (1) the reference plane's metal is shorter than the gridline, so the port's WIDTH is not
        //       the grid extent. That is measured below and carried into the calibration standard
        //       (D4 builds the standard from TransverseLines, and those are now the metal's own
        //       extents), so the error box is the same object again — the property the refusal was
        //       protecting.
        //
        //   (2) R-cut-4 can decline the outermost rooftop outright. Its Anchored test is all-or-
        //       nothing over the strips, and a shallow oblique rim leaves a sliver strip at the top
        //       of the cell whose metal does not reach the shared face — so the whole basis goes,
        //       even though the strip is under a percent of the cell. **That is a real limitation and
        //       it is NOT worked around here**: the run simply stops at the last cell pair that
        //       carries a rooftop, and how much metal that leaves undriven is reported. Accepting
        //       those bases instead would retire an EXACT property (L8c's ∫f·û dℓ = 1 A) for an
        //       approximate one, which needs its own measurement and its own brief.
        //
        // Under the staircase every metal-bearing pair is paired, so this scan reproduces the old one
        // cell for cell and every pre-conformal port is bit-identical.
        // ── AN EDGE PORT'S RUN IS THE END FACE, AND AN END IS WHERE THE METAL STOPS ─────────────
        //
        // Owner report, 2026-09-09: a port on the wall of a notch was drawn 0.853 mm wide (the wall)
        // and driven 1.213 mm (the wall PLUS the metal that carries on past it toward the feed).
        // The transverse walk asked only "is there a rooftop straddling the plane here", and at a
        // notch the answer stays yes well past the end of the edge: the metal above the wall is
        // continuous through the plane because the conductor turns and keeps going.
        //
        // Driving those rooftops is not an edge feed. Current there is injected in the MIDDLE of
        // unbroken metal — a delta gap, not an end — so the structure solved is not the one drawn,
        // and the marker and the excitation disagreed about the port's own width.
        //
        // The test is one lookup: the cell just OUTSIDE the face must be empty. On a uniform feed it
        // is empty across the whole width and every pre-existing port resolves cell for cell as
        // before; it can never fail at the seed, because `outer` is by construction the outermost
        // metal column of the seed's own run.
        //
        // It narrows the METAL walk below by the same test, deliberately, rather than letting the
        // difference fall into UndrivenMetalM: that field means "metal at the plane a conformal cell
        // declined to pair", and it carries that explanation in words. Metal that never ended here is
        // not undriven — it is not this port's cross-section at all.
        int outside = fromLow ? outer - 1 : outer + 1;
        bool EndsHere(int t) =>
            internalGap || outside < 0 || outside >= nLong || CellAt(outside, t) < 0;

        bool HasBasis(int t)
        {
            int a = CellAt(lowCol, t), b = CellAt(highCol, t);
            return a >= 0 && b >= 0 && byPair.ContainsKey((a, b, port.Direction)) && EndsHere(t);
        }

        if (!HasBasis(seedT))
        {
            refusal =
                $"Port {port.Number} sits on a cell pair the mesher did not pair into a rooftop, so " +
                "there is nothing at the port's own location for it to drive. With conformal boundary " +
                "cells that means the metal there follows an oblique outline and is not swept by the " +
                "reference plane — a rooftop across it would push its current out through the " +
                "conductor's rim instead. Move the port onto a straight, axis-aligned feed, raise " +
                "Cells per wavelength, or set Boundary cells back to \"Staircase\" for this run.";
            return false;
        }

        // The contiguous transverse run of ROOFTOPS at that column…
        int lo = seedT, hi = seedT;
        while (lo - 1 >= 0    && HasBasis(lo - 1)) lo--;
        while (hi + 1 < nTran && HasBasis(hi + 1)) hi++;

        // …and, separately, how far the METAL runs, so the note can say what was left out.
        int mLo = lo, mHi = hi;
        while (mLo - 1 >= 0    && CellAt(lowCol, mLo - 1) >= 0 && CellAt(highCol, mLo - 1) >= 0 && EndsHere(mLo - 1)) mLo--;
        while (mHi + 1 < nTran && CellAt(lowCol, mHi + 1) >= 0 && CellAt(highCol, mHi + 1) >= 0 && EndsHere(mHi + 1)) mHi++;

        double PlaneMetal(int t)
        {
            var ca = mesh.Cells[CellAt(lowCol, t)];
            var cb = mesh.Cells[CellAt(highCol, t)];
            // Both cells share that face, so the two lengths agree for a sound pair; the smaller is
            // what a pair only one side reaches would report.
            return Math.Min(
                RooftopSupport.Build(ca, port.Direction, sharedIsHigh: true,  sharedCoord).SharedFaceLength,
                RooftopSupport.Build(cb, port.Direction, sharedIsHigh: false, sharedCoord).SharedFaceLength);
        }

        int cutCells = 0;
        var metal    = new double[hi - lo + 1];
        for (int t = lo; t <= hi; t++)
        {
            if (mesh.Cells[CellAt(lowCol, t)].IsCut)  cutCells++;
            if (mesh.Cells[CellAt(highCol, t)].IsCut) cutCells++;
            metal[t - lo] = PlaneMetal(t);
        }

        double undriven = 0;
        for (int t = mLo; t < lo; t++) undriven += PlaneMetal(t);
        for (int t = hi + 1; t <= mHi; t++) undriven += PlaneMetal(t);

        var indices = new List<int>(hi - lo + 1);
        for (int t = lo; t <= hi; t++)
            indices.Add(byPair[(CellAt(lowCol, t), CellAt(highCol, t), port.Direction)]);

        // ── The geometry the report and the calibration both need ────────────────────────────────
        //
        // For an INTERNAL gap the plane IS the cut and there is no metal outside it, so the "outer
        // edge" is the plane itself. That is not a placeholder: OuterEdgeM means "where the metal
        // this port drives stops", and for an interior cut it stops at the cut. Every consumer of it
        // — the feed-clearance scan, the automatic lead, the peel — is an edge-port path and asks
        // only about edge ports (PlanarSolve gates on IsDeembeddable), so reporting the honest number
        // here cannot be mistaken for a feed of length zero.
        double gridWidth = gTran[hi + 1] - gTran[lo];
        double plane     = internalGap ? sharedCoord : fromLow ? gLong[outer + 1] : gLong[outer];
        double edge      = internalGap ? sharedCoord : fromLow ? gLong[outer]     : gLong[outer + 1];

        // ── The width, and the MEASUREMENT that says the two branches below are not both live ────
        //
        // The port's width is the metal ON the reference plane, which is the honest reading of what
        // WidthM has always documented ("not the drawn width, so a staircased edge reports what was
        // actually meshed"). **On real geometry that equals the grid extent even when the port's
        // cells are cut, and the reason is structural rather than lucky:** the face is short only
        // where the cell's metal is absent over a transverse band, and for a monotone rim the same
        // band makes one of the two halves unanchored — so R-cut-4 has already refused that pair and
        // it is not in the run. Measured on the slanted-end fixture in ConformalPortTests: 7 cut
        // cells in the port's run, face metal equal to the grid extent to the last bit.
        //
        // So the branch is taken on the DIFFERENCE rather than on "is anything cut", and the verbatim
        // path — L8d's own arithmetic, one subtraction for the width and the gridlines copied as they
        // are — is what a staircased port AND an ordinary conformal port both take. R-prt-5 asserts
        // the standard's coordinates as an EQUALITY, and rebuilding them from a running sum would
        // move them in the last bit for no reason.
        double metalWidth = 0;
        foreach (double m in metal) metalWidth += m;

        double width;
        var tLines = new double[hi - lo + 2];
        if (Math.Abs(metalWidth - gridWidth) <= 1e-12 * gridWidth)
        {
            width = gridWidth;
            for (int t = lo; t <= hi + 1; t++) tLines[t - lo] = gTran[t];
        }
        else
        {
            // The port's own CROSS-SECTION: one line per basis, spaced by the metal that basis
            // actually has on the reference plane, so D4's standard is a uniform rectangle OF THAT
            // cross-section and the error box stays the same object. Centred on the grid run, because
            // these lines are also what the mesh overlay draws the reference plane from.
            width     = metalWidth;
            tLines[0] = 0.5 * (gTran[lo] + gTran[hi + 1]) - 0.5 * width;
            for (int k = 0; k < metal.Length; k++) tLines[k + 1] = tLines[k] + metal[k];
        }

        // The cells the port's own current marches through, outermost first. For an edge port that
        // is the feed, and D4 copies the first K of them into the calibration standard. An internal
        // gap has no feed and no standard: the two cells its rooftop spans are the whole of it, given
        // upstream-first so BulkCellM still means "the cell this port's gap is discretised at".
        var run = new List<double>();
        if (internalGap)
        {
            int up = fromLow ? lowCol : highCol, down = fromLow ? highCol : lowCol;
            run.Add(gLong[up   + 1] - gLong[up]);
            run.Add(gLong[down + 1] - gLong[down]);
        }
        else
        {
            for (int k = 0; ; k++)
            {
                int i = fromLow ? outer + k : outer - k;
                if (i < 0 || i >= nLong || CellAt(i, seedT) < 0) break;
                run.Add(gLong[i + 1] - gLong[i]);
            }
        }

        resolution = new PlanarPortResolution(
            Number:            port.Number,
            Side:              port.Side,
            Direction:         port.Direction,
            Z0:                port.Z0,
            BasisIndices:      indices,
            IncidenceSign:     port.IncidenceSign,
            WidthM:            width,
            ReferencePlaneM:   plane,
            OuterEdgeM:        edge,
            TransverseLines:   tLines,
            LongitudinalRunM:  run,
            LayerIndex:        layerIndex,
            CutCellCount:      cutCells,
            GridWidthM:        gridWidth,
            UndrivenMetalM:    undriven,
            Kind:              port.Kind,
            GapOffsetM:        gapOffset);

        refusal = null;
        return true;
    }

    /// <summary>
    /// R-prt-3 — the feed must be uniform and isolated for the distance the calibration replaces.
    /// Returns a WARNING, not a refusal: a user may knowingly accept a crowded feed, and the number
    /// they need in order to decide is the measured clearance, not a yes/no.
    /// </summary>
    public static string? CheckFeedClearance(PlanarMesh mesh, PlanarPortResolution port,
                                             double requiredM)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(port);
        if (!(requiredM > 0)) return null;

        // An internal delta gap has no feed and no calibration standard, so there is no length of
        // line this warning could be about. Saying nothing is the answer; warning about a neighbour
        // that is not being replaced by anything would be noise the user cannot act on.
        if (!port.IsDeembeddable) return null;

        bool alongX = port.Direction == PlanarBasisDirection.X;
        bool fromLow = port.Side is PlanarPortSide.MinX or PlanarPortSide.MinY;

        double tLo = port.TransverseLines[0], tHi = port.TransverseLines[^1];
        double nearest = double.PositiveInfinity;

        foreach (var c in mesh.Cells)
        {
            double t0 = alongX ? c.YMin : c.XMin;
            double t1 = alongX ? c.YMax : c.XMax;
            if (t1 > tLo + 1e-15 && t0 < tHi - 1e-15) continue;   // inside the feed's own width

            // ── IS THIS CELL IN THE FEED REGION AT ALL? (fixed 2026-08-12) ──────────────────────
            //
            // `along` used to be measured to the cell's NEAR edge and then used only to skip cells
            // BEHIND the port — there was no upper bound at all, so the scan ran to the far end of
            // the structure and `nearest` came back as the smallest lateral gap ANYWHERE on the
            // board. That is not the quantity this warning's own text describes ("inside the
            // {required}m the calibration standard assumes is empty"), and the difference is not
            // cosmetic: it fired on every part that is ever wider than its port — every taper, stub
            // and tee — including feeds that are demonstrably clean. A warning that cannot be
            // cleared is one users learn to skip, and this is the one that has to stay readable,
            // because it is what R-fed-1's automatic lead CANNOT fix: a lead lengthens a feed, it
            // cannot move a neighbour sideways.
            //
            // The station is the cell's MIDPOINT, not its near edge, and that is load-bearing rather
            // than tidy. R-fed-1 sizes the lead so the feed is uniform for EXACTLY `requiredM`, so
            // the DUT's own flare always begins at the region's far boundary and the cell straddling
            // it always has a lateral gap of zero. On a near-edge test that cell re-fires the warning
            // on every extended taper — reintroducing the unclearable warning one line below the fix
            // for it. A cell is judged by where most of it sits.
            double l0 = alongX ? c.XMin : c.YMin;
            double l1 = alongX ? c.XMax : c.YMax;
            double mid = 0.5 * (l0 + l1);
            double along = fromLow ? mid - port.OuterEdgeM : port.OuterEdgeM - mid;
            if (along < -requiredM || along > requiredM) continue;

            double across = t0 >= tHi ? t0 - tHi : tLo - t1;
            nearest = Math.Min(nearest, Math.Max(across, 0));
        }

        if (double.IsInfinity(nearest) || nearest >= requiredM) return null;

        return $"Port {port.Number}'s feed has other metal {SurfaceMesher.Eng(nearest)}m away, inside " +
               $"the {SurfaceMesher.Eng(requiredM)}m the calibration standard assumes is empty. The " +
               "de-embedding replaces the port's neighbourhood with an isolated line of the same " +
               "width, so whatever is closer than that is not removed correctly. Move the feed away, " +
               "or read the result knowing this.";
    }

    private static string LayerName(PlanarMesh mesh, int layerIndex)
        => layerIndex >= 0 && layerIndex < mesh.LayerNames.Count
            ? $" ('{mesh.LayerNames[layerIndex]}')"
            : "";

    private static int IndexOf(IReadOnlyList<double> grid, double v)
    {
        int n = grid.Count - 1;
        if (n <= 0) return -1;
        if (v <= grid[0])  return 0;
        if (v >= grid[^1]) return n - 1;
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (grid[mid] <= v) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}
