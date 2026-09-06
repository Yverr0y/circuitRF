using System.Text.Json.Serialization;

namespace CircuitRF.Diagnostics;

/// <summary>
/// The serialized form of a <see cref="Diagnostic"/> — what a machine caller reads out of
/// <c>--json</c>'s <c>diagnostics</c> array (brief-automation-1-structured-output.md R-aut1-6).
///
/// <para><b>It lives here, beside the type it projects, and not in the CLI.</b> A protocol adapter
/// must be able to emit the identical shape without going near <c>src/Cli</c>
/// (<c>docs/design/automation-architecture.md</c> R-aut-13), and this project is the leaf every
/// producer of user-facing text already references.</para>
///
/// <para><b><see cref="Message"/> is always present and is always <see cref="Diagnostic.Render"/>.</b>
/// <c>docs/design/cli.md</c> §7A's rule — the CLI is English forever, and culture-invariant — holds
/// inside the JSON exactly as it holds on stderr. The id and the arguments are what a caller keys
/// on; the sentence is what a human reading the document sees, and it is the same sentence stderr
/// carried.</para>
///
/// <para><b>The id is the contract, not the message.</b> <see cref="Diagnostic"/>'s own remarks say
/// it: reword a template freely, change an id and you have made a new diagnostic. A caller matching
/// on <see cref="Message"/> is doing the thing this shape exists to make unnecessary.</para>
/// </summary>
public sealed record DiagnosticJson(
    string Id,
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, object?>? Arguments)
{
    /// <summary>
    /// Projects one diagnostic. An argument-free diagnostic writes no <c>arguments</c> key at all
    /// rather than an empty object — the absence says "this one carries no typed values", which an
    /// empty object does not.
    /// </summary>
    public static DiagnosticJson From(Diagnostic d) => new(
        d.Id,
        // Lower-case, so the wire value is one spelling rather than depending on how the enum
        // happens to be cased in C#. Invariant on purpose: a Turkish-locale ToLower turns "Info"
        // into "ınfo", which is a different string to every caller in the world.
        d.Severity.ToString().ToLowerInvariant(),
        d.Render(),
        d.Arguments.Count == 0 ? null : d.Arguments);
}
