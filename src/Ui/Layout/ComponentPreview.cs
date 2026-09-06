// What the import chooser draws for the candidate under the cursor
// (docs/sonnet-briefs/brief-PL2-component-library-breadth.md — the preview added after PL2).
//
// ── Why this is a THIN file ───────────────────────────────────────────────────────────────────────
//
// The preview is not a second reader and not a second converter. It is `ComponentRead.Read` followed
// by `ComponentImport.Build` — the SAME two calls the import itself makes, with the layer-mapping
// dialog left unasked so its own pre-selected defaults stand. What the user sees is therefore what
// the import would write, by construction rather than by resemblance: a preview with a conversion of
// its own would be free to drift, and a preview that drifts is worse than none, because its whole
// job is to be believed before anything is created.
//
// Nothing here is drawn. This file is framework-free like the rest of src/Ui/Layout — the rasterizer
// is ComponentPreviewRenderer, in src/Ui/Renderers, and it is what may touch Skia.

using CircuitRF.Design.Layout.Interchange;
using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Layout;

public static class ComponentPreview
{
    /// <summary>
    /// One candidate, read and built — or the reason it could not be.
    /// </summary>
    /// <param name="Technology">The technology the land patterns are DRAWN through: the destination's
    /// own layers plus the ones this part would add. Not the workspace's technology and never
    /// installed anywhere — a preview must not have side effects on the design, and rendering through
    /// the destination alone would draw every added layer in the unknown-layer fallback colour, which
    /// is precisely the colour it will NOT be once imported.</param>
    /// <param name="Refusal">Set when the candidate cannot be read at all. Everything else is null or
    /// empty then, and the dialog shows the sentence in place of the drawings.</param>
    public sealed record Model(
        string Name,
        Symbol? Symbol,
        IReadOnlyList<ComponentImport.BuiltLayout> Layouts,
        Technology? Technology,
        string Summary,
        string? Refusal)
    {
        public static Model Refused(string refusal) => new("", null, [], null, "", refusal);
    }

    /// <summary>
    /// Reads and builds <paramref name="candidate"/>. <b>Writes nothing, anywhere</b> — no cell
    /// folder, no technology change, no message posted.
    ///
    /// <para>Safe to call off the UI thread, and meant to be: reading a whole component library and
    /// reconciling its layers is not free, and it happens on every arrow-key press through the list.
    /// Every object it returns is freshly built and reachable from nothing else, which is what makes
    /// the rasterizer's own off-thread pass safe as well.</para>
    /// </summary>
    public static Model Build(ComponentCandidate candidate, Technology? destTech, int dbuPerMicron)
    {
        ComponentRead.ReadResult read;
        try { read = ComponentRead.Read(candidate, dbuPerMicron); }
        catch (IOException ex) { return Model.Refused(ex.Message); }
        catch (UnauthorizedAccessException ex) { return Model.Refused(ex.Message); }

        if (read.Refusal is { } refusal) return Model.Refused(refusal);
        var part = read.Part!;

        // The mapping prose belongs to an import, not to a preview: it describes a change that has not
        // been proposed yet. Collected and dropped rather than suppressed at the source, so Build stays
        // one method with one behaviour.
        var built = ComponentImport.Build(part, destTech, dbuPerMicron, []);
        if (built is null) return Model.Refused(ComponentRead.Refusal(candidate.Description));

        return new Model(
            part.Name,
            built.Symbol,
            built.Layouts,
            PreviewTechnology(destTech, built.LayersToAdd),
            Summarize(part, built),
            null);
    }

    /// <summary>
    /// The destination's layers with this part's own appended. Null when there are none of either —
    /// which is the honest "everything is fallback" case, and the same one the renderer already
    /// handles for a layout with no technology at all.
    /// </summary>
    private static Technology? PreviewTechnology(Technology? destTech, IReadOnlyList<LayerDef> toAdd)
    {
        if (destTech is null && toAdd.Count == 0) return null;

        // A NEW Technology, never the destination mutated: the destination object is the live one the
        // open documents render through, and a preview that appended to it would repaint the workspace
        // behind the dialog with layers nobody has agreed to import.
        var tech = new Technology
        {
            Name = destTech?.Name ?? "",
            DefaultDisplayUnit = destTech?.DefaultDisplayUnit ?? default,
            FillPatterns = destTech?.FillPatterns ?? [],
            Stackup = destTech?.Stackup ?? new Stackup(),
            Layers = [.. destTech?.Layers ?? []],
        };

        var seen = new HashSet<LayerKey>(tech.Layers.Select(l => l.Key));
        foreach (var def in toAdd)
            if (seen.Add(def.Key)) tech.Layers.Add(def);

        return tech;
    }

    /// <summary>
    /// The one line under the drawings: what this candidate actually holds.
    ///
    /// <para>It names the things the two pictures cannot show — how many terminals joined, and every
    /// section or package variant this phase does NOT import. A part whose second gate is dropped
    /// looks complete in a picture of its first one, and that is exactly the case the preview exists
    /// to stop someone importing by mistake.</para>
    /// </summary>
    private static string Summarize(ComponentPart part, ComponentImport.BuiltPart built)
    {
        var bits = new List<string>
        {
            $"{built.Terminals.Count:N0} terminal(s)",
            built.Layouts.Count switch
            {
                0 => "no land pattern",
                1 => "1 land pattern",
                var n => $"{n:N0} land patterns",
            },
            built.Symbol is null ? "no symbol" : "1 symbol",
        };

        int joined = built.Terminals.Count(t => t.PadName is not null && t.PinName is not null);
        if (joined != built.Terminals.Count) bits.Add($"{joined:N0} joined pin to pad");

        string line = string.Join(" · ", bits) + ".";

        foreach (var section in part.UnimportedSections)
            line += $" Section \"{section}\" would NOT be imported.";
        foreach (var variant in part.UnimportedDeviceVariants)
            line += $" Package variant \"{variant}\" would NOT be imported.";

        return line;
    }
}
