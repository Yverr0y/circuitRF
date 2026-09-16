// MIM-7 — a dielectric that is PATTERNED with a conductor, and what an extractor does with one.
//
// WHY THIS IS ITS OWN FILE, when PlanarExtractor's header is emphatic that the two extractors
// restate the stackup rules rather than call into each other. That stance is about the hard part of
// the cross-section extractor — the reduction test and its refusals, which must never appear on the
// planar acceptance path. This rule is the opposite shape: it is one paragraph of policy plus one
// sentence of USER-FACING TEXT, and the sentence is the reason it is shared. A run whose medium
// silently lost a layer is the failure the note exists to prevent, and two copies of the note would
// drift into two different accounts of the same decision.
//
// The two callers ask the same question with different words for "in this run":
//   * PlanarExtractor  — is the plate conductor one of the ANALYSIS LEVELS? (a set)
//   * CrossSectionExtractor — is the plate conductor THE signal conductor? (exactly one, since a
//     uniform-line cross-section refuses multi-level geometry outright)
// and only the planar one has a sheet surface to revert, because only it solves a conductor as a
// zero-thickness sheet — the cross-section kernel models real metal of real thickness and does not
// read SheetAt at all (MIM-6's own recorded decision).

using CircuitRF.Engine.Mom;

namespace CircuitRF.Design.Layout.Em;

internal static class PatternedDielectric
{
    /// <summary>
    /// <b>Rebuilds <paramref name="stackup"/> with every tied dielectric whose plate is not in this
    /// run turned into air, or returns <c>null</c> when there is nothing to do</b> — in which case
    /// not one byte of the caller's arithmetic changes.
    ///
    /// <para>A thin-film capacitor's dielectric is patterned: it exists under the plates and nowhere
    /// else. The 2.5D premise cannot express that laterally — inside a run every dielectric is
    /// laterally infinite — but it does not force the film to be present in EVERY run, and
    /// <paramref name="isInRun"/> is the honest per-run proxy for "this run has capacitors in it".
    /// With the planar extractor's default level selection ("every signal conductor that carries
    /// artwork") an interconnect-only layout answers no with no configuration at all.</para>
    ///
    /// <para><b>Deactivating is two changes, and they are one decision.</b> The band enters the
    /// medium as air — εᵣ 1, tanδ 0, µᵣ 1 — with its thickness untouched, so every band above it
    /// keeps the height the process states; and, where the caller solves sheets
    /// (<paramref name="revertSheetSurface"/>), <see cref="ConductorSheetSurface.Top"/> on the
    /// conductor whose band sits directly BENEATH the film is treated as unset for this run. MIM-6
    /// exists so a plate gap reads as the capacitor dielectric alone; with no capacitor dielectric
    /// there is no gap to read, and the pre-MIM-6 placement is the established baseline for ordinary
    /// interconnect. Reverting it is what makes an interconnect run on a technology carrying the
    /// module BIT-identical to the same run on one without it, rather than merely close.</para>
    ///
    /// <para><b>A tie naming a conductor the stackup does not have leaves the film ACTIVE</b> and
    /// says so. Deactivating on a typo would silently thin the medium, which is the failure this
    /// whole mechanism exists to avoid.</para>
    ///
    /// <para>Nothing here touches the kernel: a hand-authored stack whose dielectric genuinely is
    /// everywhere still meets every refusal it always did.</para>
    /// </summary>
    /// <param name="plateHasArtwork">EM-SEV R-emsev-3: does the LAYOUT contain shapes on the plate
    /// conductor? Null where the caller cannot tell, which is the same as "no". The two cases are
    /// genuinely different findings and the caller knows both halves, so the split is made here
    /// rather than left to the reader:
    /// <list type="bullet">
    /// <item>No plate drawn — MIM-7's own case, and the film's absence is CORRECT. A note, worded
    /// exactly as it always has been, byte for byte: an interconnect-only run on a technology
    /// carrying a capacitor module must read as though the module were not there.</item>
    /// <item>Plate drawn, level excluded — the user drew a capacitor and this run does not contain
    /// one. A WARNING, because half the structure on screen is not in the answer.</item>
    /// </list></param>
    public static Stackup? Deactivate(
        Stackup stackup, Func<string, bool> isInRun, bool revertSheetSurface,
        List<EmFinding> notes, Func<string, bool>? plateHasArtwork = null)
    {
        if (!stackup.Layers.Any(l => l.Kind == StackupKind.Dielectric &&
                                     l.PresentWithLayer is { Length: > 0 }))
            return null;

        var layers  = new List<StackupLayer>(stackup.Layers);
        bool changed = false;

        for (int i = 0; i < layers.Count; i++)
        {
            var film = layers[i];
            if (film.Kind != StackupKind.Dielectric || film.PresentWithLayer is not { Length: > 0 } plate)
                continue;

            if (!stackup.Layers.Any(l => l.Kind == StackupKind.Conductor &&
                                         string.Equals(l.Name, plate, StringComparison.Ordinal)))
            {
                notes.Add($"Dielectric '{film.Name}' says it is patterned with conductor '{plate}', " +
                          "which this technology has no stackup entry for. It is carried into the " +
                          "medium as stated rather than dropped — a broken tie must not silently thin " +
                          "the stack. Fix the name in the technology editor's Stackup tab.");
                continue;
            }

            if (isInRun(plate)) continue;               // the capacitor IS in this run

            var air = Clone(film);
            air.Epsr = 1.0;
            air.TanD = 0;
            air.Mur  = 1.0;
            air.PresentWithLayer = null;
            layers[i] = air;

            string reverted = "";
            if (revertSheetSurface && Beneath(layers, i) is
                { Kind: StackupKind.Conductor, SheetAt: ConductorSheetSurface.Top } lower)
            {
                int li = layers.IndexOf(lower);
                var flat = Clone(lower);
                flat.SheetAt = null;
                layers[li] = flat;
                reverted = $" and '{lower.Name}'s analysis sheet is put back on the BOTTOM of its band";
            }

            changed = true;

            // The sentence splits on ONE question and the extractor knows the answer to it. Both
            // halves state the same mechanism; what differs is whether it is the right outcome.
            notes.Add(plateHasArtwork is not null && plateHasArtwork(plate)
                ? EmFinding.Warn(
                    $"'{film.Name}' is a patterned thin film tied to '{plate}', '{plate}' CARRIES " +
                    "ARTWORK in this layout, and it is not in this run's analysis levels — so the " +
                    $"film enters the medium as AIR at its stated thickness{reverted} and the " +
                    "capacitor you drew is not in this answer. A thin-film capacitor is its two " +
                    "plates and the film between them; with the plate level excluded, what is solved " +
                    "is the remaining metal with a gap where the capacitor was. Add " +
                    $"'{plate}' to this EM setup's analysis levels.")
                : $"'{film.Name}' is a patterned thin film tied to '{plate}', and '{plate}' is not in " +
                  $"this run — so it enters the medium as AIR at its stated thickness{reverted}. The " +
                  "film exists only where its plate's artwork is, so a run with no plate in it does not " +
                  "carry it, and this run's interconnect is modelled exactly as it would be on a " +
                  $"technology with no capacitor module at all. Put '{plate}' in the run (draw on it, or " +
                  "add it to the analysis levels) to solve the capacitor.");
        }

        return changed
            ? new Stackup { Top = stackup.Top, Bottom = stackup.Bottom, Layers = layers }
            : null;
    }

