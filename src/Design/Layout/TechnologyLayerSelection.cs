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
    public static Technology WithVisibility(Technology tech, Func<LayerDef, bool> visible)
    {
        var clone = ShallowCopy(tech);
        clone.Layers = [.. tech.Layers.Select(l =>
        {
            var copy = ShallowCopy(l);
            copy.Visible = visible(l);
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
