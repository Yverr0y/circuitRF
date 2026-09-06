// The reference-designator prefix an imported component states, and what a placed instance is
// called because of it.

namespace CircuitRF.Ui.Schematic;

/// <summary>
/// A component library states the letter its parts are meant to be numbered from — <c>U</c> for an
/// integrated circuit, <c>R</c> for a resistor — and the component import carries it verbatim onto
/// the cell as a read-only <c>Reference</c> parameter. This is the one place that letter becomes an
/// instance NAME: an instance of a cell declaring <c>U</c> is placed as <c>U1</c>, <c>U2</c>, <c>U3</c>,
/// rather than the generic <c>X1</c> every cell instance used to get.
///
/// <para><b>The stated value is a prefix, not a designator.</b> The formats spell it three ways —
/// bare (<c>U</c>), with the number's placeholder (<c>U?</c>), and occasionally already numbered
/// (<c>U1</c>) — and all three mean the same thing. The trailing placeholder and digits are stripped
/// so the numbering below starts from the library's letter and nowhere else.</para>
/// </summary>
public static class CellReferenceDesignator
{
    /// <summary>The cell-parameter name the component import writes the library's prefix to.</summary>
    public const string ParameterName = "Reference";

    /// <summary>What a cell that states no prefix is placed as — unchanged from before this existed.</summary>
    public const string DefaultPrefix = "X";

    /// <summary>
    /// The instance-name prefix for an instance of <paramref name="ccell"/> — the cell's own stated
    /// <c>Reference</c>, or <see cref="DefaultPrefix"/> when it states none that can be one.
    /// </summary>
    public static string PrefixFor(CcellFile? ccell)
    {
        var declared = ccell?.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, ParameterName, StringComparison.OrdinalIgnoreCase));
        return declared is null ? DefaultPrefix : PrefixFrom(declared.DefaultExpression);
    }

    /// <summary>
    /// The prefix for a component already placed — read from the instance's own seeded parameters
    /// rather than from the cell on disk, so a pasted instance is renamed from the same letter its
    /// original carried even with nothing resolvable in reach.
    /// </summary>
    public static string PrefixFor(EditableComponent comp)
    {
        var declared = comp.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, ParameterName, StringComparison.OrdinalIgnoreCase));
        return declared is null ? DefaultPrefix : PrefixFrom(declared.Expression);
    }

    /// <summary>
    /// The prefix a stated <c>Reference</c> value carries, or <see cref="DefaultPrefix"/> when it
    /// carries none.
    ///
    /// <para>The value arrives as the parameter's DEFAULT EXPRESSION, which the import quotes — a
    /// declared default is evaluated as an expression, and a bare <c>U</c> would resolve as a variable
    /// reference rather than as the letter. The quotes come off here.</para>
    /// </summary>
    internal static string PrefixFrom(string defaultExpression)
    {
        string s = (defaultExpression ?? "").Trim();
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[^1] == s[0]) s = s[1..^1];
        s = s.Trim();

        // Letters only, from the start: "U" and "U?" and "U1" are one library saying the same thing,
        // and a name that already carries its number would otherwise number itself twice (U11, U12).
        int n = 0;
        while (n < s.Length && char.IsLetter(s[n])) n++;
        return n == 0 ? DefaultPrefix : s[..n];
    }
}
