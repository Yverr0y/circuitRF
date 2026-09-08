using CircuitRF.Design.Theming;

namespace CircuitRF.Design.Layout;

/// <summary>
/// A copy of a <see cref="Technology"/> with each layer's <see cref="LayerDef.Visible"/> decided by a
/// predicate — the one operation a caller that wants to draw SOME of a design's layers needs
/// (brief-render-2-render-verb.md R-rnd2-8).
///
/// <para><b>Why a copy at all, and why this is not fussiness.</b> <see cref="TechnologyCache"/> hands
/// back a SHARED instance: the same <c>Technology</c> object serves every document that resolves to
/// that <c>.ctech</c>, for the lifetime of the cache. Flipping <c>Visible</c> on it does not narrow one
/// picture — it narrows every later one taken through that cache, in a process that may render many
/// (<c>circuitrf serve</c> is exactly that). It is the class of defect that only ever appears on the
/// SECOND call, which is the class that gets shipped.</para>
///
/// <para><b>Why the copy is reflective rather than written out field by field.</b> A hand-written copy
/// is correct on the day it is written and silently drops whatever is added to <see cref="LayerDef"/>
/// afterwards. The symptom would be a layer that renders differently only when a layer selection is in
/// force — a picture that is plausible, specific and wrong, with nothing reporting it. Everything other
/// than the layer list is shared by reference, which is safe because the only field written here is
/// <see cref="LayerDef.Visible"/> and nothing else on the graph is mutated for the life of a render.</para>
/// </summary>
public static class TechnologyLayerSelection
{
    /// <summary>
    /// <paramref name="tech"/> with every layer's visibility replaced by <paramref name="visible"/>'s
    /// answer for it. The argument is the ORIGINAL <see cref="LayerDef"/>, so a predicate that wants
    /// "as before, minus these" can read <see cref="LayerDef.Visible"/> from it.
    /// </summary>
    /// <param name="extraLayers">
    /// Layers to append to the copy before the predicate runs — the <see cref="FallbackPalette"/>
    /// definitions for keys the DOCUMENT draws on and this technology does not define.
    ///
    /// <para><b>Without them a selection cannot reach half of an imported design.</b>
    /// <c>LayoutRenderer</c> resolves an undefined key through <see cref="FallbackPalette.For"/>,
    /// whose <see cref="LayerDef.Visible"/> is <c>true</c> — so a shape on such a layer is drawn
    /// whatever the technology says, and a copy that did not carry a <see cref="LayerDef"/> for it
    /// would have nowhere to write the answer. The symptom is the worst kind: <c>--layers A</c>
    /// produces a picture that also contains B, and nothing reports it. Passing exactly the palette's
    /// own definition means the drawing is unchanged when the predicate leaves it visible.</para>
    /// </param>
    public static Technology WithVisibility(
        Technology tech, Func<LayerDef, bool> visible, IEnumerable<LayerDef>? extraLayers = null)
        => WithLayers(tech, visible, null, extraLayers);

    /// <summary>
    /// How a layer is PAINTED, overridden for one render — <c>brief-automation-12</c>'s R-aut12-1.
    /// </summary>
    /// <param name="Color">The layer's colour. Only R, G and B reach the canvas: every one of
    /// <c>LayoutRenderer</c>'s four call sites builds its <c>SKColor</c> from those three and takes
    /// the alpha from <paramref name="FillOpacity"/> instead, so a colour's own alpha is not a knob
    /// and is not pretended to be one.</param>
    /// <param name="FillOpacity">What the fill is painted at, 0 to 1 — <see cref="LayerDef.FillOpacity"/>.
    /// Null leaves the layer's own. <b>This is the layer's real alpha</b>, which is why an override
    /// that only set a colour could not make an overlay readable.</param>
    public readonly record struct LayerAppearance(Rgba Color, double? FillOpacity = null);

    /// <summary>
    /// <paramref name="tech"/> copied, with each layer's visibility decided by
    /// <paramref name="visible"/> and its paint by <paramref name="appearance"/> — both optional, and
    /// both answered from the ORIGINAL <see cref="LayerDef"/>.
    ///
    /// <para><b>One clone, not two chained ones.</b> A second pass over the output of the first works
    /// and is the obvious way to add a colour override to an existing visibility narrowing, but it
    /// reflects every property twice per layer and — worse — makes the appearance predicate read a
    /// copy rather than the technology's own definition, so "as before, but brighter" quietly means
    /// "as the previous pass left it".</para>
    /// </summary>
    public static Technology WithLayers(
        Technology tech,
        Func<LayerDef, bool>? visible,
        Func<LayerDef, LayerAppearance?>? appearance,
        IEnumerable<LayerDef>? extraLayers = null)
    {
        var clone = ShallowCopy(tech);
        clone.Layers = [.. tech.Layers.Concat(extraLayers ?? []).Select(l =>
        {
            var copy = ShallowCopy(l);
            if (visible is not null) copy.Visible = visible(l);
            if (appearance?.Invoke(l) is { } a)
            {
                copy.Color = a.Color;
                if (a.FillOpacity is { } fo) copy.FillOpacity = fo;
            }
            return copy;
        })];
        return clone;
    }

    private static T ShallowCopy<T>(T src) where T : new()
    {
        var dst = new T();
        foreach (var p in typeof(T).GetProperties())
            if (p is { CanRead: true, CanWrite: true }) p.SetValue(dst, p.GetValue(src));
        return dst;
    }
}
