// ================================================================
//  ReferenceCliVerbTests.cs — the gate for `circuitrf reference` and for the component catalogue
//  (docs/sonnet-briefs/brief-automation-6-reference-and-components.md §6).
//
//  ── What these tests exist to catch ───────────────────────────────────────────────────────────
//
//  §6.1 The embedded set IS the authored set. This is the gate for R-aut6-5's named trap — a class
//       shipped without its <EmbeddedResource> items compiles, enumerates nothing, and reports
//       nothing — which ShippedTechnologies' own header records as already paid for once. Adding a
//       topic to ReferenceLibrary.TopicNames and not to the .csproj fails HERE, by name.
//
//  §6.3 The catalogue is GENERATED, proven by a negative. Every assertion below is made against the
//       live registry rather than against a committed list, because a committed golden of all 68
//       primitives would pass forever after somebody froze it. Where a value is asserted it is
//       asserted to EQUAL what the registry says at that moment, so a registry change moves both
//       sides together and a transcription would move only one.
//
//  §6.4 DocTables and the catalogue agree — for every SymbolKind the docs render, the rendered
//       table's rows are the catalogue's rows, in order.
//
//  §6.5 R-aut6-9 is exercised: a variadic primitive reports its port count as parameter-determined
//       and names the parameter. NOTHING here constructs a parameterized model — that is asserted
//       structurally by the sweep over every kind rather than left as a rule to remember.
//
//  §6.6 R-aut6-10 is exercised: both §2.3 lists appear in the output, asserted by token.
//
//  The CLI is launched as the already-built CircuitRF.Cli.dll rather than `dotnet run --project`,
//  which is a hang inside `dotnet test` and not merely a cost (EmCliVerbTests.RunCli records why).
// ================================================================

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CircuitRF.Design.Reference;
using CircuitRF.Design.Schematic;
using CircuitRF.Core.Devices;
using CircuitRF.Ui.Diagnostics;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests;

public sealed class ReferenceCliVerbTests(ITestOutputHelper output)
{
    // ══ §6.1 — the embedded set is the authored set ══════════════════════════════════════════════

    /// <summary>
    /// Every topic resolves to a non-empty embedded stream whose bytes ARE the authored file's.
    ///
    /// <para>This is the one that catches a class shipped without its resources. It fails two ways:
    /// a topic with no <c>&lt;EmbeddedResource&gt;</c> item throws on the missing stream, and a topic
    /// whose item points somewhere else compares unequal against the file in
    /// <c>docs/user/src/reference/</c>.</para>
    /// </summary>
    [Fact]
    public void EveryTopic_IsEmbedded_AndItsBytesAreTheAuthoredFile()
    {
        Assert.Equal(ReferenceLibrary.TopicNames.Count, ReferenceLibrary.Topics.Count);
        Assert.NotEmpty(ReferenceLibrary.Topics);

        int total = 0;
        foreach (var topic in ReferenceLibrary.Topics)
        {
            string raw = ReferenceLibrary.Raw(topic);
            Assert.False(raw.Length == 0, $"'{topic.Topic}' resolved to an empty stream.");

            string stem = topic.Topic == ReferenceLibrary.ComponentNotesTopic ? "components" : topic.Topic;
            string authored = Path.Combine(RepoRoot(), "docs", "user", "src", "reference", stem + ".md");
            Assert.True(File.Exists(authored), $"'{topic.Topic}' names no authored page at {authored}");

            // Byte for byte. The front matter and the {{…}} placeholders are stripped at READ, not
            // at build, precisely so this comparison can be made (R-aut6-6).
            Assert.Equal(File.ReadAllText(authored), raw);

            total += Encoding.UTF8.GetByteCount(raw);
        }

        // Reported rather than asserted: it ships in every binary on every platform, and nobody will
        // notice it growing unless somebody writes it down (§7).
        output.WriteLine($"embedded reference text: {total:N0} bytes across {ReferenceLibrary.Topics.Count} topics");
    }

