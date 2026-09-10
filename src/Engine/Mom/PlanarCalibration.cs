// L8d — D4: THE CALIBRATION STANDARD IS CONSTRUCTED FROM THE DUT'S OWN MESH, NOT RE-MESHED.
//
// The two-line calibration is exact only insofar as the error box is the SAME OBJECT in the DUT and
// in the standard. That is not a tolerance, it is a construction — and a standard that is "the same
// line, re-meshed" is not the same error box, because L8b's grid spacing is derived from the WHOLE
// problem's narrowness per axis, so a bare rectangle and the feed of a bend do not get the same
// cells. The difference then shows up as a de-embedding residual that reads exactly like a
// convergence problem.
//
// So the standard is built here, cell by cell, from three things the port resolution already
// carries: the DUT's transverse gridlines across the port (verbatim), the DUT's own longitudinal
// cell run for the first K cells inward (verbatim, mirrored at the far end), and the DUT's bulk cell
// size to fill the middle. R-prt-5 asserts the result on COORDINATES, as an equality.
//
// Three consequences, all of them the point:
//   • the port's cell neighbourhood is identical, so the error box is the same object;
//   • SurfaceMesher is not touched — L8c's out-of-scope list keeps it closed and nothing here needs
//     it opened;
//   • R-msh-2's (LayerIndex, IY, IX) ordering contract is honoured by construction, because the
//     builder emits cells in exactly that order.
//
// The limitation this leaves, stated rather than discovered: the DUT's feed may have other metal
// near it that the standard does not. That is inherent to any two-line calibration — it is true of
// real TRL as well — and it is why PlanarPorts.CheckFeedClearance exists (R-prt-3) and why R-prt-4's
// feed-length study is the measurement that says how much clearance is enough.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// D5 — γ COMES FROM A 2×2 EIGENVALUE, IN CLOSED FORM
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// With T₁, T₂ the wave-cascade matrices of the two standards' raw S,
//
//     M = T₂ T₁⁻¹ = T_A · diag(e^{−γΔℓ}, e^{+γΔℓ}) · T_A⁻¹
//
// so e^{∓γΔℓ} are M's eigenvalues. And it collapses further than "the quadratic formula": a
// RECIPROCAL 2-port has det T = S₁₂/S₂₁ = 1, so det M = 1, so the two eigenvalues multiply to 1 and
//
//     cosh(γΔℓ) = ½·tr(M)
//
// EXACTLY — no discriminant, no eigensolver, no library. Same shape as L7b-b's own closed-form 2×2
// and for the same recorded reason.
//
// β is then known only modulo 2π/Δℓ, and that is CHECKED rather than assumed (R-prt-6): the branch
// is anchored at the lowest frequency, where βΔℓ ≪ π, and continued upward by predicting the next
// point's βΔℓ from the last one scaled by frequency.

