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
/// collections, and only the classes named here pay for it.</para>
///
/// <para><b>It names the SAME collection as <see cref="LayoutTextOutlineTypefaceCollection"/>, and
/// that is the fix for what RND-4 half-solved.</b> Two collections are two groups that xUnit runs
/// in PARALLEL with each other, so protecting the Data Display's byte-identity gate here while
/// RND-2's schematic, symbol and layout gates sat in the other collection left exactly the window
/// this class was created to close — measured, not theorised: a full-solution run put
/// <c>ScalarCubeTests</c>' Helvetica window across
/// <c>RenderCliVerbTests.RenderingASchematicAsAProcess_WritesTheBytesTheRendererWrites</c>, which
/// draws in this process and compares against a fresh CLI process that has no override to read.
/// A class can be party to only one xUnit collection, so the two names resolve to one group. The
/// two STATICS remain distinct and each type documents its own; what is shared is the schedule.</para>
///
/// <para>The membership rule is therefore wider than "sets the override": <b>a class that sets
/// either typeface static, or that compares rendered TEXT bytes against another process, belongs
/// here.</b> The second half is the one that is easy to miss, because such a class looks like it
/// touches no global at all.</para>
/// </summary>
public class SkiaFontsTypefaceCollection
{
    public const string Name = LayoutTextOutlineTypefaceCollection.Name;
}
