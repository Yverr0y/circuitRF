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
    /// <summary>
    /// <b>The stamp answers "did the SOLVER change" as well as "did the document change", and until
    /// this it could only answer the second.</b>
    ///
    /// <para>σ and thickness have been in <see cref="EmSnpProvenance.GeometryHash"/> since L9d — they
    /// were simply never READ by the fill. So an <c>.snp</c> written when kernel B's metal was a
    /// perfect conductor hashes to exactly what the same design hashes to today, and went on reading
    /// as CURRENT while carrying numbers with 92-99% of the MMIC starter's loss missing. A hash of
    /// the document cannot see that, by construction; a model token can.</para>
    ///
    /// <para>The three cases below are the three that matter: a file from the current model is
    /// current, a file from a NAMED earlier model is stale, and a file with NO model line — which is
    /// every planar <c>.snp</c> written before this — is stale and the message says that its metal
    /// was a perfect conductor.</para>
    /// </summary>
    [Fact]
    public void AFileWrittenByAnEarlierPHYSICSReadsAsStale()
    {
        var problem = Line(5.8e7, 35e-6);
        var mesh    = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 10, EdgeMesh: false);
        var ports   = new[] { new PlanarPort(1, new EmPoint(0, 1.45e-3), PlanarPortSide.MinX, 50.0) };

        string dir = Path.Combine(Path.GetTempPath(), "crf-model-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var header = EmSnpProvenance.BuildHeader(
                problem, mesh, ports, "setup", "layout.clay", DateTimeOffset.UnixEpoch);

            // The model line is IN the header the planar path writes, in plain text.
            Assert.Contains(header, l => l == EmProvenanceStamp.ModelPrefix + PlanarKernel.ModelRevision);

            string Write(IEnumerable<string> lines, string name)
            {
                string path = Path.Combine(dir, name);
                File.WriteAllLines(path, lines.Select(l => "! " + l).Append("# HZ S RI R 50"));
                return path;
            }

            // (a) written by THIS model, nothing else moved — current.
            Assert.Null(EmSnpProvenance.DescribeStaleness(Write(header, "now.s1p"), problem, mesh, ports));

            // (b) written by a NAMED earlier model, same document — stale, and it says which.
            var older = header.Select(l => l.StartsWith(EmProvenanceStamp.ModelPrefix, StringComparison.Ordinal)
                                            ? EmProvenanceStamp.ModelPrefix + "something-earlier"
                                            : l);
            string named = EmSnpProvenance.DescribeStaleness(Write(older, "older.s1p"), problem, mesh, ports)!;
            output.WriteLine(named);
            Assert.Contains("EM physics has", named, StringComparison.Ordinal);
            Assert.Contains("something-earlier", named, StringComparison.Ordinal);
            Assert.Contains(PlanarKernel.ModelRevision, named, StringComparison.Ordinal);

            // (c) NO model line at all — every planar .snp written before this token existed. Stale,
            //     and the sentence names the reason rather than leaving "a different setup" to be
            //     read as an edit nobody made.
            var pre = header.Where(l => !l.StartsWith(EmProvenanceStamp.ModelPrefix, StringComparison.Ordinal));
            string legacy = EmSnpProvenance.DescribeStaleness(Write(pre, "pre.s1p"), problem, mesh, ports)!;
            output.WriteLine(legacy);
            Assert.Contains("PERFECT", legacy, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// <b>Kernel A gets no model token, and that is the point of having one per kernel.</b> Nothing
    /// in the conductor-loss series touched the cross-section kernel, so stamping it would mark every
    /// cross-section <c>.snp</c> in every workspace stale to record a change that did not happen.
    /// </summary>
    [Fact]
    public void TheCrossSectionKernelIsNotStamped()
    {
        var p = new EmProblem(
            [new EmConductor("sig",
                [new EmPoint(0, 0), new EmPoint(1e-3, 0),
                 new EmPoint(1e-3, 35e-6), new EmPoint(0, 35e-6)], 5.8e7)],
            [new EmDielectricRegion(0, 1.6e-3, new EmMaterial(4.4, 0.02))],
            new EmGroundPlane(0, double.PositiveInfinity),
            [new EmPort(1, "sig", null, 50.0), new EmPort(2, "sig", null, 50.0)],
            10e-3);

        var header = EmSnpProvenance.BuildHeader(p, new EmMeshSettings(), "setup", "x.clay",
                                                 DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain(header, l => l.StartsWith(EmProvenanceStamp.ModelPrefix,
                                                        StringComparison.Ordinal));
    }

}