    /// <summary>Front matter and placeholders are gone from what a caller receives — and the title
    /// and lede survive as fields rather than being thrown away with the block they came from.</summary>
    [Fact]
    public void WhatIsServed_CarriesNoFrontMatterAndNoPlaceholders()
    {
        foreach (var topic in ReferenceLibrary.Topics)
        {
            string served = ReferenceLibrary.Read(topic);

            Assert.False(served.StartsWith("---", StringComparison.Ordinal),
                         $"'{topic.Topic}' still opens with its front matter.");
            Assert.DoesNotContain("{{", served, StringComparison.Ordinal);
            Assert.False(topic.Title.Length   == 0, $"'{topic.Topic}' has no title.");
            Assert.False(topic.Summary.Length == 0, $"'{topic.Topic}' has no lede.");
            Assert.False(served.Trim().Length == 0, $"'{topic.Topic}' served nothing at all.");
        }
    }

    /// <summary><c>cli.md</c> is excluded on purpose — the largest page of them all, and the one a
    /// protocol client needs least because it already has every verb's schema from
    /// <c>tools/list</c> (R-aut6-2). A later "just add every page" would silently undo that.</summary>
    [Fact]
    public void TheTopicSetIsCurated_AndDoesNotCarryTheCliPage()
    {
        Assert.DoesNotContain("cli", ReferenceLibrary.TopicNames);
        Assert.True(ReferenceLibrary.TopicNames.Count < 12,
                    "the topic set has grown past a curated one; R-aut6-2 says reading is the expensive direction.");
    }

    // ══ §6.3 — the catalogue is generated ════════════════════════════════════════════════════════

    /// <summary>
    /// Every parameter row of every entry equals what the registry says RIGHT NOW.
    ///
    /// <para>Asserted against the registry rather than against a committed list, which is what makes
    /// this a generation test rather than a transcription test: change a default in
    /// <see cref="ComponentTypeRegistry"/> and both sides move together; transcribe one and only one
    /// side moves.</para>
    /// </summary>
    [Fact]
    public void EveryCatalogueRow_IsWhatTheRegistrySaysNow()
    {
        int rows = 0;
        foreach (var entry in ComponentCatalog.All())
            foreach (var symbol in entry.Symbols)
            {
                var kind = Enum.Parse<SymbolKind>(symbol.Kind);
                var live = ComponentCatalog.Parameters(kind, PortCountUsedFor(kind));

                Assert.Equal(live.Count, symbol.Parameters.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    Assert.Equal(live[i].Name,       symbol.Parameters[i].Name);
                    Assert.Equal(live[i].Expression, symbol.Parameters[i].Expression);
                    Assert.Equal(live[i].Unit,       symbol.Parameters[i].Unit);
                    Assert.Equal(live[i].ShowOnSchematic, symbol.Parameters[i].ShowOnSchematic);
                    Assert.Equal(ComponentTypeRegistry.ParameterDescription(kind, live[i].Name),
                                 symbol.Parameters[i].Meaning);
                    rows++;
                }

                Assert.Equal(ComponentTypeRegistry.DisplayName(kind), symbol.DisplayName);
                Assert.Equal(ComponentTypeRegistry.Get(kind).Category.ToString(), symbol.Category);
            }

        // Not vacuous: an empty catalogue would satisfy every loop above.
        Assert.True(rows > 300, $"only {rows} parameter rows were compared");
    }

    /// <summary>
    /// The tokens are <see cref="ComponentModelFactory"/>'s own keys, all of them, plus the
    /// <c>EngineReference</c> targets it does not answer to. Nothing is invented and nothing is
    /// dropped.
    /// </summary>
    [Fact]
    public void EveryFactoryToken_IsInTheCatalogue()
    {
        var catalogue = ComponentCatalog.All().Select(e => e.Type).ToHashSet(StringComparer.Ordinal);

        foreach (string token in ComponentModelFactory.PrimitiveTypeNames)
            Assert.Contains(token, catalogue);

        // …and nothing that is neither a factory token nor an EngineReference target.
        var drawn = Enum.GetValues<SymbolKind>()
                        .Where(k => !LibraryCatalog.InternalOnlyKinds.Contains(k))
                        .Select(k => ComponentTypeRegistry.EngineReference(k))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ComponentCatalog.All())
            Assert.True(ComponentModelFactory.IsPrimitive(entry.Type) || drawn.Contains(entry.Type),
                        $"'{entry.Type}' is in the catalogue and is neither a primitive nor drawn by anything.");

