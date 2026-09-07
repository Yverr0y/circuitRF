// The two process constants a stackup entry is defaulted from, in ONE place each.
//
// WHY THIS FILE EXISTS. Both numbers were already in the tree, more than once. Copper's 5.8e7 S/m was
// a named constant in PcbStackupMapping and a bare literal at two sites in StarterTechnologies and two
// more inside CrossSectionExtractor's refusal text; gold's 4.1e7 was a literal at four sites; the
// 25 µm plated wall was Um(25) in StarterTechnologies and 25000 in five shipped .ctech files, and was
// the one value a Gerber import — the document GUARANTEED to need it — did not write at all.
// A sixth copy is the failure mode this file exists to prevent (brief-gi3-substrate-fields.md
// R-gi3-2, R-gi3-4).
//
// WHAT MAY AND MAY NOT GO IN HERE. Element conductivities out of any physics handbook, and nothing
// else. A laminate/dielectric material table is REFUSED, permanently, for the reason
// GerberStackupMapping's own header gives: it is a lookup table of third-party trade names and it is
// out of scope (root CLAUDE.md §"Commercial Vendor References"). εr and tanδ stay typed in by whoever
// knows the board. Elements have no vendor.

namespace CircuitRF.Design.Layout;

/// <summary>One conductor metal and its bulk DC conductivity at 20 °C, S/m.</summary>
/// <param name="Name">What the editor's preset list calls it.</param>
/// <param name="SigmaSm">Bulk conductivity at 20 °C, S/m.</param>
public sealed record ConductorMaterial(string Name, double SigmaSm);

/// <summary>
/// The conductor metals the Stackup tab offers as presets for <see cref="StackupLayer.SigmaSm"/>, and
/// the single source of every conductivity constant in this repository's design layer.
///
/// <para><b>The preset is a shortcut, never a constraint.</b> The σ field stays freely typeable and
/// remains the source of truth; a value matching no entry here displays as <see cref="Custom"/> and is
/// left exactly as typed. Nothing in the model, the file format or the extractors knows a material
/// name — only a number is ever stored.</para>
///
/// <para>These are 20 °C handbook figures for the pure elements. A real plated or alloyed conductor
/// runs below them, which is why the field is editable at all; <c>WireMaterials</c> in
/// <c>src/WBond</c> carries its own copy of four of them WITH a temperature coefficient and a density,
/// because a bond wire is evaluated at its operating temperature and that project sits outside this
/// one's reference graph.</para>
/// </summary>
public static class ConductorMaterials
{
    /// <summary>What the preset list shows when the field's value matches no metal below. Selecting it
    /// is a no-op — it is a readout of the field, not a value.</summary>
    public const string Custom = "Custom";

    public static ConductorMaterial Copper    { get; } = new("Copper",    5.80e7);
    public static ConductorMaterial Silver    { get; } = new("Silver",    6.30e7);
    public static ConductorMaterial Gold      { get; } = new("Gold",      4.10e7);
    public static ConductorMaterial Aluminium { get; } = new("Aluminium", 3.77e7);
    public static ConductorMaterial Nickel    { get; } = new("Nickel",    1.43e7);

    /// <summary>Every preset, in the order the editor lists them — most conductive first, which is the
    /// order the question "which metal is this?" is usually asked in.</summary>
    public static IReadOnlyList<ConductorMaterial> All { get; } =
        [Silver, Copper, Gold, Aluminium, Nickel];

    /// <summary>The metal whose conductivity <paramref name="sigmaSm"/> is, or null for a custom
    /// value. Compared with a relative tolerance rather than exactly: the editor formats σ to three
    /// significant figures and parses that string back, so an untouched copper row must still read as
    /// copper after a round trip through its own text box.</summary>
    public static ConductorMaterial? Match(double sigmaSm)
    {
        foreach (var m in All)
            if (Math.Abs(sigmaSm - m.SigmaSm) <= 5e-4 * m.SigmaSm) return m;
        return null;
    }

    /// <summary>Looks a metal up by its preset name, case-insensitively. Null for anything else,
    /// including <see cref="Custom"/>.</summary>
    public static ConductorMaterial? ByName(string name)
    {
        foreach (var m in All)
            if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }
}

/// <summary>
/// The via-process defaults an import applies when the file set states none.
/// </summary>
public static class ViaDefaults
{
    /// <summary>
    /// The plated barrel wall thickness a plated via defaults to, in microns — 25 µm, which is what
    /// every shipped technology in this repository already writes and what a plated through-hole board
    /// is typically specified at (20–25 µm).
    ///
    /// <para><b>Defaulted and NAMED as a default</b>, per R-L4d-7's precedent: an import that applies
    /// it says so in its own message rather than letting the number read as something the files
    /// stated. Nothing in a Gerber or Excellon file says anything about plating thickness.</para>
    ///
    /// <para>Kept in MICRONS, not DBU: an import's destination resolution is a parameter
    /// (<c>dbuPerMicron</c>) and a DBU constant would be silently wrong at any resolution but the
    /// default. Convert at the point of use through <see cref="LayoutUnits.ToDbu"/>.</para>
    /// </summary>
    public const decimal PlatedWallThicknessUm = 25m;

    /// <summary><see cref="PlatedWallThicknessUm"/> in DBU at <paramref name="dbuPerMicron"/>.</summary>
    public static long PlatedWallThicknessDbu(int dbuPerMicron) =>
        LayoutUnits.ToDbu(PlatedWallThicknessUm, LayoutUnit.Um, dbuPerMicron);
}
