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
    ///
    /// <para><b><c>arguments.text</c> is emitted only when it differs from <c>message</c></b>
    /// (AUT-9 R-aut9-8). A large family of diagnostics here is templated <c>"{text}"</c> — the whole
    /// sentence is one substituted value, forwarded from a reader or an elaborator that authored
    /// prose rather than a datum — so the two came out byte-identical on the great majority of them.
    /// One Gerber import was ~15 KB of exact duplication in a single response, and it carried no
    /// information: a caller that wants the sentence reads <c>message</c>, which is always present.
    /// It survives wherever it differs, which is every template that says anything of its own around
    /// the value (<c>"{path}: {text}"</c> keeps both).</para>
    ///
    /// <para><b>Only <c>text</c>, and that is deliberate.</b> The rule is by NAME rather than "any
    /// argument that equals the message", because the two are not the same thing:
    /// <c>convert.cell.listed</c> is templated <c>"{cell}"</c>, and its argument is the answer to
    /// the question the caller asked — one cell name — which happens to be the whole sentence. An
    /// argument called <c>text</c> is prose by convention here; every other name is a value, and a
    /// value must not be dropped for coinciding with the sentence built out of it.</para>
    /// </summary>
    public static DiagnosticJson From(Diagnostic d)
    {
        // Lower-case, so the wire value is one spelling rather than depending on how the enum
        // happens to be cased in C#. Invariant on purpose: a Turkish-locale ToLower turns "Info"
        // into "ınfo", which is a different string to every caller in the world.
        string severity = d.Severity.ToString().ToLowerInvariant();
        string message  = d.Render();

        if (d.Arguments.Count == 0) return new DiagnosticJson(d.Id, severity, message, null);

        // Ordinal, because this is a byte-identity test and nothing else: a string that merely
        // happens to compare equal under some culture's collation is not the message.
        if (!d.Arguments.TryGetValue(RedundantArgument, out var carried) ||
            carried is not string prose || !string.Equals(prose, message, StringComparison.Ordinal))
            return new DiagnosticJson(d.Id, severity, message, d.Arguments);

        if (d.Arguments.Count == 1) return new DiagnosticJson(d.Id, severity, message, null);

        var kept = new Dictionary<string, object?>(d.Arguments.Count - 1, StringComparer.Ordinal);
        foreach (var (name, value) in d.Arguments)
            if (!string.Equals(name, RedundantArgument, StringComparison.Ordinal)) kept[name] = value;

        return new DiagnosticJson(d.Id, severity, message, kept);
    }

    /// <summary>The argument name this project uses for "the whole sentence, authored elsewhere and
    /// forwarded" — the only one dropped when it duplicates <see cref="Message"/>.</summary>
    private const string RedundantArgument = "text";
}