    /// <summary>The entry directly BENEATH <paramref name="i"/> that has a z band at all — a Via
    /// entry has none, so it is skipped. <c>Stackup.Layers</c> is ordered TOP to BOTTOM.</summary>
    private static StackupLayer? Beneath(List<StackupLayer> layers, int i)
    {
        for (int j = i + 1; j < layers.Count; j++)
            if (layers[j].Kind != StackupKind.Via)
                return layers[j];
        return null;
    }

    /// <summary>A field-for-field copy, so a deactivated tie is a change to THIS RUN's stackup and
    /// never to the <see cref="Technology"/> object the caller was handed — which is a live document
    /// in the application, and is re-extracted at every frequency of a sweep.</summary>
    private static StackupLayer Clone(StackupLayer l) => new()
    {
        Kind = l.Kind, Name = l.Name, ThicknessDbu = l.ThicknessDbu,
        Epsr = l.Epsr, TanD = l.TanD, Mur = l.Mur, SigmaSm = l.SigmaSm,
        DrawingLayers = [.. l.DrawingLayers],
        IsGroundReference = l.IsGroundReference, SheetAt = l.SheetAt,
        PresentWithLayer = l.PresentWithLayer,
        Fill = l.Fill, WallThicknessDbu = l.WallThicknessDbu,
        SpanFromLayer = l.SpanFromLayer, SpanToLayer = l.SpanToLayer,
    };
}
