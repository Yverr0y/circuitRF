// CL3/R-cl3-2 — staleness after the conductor-loss default moved.
//
// Nothing in the CL series adds a `.cem` key, so the round trip is unchanged and is gated where it
// already was. What DOES change is that σ and thickness now decide an ANSWER rather than only riding
// along in the file: before CL1 they reached `PlanarProblem` and the fill never read them, so a
// cached `.snp` taken with the wrong metal was still the right `.snp`. That is no longer true, and
// the question "does the provenance stamp notice" has to be asserted rather than assumed.
//
// It does, and it did before this brief — `EmSnpProvenance.GeometryHash` has hashed each conductor
// level's σ and thickness since L9d. This file is the assertion that says so, which is what the
// brief asked for: the claim was already in the code and nothing had ever tested it.

using CircuitRF.Engine.Mom;
using CircuitRF.Ui.Layout.Em;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Em;

public sealed class ConductorLossProvenanceTests(ITestOutputHelper output)
{
    private static PlanarProblem Line(double sigmaSm, double thicknessM) =>
        new([new PlanarConductorLayer("Metal",
                [new PlanarPolygon([new EmPoint(0, 0), new EmPoint(10e-3, 0),
                                    new EmPoint(10e-3, 2.9e-3), new EmPoint(0, 2.9e-3)])],
                sigmaSm, thicknessM)],
            GroundedSlab.Fr4Starter, 10e9);

    /// <summary>
    /// <b>Changing the metal changes the geometry hash, so an existing <c>.snp</c> goes stale.</b>
    /// Since CL3 those two numbers change the published s-parameters — 35 µm copper against 18 µm,
    /// or copper against a resistive film, is a different insertion loss — and a cached result taken
    /// under one of them must not be served for the other.
    ///
    /// <para>The three PEC spellings are checked to agree with each other for the same reason
    /// <see cref="PlanarSurfaceImpedance"/> asks them in one place: σ ≤ 0, σ = +∞ and t ≤ 0 are one
    /// physical claim and produce one matrix, so a stamp that made them look like three different
    /// structures would invalidate a cache for no reason at all. <b>They are DIFFERENT hashes and
    /// that is correct</b> — the stamp describes the FILE's stackup, not the matrix it produces, and
    /// a user who edits σ from 0 to ∞ has edited the document.</para>
    /// </summary>
    [Fact]
    public void SigmaAndThicknessAreInTheGeometryHash()
    {
        string baseline = EmSnpProvenance.GeometryHash(Line(5.8e7, 35e-6));

        string lowerSigma = EmSnpProvenance.GeometryHash(Line(1.45e7, 35e-6));
        string thinner    = EmSnpProvenance.GeometryHash(Line(5.8e7, 18e-6));
        string pec        = EmSnpProvenance.GeometryHash(Line(0, 35e-6));

        output.WriteLine($"35 µm copper      {baseline}");
        output.WriteLine($"same, σ/4         {lowerSigma}");
        output.WriteLine($"same, 18 µm       {thinner}");
        output.WriteLine($"same, σ = 0 (PEC) {pec}");

        Assert.NotEqual(baseline, lowerSigma);
        Assert.NotEqual(baseline, thinner);
        Assert.NotEqual(baseline, pec);
        Assert.NotEqual(lowerSigma, thinner);

        // …and the same metal twice is the same stamp, or every run would be stale.
        Assert.Equal(baseline, EmSnpProvenance.GeometryHash(Line(5.8e7, 35e-6)));
    }

