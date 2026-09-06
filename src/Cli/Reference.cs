using System.Text;
using CircuitRF.Design.Reference;
using CircuitRF.Design.Schematic;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf reference [topic] [item]</c> — what a caller may WRITE, before it writes it
/// (brief-automation-6-reference-and-components.md).
///
/// <para><b>Why this verb exists.</b> <c>check</c> tells a caller that what it wrote is wrong;
/// <c>explain</c> tells it what circuitRF made of what it wrote. Neither tells it what it is
/// ALLOWED to write — the primitive type names, how many nets each takes, what its parameters are
/// called, what a unit suffix means, where a <c>define … end</c> block goes. A client that cannot
/// spell <c>MLIN</c> is blocked before <c>check</c> can help it, and the failure is quiet: a
/// component given a plausible-but-wrong parameter name resolves to that parameter's default and
/// simulates, producing a converged, complete-looking, wrong answer.</para>
///
/// <para><b>Two halves, and they are different in kind.</b> The prose topics are AUTHORED and
/// maintained (<see cref="ReferenceLibrary"/>); the component catalogue is GENERATED from the live
/// registries at every call (<see cref="ComponentCatalog"/>). There is no third thing — in
/// particular no grammar, schema or BNF, because a hand-written grammar here would be a second
/// description of <c>CnlReader</c> that drifts from it silently, which is the exact failure this
/// whole series exists to prevent (§5).</para>
///
/// <para><b>It is read-only in every sense</b> — no analysis, no document, nothing written. Like
/// <c>check</c> and <c>explain</c> it runs on a read-only tree and on a workspace another process
/// has open (R-aut4-6), and unlike them it does not even take a path: a catalogue is about no
/// document, which is also why it is not a mode of <c>explain</c> (§5).</para>
///
/// <para><b>One verb with a topic, not one verb per topic</b> (R-aut6-1). An unknown topic is a
/// refusal LISTING the real ones, following <c>--tech</c>'s precedent from AUT-3 — never a
/// fallback, because a fallback here answers a different question than the one asked and says
/// nothing about it.</para>
/// </summary>
internal static class Reference
{
    /// <summary>The topic name the generated catalogue answers to. The prose page about components
    /// ships as <see cref="ReferenceLibrary.ComponentNotesTopic"/> — see there for why the machine
    /// answer keeps the plain name.</summary>
    public const string ComponentsTopic = "components";

    /// <summary>Every topic a caller may ask for, in the order the list prints them. Built from the
    /// library rather than written down, so a topic added there appears here with nothing to
    /// remember.</summary>
    public static IEnumerable<string> AllTopics
        => ReferenceLibrary.TopicNames.Concat([ComponentsTopic]);

    public static int Run(string[] args)
    {
        string? topic = null, item = null;

        foreach (string a in args)
        {
            // No options at all, deliberately. `--json`, `--only` and `--group` are taken before
            // dispatch; anything else is a flag this verb does not have, and a dropped flag's value
            // would become the topic.
            if (a.StartsWith('-'))    return JsonRun.Fail(CliDiagnostics.ReferenceUnknownOption(a));
            if (topic is null)        topic = a;
            else if (item is null)    item  = a;
            else                      return JsonRun.Fail(CliDiagnostics.ReferenceTooManyArguments());
        }

        if (topic is null) return ListTopics(item);

        if (string.Equals(topic, ComponentsTopic, StringComparison.OrdinalIgnoreCase))
            return Components(item);

        if (item is not null) return JsonRun.Fail(CliDiagnostics.ReferenceItemNotForTopic(topic));

        if (ReferenceLibrary.Find(topic) is not { } found)
            return JsonRun.Fail(CliDiagnostics.ReferenceUnknownTopic(topic, string.Join(", ", AllTopics)));

        return Topic(found);
    }

    // ── the topic list ───────────────────────────────────────────────────────

    /// <summary>
    /// Every topic and what it costs. The size is the SERVED size — a client choosing between a
    /// 4.7 kB page and a 37 kB one should be able to choose, and a list that hides the cost makes
    /// the cheap topics and the expensive ones look alike (R-aut6-3).
    /// </summary>
    private static int ListTopics(string? strayItem)
    {
        // `circuitrf reference <nothing> <something>` cannot happen from the command line, but a
        // tool call can name an item with no topic and it is a refusal rather than a silent drop.
        if (strayItem is not null) return JsonRun.Fail(CliDiagnostics.ReferenceItemWithoutTopic());

        var topics = new List<ReferenceTopicJson>();
        foreach (var t in ReferenceLibrary.Topics)
            topics.Add(new ReferenceTopicJson(t.Topic, t.Title, t.Summary, ByteLength(ReferenceLibrary.Read(t))));

        var catalog = ComponentCatalog.All();
        topics.Add(new ReferenceTopicJson(
            ComponentsTopic,
            "Component types",
            $"Generated from the live registries: the {catalog.Count} .cnl type tokens, their " +
            "terminals, and every parameter with its default, unit and visibility. Name one to get " +
            "just that primitive.",
            ByteLength(RenderComponents(catalog))));

        JsonRun.Reference = new ReferenceReportJson(topics, null, null);

        Console.WriteLine("Reference topics — circuitrf reference <topic>");
        Console.WriteLine();
        foreach (var t in topics)
            Console.WriteLine($"  {t.Topic,-17} {Kb(t.Bytes),8}  {t.Title}");
        Console.WriteLine();
        Console.WriteLine("  circuitrf reference components <TYPE>   one primitive");
        return 0;
    }

