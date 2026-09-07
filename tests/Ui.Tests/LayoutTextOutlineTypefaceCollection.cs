namespace CircuitRF.Ui.Tests;

/// <summary>
/// xUnit runs test CLASSES in parallel by default. <c>LayoutTextOutline.TestOverrideTypeface</c> is a
/// single static field — any two test classes that set/clear it independently race, and one class's
/// teardown can null it out while another class's label-rendering test is still mid-run.
///
/// <para><b>The original reason is gone; the race is not, and it now costs more.</b> The seam existed
/// because <c>SkiaFonts.PlexRegular</c> could not load without a live Avalonia app host. Since
/// R-rnd1-4 it loads out of <c>CircuitRF.Render</c>'s own manifest resources in any process, so what
/// this field now selects is a DIFFERENT FACE rather than "a face at all" — and a class that reads it
/// ambiently gets different glyph geometry depending on what else is running. That is why
/// <c>ConvertCliVerbTests</c> is in this collection: it compares the application's in-process Gerber
/// export byte for byte against the CLI's, the CLI has no override to read, and a label flattened
/// against a substituted face is a different set of coordinates. <b>The real end state is to delete
/// this override and let every one of these tests use the real face</b> — that is a change to a lot of
/// fixtures and belongs to its own piece of work, not to RND-1.</para> Every test class that touches this seam
/// declares <c>[Collection(Name)]</c> so xUnit serializes them relative to each other (xUnit still
/// parallelizes across OTHER collections normally — this is not a blanket
/// <c>DisableTestParallelization</c>, which would slow the whole ~2300-test suite for a race that only
/// four classes are actually party to).
/// </summary>
[CollectionDefinition(Name)]
public class LayoutTextOutlineTypefaceCollection
{
    public const string Name = "LayoutTextOutline.TestOverrideTypeface";
}