using System.Numerics;
using NumFlat;
using RfCore;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// How the calibration standards are dimensioned. Every one of these is a length expressed in
/// SUBSTRATE HEIGHTS, because that is the scale the port's evanescent field actually decays on —
/// R-prt-4 measures the number and this is where it lands.
/// </summary>
/// <param name="EndRunHeights">How far inward from the port the standard must reproduce the DUT's
/// own cells. The error box has to fit inside this.</param>
/// <param name="ShortLineHeights">The shorter standard's length between reference planes, so the
/// two error boxes do not see each other.</param>
/// <param name="TargetElectricalDegrees">Where βΔℓ is aimed at each sub-band's geometric mean.
/// <b>60°, not the interval's own 90° centre, and that is measured margin rather than timidity:</b>
/// Δℓ has to be chosen before any solve, from ε_eff ≈ (εᵣ+1)/2, which underestimates a real
/// microstrip's ε_eff by ~15% on FR-4 and by more once dispersion lifts it — so the realised βΔℓ
/// comes out systematically ABOVE the target. Aiming at 90° put the 20 GHz point at 202°, past π,
/// where the branch wraps. Aiming low costs nothing at the other end: 60/√3.16 = 34° is still well
/// clear of 20°.</param>
public sealed record PlanarCalibrationSettings(
    double EndRunHeights           = 3.0,
    double ShortLineHeights        = 3.0,
    double TargetElectricalDegrees = 60.0)
{
    public static readonly PlanarCalibrationSettings Default = new();

    /// <summary>TRL's own usable interval, and the interval where D6's denominator is well away
    /// from its zero at βΔℓ = nπ. R-prt-6 flags every frequency outside it.</summary>
    public const double UsableLoDegrees = 20.0;
    public const double UsableHiDegrees = 160.0;

    /// <summary>
    /// The band ratio ONE line separation can cover — 160/20 = 8, straight off the usable interval.
    ///
    /// <para><b>R-prt-6, MEASURED, and it settles the brief's own open question: two standards do NOT
    /// suffice for a 2–20 GHz sweep.</b> That is a 10:1 band against an 8:1 interval, so no single Δℓ
    /// exists — aiming 90° at the geometric mean puts the edges at 28° and 285°, and the measured
    /// run reads 59.7° / 122.5° / 345.4° at 2 / 6 / 20 GHz with the 20 GHz point 6.75e-2 wrong in β
    /// while the other two are 2.5e-4 and 7.8e-4. The number of separations is therefore DERIVED from
    /// the band rather than fixed, and the answer for 2–20 GHz is two of them (three standards).</para>
    /// </summary>
    public const double BandRatioPerSeparation = UsableHiDegrees / UsableLoDegrees;

    /// <summary>
    /// What the separation COUNT is actually derived from — half the theoretical 8:1, and the halving
    /// is measured rather than nervous. Δℓ is fixed before any solve from an ε_eff estimate that runs
    /// 15–20% low, so a separation lands ~1.17× higher in βΔℓ than it was aimed; designing to the
    /// full 8:1 leaves no room for that and puts the top of a 5:1 band at 157°, one point from the
    /// edge. Designing to 4:1 costs one extra standard mesh and puts the same band at 105°.
    /// </summary>
    public const double DesignBandRatioPerSeparation = 4.0;
}

/// <summary>One synthesised uniform line: its mesh, its two ports, and the length between the two
/// reference planes (which is what γ multiplies, not the drawn length).</summary>
/// <param name="ModePotential">
/// <b>RP-2c — the electrostatic problem D7's C_pul is taken from, per CELL, or null for a
/// single-conductor standard.</b>
///
/// <para>A microstrip standard has one conductor and one answer: put the whole sheet at 1 V above
/// the plane and total its charge. A coplanar standard has two, and the port drives the voltage
/// BETWEEN them — so the electrostatic problem is the signal conductor at +½ V and the return at
/// −½ V, and the capacitance that belongs in <c>Z_c = γ/(jωC_pul)</c> is the one that mode sees.
/// Putting a coplanar standard's whole sheet at 1 V measures the COMMON mode instead: a complete,
/// plausible reference impedance for a mode the port does not drive.</para>
/// </param>
/// <param name="ModeWeight">
/// What <c>C = Σ wᵢqᵢ</c> sums, per cell — <c>+½</c> on the signal conductor and <c>−½</c> on the
/// return, so the answer is <c>(Q⁺ − Q⁻)/2</c> per volt. <b>The average rather than one plate's
/// charge, deliberately:</b> with a ground plane present the two are not exactly equal and opposite,
/// and the average is the reading that does not depend on which conductor the user happened to name
/// as the return.
/// </param>
public sealed record PlanarStandard(
    PlanarMesh           Mesh,
    PlanarPortResolution Port1,
    PlanarPortResolution Port2,
    double               LengthM,
    int                  EndRunCells,
    IReadOnlyList<double>? ModePotential = null,
    IReadOnlyList<double>? ModeWeight    = null)
{
    public IReadOnlyList<PlanarPortResolution> Ports => [Port1, Port2];

    /// <summary>RP-2c — whether this standard is a coplanar pair rather than one conductor over the
    /// plane. The two differ in their MESH and in their electrostatics; nothing in the calibration
    /// ALGEBRA asks (R-rp2c-3).</summary>
    public bool IsCoplanar => ModePotential is not null;
}