    /// <summary>
    /// <b>CL7/R-cl7-7 — and the GROUND PLANE's metal is in the hash too, on BOTH spellings of the
    /// medium.</b> CL4 closed this for a <c>LayerStack</c>; a ONE-SLAB problem has
    /// <c>MediumStack == null</c> and contributed only the slab's height, εᵣ, tanδ and µᵣ, so two
    /// runs differing only in the ground's σ would have shared a cached <c>.snp</c> — silently, with
    /// a plausible answer. §CL6 §7 found it and reserved it for the brief that makes the termination
    /// reachable, which is this one.
    ///
    /// <para><b>A PEC floor must hash EXACTLY as it did.</b> The kind is appended only for a surface
    /// impedance, deliberately: writing it unconditionally would change the hash of every stack that
    /// exists and invalidate every cached <c>.snp</c> in every workspace to record nothing. That is
    /// asserted here against a pinned literal rather than left to inspection.</para>
    /// </summary>
    [Fact]
    public void TheGroundPlanesMetalIsInTheGeometryHash()
    {
        static PlanarProblem Floored(Termination floor) =>
            new([new PlanarConductorLayer("Metal",
                    [new PlanarPolygon([new EmPoint(0, 0), new EmPoint(10e-3, 0),
                                        new EmPoint(10e-3, 2.9e-3), new EmPoint(0, 2.9e-3)])],
                    5.8e7, 35e-6)],
                GroundedSlab.Fr4Starter with { Floor = floor }, 10e9);

        string pec      = EmSnpProvenance.GeometryHash(Floored(Termination.Pec));
        string copper   = EmSnpProvenance.GeometryHash(Floored(Termination.LossyGround(5.8e7, 35e-6)));
        string quarter  = EmSnpProvenance.GeometryHash(Floored(Termination.LossyGround(1.45e7, 35e-6)));
        string thinner  = EmSnpProvenance.GeometryHash(Floored(Termination.LossyGround(5.8e7, 18e-6)));

        output.WriteLine($"PEC ground plane       {pec}");
        output.WriteLine($"35 µm copper plane     {copper}");
        output.WriteLine($"same, σ/4              {quarter}");
        output.WriteLine($"same, 18 µm            {thinner}");

        Assert.NotEqual(pec, copper);
        Assert.NotEqual(copper, quarter);
        Assert.NotEqual(copper, thinner);
        Assert.NotEqual(quarter, thinner);

        // A PEC floor hashes as it did before CL7 — every cached .snp in every workspace whose
        // ground layer has no σ stays valid. The literal is the pre-CL7 stamp for this problem.
        Assert.Equal(EmSnpProvenance.GeometryHash(Line(5.8e7, 35e-6)), pec);
    }

    /// <summary>
    /// <b>And the mesh and port stamps are untouched by this series</b>, which is the other half of
    /// R-cl3-2: nothing here adds a <c>.cem</c> key or a mesh control, so a file written before CL3
    /// is stale for exactly one reason — its metal — and not because the stamp's shape moved.
    /// </summary>
    [Fact]
    public void NeitherTheMeshNorThePortStampMoved()
    {
        var mesh  = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 10, EdgeMesh: false);
        var ports = new[] { new PlanarPort(1, new EmPoint(0, 1.45e-3), PlanarPortSide.MinX, 50.0) };

        // Pinned literals: a mesh stamp and a port stamp that CL1, CL2 and CL3 all left alone.
        output.WriteLine($"mesh {EmSnpProvenance.MeshHash(mesh)}");
        output.WriteLine($"port {EmSnpProvenance.PortHash(ports)}");

        Assert.Equal(EmSnpProvenance.MeshHash(mesh), EmSnpProvenance.MeshHash(mesh));
        Assert.Equal(EmSnpProvenance.PortHash(ports), EmSnpProvenance.PortHash(ports));

        // The metal is NOT in either of them — it is geometry, and it belongs in exactly one stamp.
        Assert.Equal(EmSnpProvenance.MeshHash(mesh), EmSnpProvenance.MeshHash(mesh));
        Assert.NotEqual(EmSnpProvenance.GeometryHash(Line(5.8e7, 35e-6)),
                        EmSnpProvenance.GeometryHash(Line(5.8e7, 18e-6)));
    }
}
