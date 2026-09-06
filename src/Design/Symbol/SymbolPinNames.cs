// Which of a symbol's pin names are worth drawing on the schematic, and which are the ordinal the
// pin would have anyway.

namespace CircuitRF.Design.Symbol;

/// <summary>
/// A built-in component draws its pin names as TEXT baked into the artwork, so the schematic renderer
/// never had to draw a pin name of its own. A symbol read from a component library carries them as
/// pin NAMES instead — <c>VDD</c>, <c>GND</c>, <c>RESET</c> — which is the right place for them (one
/// source of truth, drawn by the symbol editor, the palette preview and the schematic alike), but it
/// meant a placed imported part showed a body with unlabelled pins.
///
/// <para>This is the rule that decides which of those names says anything. A name that is nothing but
/// the pin's own ordinal — <c>1</c>, <c>2</c>, <c>P3</c> — is what circuitRF itself invents when a
/// source file names nothing (<c>KitTemplateSymbol.PlacePins</c>, <c>AutoSymbolGenerator</c>), and
/// drawing those would put a number beside a pin that an auto-generated symbol has already drawn the
/// number of. A name that is anything else came from the file and is drawn.</para>
///
/// <para>Lexical on purpose: it asks what the name SAYS, not which index it came from. The two
/// generators disagree about whether <c>PortIndex</c> is 0- or 1-based, so an index-based test would
/// be right for one of them and wrong for the other.</para>
/// </summary>
public static class SymbolPinNames
{
    /// <summary>
    /// True when <paramref name="name"/> is a name rather than an ordinal — non-blank, and not
    /// digits alone nor <c>P</c> followed by digits alone.
    /// </summary>
    public static bool IsStated(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) return false;

        int i = n[0] is 'P' or 'p' ? 1 : 0;
        if (i >= n.Length) return true;              // a pin genuinely named "P"
        for (int k = i; k < n.Length; k++)
            if (!char.IsAsciiDigit(n[k])) return true;
        return false;
    }

    /// <summary>True when any of <paramref name="pins"/> carries a stated name.</summary>
    public static bool AnyStated(IReadOnlyList<SymbolPin> pins)
    {
        foreach (var pin in pins)
            if (IsStated(pin.Name)) return true;
        return false;
    }
}