public static class PlanarCalibration
{
    // ══════════════════════════════════════════════════════════════════════════════════════════
    // D4 — building the standard
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>D4's longitudinal half: end run, bulk fill, mirrored end run</b> — the cumulative
    /// gridlines along the port's own axis, with the length rounded UP to a whole number of bulk
    /// cells.
    ///
    /// <para>Shared by the microstrip and the coplanar builders because it is the same partition:
    /// both conductors of a coplanar pair are cells of ONE mesh, so they share every longitudinal
    /// gridline, and a second copy of this arithmetic is a second chance for the two standards to
    /// discretise a length differently. The operations and their order are L8d's own, so a
    /// single-conductor standard's coordinates are bit-identical to what it built before RP-2c.</para>
    /// </summary>
    private static double[] LongitudinalPartition(PlanarPortResolution port, double targetLengthM,
                                                  int endRunCells)
    {
        if (endRunCells < 1)
            throw new ArgumentOutOfRangeException(nameof(endRunCells),
                "A standard needs at least the port's own outer cell reproduced.");
        if (port.LongitudinalRunM.Count < endRunCells)
            throw new InvalidOperationException(
                $"Port {port.Number}'s feed is only {port.LongitudinalRunM.Count} cell(s) long, and the " +
                $"calibration standard has to reproduce {endRunCells} of them. Lengthen the feed line: " +
                "the de-embedding replaces exactly that much of it, so the structure has to have it.");

        var sizes = new List<double>();
        double endLen = 0;
        for (int k = 0; k < endRunCells; k++) { sizes.Add(port.LongitudinalRunM[k]); endLen += port.LongitudinalRunM[k]; }

        // The reference planes sit one cell in from each end (D2), so the length BETWEEN them is the
        // whole line minus the two outer cells. Solve for the number of bulk cells that reaches the
        // target, and report what was actually built rather than what was asked for.
        double bulk    = port.BulkCellM;
        double covered = 2 * (endLen - port.LongitudinalRunM[0]);
        int    fill    = Math.Max(0, (int)Math.Ceiling((targetLengthM - covered) / bulk));

        // Four cells is the floor: fewer and the two ports' rooftop rows are not disjoint, so the
        // "line between the planes" the algebra assumes does not exist.
        fill = Math.Max(fill, 4 - 2 * endRunCells);

        for (int k = 0; k < fill; k++) sizes.Add(bulk);
        for (int k = endRunCells - 1; k >= 0; k--) sizes.Add(port.LongitudinalRunM[k]);

        var gLong = new double[sizes.Count + 1];
        for (int i = 0; i < sizes.Count; i++) gLong[i + 1] = gLong[i] + sizes[i];
        return gLong;
    }

    /// <summary>
    /// <b>RP-2c/R-rp2c-1 — the standard for a port that returns through drawn metal: BOTH
    /// conductors, the slot between them, and the DUT's own transverse gridlines verbatim.</b>
    ///
    /// <para>D4's rule is unchanged and is the whole point — the standard must rebuild the port's
    /// neighbourhood exactly — and the neighbourhood is now more than one piece of metal. So the
    /// transverse partition is <see cref="PlanarPortCrossSection.Lines"/> (the DUT's own gridlines
    /// at the cut, trimmed to the outermost metal) and a cell exists only where that profile says
    /// metal. A basis needs both of its cells, so no basis crosses the slot — which is the mesh's
    /// own structural property, measured in <c>CoplanarSlotMeshTests</c> and not something this
    /// builder has to arrange.</para>
    ///
    /// <para><b>The standard's own two ports are two-cut ports, at the same station (R-rp2c-2).</b>
    /// They are placed by transverse coordinate and resolved through <see cref="PlanarPorts"/> — the
    /// one resolution rule, including its skew, level and same-conductor checks — rather than
    /// assembled here. A standard whose two cuts were skewed would calibrate out a length that is
    /// not in the DUT, and the check that says so already exists.</para>
    /// </summary>
    public static PlanarStandard BuildCoplanarLine(PlanarPortResolution port, double targetLengthM,
                                                   int endRunCells, string layerName = "Metal")
    {
        ArgumentNullException.ThrowIfNull(port);
        var xs = port.CrossSection
               ?? throw new InvalidOperationException(
                      $"Port {port.Number} has no cross-section, so it is not a conductor-referenced " +
                      "edge port and BuildLine is what builds its standard.");

        var gLong = LongitudinalPartition(port, targetLengthM, endRunCells);
        var gTran = xs.Lines.ToArray();

        bool alongX = port.Direction == PlanarBasisDirection.X;
        var  gx     = alongX ? gLong : gTran;
        var  gy     = alongX ? gTran : gLong;

        int nx = gx.Length - 1, ny = gy.Length - 1;
        var cells = new List<PlanarCell>(nx * ny);
        var at    = new int[nx * ny];
        Array.Fill(at, -1);

        // R-msh-2's (LayerIndex, IY, IX) order, as the single-conductor builder emits it — with the
        // void intervals skipped, which is the only difference.
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                if (!xs.IsMetal[alongX ? iy : ix]) continue;
                at[iy * nx + ix] = cells.Count;
                cells.Add(new PlanarCell(0, ix, iy, gx[ix], gy[iy], gx[ix + 1], gy[iy + 1]));
            }

