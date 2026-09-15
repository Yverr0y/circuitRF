using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout.PCells;

namespace CircuitRF.Design.Schematic;

// `CircuitRF.Design.Symbol` is both a namespace and, inside it, the type `Symbol` — see the same
// alias in PdkPartInstaller.cs for why a compilation-unit `using` cannot fix it.
using Symbol = global::CircuitRF.Design.Symbol.Symbol;

/// <summary>
/// The schematic half of a kit that ships PARAMETRIC CELLS and no schematic side of its own.
///
/// <h3>Why this exists</h3>
/// <para>A complete kit part is four artefacts (<c>Reference ▸ PDK Authoring</c>): a schematic side,
/// a model, a technology and artwork. A kit may ship only the last — the PCell generators that turn
/// parameters into geometry — and until this existed such a cell could be placed in a LAYOUT and
/// nowhere else. The consequences were all silent. Dropping its palette tile on a schematic landed a
/// blank generic box with no pins and no parameters, because the tile named a generator and no part.
/// <b>Update Schematic from Layout placed nothing at all</b> and reported that the kit "is not
/// loaded" — the kit was loaded; what it had no part for was the cell.</para>
///
/// <h3>The kit ships the symbol, and the file's NAME is the declaration</h3>
/// <para>A <c>&lt;generator-id&gt;.csym</c> beside <c>pcell-generators.json</c> — or in a
/// <c>symbols/</c> folder beside it — is that generator's schematic symbol. Nothing lists it, for
/// the reason <c>pcell-generators.json</c> lists no generators either:
/// a second list is a cache that can silently disagree with the thing it describes. A name that
/// matches nothing is simply a cell with no schematic side, which is the state every such kit was in
/// before.</para>
/// <para><b>circuitRF draws nothing on the kit's behalf.</b> A generated box with the right pin count
/// was the alternative and is worse than it sounds: a spiral inductor drawn as a rectangle reads as a
/// part nobody recognises, and the kit author is the one who knows what the part IS. The
/// <c>.csym</c> is circuitRF's own symbol format, so the kit author draws it in circuitRF's symbol
/// editor rather than learning a description language.</para>
///
/// <h3>The parameters come from the generator, never from the symbol</h3>
/// <para>The cell's own declaration is the published interface — the SAME list the generator reads,
/// which is the one-list rule the PDK authoring reference states. So the symbol may not declare
/// parameters and is not asked to: it supplies pins and a glyph, and a symbol that disagreed with
/// its generator about a parameter name would be a defect that surfaces only as wrong artwork.</para>
///
/// <para>Framework-free, like everything else in <c>src/Design</c>.</para>
/// </summary>
public static class PCellKitSchematicParts
{
    /// <summary>A folder beside the manifest that may hold the symbols instead, for a kit with more
    /// cells than anyone wants loose in one directory.</summary>
    public const string SymbolsFolderName = "symbols";

    /// <summary>The extension a symbol file carries — circuitRF's own.</summary>
    public const string SymbolExtension = ".csym";

