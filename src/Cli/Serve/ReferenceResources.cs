using System.Text;
using System.Text.Json.Nodes;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Reference;
using CircuitRF.Design.Schematic;

namespace CircuitRF.Cli.Serve;

/// <summary>
/// The reference surface as MCP RESOURCES — the same topics the <c>reference</c> tool serves, on the
/// channel that costs less.
///
/// <para><b>Why both channels, in the same commit.</b> A resource costs a URI and a title until it is
/// read, where a tool description is a standing per-session cost paid whether or not anything calls
/// it (<c>automation-architecture.md</c> R-aut-9, R-aut5-4) — so resources are the correct channel.
/// But not every client surfaces resources to the model, and a capability the model cannot reach is
/// not a capability, so the tool exists too (R-aut6-4).</para>
///
/// <para><b>They cannot disagree, and that is structural rather than tested-for.</b> Both channels
/// translate to <c>circuitrf reference &lt;topic&gt; --json</c> and hand back what came out of
/// <see cref="CliEntry.Run"/>. The parity gate compares two documents that came from one function
/// (R-aut-13).</para>
///
/// <para><b>The URI is the topic.</b> <c>circuitrf://reference/&lt;topic&gt;</c> — no path, no
/// query, nothing to escape, and a client that read the topic list from the CLI can build one.
/// Deliberately NOT a <c>file:</c> URI: none of this is a file in the client's tree, and
/// <c>circuitrf read</c>'s path confinement is what would have had to be bent to pretend otherwise
/// (R-aut6-4).</para>
/// </summary>
internal static class ReferenceResources
{
    private const string Scheme = "circuitrf://reference/";

    /// <summary>Every resource URI, in the order the topic list prints them.</summary>
    public static IEnumerable<string> Uris() => Reference.AllTopics.Select(t => Scheme + t);

    /// <summary>The topic a URI names, or null when it names none.</summary>
    public static string? TopicOf(string uri)
    {
        if (!uri.StartsWith(Scheme, StringComparison.Ordinal)) return null;
        string topic = uri[Scheme.Length..];
        return Reference.AllTopics.Any(t => string.Equals(t, topic, StringComparison.Ordinal))
            ? topic
            : null;
    }

    /// <summary>
    /// The <c>resources/list</c> payload.
    ///
    /// <para>Each entry carries its <c>size</c>, for the reason the CLI's topic list carries one: a
    /// client choosing between a 4.7 kB page and a 37 kB one should be able to choose, and a list
    /// that hides the cost makes the cheap topics and the expensive ones look alike (R-aut6-3). The
    /// size is of the TEXT the topic serves, not of the JSON envelope it arrives in.</para>
    /// </summary>
    public static JsonArray Advertise()
    {
        var resources = new JsonArray();

        foreach (var topic in ReferenceLibrary.Topics)
            resources.Add(Describe(topic.Topic, topic.Title, topic.Summary,
                                   Encoding.UTF8.GetByteCount(ReferenceLibrary.Read(topic))));

        // The two generated FORMAT topics. Advertised on the resource channel like every other,
        // measured by rendering them, and the summary is the one the CLI's own topic list prints.
        foreach (var f in DocumentSchema.All)
            resources.Add(Describe(f.Topic, f.Title, Reference.SchemaSummary(f),
                                   Encoding.UTF8.GetByteCount(DocumentSchema.Render(f))));

        resources.Add(Describe(
            Reference.AnalysesTopic,
            "Analysis directives",
            $"Generated from the schema the .cnl reader validates against: the " +
            $"{AnalysisDirectiveSchema.Specs.Count} analysis type= tokens, their other accepted " +
            "spellings, and every key with its default and whether it is required.",
            Encoding.UTF8.GetByteCount(Reference.AnalysesText())));

        var catalog = ComponentCatalog.All();
        resources.Add(Describe(
            Reference.ComponentsTopic,
            "Component types",
            $"Generated from the live registries: the {catalog.Count} .cnl type tokens, how many " +
            "nets each instance line binds, their terminals, and every parameter with its default, " +
            "unit and visibility.",
            // Measured by rendering it, the way the CLI's own topic list measures it. The catalogue
            // is a walk over static tables, so this is cheap, and a hard-coded number here would be a
            // second fact about the first that nothing keeps in step.
            Encoding.UTF8.GetByteCount(Reference.CatalogText())));

        return resources;
    }

    private static JsonObject Describe(string topic, string title, string summary, int size) => new()
    {
        ["uri"]         = Scheme + topic,
        ["name"]        = topic,
        ["title"]       = title,
        ["description"] = summary,
        // application/json, not text/markdown: what comes back is the verb's own `--json` document
        // with the text inside it, because a resource and a tool call must return the same bytes
        // (R-aut-13). A client that wants the prose reads `result.reference.topic.text`.
        ["mimeType"]    = "application/json",
        ["size"]        = size,
    };
}
