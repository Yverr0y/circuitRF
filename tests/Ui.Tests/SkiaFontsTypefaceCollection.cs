namespace CircuitRF.Ui.Tests;

/// <summary>
/// Serializes the test classes that are party to <c>SkiaFonts.TestOverrideTypeface</c> — a
/// process-wide static that swaps the face every Data Display renderer draws text with.
///
/// <para><b>The sibling of <see cref="LayoutTextOutlineTypefaceCollection"/>, for the OTHER
/// override.</b> That one governs the face a layout LABEL is flattened to polygons with; this one
/// governs the face a plot's ticks, titles, table cells and marker readouts are drawn with. They
/// are different statics and a class can be party to one and not the other.</para>
///
/// <para><b>Why it had to exist (RND-4).</b> <c>ScalarCubeTests</c> sets the override for one test
/// and restores it in a <c>finally</c> — correct within its own class, and no protection at all
/// against a class running CONCURRENTLY in another collection. That window is invisible until a
/// test compares rendered bytes: <c>RenderDataDisplayCliTests</c> renders the same display in this
/// process and in a fresh CLI process, and the CLI has no override to read, so a plot drawn during
/// that window comes back in Helvetica on one side and IBM Plex Sans on the other. It was reliably
/// green alone and reliably red beside its neighbours, which is the shape of every shared-static
/// hazard.</para>
///
/// <para>Not a blanket <c>DisableTestParallelization</c>: xUnit still parallelizes across other
/// collections, and only the handful of classes named here pay for it.</para>
/// </summary>
[CollectionDefinition(Name)]
public class SkiaFontsTypefaceCollection
{
    public const string Name = "SkiaFonts.TestOverrideTypeface";
}
