using System;

namespace CircuitRF.Ui.Controls;

/// <summary>
/// <b>One <see cref="StackupScene"/> per layout pass, held for every reader of that pass.</b>
///
/// <para>R-stk2-1: the scene is built in <c>MeasureOverride</c> and read by <c>Render</c> — and, from
/// brief 3 onward, by every pointer handler. Building it per frame would be waste; building it per
/// hit-test would be worse than waste, because a scene built at a slightly different width answers
/// "what is under this pointer?" about a picture nobody drew. So there is exactly one, and this holds
/// it.</para>
///
/// <h3>The key is the Technology's REFERENCE, and that is not a shortcut</h3>
/// <para><c>TechEditorViewModel.ApplySnapshot</c> — the single choke point for a committed edit, an
/// undo and a redo — assigns <c>Working</c> a freshly deserialized instance every time. So a changed
/// stackup IS a changed reference, and an unchanged reference IS an unchanged stackup as far as any
/// COMMITTED state goes. What reference identity cannot see is an in-flight mutation (a row view
/// model writes <c>Layer.ThicknessDbu</c> in place before it calls <c>CommitEdit</c>), and nothing
/// asks it to: <see cref="Invalidate"/> is wired to the same event the commit raises.</para>
///
/// <para>Framework-free on purpose — this is the half of the canvas that can be tested without an
/// application host.</para>
/// </summary>
internal sealed class StackupSceneCache
{
    /// <summary>What a null view model draws: a real scene over an empty technology, so the pane
    /// shows its boundary notes and measures a sensible height before the data context binds,
    /// rather than collapsing to nothing and then jumping (R-stk2-4).</summary>
    private static readonly Technology Empty = new();

    private StackupScene? _scene;
    private Technology? _keyTech;
    private float _keyWidth = float.NaN;

    /// <summary>How many scenes this cache has actually built. The structural property R-stk2-1 is
    /// about — one build per layout pass, not one per frame — is a COUNTER rather than a timing.</summary>
    public int Builds { get; private set; }

    /// <summary>The scene the last <see cref="Get"/> produced, or null if there has not been one
    /// since the last <see cref="Invalidate"/>.</summary>
    public StackupScene? Current => _scene;

    /// <summary>Drops the held scene. The next <see cref="Get"/> rebuilds.</summary>
    public void Invalidate()
    {
        _scene = null;
        _keyTech = null;
        _keyWidth = float.NaN;
    }

    /// <summary>The scene for this technology at this width, building it only if the one in hand is
    /// not already it.</summary>
    public StackupScene Get(Technology? tech, float width)
    {
        var key = tech ?? Empty;
        if (_scene is not null && ReferenceEquals(_keyTech, key) && _keyWidth.Equals(width))
            return _scene;

        _keyTech = key;
        _keyWidth = width;
        Builds++;
        return _scene = StackupScene.Build(key, width);
    }
}
