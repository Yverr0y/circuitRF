using System.Globalization;
using System.Text.Json.Serialization;

namespace CircuitRF.Design.Theming;

/// <summary>
/// Framework-free RGBA color value. Used only inside the theme model — no SKColor or Avalonia types here.
/// </summary>
public readonly record struct Rgba
{
    [JsonPropertyName("r")] [JsonPropertyOrder(0)] public byte R { get; init; }
    [JsonPropertyName("g")] [JsonPropertyOrder(1)] public byte G { get; init; }
    [JsonPropertyName("b")] [JsonPropertyOrder(2)] public byte B { get; init; }
    [JsonPropertyName("a")] [JsonPropertyOrder(3)] public byte A { get; init; }

    public Rgba(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    /// <summary><c>#rrggbb</c>, the spelling <c>explain --layers</c> prints and
    /// <see cref="TryParseHex"/> reads back. The alpha is deliberately not in it: it is written only
    /// by <see cref="TryParseHex"/>'s eight-digit form and nothing that renders a layer reads it.</summary>
    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";

    /// <summary>
    /// Reads <c>#rgb</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>, with or without the leading <c>#</c>.
    /// Case-insensitive; the three-digit form doubles each nibble, as CSS does, so <c>#f00</c> and
    /// <c>#ff0000</c> are the same colour. Anything else is false rather than a colour — a partially
    /// parsed hex string is a plausible wrong colour, which is exactly what a caller cannot notice.
    /// </summary>
    public static bool TryParseHex(string? text, out Rgba color)
    {
        color = default;
        if (text is null) return false;

        ReadOnlySpan<char> s = text.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#') s = s[1..];
        if (s.Length is not (3 or 6 or 8)) return false;

        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;

        if (s.Length == 3)
        {
            byte r3 = Nibble(s[0]), g3 = Nibble(s[1]), b3 = Nibble(s[2]);
            color = new Rgba((byte)(r3 * 17), (byte)(g3 * 17), (byte)(b3 * 17));
            return true;
        }

        color = new Rgba(Byte(s[..2]), Byte(s.Slice(2, 2)), Byte(s.Slice(4, 2)),
                         s.Length == 8 ? Byte(s.Slice(6, 2)) : (byte)255);
        return true;

        static byte Nibble(char c) => byte.Parse([c], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static byte Byte(ReadOnlySpan<char> pair) => byte.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