        var bases = new List<PlanarBasis>();
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                int a = at[iy * nx + ix];
                if (a < 0) continue;
                if (ix + 1 < nx && at[iy * nx + ix + 1] >= 0)
                    bases.Add(new PlanarBasis(0, a, at[iy * nx + ix + 1], PlanarBasisDirection.X));
                if (iy + 1 < ny && at[(iy + 1) * nx + ix] >= 0)
                    bases.Add(new PlanarBasis(0, a, at[(iy + 1) * nx + ix], PlanarBasisDirection.Y));
            }

        var mesh = new PlanarMesh(cells, bases, [layerName], gx, gy);

        var (sideLo, sideHi) = alongX
            ? (PlanarPortSide.MinX, PlanarPortSide.MaxX)
            : (PlanarPortSide.MinY, PlanarPortSide.MaxY);

        EmPoint Pt(double along, double across) =>
            alongX ? new EmPoint(along, across) : new EmPoint(across, along);

        PlanarPort Std(int number, double along, PlanarPortSide side) =>
            new(number, Pt(along, xs.PositiveCentreM), side, port.Z0,
                Reference: port.Reference, Kind: PlanarPortKind.Edge,
                NegativeLocation: Pt(along, xs.NegativeCentreM));

        var p1 = PlanarPorts.Resolve(mesh, Std(1, gLong[0],  sideLo));
        var p2 = PlanarPorts.Resolve(mesh, Std(2, gLong[^1], sideHi));

        // ── D7's electrostatic problem, per cell: +½ V on the signal conductor, −½ on the return ──
        var v = new double[cells.Count];
        var w = new double[cells.Count];
        for (int c = 0; c < cells.Count; c++)
        {
            int t = alongX ? cells[c].IY : cells[c].IX;
            double sign = t >= xs.PositiveLo && t <= xs.PositiveHi ? +1.0
                        : t >= xs.NegativeLo && t <= xs.NegativeHi ? -1.0
                        : 0.0;
            v[c] = 0.5 * sign;
            w[c] = 0.5 * sign;
        }

        return new PlanarStandard(mesh, p1, p2, p2.ReferencePlaneM - p1.ReferencePlaneM, endRunCells,
                                  v, w);
    }

    /// <summary>
    /// A uniform line of the port's own cross-section, at least <paramref name="targetLengthM"/>
    /// between reference planes. The actual length is rounded UP to a whole number of bulk cells and
    /// is reported on the result — the requested length is never assumed.
    /// </summary>
    public static PlanarStandard BuildLine(PlanarPortResolution port, double targetLengthM,
                                           int endRunCells, string layerName = "Metal")
    {
        ArgumentNullException.ThrowIfNull(port);

        // ── RP-2c — A CONDUCTOR-REFERENCED PORT'S STANDARD IS A COPLANAR LINE ────────────────────
        //
        // Its neighbourhood is two pieces of metal with a slot between them, not one rectangle, and
        // calibrating it against a rectangle publishes s-parameters that are plausible and
        // referenced to nothing (RP-2's R-rp2-4). The LONGITUDINAL half of D4 is the same either
        // way and is shared below; what differs is the cross-section and the standard's own ports.
        if (port.CrossSection is not null)
            return BuildCoplanarLine(port, targetLengthM, endRunCells, layerName);

        var gLong = LongitudinalPartition(port, targetLengthM, endRunCells);
        var gTran = port.TransverseLines.ToArray();

        bool alongX = port.Direction == PlanarBasisDirection.X;
        var  gx     = alongX ? gLong : gTran;
        var  gy     = alongX ? gTran : gLong;

        // ── The cells, in R-msh-2's order: (LayerIndex, IY, IX), integers, no ties ───────────────
        int nx = gx.Length - 1, ny = gy.Length - 1;
        var cells = new List<PlanarCell>(nx * ny);
        var at    = new int[nx * ny];
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                at[iy * nx + ix] = cells.Count;
                cells.Add(new PlanarCell(0, ix, iy, gx[ix], gy[iy], gx[ix + 1], gy[iy + 1]));
            }

        var bases = new List<PlanarBasis>();
        for (int iy = 0; iy < ny; iy++)
            for (int ix = 0; ix < nx; ix++)
            {
                if (ix + 1 < nx) bases.Add(new PlanarBasis(0, at[iy * nx + ix], at[iy * nx + ix + 1], PlanarBasisDirection.X));
                if (iy + 1 < ny) bases.Add(new PlanarBasis(0, at[iy * nx + ix], at[(iy + 1) * nx + ix], PlanarBasisDirection.Y));
            }

        var mesh = new PlanarMesh(cells, bases, [layerName], gx, gy);

        // ── Its own two ports, at the same side conventions the DUT's port uses ──────────────────
        double tMid = 0.5 * (gTran[0] + gTran[^1]);
        var (sideLo, sideHi) = alongX
            ? (PlanarPortSide.MinX, PlanarPortSide.MaxX)
            : (PlanarPortSide.MinY, PlanarPortSide.MaxY);

        EmPoint Pt(double along) => alongX ? new EmPoint(along, tMid) : new EmPoint(tMid, along);

        var p1 = PlanarPorts.Resolve(mesh, new PlanarPort(1, Pt(gLong[0]),  sideLo, port.Z0));
        var p2 = PlanarPorts.Resolve(mesh, new PlanarPort(2, Pt(gLong[^1]), sideHi, port.Z0));

        return new PlanarStandard(mesh, p1, p2, p2.ReferencePlaneM - p1.ReferencePlaneM, endRunCells);
    }

    /// <summary>
    /// How many of the DUT's own cells the standard must reproduce so that the port's evanescent
    /// field is entirely inside the identical region. Derived from the substrate height, clamped to
    /// what the feed actually has.
    /// </summary>
    public static int EndRunCellsFor(PlanarPortResolution port, GroundedSlab slab,
                                     PlanarCalibrationSettings? settings = null)
    {
        var s = settings ?? PlanarCalibrationSettings.Default;
        double want = s.EndRunHeights * slab.HeightM;

        double acc = 0;
        int k = 0;
        while (k < port.LongitudinalRunM.Count && acc < want) acc += port.LongitudinalRunM[k++];
        return Math.Clamp(k, 1, Math.Max(1, port.LongitudinalRunM.Count));
    }

    /// <summary>
    /// The short standard's target length and the SEPARATION between the two, for a band. Δℓ is
    /// aimed at <see cref="PlanarCalibrationSettings.TargetElectricalDegrees"/> at the band's
    /// GEOMETRIC mean, which is what centres the usable interval on a log-frequency sweep; the
    /// realised electrical lengths are reported afterwards from the measured γ, never from this
    /// estimate (R-prt-6).
    ///
    /// <para><b>The second return value is a SEPARATION, not a second length, and that is a
    /// measured correction rather than a stylistic choice.</b> Returning two absolute lengths is the
    /// obvious API and it silently breaks the calibration: <see cref="BuildLine"/> has a floor — a
    /// standard cannot be shorter than its two end runs — so a short target gets inflated while the
    /// long one does not, and Δℓ comes out at half what was asked for. Measured on the 20 mm FR-4
    /// fixture at 2 GHz: a requested Δℓ of 7.2 mm was realised as 3.58 mm, dropping βΔℓ from 31° to
    /// 15.6° and out of the usable interval. The caller must build the short line FIRST and take Δℓ
    /// from its ACTUAL length.</para>
    /// </summary>
    public static (double Short, double Delta) SuggestLengths(
        GroundedSlab slab, double fLoHz, double fHiHz, PlanarCalibrationSettings? settings = null)
    {
        var s = settings ?? PlanarCalibrationSettings.Default;
        double fGm = Math.Sqrt(Math.Max(fLoHz, 1.0) * Math.Max(fHiHz, 1.0));
        return (s.ShortLineHeights * slab.HeightM, DeltaAt(slab, fGm, s));
    }

    /// <summary>
    /// The line SEPARATIONS a band needs — one per <see cref="PlanarCalibrationSettings
    /// .BandRatioPerSeparation"/> of band, geometrically spaced so each covers its own octave-and-a-
    /// bit with βΔℓ centred on the usable interval. A narrow band gets one; 2–20 GHz gets two.
    /// </summary>
    public static double[] SuggestDeltas(GroundedSlab slab, double fLoHz, double fHiHz,
                                         PlanarCalibrationSettings? settings = null)
    {
        var s = settings ?? PlanarCalibrationSettings.Default;
        double lo = Math.Max(Math.Min(fLoHz, fHiHz), 1.0);
        double hi = Math.Max(Math.Max(fLoHz, fHiHz), 1.0);

        double ratio = hi / lo;
        int n = Math.Max(1, (int)Math.Ceiling(Math.Log(ratio) /
                                              Math.Log(PlanarCalibrationSettings.DesignBandRatioPerSeparation)));

        var deltas = new double[n];
        for (int k = 0; k < n; k++)
        {
            // Each separation is centred on its own sub-band's geometric mean.
            double f = lo * Math.Pow(ratio, (k + 0.5) / n);
            deltas[k] = DeltaAt(slab, f, s);
        }
        return deltas;
    }

    private static double DeltaAt(GroundedSlab slab, double fHz, PlanarCalibrationSettings s)
    {
        // ε_eff is not known before a solve; (εᵣ+1)/2 is the standard crude microstrip estimate, and
        // it only has to be right to a factor for βΔℓ to land inside an 8:1-wide usable interval.
        double epsEst = 0.5 * (slab.Material.EpsR + 1.0);
        double lambda = EmConstants.C0 / (fHz * Math.Sqrt(epsEst));
        return lambda * (s.TargetElectricalDegrees / 360.0);
    }

    /// <summary>
    /// The full standard set for a port and a band: element 0 is the short line, and each further
    /// element is the short line plus one of <see cref="SuggestDeltas"/>'s separations.
    ///
    /// <para>Δℓ is taken from the SHORT line's ACTUAL length, never from its target — see
    /// <see cref="SuggestLengths"/> for the measurement that made that necessary.</para>
    /// </summary>
    public static PlanarStandard[] BuildSet(
        PlanarPortResolution port, GroundedSlab slab, double fLoHz, double fHiHz,
        PlanarCalibrationSettings? settings = null)
    {
        int k = EndRunCellsFor(port, slab, settings);
        var (shortTarget, _) = SuggestLengths(slab, fLoHz, fHiHz, settings);
        var deltas = SuggestDeltas(slab, fLoHz, fHiHz, settings);

        var set = new PlanarStandard[deltas.Length + 1];
        set[0] = BuildLine(port, shortTarget, k);
        for (int i = 0; i < deltas.Length; i++)
            set[i + 1] = BuildLine(port, set[0].LengthM + deltas[i], k);

        return set;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // D5 — γ from the two standards
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>What the two-line step produced, plus what R-prt-6 has to report about it.</summary>
    /// <param name="ElectricalDegrees">βΔℓ in degrees — the number that decides whether this
    /// frequency is inside TRL's usable interval.</param>
    /// <param name="Unwrapped">How many 2π were added to the principal branch. Non-zero is fine;
    /// non-zero at the FIRST frequency of a sweep means Δℓ is already too long.</param>
    public sealed record GammaResult(Complex Gamma, double ElectricalDegrees, int Unwrapped, bool Usable)
    {
        public double Alpha => Gamma.Real;
        public double Beta  => Gamma.Imaginary;

        public double EffectivePermittivity(double fHz)
        {
            double b = Beta / (2.0 * Math.PI * fHz / EmConstants.C0);
            return b * b;
        }
    }

    /// <summary>
    /// γ from the two standards' raw s-parameters. <paramref name="expectedBetaDeltaL"/> is the
    /// previous frequency's βΔℓ scaled by frequency; pass NaN at the first point, where the branch is
    /// anchored on βΔℓ &lt; π instead.
    /// </summary>
    public static GammaResult Gamma(Mat<Complex> sShort, Mat<Complex> sLong, double deltaLM,
                                    double expectedBetaDeltaL = double.NaN)
    {
        if (!(deltaLM > 0)) throw new ArgumentOutOfRangeException(nameof(deltaLM));

        var m = RFNetwork.SToT2Port(sLong) * Inverse2(RFNetwork.SToT2Port(sShort));
        Complex w = 0.5 * (m[0, 0] + m[1, 1]);          // det M = 1, so this IS cosh(γΔℓ)

        // cosh is EVEN and 2πj-periodic, so the solutions are ±g₀ + 2πjk. Both families have to be
        // considered together.
        //
        // MEASURED, AND IT WAS A REAL BUG: the obvious rule — "negate g₀ if Re g₀ < 0, because a
        // passive line has α ≥ 0" — is WRONG, because negating flips β as well. On the FR-4 fixture
        // at 20 GHz the principal value came back as (−0.061, +2.2005) with a true αΔℓ of only
        // +0.016: α is two orders of magnitude smaller than β here, so its extracted SIGN is noise,
        // and flipping on it turned a correct β = 804 into 1492. β is the well-determined half, so
        // β is what selects the branch; Re is used only to break a tie when there is no prediction
        // to select against.
        Complex g0 = Acosh(w);

        Complex best = default;
        double  bestErr = double.PositiveInfinity;
        int     k = 0;

        foreach (double sign in new[] { +1.0, -1.0 })
        {
            Complex c  = sign * g0;
            int     kk = double.IsNaN(expectedBetaDeltaL)
                ? (c.Imaginary < 0 ? 1 : 0)                       // anchor βΔℓ into (0, 2π)
                : (int)Math.Round((expectedBetaDeltaL - c.Imaginary) / (2 * Math.PI));

            Complex cc = c + new Complex(0, 2 * Math.PI * kk);
            if (cc.Imaginary <= 0) continue;                       // β > 0 on a forward-travelling wave

            double err = double.IsNaN(expectedBetaDeltaL)
                ? (cc.Real >= 0 ? 0.0 : 1.0)                       // no prediction: fall back on α ≥ 0
                : Math.Abs(cc.Imaginary - expectedBetaDeltaL);

            if (err < bestErr) { bestErr = err; best = cc; k = kk; }
        }

        Complex g = best;
        double deg = g.Imaginary * 180.0 / Math.PI;
        bool usable = deg >= PlanarCalibrationSettings.UsableLoDegrees
                   && deg <= PlanarCalibrationSettings.UsableHiDegrees;

        return new GammaResult(g / deltaLM, deg, k, usable);
    }

    /// <summary>
    /// γ from a short standard and SEVERAL long ones, choosing per frequency the separation whose
    /// realised βΔℓ sits closest to the middle of the usable interval. This is multiline TRL's own
    /// idea reduced to what a simulator needs: every standard is solved at every frequency anyway,
    /// so the selection costs nothing beyond the extra fill and can be made on the MEASURED
    /// electrical length rather than on the pre-solve estimate.
    /// </summary>
    /// <param name="expectedBetaPerMetre">The running β estimate — the previous frequency's measured
    /// β scaled by frequency, or <see cref="EstimateBeta"/> at the first point. <b>The choice is made
    /// on the PREDICTION, never on the extracted value, and that is not a stylistic preference:</b> an
    /// aliased separation (βΔℓ past π) reports a wrapped electrical length that can land inside the
    /// usable interval by accident and score better than the correct one.</param>
    /// <param name="index">Which separation was chosen — reported, because "which standard produced
    /// this number" is exactly what someone debugging a bad point needs to know.</param>
    public static GammaResult GammaBest(Mat<Complex> sShort, IReadOnlyList<Mat<Complex>> sLong,
                                        IReadOnlyList<double> deltaLM, double expectedBetaPerMetre,
                                        out int index)
    {
        if (sLong.Count == 0 || sLong.Count != deltaLM.Count)
            throw new ArgumentException("Each long standard needs its own Δℓ.", nameof(sLong));

        index = SelectSeparation(deltaLM, expectedBetaPerMetre);
        return Gamma(sShort, sLong[index], deltaLM[index], expectedBetaPerMetre * deltaLM[index]);
    }

    /// <summary>
    /// Which separation <see cref="GammaBest"/> will choose — <b>a pure function of the Δℓ set and
    /// the PREDICTED β, so it can be asked BEFORE any standard has been solved.</b>
    ///
    /// <para>That is the whole point of it being extracted rather than inlined. <see cref="GammaBest"/>
    /// reads exactly two of the standards' raw matrices at any one frequency — the short line and the
    /// one long line named here — so a driver that knows the answer in advance can solve two meshes
    /// instead of all of them. <c>GammaBest</c>'s own doc comment used to say the selection "costs
    /// nothing beyond the extra fill"; the extra fill turned out to be most of the run, and this is
    /// what lets the sentence become true rather than remain a caveat.</para>
    ///
    /// <para>The scoring is unchanged and deliberately still keyed on the PREDICTION, never on an
    /// extracted electrical length — see <see cref="GammaBest"/>'s own parameter note for the aliasing
    /// reason. Moving it here changes no arithmetic; it changes only who may ask.</para>
    /// </summary>
    public static int SelectSeparation(IReadOnlyList<double> deltaLM, double expectedBetaPerMetre)
    {
        ArgumentNullException.ThrowIfNull(deltaLM);
        if (deltaLM.Count == 0) throw new ArgumentException("No separations.", nameof(deltaLM));

        double centre = Math.Sqrt(PlanarCalibrationSettings.UsableLoDegrees *
                                  PlanarCalibrationSettings.UsableHiDegrees) * Math.PI / 180.0;

        int index = 0;
        double bestScore = double.PositiveInfinity;
        for (int i = 0; i < deltaLM.Count; i++)
        {
            double predicted = Math.Max(expectedBetaPerMetre * deltaLM[i], 1e-12);
            double score = Math.Abs(Math.Log(predicted / centre));
            if (score < bestScore) { bestScore = score; index = i; }
        }
        return index;
    }

    /// <summary>
    /// β before any solve, from ε_eff ≈ (εᵣ+1)/2. Only used to seed the first frequency's branch and
    /// separation choice, and it only has to be right to ~20% for either.
    /// </summary>
    public static double EstimateBeta(GroundedSlab slab, double fHz) =>
        2.0 * Math.PI * fHz * Math.Sqrt(0.5 * (slab.Material.EpsR + 1.0)) / EmConstants.C0;

    /// <summary>acosh, principal branch — written here rather than depended on, as L8a's Bessel
    /// functions and Legendre nodes were, and for the same recorded reason.</summary>
    public static Complex Acosh(Complex w) => Complex.Log(w + Complex.Sqrt(w * w - Complex.One));

    /// <summary>The 2×2 inverse, in closed form. A general solver here would be silly.</summary>
    internal static Mat<Complex> Inverse2(Mat<Complex> a)
    {
        Complex det = a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        var r = new Mat<Complex>(2, 2);
        r[0, 0] =  a[1, 1] / det;
        r[0, 1] = -a[0, 1] / det;
        r[1, 0] = -a[1, 0] / det;
        r[1, 1] =  a[0, 0] / det;
        return r;
    }
}
