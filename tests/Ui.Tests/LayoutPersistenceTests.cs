using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests;

public class LayoutPersistenceTests
{
    // ── Fixture: one of every shape type + arc-bearing Curve/Path + Net + instances ────────────

    private static LayoutView BuildFullFixture()
    {
        var view = new LayoutView
        {
            DbuPerMicron = 1000,
            DisplayUnit  = LayoutUnit.Mil,
            SnapDbu      = 1000,
            AngleMode    = AngleMode.AnyAngle,
            TechRef      = "../../tech/pcb-2layer.ctech",
        };

        view.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), Net = "RFin", X1 = 0, Y1 = 0, X2 = 2_900_000, Y2 = 20_000_000 });
        view.Shapes.Add(new PolygonShape { Layer = new LayerKey(1, 0), Xy = [0, 0, 500, 0, 500, 300, 0, 300] });
        view.Shapes.Add(new RoundedRectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 1_000_000, Y2 = 600_000, CornerRadius = 150_000 });
        view.Shapes.Add(new CircleShape { Layer = new LayerKey(2, 0), Net = "GND", Cx = 4_000_000, Cy = 1_000_000, R = 300_000 });

        view.Shapes.Add(new CurveShape
        {
            Layer = new LayerKey(1, 0),
            Xy = [0, 0, 2_000_000, 0, 2_000_000, 2_000_000],
            Edges =
            [
                new LayoutEdge { Kind = EdgeKind.Line },
                new LayoutEdge { Kind = EdgeKind.Arc, Bulge = 0.4142 },
                new LayoutEdge { Kind = EdgeKind.Line },
            ],
            FlattenTolDbu = 1000,
        });

        view.Shapes.Add(new PathShape
        {
            Layer = new LayerKey(1, 0),
            Net = "RFin",
            Xy = [0, 0, 5_000_000, 0, 5_000_000, 3_000_000],
            Width = 2_900_000,
            End = PathEndStyle.Flush,
            Edges =
            [
                new LayoutEdge { Kind = EdgeKind.Line },
                new LayoutEdge { Kind = EdgeKind.Arc, Bulge = 0.4142 },
            ],
            FlattenTolDbu = 1000,
        });

        view.Shapes.Add(new ViaShape { Layer = new LayerKey(3, 0), X = 100_000, Y = 100_000, PadSize = 500_000, DrillSize = 200_000, LandingLayer = new LayerKey(2, 0) });
        view.Shapes.Add(new LabelShape { Layer = new LayerKey(1, 0), X = 0, Y = 0, Text = "P1", Height = 500_000, Rotation = LayoutRotation.R0, IsPort = true });

        view.Instances.Add(new LayoutInstance { CellRef = "../../inductor_2n5", X = 100_000, Y = 0, Rot = LayoutRotation.R90, MirrorX = false });
        view.Instances.Add(new LayoutInstance { CellRef = "../../via_cell", X = 0, Y = 0, Rows = 4, Cols = 4, PitchX = 50_000, PitchY = 50_000 });

        return view;
    }

    // ── Gate 3: display-unit change is a serialization no-op ─────────────────

    [Fact]
    public void DisplayUnitChange_IsSerializationNoOp_ExceptDisplayUnitToken()
    {
        var view = BuildFullFixture();

        view.DisplayUnit = LayoutUnit.Um;
        var jsonUm = LayoutPersistence.Serialize(view);

        view.DisplayUnit = LayoutUnit.Mil;
        var jsonMil = LayoutPersistence.Serialize(view);

        Assert.NotEqual(jsonUm, jsonMil);

        var linesUm  = jsonUm.Split('\n').Where(l => !l.TrimStart().StartsWith("\"DisplayUnit\"")).ToArray();
        var linesMil = jsonMil.Split('\n').Where(l => !l.TrimStart().StartsWith("\"DisplayUnit\"")).ToArray();
        Assert.Equal(string.Join('\n', linesUm), string.Join('\n', linesMil));
    }

    // ── Gate 4: .clay round-trips byte-identically ────────────────────────────

    [Fact]
    public void Clay_RoundTrip_ByteIdentical()
    {
        var view = BuildFullFixture();
        var json1 = LayoutPersistence.Serialize(view);
        var restored = LayoutPersistence.Deserialize(json1);
        var json2 = LayoutPersistence.Serialize(restored);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Clay_SaveLoadFile_RoundTrips()
    {
        var view = BuildFullFixture();
        var tmp = Path.GetTempFileName();
        try
        {
            LayoutPersistence.SaveToFile(tmp, view);
            var restored = LayoutPersistence.LoadFromFile(tmp);
            Assert.Equal(LayoutPersistence.Serialize(view), LayoutPersistence.Serialize(restored));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    // ── Gate 6: format_version reject-on-mismatch ─────────────────────────────

    [Fact]
    public void Clay_NewerFormatVersion_ThrowsInvalidDataException()
    {
        var json = LayoutPersistence.Serialize(new LayoutView());
        var broken = json.Replace("\"FormatVersion\": 1", "\"FormatVersion\": 999");

        Assert.Throws<InvalidDataException>(() => LayoutPersistence.Deserialize(broken));
    }

    // ── Gate 7: gzip sniff ─────────────────────────────────────────────────────

    [Fact]
    public void Clay_GzippedFile_LoadsSameAsPlainTextFile()
    {
        var view = BuildFullFixture();
        var json = LayoutPersistence.Serialize(view);

        var plainPath = Path.GetTempFileName();
        var gzPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(plainPath, json);

            using (var fs = File.Create(gzPath))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            using (var writer = new StreamWriter(gz, Encoding.UTF8))
                writer.Write(json);

            var fromPlain = LayoutPersistence.LoadFromFile(plainPath);
            var fromGz = LayoutPersistence.LoadFromFile(gzPath);

            Assert.Equal(LayoutPersistence.Serialize(fromPlain), LayoutPersistence.Serialize(fromGz));
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(gzPath);
        }
    }

    // ── Misc ────────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_NullTechRef_NotWrittenToJson()
    {
        var view = new LayoutView();
        var json = LayoutPersistence.Serialize(view);
        Assert.DoesNotContain("TechRef", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_ShapeWithoutNet_NetNotWritten()
    {
        var view = new LayoutView();
        view.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 });
        var json = LayoutPersistence.Serialize(view);
        Assert.DoesNotContain("\"Net\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShorterThanExpectedEdges_PaddedWithLineOnLoad()
    {
        var view = new LayoutView();
        view.Shapes.Add(new CurveShape
        {
            Layer = new LayerKey(1, 0),
            Xy = [0, 0, 100, 0, 100, 100],
            Edges = [new LayoutEdge { Kind = EdgeKind.Arc, Bulge = 0.5 }], // 1 edge, expects 3
        });

        var restored = LayoutPersistence.Deserialize(LayoutPersistence.Serialize(view));
        var curve = Assert.IsType<CurveShape>(Assert.Single(restored.Shapes));

        Assert.Equal(3, curve.Edges!.Count);
        Assert.Equal(EdgeKind.Arc, curve.Edges[0].Kind);
        Assert.Equal(EdgeKind.Line, curve.Edges[1].Kind);
        Assert.Equal(EdgeKind.Line, curve.Edges[2].Kind);
    }

    // ── L1h gate 6a: CircleShape/RoundedRectShape.FlattenTolDbu round-trips with NO FormatVersion
    // change, and a file written before those fields existed still loads. ───────────────────────

    [Fact]
    public void CircleAndRoundedRect_FlattenTolDbu_RoundTrips_NoFormatVersionChange()
    {
        var view = new LayoutView();
        view.Shapes.Add(new CircleShape { Layer = new LayerKey(1, 0), Cx = 0, Cy = 0, R = 5000, FlattenTolDbu = 750 });
        view.Shapes.Add(new RoundedRectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 10_000, Y2 = 10_000, CornerRadius = 1000, FlattenTolDbu = 1250 });

        var json = LayoutPersistence.Serialize(view);
        Assert.Contains("\"FormatVersion\": 1", json);

        var restored = LayoutPersistence.Deserialize(json);
        var circle = Assert.IsType<CircleShape>(restored.Shapes[0]);
        var roundedRect = Assert.IsType<RoundedRectShape>(restored.Shapes[1]);
        Assert.Equal(750, circle.FlattenTolDbu);
        Assert.Equal(1250, roundedRect.FlattenTolDbu);
    }

    [Fact]
    public void CircleAndRoundedRect_WithoutFlattenTolDbu_SerializesWithoutTheField_AndLoadsAsNull()
    {
        var view = new LayoutView();
        view.Shapes.Add(new CircleShape { Layer = new LayerKey(1, 0), Cx = 0, Cy = 0, R = 5000 });
        var json = LayoutPersistence.Serialize(view);

        Assert.DoesNotContain("FlattenTolDbu", json);

        var restored = LayoutPersistence.Deserialize(json);
        Assert.Null(((CircleShape)restored.Shapes[0]).FlattenTolDbu);
    }

    [Fact]
    public void PreExistingClayFile_WithoutTheNewFields_StillLoads()
    {
        // Simulates a file written before L1h added FlattenTolDbu to Circle/RoundedRect — the exact
        // same FormatVersion, simply missing the new (additive, nullable) property.
        const string legacyJson = """
        {
          "FormatVersion": 1,
          "DbuPerMicron": 1000,
          "DisplayUnit": "Um",
          "SnapDbu": 1000,
          "AngleMode": "AnyAngle",
          "Shapes": [
            { "$type": "Circle", "Layer": { "Layer": 1, "Datatype": 0 }, "Cx": 0, "Cy": 0, "R": 5000 }
          ],
          "Instances": []
        }
        """;

        var restored = LayoutPersistence.Deserialize(legacyJson);
        var circle = Assert.IsType<CircleShape>(Assert.Single(restored.Shapes));
        Assert.Equal(5000, circle.R);
        Assert.Null(circle.FlattenTolDbu);
    }

    // ── Label anchoring is additive (owner report, 2026-08-25) ──────────────────

    /// <summary>
    /// <see cref="LabelShape.HAlign"/>/<see cref="LabelShape.VAlign"/> must be invisible in a file that
    /// does not use them — that is what makes every <c>.clay</c> written before they existed load and
    /// render byte-for-byte as it did, with no <c>FormatVersion</c> bump.
    /// </summary>
    [Fact]
    public void LabelAlignment_IsOmittedWhenUnset_AndRoundTripsWhenSet()
    {
        var plain = new LayoutView { DbuPerMicron = 1000 };
        plain.Shapes.Add(new LabelShape { Layer = new LayerKey(1, 0), Text = "T", Height = 1000 });
        var plainJson = LayoutPersistence.Serialize(plain);
        Assert.DoesNotContain("HAlign", plainJson);
        Assert.DoesNotContain("VAlign", plainJson);
        Assert.Null(((LabelShape)LayoutPersistence.Deserialize(plainJson).Shapes[0]).HAlign);

        var aligned = new LayoutView { DbuPerMicron = 1000 };
        aligned.Shapes.Add(new LabelShape
        {
            Layer = new LayerKey(1, 0), Text = "T", Height = 1000,
            HAlign = LabelHAlign.Right, VAlign = LabelVAlign.Top,
        });
        var alignedJson = LayoutPersistence.Serialize(aligned);
        var restored = (LabelShape)LayoutPersistence.Deserialize(alignedJson).Shapes[0];
        Assert.Equal(LabelHAlign.Right, restored.HAlign);
        Assert.Equal(LabelVAlign.Top, restored.VAlign);
        Assert.Equal(alignedJson, LayoutPersistence.Serialize(LayoutPersistence.Deserialize(alignedJson)));
    }

    // ── The PCell-snapshot sniff (owner report, 2026-09-12: a workspace took seconds to OPEN) ──

    /// <summary>
    /// <see cref="LayoutPersistence.MightCarryPCellSnapshots"/> exists so the generated-cell pass on
    /// workspace open can skip a layout without loading it, and it is only safe to skip on a NO. So
    /// the gate is agreement with the full load: whenever the loaded view has snapshots, the sniff
    /// must have said so — and a layout with none, INCLUDING one whose own content spells the name,
    /// must be skippable, since that is the case the whole thing exists to make cheap.
    /// </summary>
    [Fact]
    public void MightCarryPCellSnapshots_NeverMissesSnapshotsTheLoadFinds()
    {
        var without = BuildFullFixture();
        // The name occurring INSIDE the document is not the property: a text search would say yes here
        // and pay the load this exists to avoid.
        without.Shapes.Add(new LabelShape
        {
            Layer = new LayerKey(1, 0), Text = nameof(LayoutView.PCellSnapshots), Height = 1000,
        });
        without.SchematicPCellSnapshots["X1"] = new Dictionary<string, PCellValue>();

        var with = BuildFullFixture();
        with.PCellSnapshots["mline_ab12"] = new PCellSnapshot(
            "wire.mline", new Dictionary<string, PCellValue> { ["w"] = PCellValue.Real(120.0) },
            TechIdentity: null, SignalLayerNameOverride: null, GroundLayerNameOverride: null);

        foreach (var (view, expected) in new[] { (without, false), (with, true) })
        {
            var path = Path.GetTempFileName();
            try
            {
                LayoutPersistence.SaveToFile(path, view);
                bool sniffed = LayoutPersistence.MightCarryPCellSnapshots(path);
                bool loaded = LayoutPersistence.LoadFromFile(path).PCellSnapshots.Count > 0;

                Assert.Equal(expected, loaded);
                Assert.Equal(loaded, sniffed);
            }
            finally { File.Delete(path); }
        }
    }

    /// <summary>
    /// The two ways a file can carry the property without the writer having spelled it the writer's
    /// way: gzipped (<see cref="GzipTextFile"/> is what the sniff reads through, not File.ReadAllText)
    /// and hand-edited in another case (the reader's own JsonSerializerOptions are case-insensitive,
    /// so such a file DOES load its snapshots and the sniff must not be the thing that hides them).
    /// </summary>
    [Fact]
    public void MightCarryPCellSnapshots_SeesThroughGzip_AndIgnoresCase()
    {
        const string hand = """
        {
          "FormatVersion": 1,
          "DbuPerMicron": 1000,
          "pcellsnapshots": {
            "mline_ab12": { "GeneratorId": "wire.mline", "Parameters": {} }
          },
          "Shapes": [],
          "Instances": []
        }
        """;

        var plainPath = Path.GetTempFileName();
        var gzPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(plainPath, hand);
            using (var fs = File.Create(gzPath))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            using (var writer = new StreamWriter(gz, Encoding.UTF8))
                writer.Write(hand);

            // The premise: this file really does load a snapshot, in both spellings on disk.
            Assert.Single(LayoutPersistence.LoadFromFile(plainPath).PCellSnapshots);
            Assert.Single(LayoutPersistence.LoadFromFile(gzPath).PCellSnapshots);

            Assert.True(LayoutPersistence.MightCarryPCellSnapshots(plainPath));
            Assert.True(LayoutPersistence.MightCarryPCellSnapshots(gzPath));
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(gzPath);
        }
    }

    /// <summary>
    /// The reason this TOKENIZES rather than searching the text. The caller's next decision is whether
    /// it has seen every snapshot in the workspace, and a layout nobody could read is one it must
    /// assume carries some — so an unreadable file has to fail here exactly as the full load fails,
    /// rather than come back as a confident "not mentioned" and license a prune.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("{ this is not a layout")]
    [InlineData("{\"FormatVersion\": 1, \"Shapes\": [{\"$type\": \"Rect\"")]
    [InlineData("{\"FormatVersion\": 1, \"Shapes\": [], \"Instances\": []} and then some")]
    public void MightCarryPCellSnapshots_ThrowsOnAFileThatDoesNotParse(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            Assert.ThrowsAny<JsonException>(() => LayoutPersistence.LoadFromFile(path));
            Assert.ThrowsAny<JsonException>(() => LayoutPersistence.MightCarryPCellSnapshots(path));
        }
        finally { File.Delete(path); }
    }
}