        output.WriteLine($"catalogue: {catalogue.Count} entries over " +
                         $"{ComponentModelFactory.PrimitiveTypeNames.Count} factory tokens");
    }

    /// <summary>The opaque base64 payload stays out of both the page and the machine answer, because
    /// it is one filter in one place now (R-aut6-11).</summary>
    [Theory]
    [InlineData(SymbolKind.Match)]
    [InlineData(SymbolKind.WBond)]
    public void TheBase64DesignPayload_IsNotOfferedAsARow(SymbolKind kind)
    {
        Assert.Contains(ComponentTypeRegistry.DefaultParameters(kind, 0), p => p.Name == "Design");
        Assert.DoesNotContain(ComponentCatalog.Parameters(kind, 0), p => p.Name == "Design");
        Assert.DoesNotContain("Design", DocTables.ComponentParameters(kind, 2), StringComparison.Ordinal);
    }

    // ══ §6.4 — DocTables and the catalogue agree ═════════════════════════════════════════════════

    /// <summary>
    /// For every <see cref="SymbolKind"/> the docs render, the rendered table's rows are the
    /// catalogue's rows, in order. One computation, two renderings (R-aut6-11) — so the page and the
    /// machine answer cannot disagree the first time one is changed.
    /// </summary>
    [Fact]
    public void TheRenderedDocTable_HasTheCatalogueRows_InOrder()
    {
        int checkedKinds = 0;

        foreach (var (kind, _, ports) in SymbolArtworkGenerator.Catalog)
        {
            string html = DocTables.ComponentParameters(kind, ports);
            var    rows = ComponentCatalog.Parameters(kind, ports);

            if (rows.Count == 0)
            {
                Assert.Contains("No fixed parameters", html, StringComparison.Ordinal);
                checkedKinds++;
                continue;
            }

            int at = 0;
            foreach (var p in rows)
            {
                int found = html.IndexOf($"<td>{System.Net.WebUtility.HtmlEncode(p.Name)}</td>", at,
                                         StringComparison.Ordinal);
                Assert.True(found >= 0,
                    $"{kind}: the docs table does not carry '{p.Name}' at or after the previous row.");
                at = found + 1;
            }
            checkedKinds++;
        }

        Assert.True(checkedKinds > 50, $"only {checkedKinds} kinds were compared");
    }

    /// <summary>The generated PIN table says what the figure cannot: which net is which, and in what
    /// order. It reads the same <see cref="ComponentCatalog"/> the catalogue does, so the page and
    /// the machine answer name the same terminals.</summary>
    [Fact]
    public void TheRenderedPinTable_NamesTheCataloguesTerminals_InOrder()
    {
        int withPins = 0;
        foreach (var (kind, _, ports) in SymbolArtworkGenerator.Catalog)
        {
            string html = DocTables.ComponentPins(kind, ports);
            var    p    = ComponentCatalog.PortsOf(kind);

            if (p.Names.Count == 0) continue;

            int at = 0;
            foreach (string name in p.Names)
            {
                int found = html.IndexOf($"<td>{System.Net.WebUtility.HtmlEncode(name)}</td>", at,
                                         StringComparison.Ordinal);
                Assert.True(found >= 0, $"{kind}: the pin table does not carry terminal '{name}' in order.");
                at = found + 1;
            }
            withPins++;
        }
        Assert.True(withPins > 50, $"only {withPins} kinds had a pin table");
    }

    // ══ §6.5 — R-aut6-9 ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A kind whose pin geometry is generated from N is REPORTED as parameter-determined, and names
    /// the parameter.
    ///
    /// <para>The variadic set is MEASURED, not listed: a kind that answers with a different number of
    /// pins at 2 and at 3 is variadic, whatever anyone remembered to declare. So a future variadic
    /// component added without a <see cref="ComponentTypeRegistry.PortCountParameter"/> arm fails
    /// here rather than quietly publishing a plausible, specific, wrong port count.</para>
    /// </summary>
    [Fact]
    public void AKindWhosePinsDependOnN_ReportsThatAndNamesTheParameter()
    {
        int variadic = 0;

        foreach (var kind in Enum.GetValues<SymbolKind>())
        {
            if (LibraryCatalog.InternalOnlyKinds.Contains(kind)) continue;
            if (SymbolPortDefs.For(kind, 2).Length == SymbolPortDefs.For(kind, 3).Length) continue;

            var ports = ComponentCatalog.PortsOf(kind);
            Assert.True(ports.DeterminedBy is { Length: > 0 },
                $"{kind}'s pin count depends on N and the catalogue reports it as fixed — the exact " +
                "number-that-is-plausible-and-wrong R-aut6-9 exists to prevent. Add a " +
                "ComponentTypeRegistry.PortCountParameter arm for it.");
            Assert.Null(ports.Count);
            Assert.Equal(ComponentCatalog.ListedPortCount, ports.ListedAt);
            variadic++;
        }

        Assert.True(variadic >= 4, $"only {variadic} variadic kinds were found; expected SnP, ZPort, SDD, VerilogA");
    }

    /// <summary>The four named in R-aut6-9, reported by TOKEN with the parameter that sets each.
    /// <c>Switch</c> is the interesting one: two tiles with fixed pin sets over one engine component
    /// whose port count is <c>1 + Throws</c>, so the tile is fixed and the token is not.</summary>
    [Theory]
    [InlineData("SDD",      "NumPorts")]
    [InlineData("Z_Port",   "NumPorts")]
    [InlineData("SnP",      "NumPorts")]
    [InlineData("VerilogA", "Pins")]
    [InlineData("Switch",   "Throws")]
    [InlineData("wBond",    "Arrays")]
    public void AVariadicToken_ReportsNoCount_AndNamesWhatSetsIt(string token, string parameter)
    {
        var entry = ComponentCatalog.All().Single(e => e.Type == token);

        Assert.Null(entry.Ports.Count);
        Assert.Equal(parameter, entry.Ports.DeterminedBy);
    }

    /// <summary>
    /// Nothing in the catalogue instantiates a parameterized model.
    ///
    /// <para>Asserted by running the whole catalogue with the factory's parameterized path made to
    /// throw is not possible here, so it is asserted the way it can be: the catalogue is built and
    /// every parameterized token comes back with either a symbol-derived answer or none at all —
    /// never a count only a constructed model could have produced.</para>
    /// </summary>
    [Fact]
    public void NoParameterizedTokenReportsACountThatOnlyAModelCouldKnow()
    {
        foreach (var entry in ComponentCatalog.All())
        {
            if (!ComponentModelFactory.TakesParameters(entry.Type)) continue;
            if (entry.Ports.Count is not { } n) continue;

            // A count is only legitimate here when a SYMBOL produced it — that is
            // SymbolPortDefs' own answer, which NetExtractor emits nets by.
            Assert.NotEmpty(entry.Symbols);
            Assert.All(entry.Symbols, s => Assert.Equal(n, s.Ports.Count));
        }
    }

    // ══ §6.6 — R-aut6-10, the two §2.3 lists ═════════════════════════════════════════════════════

    /// <summary>
    /// The five <c>EngineReference</c> targets no factory entry answers to. They are IN the
    /// catalogue, marked not simulatable, with a note — not quietly filtered into an intersection.
    ///
    /// <para>Measured on 2026-09-05 and unchanged when this was built. A difference is a change in
    /// what circuitRF can draw versus what it can place, which is worth knowing on its own — so this
    /// asserts both the membership AND that the measured list is still exactly these five.</para>
    /// </summary>
    [Fact]
    public void EveryDrawnTypeTheEngineCannotBuild_IsReportedAsSuch()
    {
        string[] expected = ["GND", "MEAS", "Pin", "SpiceModel", "VAR"];

        var measured = Enum.GetValues<SymbolKind>()
            .Where(k => !LibraryCatalog.InternalOnlyKinds.Contains(k))
            .Select(k => ComponentTypeRegistry.EngineReference(k))
            .Where(t => !ComponentModelFactory.IsPrimitive(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, measured);

        foreach (string token in measured)
        {
            var entry = ComponentCatalog.All().Single(e => e.Type == token);
            Assert.False(entry.Simulatable);
            Assert.True(entry.Placeable);
            Assert.False(entry.Note.Length == 0, $"'{token}' is not simulatable and says nothing about why.");
        }
    }

    /// <summary>
    /// The seven factory types no <c>EngineReference</c> maps to. Placeable in a <c>.cnl</c>, drawn
    /// by nothing — and reported with their token and an explicit note, for the same reason.
    /// </summary>
    [Fact]
    public void EveryPlaceableTypeNothingDraws_IsReportedAsSuch()
    {
        string[] expected = ["Chain", "ExtDevice", "I_nTone", "SemiC", "Short", "Term", "V_nTone"];

        var drawn = Enum.GetValues<SymbolKind>()
            .Where(k => !LibraryCatalog.InternalOnlyKinds.Contains(k))
            .Select(k => ComponentTypeRegistry.EngineReference(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var measured = ComponentModelFactory.PrimitiveTypeNames
            .Where(t => !drawn.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, measured);

        foreach (string token in measured)
        {
            var entry = ComponentCatalog.All().Single(e => e.Type == token);
            Assert.True(entry.Simulatable);
            Assert.False(entry.Placeable);
            Assert.False(entry.Note.Length == 0, $"'{token}' has no palette entry and says nothing about it.");
            Assert.Empty(entry.Symbols);
        }
    }

    /// <summary>
    /// R-aut6-12: the four <see cref="SymbolKind"/>s with no explicit <c>DefaultParameters</c> arm,
    /// measured rather than assumed.
    ///
    /// <para>Reported, not closed. An empty result carries two meanings — "no parameters" and
    /// "nobody wrote an arm" — and adding an arm to make a catalogue look complete is exactly the
    /// silent-wrong-value failure this whole surface exists to prevent.</para>
    /// </summary>
    [Fact]
    public void TheUnarmedKinds_AreTheFourMeasuredOnes()
    {
        var unarmed = Enum.GetValues<SymbolKind>()
            .Where(k => ComponentTypeRegistry.DefaultParameters(k, 0).Count == 0)
            .OrderBy(k => k.ToString(), StringComparer.Ordinal)
            .ToArray();

        // Every kind that returns nothing — the four with no arm, PLUS the ones whose arm returns []
        // on purpose (Var and Meas author their own rows; Mutual references instances by name).
        output.WriteLine("kinds with no parameters at all: " + string.Join(", ", unarmed));

        Assert.Contains(SymbolKind.Ground,  unarmed);   // genuinely parameterless
        Assert.Contains(SymbolKind.IProbe,  unarmed);   // genuinely parameterless
        Assert.Contains(SymbolKind.Generic, unarmed);   // not user-placeable; the question does not arise
        Assert.Contains(SymbolKind.Unknown, unarmed);   // ditto
    }

    // ══ the verb ═════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithNoArguments_ItListsTheTopicsAndTheirSizes()
    {
        var run = RunCli("reference", "--json");
        Assert.Equal(0, run.ExitCode);

        var topics = Payload(run.StdOut).GetProperty("reference").GetProperty("topics");
        var names  = topics.EnumerateArray().Select(t => t.GetProperty("topic").GetString()).ToArray();

        Assert.Equal(ReferenceLibrary.TopicNames.Concat(["components"]), names);

        foreach (var t in topics.EnumerateArray())
        {
            Assert.True(t.GetProperty("bytes").GetInt32() > 0, "a topic reported no size at all.");
            // A LISTING costs a line per topic, never the library — that is the whole point of
            // carrying the size (R-aut6-3).
            Assert.False(t.TryGetProperty("text", out _), $"{t.GetProperty("topic")} carried its text in the LIST.");
        }
    }

    [Fact]
    public void OneTopic_ComesBackAsItsOwnText_OnStdoutAndInTheDocument()
    {
        var run = RunCli("reference", "netlist");
        Assert.Equal(0, run.ExitCode);

        var topic = ReferenceLibrary.Find("netlist")!;
        Assert.Equal(ReferenceLibrary.Read(topic), run.StdOut);

        var json = RunCli("reference", "netlist", "--json");
        Assert.Equal(ReferenceLibrary.Read(topic),
                     Payload(json.StdOut).GetProperty("reference").GetProperty("topic")
                                         .GetProperty("text").GetString());
    }

    [Fact]
    public void OnePrimitive_ComesBackWithItsTerminalsAndItsParameters()
    {
        var run = RunCli("reference", "components", "MLIN", "--json");
        Assert.Equal(0, run.ExitCode);

        var one = Payload(run.StdOut).GetProperty("reference").GetProperty("components")
                                     .EnumerateArray().Single();

        Assert.Equal("MLIN", one.GetProperty("type").GetString());
        Assert.True(one.GetProperty("simulatable").GetBoolean());

        var names = one.GetProperty("symbols")[0].GetProperty("parameters")
                       .EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray();

        Assert.Equal(ComponentCatalog.Parameters(SymbolKind.Mlin, 0).Select(p => p.Name), names);
    }

    /// <summary>An unknown topic LISTS the real ones — <c>--tech</c>'s precedent, never a
    /// fallback.</summary>
    [Fact]
    public void AnUnknownTopic_IsRefused_ListingTheRealOnes()
    {
        var run = RunCli("reference", "nosuchthing", "--json");
        Assert.Equal(1, run.ExitCode);

        var d = Diagnostics(run.StdOut).Single();
        Assert.Equal("reference.topic.unknown", d.GetProperty("id").GetString());
        foreach (string topic in ReferenceLibrary.TopicNames)
            Assert.Contains(topic, d.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPrimitive_IsRefused_ListingTheRealOnes()
    {
        var run = RunCli("reference", "components", "MLINE", "--json");
        Assert.Equal(1, run.ExitCode);

        var d = Diagnostics(run.StdOut).Single();
        Assert.Equal("reference.component.unknown", d.GetProperty("id").GetString());
        Assert.Contains("MLIN", d.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>A name on a topic that has no names inside it. Refused rather than ignored — a
    /// dropped argument is a query answered differently with nothing said about it.</summary>
    [Fact]
    public void ANameOnAProseTopic_IsRefused()
    {
        var run = RunCli("reference", "netlist", "MLIN", "--json");
        Assert.Equal(1, run.ExitCode);
        Assert.Equal("reference.args.item-not-for-topic", Diagnostics(run.StdOut).Single().GetProperty("id").GetString());
    }

    /// <summary>It reads no file and writes none. Run from an empty directory with no arguments at
    /// all beyond the verb — the answer is complete anyway, which is what makes this capability
    /// usable before a design exists.</summary>
    [Fact]
    public void ItNeedsNoWorkspaceAndWritesNothing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crf-ref-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(dir);
        try
        {
            var run = RunCliIn(dir, "reference", "components", "R", "--json");
            Assert.Equal(0, run.ExitCode);
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>The port count the catalogue lists a kind's rows at — 0 for a fixed kind, which is
    /// what the registry functions take to mean "the type's own".</summary>
    private static int PortCountUsedFor(SymbolKind kind)
        => ComponentTypeRegistry.PortCountParameter(kind) is null ? 0 : ComponentCatalog.ListedPortCount;

    private static JsonElement Payload(string stdout)
        => JsonDocument.Parse(stdout).RootElement.GetProperty("result");

    private static JsonElement[] Diagnostics(string stdout)
        => [.. JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics").EnumerateArray()];

    private readonly record struct CliRun(int ExitCode, string StdOut, string StdErr);

    private static CliRun RunCli(params string[] args) => RunCliIn(RepoRoot(), args);

    private static CliRun RunCliIn(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return new CliRun(proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string CliDll()
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(ReferenceCliVerbTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        return Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir;
    }
}