    // ── one prose topic ──────────────────────────────────────────────────────

    private static int Topic(ReferenceTopic topic)
    {
        string text = ReferenceLibrary.Read(topic);

        JsonRun.Reference = new ReferenceReportJson(
            null, new ReferenceTopicJson(topic.Topic, topic.Title, topic.Summary, ByteLength(text), text), null);

        // Write, not WriteLine — the page ends with its own newline and adding a second one would
        // make `circuitrf reference netlist > netlist.md` differ from the page by a byte.
        Console.Out.Write(text);
        return 0;
    }

    // ── the catalogue ────────────────────────────────────────────────────────

    private static int Components(string? type)
    {
        var all = ComponentCatalog.All();

        var chosen = type is null
            ? all
            : [.. all.Where(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))];

        if (chosen.Count == 0)
            return JsonRun.Fail(CliDiagnostics.ReferenceUnknownComponent(
                type!, string.Join(", ", all.Select(e => e.Type))));

        JsonRun.Reference = new ReferenceReportJson(null, null, [.. chosen.Select(ToJson)]);

        Console.Out.Write(RenderComponents(chosen));
        return 0;
    }

    private static ReferenceComponentJson ToJson(CatalogEntry e) => new(
        e.Type, e.Simulatable, e.Placeable,
        e.Note.Length == 0 ? null : e.Note,
        ToJson(e.Ports),
        [.. e.Symbols.Select(s => new ReferenceSymbolJson(
            s.Kind, s.DisplayName, s.Category, s.SearchTerms, ToJson(s.Ports),
            [.. s.Parameters.Select(p => new ReferenceParameterJson(
                p.Name, p.Expression, p.Unit, p.Dimension, p.ShowOnSchematic,
                p.Meaning.Length == 0 ? null : p.Meaning))]))]);

    private static ReferencePortsJson ToJson(CatalogPorts p)
        => new(p.Count, p.Names, p.DeterminedBy, p.ListedAt,
               p.OrderNote.Length == 0 ? null : p.OrderNote);

    /// <summary>The catalogue's own text, rendered without running the verb — what the topic list
    /// and the resource listing measure to state a size. Pure: it touches neither
    /// <see cref="JsonRun"/> nor <see cref="Console"/>.</summary>
    public static string CatalogText() => RenderComponents(ComponentCatalog.All());

    /// <summary>
    /// The human form. Same catalogue, same order, same facts — the two forms read one computation,
    /// so they cannot disagree (R-aut1-9's rule, applied again).
    /// </summary>
    private static string RenderComponents(IReadOnlyList<CatalogEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(e.Type);
            if (!e.Simulatable) sb.Append("   [not simulatable]");
            else if (!e.Placeable) sb.Append("   [no palette entry]");
            sb.AppendLine();

            sb.Append("  nets: ").AppendLine(Ports(e.Ports));
            // The ORDER note comes before the type note: it is about the thing the caller is holding
            // (which net goes where), while the type note is about the catalogue's own bookkeeping.
            if (e.Ports.OrderNote.Length > 0) sb.Append("  order: ").AppendLine(e.Ports.OrderNote);
            if (e.Note.Length > 0) sb.Append("  note: ").AppendLine(e.Note);

            foreach (var s in e.Symbols)
            {
                sb.Append($"  {s.Kind} ({s.DisplayName}) — {s.Category}");
                if (s.SearchTerms.Count > 0) sb.Append("   search: ").Append(string.Join(", ", s.SearchTerms));
                sb.AppendLine();
                sb.Append("    nets: ").AppendLine(Ports(s.Ports));
                // Repeated per symbol rather than only at the token, because a token whose tiles
                // disagree drops it above and the tile is then the only place it is stated.
                if (s.Ports.OrderNote.Length > 0) sb.Append("    order: ").AppendLine(s.Ports.OrderNote);

                if (s.Parameters.Count == 0)
                {
                    sb.AppendLine("    parameters: none declared");
                    continue;
                }
                foreach (var p in s.Parameters)
                {
                    string row = $"    {p.Name,-18} {(p.Expression.Length == 0 ? "-" : p.Expression),-16} " +
                                 $"{(p.Unit.Length == 0 ? "-" : p.Unit),-6} {(p.ShowOnSchematic ? "shown" : "-"),-6}";
                    if (p.Meaning.Length > 0) row += "  " + p.Meaning;
                    // Trimmed, because the column padding is there to line the NEXT column up and a
                    // row with nothing after it would otherwise carry the padding into the output —
                    // trailing whitespace a caller diffs against and a reader cannot see.
                    sb.AppendLine(row.TrimEnd());
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Ports(CatalogPorts p)
    {
        if (p.DeterminedBy is { } by)
            return p.Names.Count == 0
                ? $"set by {by}"
                : $"set by {by} — at {by}={p.ListedAt}: {string.Join(" ", p.Names)}";
        if (p.Count is { } n) return $"{n}  {string.Join(" ", p.Names)}";
        return "not stated below the UI firewall";
    }

    private static int ByteLength(string text) => Encoding.UTF8.GetByteCount(text);

    private static string Kb(int bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024.0:0.#} kB";
}
