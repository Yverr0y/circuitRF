using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests;

public class LayoutUnitsTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron; // 1000

    // ── Gate 2: unit exactness ────────────────────────────────────────────────

    [Fact]
    public void ToDbu_OneMil_Is25400()
        => Assert.Equal(25_400L, LayoutUnits.ToDbu(1m, LayoutUnit.Mil, Dbu));

    [Fact]
    public void ToDbu_OneMicron_Is1000()
        => Assert.Equal(1_000L, LayoutUnits.ToDbu(1m, LayoutUnit.Um, Dbu));

    [Fact]
    public void ToDbu_OneMillimeter_Is1000000()
        => Assert.Equal(1_000_000L, LayoutUnits.ToDbu(1m, LayoutUnit.Mm, Dbu));

    [Fact]
    public void ToDbu_OneInch_Is25400000()
        => Assert.Equal(25_400_000L, LayoutUnits.ToDbu(1m, LayoutUnit.Inch, Dbu));

    [Fact]
    public void ToDbu_ThenFromDbu_RoundTripsExactly()
    {
        foreach (var unit in new[] { LayoutUnit.Nm, LayoutUnit.Um, LayoutUnit.Mm, LayoutUnit.Mil, LayoutUnit.Inch })
        {
            var dbu = LayoutUnits.ToDbu(3.0m, unit, Dbu);
            Assert.Equal(3.0m, LayoutUnits.FromDbu(dbu, unit, Dbu));
        }
    }

    // ── TryParse ──────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_2p9mm_Exact()
    {
        Assert.True(LayoutUnits.TryParse("2.9mm", LayoutUnit.Um, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(2.9m, LayoutUnit.Mm, Dbu), dbu);
    }

    [Fact]
    public void TryParse_115Mil_Exact()
    {
        Assert.True(LayoutUnits.TryParse("115 mil", LayoutUnit.Um, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(115m, LayoutUnit.Mil, Dbu), dbu);
    }

    [Fact]
    public void TryParse_50u_Exact()
    {
        Assert.True(LayoutUnits.TryParse("50u", LayoutUnit.Mil, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(50m, LayoutUnit.Um, Dbu), dbu);
    }

    [Fact]
    public void TryParse_1e3nm_Exact()
    {
        Assert.True(LayoutUnits.TryParse("1e3nm", LayoutUnit.Um, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(1000m, LayoutUnit.Nm, Dbu), dbu);
    }

    [Fact]
    public void TryParse_NegativeHalfMm_Exact()
    {
        Assert.True(LayoutUnits.TryParse("-0.5mm", LayoutUnit.Um, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(-0.5m, LayoutUnit.Mm, Dbu), dbu);
    }

    [Fact]
    public void TryParse_BareNumber_UsesFallbackUnit()
    {
        Assert.True(LayoutUnits.TryParse("42", LayoutUnit.Mil, Dbu, out var dbu));
        Assert.Equal(LayoutUnits.ToDbu(42m, LayoutUnit.Mil, Dbu), dbu);
    }

    [Fact]
    public void TryParse_UnknownSuffix_Rejected()
    {
        Assert.False(LayoutUnits.TryParse("2.9 furlongs", LayoutUnit.Um, Dbu, out _));
    }

    [Fact]
    public void TryParse_EmptyOrWhitespace_Rejected()
    {
        Assert.False(LayoutUnits.TryParse("", LayoutUnit.Um, Dbu, out _));
        Assert.False(LayoutUnits.TryParse("   ", LayoutUnit.Um, Dbu, out _));
    }

    [Fact]
    public void Format_TrimsTrailingZeros()
    {
        var dbu = LayoutUnits.ToDbu(2.9m, LayoutUnit.Mm, Dbu);
        Assert.Equal("2.9", LayoutUnits.Format(dbu, LayoutUnit.Mm, Dbu));
    }

    // ── AUT-12 R-aut12-3: the inverse of TryParse ─────────────────────────────

    /// <summary>
    /// <see cref="LayoutUnits.Spell"/> is the inverse of <see cref="LayoutUnits.TryParse"/>, exactly,
    /// at every unit and at DBU resolutions either side of the default — which is the property
    /// <c>explain --extents</c> emits a <c>render --window</c> string on the strength of.
    ///
    /// <para>The coordinates are deliberately not round in any of them: a value landing on whole
    /// micrometres round-trips through a formatter that quantises as happily as through one that does
    /// not, so it would prove nothing.</para>
    /// </summary>
    [Theory]
    [InlineData(LayoutUnit.Nm,   1000)]
    [InlineData(LayoutUnit.Um,   1000)]
    [InlineData(LayoutUnit.Mm,   1000)]
    [InlineData(LayoutUnit.Mil,  1000)]
    [InlineData(LayoutUnit.Inch, 1000)]
    [InlineData(LayoutUnit.Mm,   1)]
    [InlineData(LayoutUnit.Um,   10_000)]
    public void Spell_ReadsBackToTheSameDbu(LayoutUnit unit, int dbuPerMicron)
    {
        foreach (long dbu in new[] { 0L, 1L, -1L, 7L, -3_000_001L, 20_000_003L })
        {
            string text = LayoutUnits.Spell(dbu, unit, dbuPerMicron);
            Assert.True(LayoutUnits.TryParse(text, unit, dbuPerMicron, out long back),
                        $"'{text}' did not parse");
            Assert.Equal(dbu, back);
        }
    }

    /// <summary>The WRITTEN suffix is ASCII, and it is deliberately not the same table as the DISPLAY
    /// suffix — a coordinate travels through an argument list, a JSON document and somebody's shell
    /// before it gets back here, and <c>µm</c> does not survive all three reliably.</summary>
    [Fact]
    public void TheWrittenSuffix_IsAscii()
    {
        foreach (LayoutUnit u in Enum.GetValues<LayoutUnit>())
            Assert.All(LayoutUnits.AsciiSuffix(u), c => Assert.InRange(c, (char)0x20, (char)0x7e));
        Assert.Equal("um", LayoutUnits.AsciiSuffix(LayoutUnit.Um));
        Assert.Equal("µm", LayoutUnits.Suffix(LayoutUnit.Um));
    }

    // ── AUT-12 R-aut12-1: the colour a --layer-colors entry carries ───────────

    [Theory]
    [InlineData("#f00",       255,  0,    0,    255)]
    [InlineData("#00ff00",    0,    255,  0,    255)]
    [InlineData("00ff00",     0,    255,  0,    255)]   // the # is optional
    [InlineData("#3060E080",  0x30, 0x60, 0xE0, 0x80)]
    [InlineData("  #abc  ",   0xaa, 0xbb, 0xcc, 255)]
    public void Rgba_ReadsEveryHexForm(string text, int r, int g, int b, int a)
    {
        Assert.True(Rgba.TryParseHex(text, out var c));
        Assert.Equal(new Rgba((byte)r, (byte)g, (byte)b, (byte)a), c);
    }

    /// <summary>Anything else is FALSE rather than a colour. A hex string parsed as far as it goes is
    /// a plausible wrong colour, which is exactly what a caller looking at a picture cannot notice.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#ff")]
    [InlineData("#fffff")]
    [InlineData("#ff00zz")]
    [InlineData("puce")]
    [InlineData("#ff0000ff00")]
    public void Rgba_RefusesAnythingElse(string text) => Assert.False(Rgba.TryParseHex(text, out _));

    [Fact]
    public void Rgba_HexRoundTrips() => Assert.Equal("#3060e0", new Rgba(0x30, 0x60, 0xE0).ToHex());
}