    /// <summary>
    /// The symbol file this kit ships for <paramref name="generatorId"/>, or null when it ships
    /// none — which is not a fault: a kit whose cells are artwork only is the case this whole file
    /// is the exception to.
    /// </summary>
    public static string? FindSymbolFile(string? kitDirectory, string? generatorId)
    {
        if (string.IsNullOrWhiteSpace(kitDirectory) || string.IsNullOrWhiteSpace(generatorId))
            return null;

        // A generator id is a Python identifier, not a path — but it arrives from a script, so it is
        // checked rather than trusted. A separator in it would make the lookup escape the kit.
        if (generatorId.AsSpan().IndexOfAny('/', '\\') >= 0) return null;
        if (generatorId is "." or "..") return null;

        foreach (string candidate in new[]
                 {
                     Path.Combine(kitDirectory, generatorId + SymbolExtension),
                     Path.Combine(kitDirectory, SymbolsFolderName, generatorId + SymbolExtension),
                 })
        {
            try { if (File.Exists(candidate)) return candidate; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        return null;
    }

    /// <summary>
    /// The kit part for one parametric cell, or null when the kit ships no symbol for it.
    /// </summary>
    /// <param name="declaredParameters">
    /// What the generator itself declares. Null or empty is allowed and produces a part with a
    /// symbol and no parameters — honest for a cell that takes none, and still placeable.
    /// </param>
    /// <param name="problem">
    /// Non-null when a symbol file was FOUND and could not be read. Distinguished from "there is
    /// none" deliberately: a kit that shipped a symbol and got no part back needs to be told why.
    /// </param>
    public static PdkKitPart? TryBuild(
        string kitName, string generatorId, string? kitDirectory,
        IReadOnlyList<PCellParameterInfo>? declaredParameters, out string? problem)
    {
        problem = null;
        if (FindSymbolFile(kitDirectory, generatorId) is not { } symbolPath) return null;

        Symbol symbol;
        try { symbol = CircuitRF.Design.Symbol.SymbolPersistence.LoadFromFile(symbolPath); }
        catch (Exception ex)
        {
            problem = $"'{symbolPath}' is the schematic symbol for the parametric cell " +
                      $"'{generatorId}', and could not be read: {ex.Message}";
            return null;
        }

        var ccell = new CcellFile
        {
            NumPorts   = symbol.Pins.Count,
            Parameters = BuildParameters(declaredParameters),
        };

        // Deliberately NOT an external-device leaf. A kit that supplies a model says so through its
        // own schematic part; this one supplied artwork and a glyph, and claiming it can be
        // evaluated would turn "this cell has no electrical model" into a device the resolver goes
        // looking for and reports as missing — a different sentence about a different thing.

        return new PdkKitPart(generatorId, symbol, ccell, IconPath: null);
    }

    /// <summary>
    /// The palette tile for a synthesised part, shaped exactly as an imported kit part's tile is so
    /// that <c>KitPaletteMerge</c> merges the generator onto it by the ordinary same-kit-same-id
    /// rule and the result is ONE tile carrying both views.
    /// </summary>
    public static PaletteItem PaletteItemFor(string kitName, PdkKitPart part)
        => new(
            Kind:            SymbolKind.Generic,
            PortCount:       0,
            DisplayName:     part.PartId,
            Category:        ComponentCategory.Other,
            SearchTerms:     [part.PartId, kitName],
            IsCommon:        false,
            ExtraCategories: null,
            Pdk:             new PdkPartRef(kitName, part.PartId, IconPath: null,
                                            CellDir: PdkKitRegistry.RefFor(kitName, part.PartId),
                                            ParameterNames: [.. part.Ccell.Parameters.Select(p => p.Name)]));

    // ── The parameter interface ───────────────────────────────────────────────

    /// <summary>
    /// The generator's declaration as the cell's published interface.
    ///
    /// <para><b>A COMPUTED parameter is left out.</b> The declaration marks it as one the cell
    /// derives and never reads, so it is an output; putting it on a schematic instance would offer a
    /// field whose value the generator overwrites on every draw.</para>
    ///
    /// <para><b>A length default is converted out of SI metres into millimetres</b>, and the
    /// parameter carries "mm". That is not a preference: "mm" is the baseline
    /// <see cref="MicrostripSubstrateInjection.ApplyTechnologyLengthUnit"/> rewrites to the placing
    /// technology's own display unit, so a width declared as 10 µm reaches an MMIC schematic as
    /// <c>10 µm</c> and a PCB one as <c>0.394 mil</c> — the same physical number, spelled the way
    /// that workspace spells lengths. Writing the bare SI value instead would put <c>1E-05</c> in a
    /// field with no unit beside it.</para>
    /// </summary>
    private static List<CcellParameter> BuildParameters(IReadOnlyList<PCellParameterInfo>? declared)
    {
        var list = new List<CcellParameter>();
        if (declared is null) return list;

        foreach (var p in declared)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            if (p.Computed) continue;
            if (list.Any(c => c.Name.Equals(p.Name, StringComparison.Ordinal))) continue;

            var dimension = p.Dimension switch
            {
                PCellDimension.Length => UnitDimension.Length,
                PCellDimension.Angle  => UnitDimension.Angle,
                _                     => UnitDimension.None,
            };

            list.Add(new CcellParameter
            {
                Name              = p.Name,
                DefaultExpression = DefaultExpressionFor(p, dimension),
                Unit              = dimension switch
                {
                    UnitDimension.Length => LengthBaselineUnit,
                    UnitDimension.Angle  => "deg",
                    _                    => "",
                },
                Dimension = dimension,
                // ANNOTATED, unlike an imported kit part's. A parametric cell IS its parameters —
                // a spiral is three turns of 10 um metal, and a reader who cannot see that from the
                // sheet has to click every instance to find out what the design is. An imported
                // part's defaults stay off because most of a vendor part's interface is model
                // plumbing; a generator declares only what it draws from. Each is a tick in the
                // Properties Inspector for whoever wants a quieter sheet.
                ShowOnSchematic = true,
                Choices         = p.Choices is { Count: > 0 } c ? [.. c.Select(v => v.AsText())] : null,
                Description     = string.IsNullOrWhiteSpace(p.Label) || p.Label == p.Name ? null : p.Label,
            });
        }

        return list;
    }

    /// <summary>The unit a freshly-written Length parameter carries before a placement rewrites it
    /// to the technology's own — stated here because the conversion below has to agree with it.</summary>
    private const string LengthBaselineUnit = "mm";

    private static string DefaultExpressionFor(PCellParameterInfo p, UnitDimension dimension)
    {
        if (p.Default is not { } value) return "";

        // Anything that is not a number is its own text — a model name, a layer name, a choice.
        if (value.Kind == PCellValueKind.String) return value.AsText();
        if (value.Kind == PCellValueKind.Bool)   return value.AsText();

        double n = value.AsReal();
        if (dimension == UnitDimension.Length) n *= 1000.0;   // SI metres → the mm baseline

        return n.ToString("R", CultureInfo.InvariantCulture);
    }
}
