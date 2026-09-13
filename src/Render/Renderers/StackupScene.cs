using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkiaSharp;

namespace CircuitRF.Render;

/// <summary>What one thing the pointer can be over IS.</summary>
public enum StackupHitKind
{
    /// <summary>A conductor or dielectric band of the sandwich.</summary>
    Band,
    /// <summary>A via's barrel, drawn across the bands it spans.</summary>
    ViaBarrel,
    /// <summary>The grab target at the TOP end of a via barrel (brief 5 drags it).</summary>
    ViaGripTop,
    /// <summary>The grab target at the BOTTOM end of a via barrel.</summary>
    ViaGripBottom,
    /// <summary>A piece of text. <see cref="StackupHit.Field"/> says which value editing it would
    /// change, and is <see cref="StackupField.None"/> for a static word or a unit.</summary>
    Label,
    /// <summary>Nothing — the value a caller uses for "the pointer is over the background".
    /// <see cref="StackupScene.HitTest"/> itself returns <c>null</c> rather than this.</summary>
    Empty,
}

// Which value a Label hit would edit is CircuitRF.Design.Layout.StackupField — the enum
// StackupFieldReadiness already keys its per-field checks on, widened there with None, Name and Span
// rather than duplicated here. See that file for why a second enum of the same name could not exist.
//
// StackupField.Span is deliberately carried and deliberately never typed: a via's span is a pair of
// conductor NAMES, chosen from the card's combo boxes and dragged on the canvas (brief 5), so the
// label carries the field for selection and reporting while brief 4's inline editor refuses it.

/// <summary>How a piece of text is drawn. The scene decides; the renderer only maps this onto a
/// face, a size and a colour, so the two cannot disagree about which text is which.</summary>
public enum StackupLabelStyle
{
    /// <summary>A band's own name, sitting ON the band in fixed dark ink.</summary>
    BandName,
    /// <summary>The same name after it MOVED OFF its band — same face, but the theme's ink, because
    /// it is now on the pane's background rather than on the technology's metal. Drawing it in
    /// <see cref="BandName"/>'s fixed dark would make it all but invisible in the dark variant, which
    /// is the same bug that rule exists to prevent, one step along.</summary>
    ColumnName,
    /// <summary>A spec piece in the label column.</summary>
    Spec,
    /// <summary>A spec piece that names something structural — the ground reference, a patterned
    /// dielectric's tie.</summary>
    Accent,
    /// <summary>A boundary condition, above or below the stack.</summary>
    Note,
    /// <summary>A via the drawing could not place. Never silence: see R-stk1-6.</summary>
    Refusal,
    /// <summary>A via's own name, beside its barrel.</summary>
    ViaName,
}

/// <summary>The three drawn via states (R-stk1-7). Two INDEPENDENT model fields produce them —
/// <c>StackupLayer.Plated</c> (is this hole metal at all) and <c>StackupLayer.Fill</c> (the fill
/// model, both of whose values are conductive).</summary>
public enum StackupViaLook
{
    /// <summary>Two metal walls with the hole between them. <see cref="StackupBarrel.WallPx"/> is
    /// the WALL, not the hole radius.</summary>
    PlatedBarrel,
    /// <summary>Solid metal, edge to edge.</summary>
    SolidFill,
    /// <summary>An outline with no metal fill — a hole, drawn as one.</summary>
    UnplatedHole,
}

/// <summary>One thing the pointer can be over, and what editing it would mean.</summary>
public sealed record StackupHit(
    StackupHitKind Kind,
    string         LayerName,   // StackupLayer.Name — the key everything else resolves by
    StackupField   Field,       // which value a Label hit would edit; None otherwise
    SKRect         Rect);

/// <summary>A conductor or dielectric band, placed.</summary>
/// <param name="Fill">The TECHNOLOGY's colour, not the theme's: a conductor takes the colour of the
/// drawing layer it is bound to (R-stk1-5) so the cross-section and the layout editor agree about
/// which metal is which. A dielectric's is <see cref="StackupRenderTheme.DielectricFill"/> instead
/// and this is <c>null</c> — a dielectric is not something anyone draws on.</param>
public sealed record StackupBand(
    string       Name,
    StackupKind  Kind,
    SKRect       Rect,
    Rgba?        Fill,
    bool         IsGroundReference,
    long         ThicknessDbu);

/// <summary>A via, drawn ACROSS the bands it spans at its own x. It is not a band of the sandwich
/// and has no z band of its own.</summary>
/// <param name="WallPx">The metal wall's drawn width in pixels, for
/// <see cref="StackupViaLook.PlatedBarrel"/> only; 0 otherwise.</param>
/// <param name="GripTop">The HIT rect of the top gripper — larger than the glyph the renderer draws
/// by <see cref="StackupScene.GripHitSlop"/> on each side. A 4-pixel target that is exactly 4 pixels
/// to hit is a target nobody hits.</param>
public sealed record StackupBarrel(
    string         Name,
    SKRect         Rect,
    StackupViaLook Look,
    float          WallPx,
    Rgba?          Fill,
    SKRect         GripTop,
    SKRect         GripBottom,
    string?        SpanFromLayer,
    string?        SpanToLayer);

/// <summary>
/// One measured, padded piece of text.
///
/// <para><b>A band's spec is SEVERAL of these</b>, not one sentence (R-stk1-10): a static word, a
/// value, a unit. Only the value pieces carry a <see cref="Field"/> other than
/// <see cref="StackupField.None"/>, which is what lets brief 4 double-click a number rather than the
/// sentence it sits in.</para>
/// </summary>
/// <param name="Rect">The measured rect INFLATED by <see cref="StackupScene.LabelPadX"/> /
/// <see cref="StackupScene.LabelPadY"/>. This is the rect the no-overlap guarantee (R-stk1-9) is
/// stated over and the rect that goes into <see cref="StackupScene.Hits"/>.</param>
public sealed record StackupLabel(
    string            Text,
    string            LayerName,
    StackupField      Field,
    StackupLabelStyle Style,
    SKRect            Rect,
    float             TextX,
    float             Baseline);

// There is no leader-line type, deliberately.
//
// R-stk1-9's rule 3 joins a pushed-apart label back to its band with one, and that is what this
// originally drew. The owner looked at it and asked for the callout lines to go (2026-09-13). What
// carries the correspondence instead is already there and costs no ink: a pushed label is still the
// nearest one to its band, the label column is in stack order, and every group that is not beside
// its own band leads with that layer's NAME. See src/Render/RESOLVED.md.

/// <summary>Caller-supplied layout choices. Everything else is a pure function of the technology
/// and the width.</summary>
public sealed record StackupSceneOptions
{
    public static readonly StackupSceneOptions Default = new();

    /// <summary>
    /// Per-via lateral lane, keyed by <c>StackupLayer.Name</c>, as a fraction of the band column
    /// (0 = its left edge, 1 = its right edge) at which the barrel's CENTRE sits. A via not named
    /// here takes the default spread.
    ///
    /// <para><b>Brief 5 makes this user-settable and persists it</b> — a field and a drag and nothing
    /// else, because the honouring is already here. It is COSMETIC and must stay cosmetic: nothing
    /// downstream of the drawing may read it (series overview §3e).</para>
    /// </summary>
    public IReadOnlyDictionary<string, float>? ViaLanes { get; init; }
}

/// <summary>
/// <b>The layout of a stackup cross-section: where every band, barrel, label and gripper lands, as
/// rectangles in scene coordinates.</b> A pure function of <c>(Technology, width, options)</c>.
///
/// <h3>R-stk1-1 — computed ONCE, read TWICE</h3>
/// <para>The control that draws this scene also has to answer "what is under this pointer?" for
/// clicks, drags, grippers, context menus and double-click-to-edit, and <b>it answers it by reading
/// the same scene the renderer drew</b> — never by re-deriving band positions. A second copy of the
/// placement arithmetic is the defect that does not appear until the geometry changes: the picture
/// moves, the hit-test does not, the user clicks a band and selects its neighbour, and nothing
/// throws.</para>
///
/// <h3>The z order, which is the whole disambiguation rule</h3>
/// <para><see cref="Hits"/> is in DRAW order, topmost LAST, and <see cref="HitTest"/> walks it
/// backwards. The order is <b>bands, then barrels, then labels, then grippers</b>: a gripper beats a
/// label beats a barrel beats a band. It is invisible from the call site, which is why it is stated
/// here.</para>
///
/// <h3>What the picture never says</h3>
/// <para>Band heights are relative WITHIN a kind and never across kinds (R-stk1-3), and when a kind's
/// own dynamic range does not fit its height budget the mapping compresses — monotonically, so the
/// ordering stays truthful. <see cref="ConductorsCompressed"/> / <see cref="DielectricsCompressed"/>
/// record which branch was taken and <b>nothing draws them</b> (R-stk1-4, owner 2026-09-13): no note,
/// no asterisk, no "not to scale" caption. What carries the honesty instead costs no extra ink — the
/// real thickness is printed on every band.</para>
/// </summary>
public sealed class StackupScene
{
    // ── Column geometry ───────────────────────────────────────────────────────────────────────────

    /// <summary>Left and right margin.</summary>
    public const float Gutter = 24f;
    /// <summary>Between the band column and the label column.</summary>
    public const float ColumnGap = 18f;
    /// <summary>The band column's share of the usable width. Both columns FLEX — this lives in a
    /// dockable pane a user resizes, unlike <c>DocStackupFixtures</c>'s fixed-width figures.</summary>
    public const float BandColumnFraction = 0.55f;
    /// <summary>The band column never narrows past this, so a narrow pane degrades predictably
    /// rather than collapsing (R-stk1-2).</summary>
    public const float MinBandColumnWidth = 180f;

    /// <summary>Below this width the label column is DROPPED and the specs move onto the band.</summary>
    public const float LabelColumnDropWidth = 380f;
    /// <summary>Below this width the scene is a single line saying the pane is too narrow.</summary>
    public const float MinRenderableWidth = 150f;

    /// <summary>Inset of on-band text from the band's left edge.</summary>
    public const float InnerPad = 6f;

    // ── Band heights (R-stk1-3): a separate, deliberately OVERLAPPING range per kind ──────────────

    public const float ConductorMinHeight = 16f;
    public const float ConductorMaxHeight = 36f;
    public const float DielectricMinHeight = 20f;
    public const float DielectricMaxHeight = 110f;

    // ── Vias ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A barrel's drawn width, and the range its metal WALL scales over.
    ///
    /// <para>Wider than <c>DocStackupFixtures</c>' 16 px, and the walls are a large fraction of it,
    /// because a plated barrel has to read as ONE object with a bore rather than as two thin lines
    /// with a gap (owner, 2026-09-13). It is not to scale against the bands and never could be — a
    /// 0.3 mm drill through a 1.6 mm board is a tenth of the picture's own vertical compression — so
    /// the width is chosen to be legible, and the real wall thickness is printed beside it.</para>
    /// </summary>
    public const float BarrelWidth  = 22f;
    public const float ViaWallMin   = 3f;
    public const float ViaWallMax   = 6f;
    /// <summary>Lane 0's centre, as a fraction of the band column. Deliberately on the RIGHT-hand
    /// side, exactly as <c>DocStackupFixtures</c> puts it, so a barrel never runs through the band
    /// names on the left.</summary>
    public const float LaneStartFraction = 0.68f;
    public const float LaneStepFraction  = 0.18f;
    /// <summary>How many lanes before the spread WRAPS to a second pass rather than running off the
    /// edge (R-stk1-8).</summary>
    public const int   LaneCount = 3;
    /// <summary>Half-width of a gripper's drawn glyph.</summary>
    public const float GripGlyphHalf = 3.5f;
    /// <summary>How much larger than the glyph the gripper's HIT rect is, on every side.</summary>
    public const float GripHitSlop = 4f;
    /// <summary>Clearance kept between the leftmost barrel and a band's on-band name.</summary>
    public const float NameZoneGap = 8f;

    // ── Text (R-stk1-9) ───────────────────────────────────────────────────────────────────────────

    public const float BandNameSize = 13f;
    public const float SpecSize     = 11.5f;
    public const float NoteSize     = 13f;

    /// <summary>
    /// The point size a label of this style is measured — and therefore drawn — at.
    ///
    /// <para>Here rather than in the renderer because it is a LAYOUT fact: it is what the scene
    /// measured the piece with, and <c>StackupRenderer.FontFor</c> now reads it back instead of
    /// keeping a second copy. brief 4's inline editor is the caller that made the difference visible
    /// — a box opened over a label has to be the size of the text it covers, and a size table that
    /// had drifted from the measurement would be wrong with nothing saying so.</para>
    /// </summary>
    public static float FontSizeFor(StackupLabelStyle style) => style switch
    {
        StackupLabelStyle.BandName   => BandNameSize,
        StackupLabelStyle.ColumnName => BandNameSize,
        StackupLabelStyle.Note       => NoteSize,
        _                            => SpecSize,
    };

    /// <summary>Horizontal padding around every measured label. Two abutting pieces of one sentence
    /// are therefore separated by <c>2 × LabelPadX + PieceGap</c>, which is what makes R-stk1-9's
    /// "no two label rects intersect" hold for a sentence built out of several of them.</summary>
    public const float LabelPadX = 1.5f;
    public const float LabelPadY = 3f;
    /// <summary>Strictly positive, so adjacent pieces' padded rects are disjoint rather than merely
    /// touching — a guarantee that survives a future change to the intersection test.</summary>
    public const float PieceGap    = 1f;
    /// <summary>Extra space before a piece that starts a new QUANTITY, so the sentence reads as
    /// quantities rather than as words.</summary>
    public const float QuantityGap = 5f;
    /// <summary>Minimum vertical clearance between two pushed-apart labels.</summary>
    public const float LabelGap = 2f;
    /// <summary>Clearance between two WRAPPED lines of one label group. Strictly positive for the
    /// same reason <see cref="PieceGap"/> is.</summary>
    public const float LineGap = 1f;

    public const float TopPad    = 10f;
    public const float NoteGap   = 8f;
    public const float FooterPad = 12f;
    /// <summary>Between a via's barrel and the name drawn beside it.</summary>
    public const float ViaNameGap = 5f;

    /// <summary>The fallback conductor colour, for a conductor bound to no drawing layer.</summary>
    public static readonly Rgba MetalFallback = new(190, 150, 90);

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Dictionary<string, SKRect> _rectByName = new(StringComparer.Ordinal);

    private StackupScene() { }

    // ── What a caller reads ───────────────────────────────────────────────────────────────────────

    public float Width  { get; private init; }

    /// <summary>The INTRINSIC height — what brief 2's control reports as its desired height and a
    /// <c>ScrollViewer</c> scrolls. Nothing in this series compresses the drawing to fit a
    /// viewport.</summary>
    public float Height { get; private init; }

    /// <summary>Topmost LAST; <see cref="HitTest"/> walks it backwards.</summary>
    public IReadOnlyList<StackupHit>    Hits    { get; private init; } = [];
    public IReadOnlyList<StackupBand>   Bands   { get; private init; } = [];
    public IReadOnlyList<StackupBarrel> Barrels { get; private init; } = [];
    public IReadOnlyList<StackupLabel>  Labels  { get; private init; } = [];

    /// <summary>Scene METADATA, read by tests and by a caller outside the picture (a documentation
    /// figure's own caption). <b>Nothing draws it</b> — R-stk1-4.</summary>
    public bool ConductorsCompressed  { get; private init; }
    /// <inheritdoc cref="ConductorsCompressed"/>
    public bool DielectricsCompressed { get; private init; }

    /// <summary>The pane was too narrow to draw anything; the scene is one line of text.</summary>
    public bool IsTooNarrow { get; private init; }
    /// <summary>The pane was too narrow for a label column, so the specs moved onto the bands.</summary>
    public bool LabelColumnDropped { get; private init; }

    /// <summary>The topmost thing at <paramref name="x"/>, <paramref name="y"/>, or <c>null</c> for
    /// the background.</summary>
    public StackupHit? HitTest(float x, float y)
    {
        for (int i = Hits.Count - 1; i >= 0; i--)
            if (Hits[i].Rect.Contains(x, y)) return Hits[i];
        return null;
    }

    /// <summary>The band's rect, or a via's barrel rect — what brief 3's selection outline frames.
    /// Null for a name that is not in the stackup, or for a via whose barrel could not be placed.</summary>
    public SKRect? RectOf(string layerName)
        => layerName is not null && _rectByName.TryGetValue(layerName, out var r) ? r : null;

    // ── Build ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Lays the whole cross-section out. Deterministic: called twice on the same technology
    /// and width it produces element-wise equal scenes (R-stk1-11), which brief 7's clipboard export
    /// and brief 8's doc figures both depend on.</summary>
    public static StackupScene Build(Technology tech, float width, StackupSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(tech);
        options ??= StackupSceneOptions.Default;

        using var nameFont = new SKFont(SkiaFonts.PlexSemiBold, BandNameSize);
        using var specFont = new SKFont(SkiaFonts.PlexRegular,  SpecSize);
        using var noteFont = new SKFont(SkiaFonts.PlexSemiBold, NoteSize);

        if (width < MinRenderableWidth) return TooNarrowScene(width, noteFont);

        // The widest UNBREAKABLE token the scene can emit — see WidestToken. The label column has to
        // hold it, because a piece wider than its column is a piece that runs off the right edge: a
        // value the reader cannot read and brief 4 cannot edit. So the band column YIELDS to it, down
        // to its own floor, and if even the floor will not do the label column is dropped. The width
        // decides the layout; the layout never decides to overflow the width.
        float usable    = width - 2 * Gutter;
        float bandLeft  = Gutter;
        float widest    = WidestToken(tech, nameFont, specFont);
        float bandWidth = Math.Max(MinBandColumnWidth, usable * BandColumnFraction);
        if (usable - bandWidth - ColumnGap < widest)
            bandWidth = Math.Max(MinBandColumnWidth, usable - ColumnGap - widest);

        bool dropLabels = width < LabelColumnDropWidth || usable - bandWidth - ColumnGap < widest;
        if (dropLabels) bandWidth = usable;
        float bandRight = bandLeft + bandWidth;
        // With no label column the specs move out of the columns entirely and stack BENEATH the
        // drawing, in stack order, each joined to its band by a leader — and a band's name leads its
        // own group rather than sitting on the band, which is R-stk1-9's rule 1 with the label column
        // standing where it can.
        //
        // The brief's narrow mode puts the specs ON the bands, and that was written before the text
        // was measured: a dielectric's full spec wraps to three lines in a 290-pixel column, against
        // a band that is 21 pixels tall, so every spec lands over a band that is not its own and the
        // picture asserts a correspondence that is false. Below the stack costs vertical space in a
        // pane that scrolls anyway, and says nothing untrue. See src/Render/RESOLVED.md.
        float labelLeft  = dropLabels ? bandLeft + InnerPad : bandRight + ColumnGap;
        float labelWidth = width - Gutter - labelLeft;

        var layers     = tech.Stackup.Layers;
        var bandLayers = layers.Where(l => l.Kind != StackupKind.Via).ToList();
        var viaLayers  = layers.Where(l => l.Kind == StackupKind.Via).ToList();

        var conductorScale  = HeightScale.For(
            bandLayers.Where(l => l.Kind == StackupKind.Conductor).Select(l => l.ThicknessDbu),
            ConductorMinHeight, ConductorMaxHeight);
        var dielectricScale = HeightScale.For(
            bandLayers.Where(l => l.Kind == StackupKind.Dielectric).Select(l => l.ThicknessDbu),
            DielectricMinHeight, DielectricMaxHeight);

        var labels  = new List<StackupLabel>();
        var hits    = new List<StackupHit>();

        // ── The stack itself ─────────────────────────────────────────────────────────────────────
        float y = TopPad;

        // Two pieces, so a narrow pane WRAPS the note rather than running it off the edge.
        var topNote = new PieceRun(bandLeft, width - Gutter - bandLeft);
        topNote.Add($"Top: {tech.Stackup.Top}", StackupField.None, StackupLabelStyle.Note, noteFont, 0f);
        if (tech.Stackup.Top == BoundaryCondition.Open)
            topNote.Add("— free space above", StackupField.None, StackupLabelStyle.Note, noteFont, PieceGap);
        float noteHeight = topNote.Height;
        y += noteHeight + NoteGap;

        float stackTop = y;
        var   bands    = new List<StackupBand>(bandLayers.Count);
        foreach (var band in bandLayers)
        {
            float h = band.Kind == StackupKind.Conductor
                ? conductorScale.Map(band.ThicknessDbu)
                : dielectricScale.Map(band.ThicknessDbu);
            var rect = new SKRect(bandLeft, y, bandRight, y + h);
            bands.Add(new StackupBand(
                band.Name, band.Kind, rect,
                band.Kind == StackupKind.Conductor ? MetalOf(band, tech) : null,
                band is { Kind: StackupKind.Conductor, IsGroundReference: true },
                band.ThicknessDbu));
            y += h;
        }
        float stackBottom = y;

        // ── Barrels, and the lane spread they occupy ─────────────────────────────────────────────
        var barrels     = new List<StackupBarrel>();
        var barrelVias  = new List<StackupLayer>();   // parallel to barrels — two vias may share a name
        var unresolved  = new List<StackupLayer>();
        var wallScale   = HeightScale.For(
            viaLayers.Where(v => LookOf(v) == StackupViaLook.PlatedBarrel && v.WallThicknessDbu is > 0)
                     .Select(v => v.WallThicknessDbu!.Value),
            ViaWallMin, ViaWallMax);

        int slot = 0;
        foreach (var via in viaLayers)
        {
            var a = FindBand(bands, via.SpanFromLayer);
            var b = FindBand(bands, via.SpanToLayer);
            if (a is null || b is null) { unresolved.Add(via); continue; }

            float cx    = LaneCentre(via, slot++, bandLeft, bandWidth, dropLabels, options);
            float y0    = Math.Min(a.Rect.Top,    b.Rect.Top);
            float y1    = Math.Max(a.Rect.Bottom, b.Rect.Bottom);
            var   look  = LookOf(via);
            float wall  = look == StackupViaLook.PlatedBarrel
                ? wallScale.Map(via.WallThicknessDbu ?? 0L)
                : 0f;
            // Never let the wall close the hole: a "plated" barrel drawn solid says the wrong thing
            // about the one field this look exists to show.
            wall = Math.Min(wall, (BarrelWidth - 2f) * 0.5f);

            var rect = new SKRect(cx - BarrelWidth * 0.5f, y0, cx + BarrelWidth * 0.5f, y1);
            float g  = GripGlyphHalf + GripHitSlop;
            barrels.Add(new StackupBarrel(
                via.Name, rect, look, wall, MetalOf(via, tech),
                new SKRect(cx - g, y0 - g, cx + g, y0 + g),
                new SKRect(cx - g, y1 - g, cx + g, y1 + g),
                via.SpanFromLayer, via.SpanToLayer));
            barrelVias.Add(via);
        }

        // A band's own name shares the band column with the barrels, so the zone it may occupy stops
        // short of the leftmost one. DocStackupFixtures states the same constraint as a comment; here
        // it is arithmetic, because the lanes move with the pane's width.
        float nameZoneRight = barrels.Count > 0
            ? barrels.Min(v => v.Rect.Left) - NameZoneGap
            : bandRight - InnerPad;
        float nameZoneWidth = Math.Max(0f, nameZoneRight - (bandLeft + InnerPad));

        // ── Labels ───────────────────────────────────────────────────────────────────────────────
        var groups = new List<LabelGroup>();

        for (int i = 0; i < bandLayers.Count; i++)
        {
            var layer = bandLayers[i];
            var band  = bands[i];

            var group = new LabelGroup(labelLeft, labelWidth)
            {
                LayerName = layer.Name,
                Index     = groups.Count,
                AnchorY   = band.Rect.MidY,
            };

            // Rule 1: the name goes ON the band when the band is tall enough for the padded text AND
            // the name zone is wide enough for it. Otherwise it moves to the label column AHEAD of
            // the spec — which is the same rule in both modes, because in the narrow mode the "label
            // column" is the band column's own right-hand part.
            float nameW = nameFont.MeasureText(layer.Name) + 2 * LabelPadX;
            float nameH = FaceHeight(nameFont) + 2 * LabelPadY;
            bool  onBand = !dropLabels && layer.Name.Length > 0
                        && nameH <= band.Rect.Height && nameW <= nameZoneWidth;

            if (onBand)
            {
                var run = new PieceRun(bandLeft + InnerPad);
                run.Add(layer.Name, StackupField.Name, StackupLabelStyle.BandName, nameFont, 0f);
                run.Place(band.Rect.MidY - run.Height * 0.5f, layer.Name, labels, hits);
            }
            else if (layer.Name.Length > 0)
            {
                group.Add(layer.Name, StackupField.Name, StackupLabelStyle.ColumnName, nameFont, 0f);
            }

            AddSpecPieces(group, layer, tech, specFont);
            if (group.PieceCount > 0) groups.Add(group);
        }

        for (int i = 0; i < barrels.Count; i++)
        {
            var barrel = barrels[i];
            var via    = barrelVias[i];

            // Rule 4: the via's name goes beside its barrel — unless it would run out of the band
            // column, in which case it joins the spec group, exactly as an over-wide band name does.
            float nameW = specFont.MeasureText(via.Name) + 2 * LabelPadX;
            var   nameGroup = new LabelGroup(barrel.Rect.Right + ViaNameGap,
                                             bandRight - barrel.Rect.Right - ViaNameGap)
            {
                LayerName = via.Name,
                Index     = groups.Count,
                AnchorY   = barrel.Rect.MidY,
            };
            bool besideBarrel = !dropLabels && nameGroup.Left + nameW <= bandRight;
            if (besideBarrel)
            {
                nameGroup.Add(via.Name, StackupField.Name, StackupLabelStyle.ViaName, specFont, 0f);
                groups.Add(nameGroup);
            }

            var group = new LabelGroup(labelLeft, labelWidth)
            {
                LayerName = via.Name,
                Index     = groups.Count,
                AnchorY   = barrel.Rect.MidY,
            };
            if (!besideBarrel)
                group.Add(via.Name, StackupField.Name, StackupLabelStyle.ColumnName, nameFont, 0f);
            AddViaSpecPieces(group, via, barrel, tech, specFont);
            if (group.PieceCount > 0) groups.Add(group);
        }

        Separate(groups, dropLabels ? stackBottom + NoteGap : stackTop, LabelGap);

        foreach (var group in groups) group.Emit(labels, hits);

        // ── The two boundary conditions, which belong to the STACK rather than to any band ───────
        string bottomText   = $"Bottom: {tech.Stackup.Bottom}";
        float  bottomWidth  = noteFont.MeasureText(bottomText) + 2 * LabelPadX;
        float  bottomTop    = Math.Max(stackBottom, BottomOfColumn(labels, bandLeft, bandLeft + bottomWidth)) + NoteGap;

        topNote.Place(TopPad, "", labels, hits);

        var bottomNote = new PieceRun(bandLeft, width - Gutter - bandLeft);
        bottomNote.Add(bottomText, StackupField.None, StackupLabelStyle.Note, noteFont, 0f);
        bottomNote.Place(bottomTop, "", labels, hits);
        float footerY = bottomTop + bottomNote.Height + LabelGap;

        // R-stk1-6: an unresolvable via is drawn as a marker, never omitted. A via that simply does
        // not appear is a stackup the user cannot tell is broken from the picture, and the card list
        // already flags it — the drawing must not disagree by staying silent.
        foreach (var via in unresolved)
        {
            var run = new PieceRun(labelLeft, labelWidth);
            bool isHole = via.Plated == false;
            run.Add(via.Name, StackupField.Name,
                    isHole ? StackupLabelStyle.ViaName : StackupLabelStyle.Refusal, specFont, 0f);
            run.Add(isHole ? "unplated hole — no span" : UnresolvedSpanText(via),
                    StackupField.Span,
                    isHole ? StackupLabelStyle.Spec : StackupLabelStyle.Refusal, specFont, QuantityGap);
            run.Place(footerY, via.Name, labels, hits);
            footerY += run.Height + LabelGap;
        }

        // ── Hit order: bands, barrels, labels, grippers. Topmost LAST. ───────────────────────────
        var ordered = new List<StackupHit>(hits.Count + bands.Count + barrels.Count * 3);
        foreach (var band in bands)
            ordered.Add(new StackupHit(StackupHitKind.Band, band.Name, StackupField.None, band.Rect));
        foreach (var barrel in barrels)
            ordered.Add(new StackupHit(StackupHitKind.ViaBarrel, barrel.Name, StackupField.None, barrel.Rect));
        ordered.AddRange(hits);
        foreach (var barrel in barrels)
        {
            ordered.Add(new StackupHit(StackupHitKind.ViaGripTop,    barrel.Name, StackupField.None, barrel.GripTop));
            ordered.Add(new StackupHit(StackupHitKind.ViaGripBottom, barrel.Name, StackupField.None, barrel.GripBottom));
        }

        float bottom = footerY;
        foreach (var l in labels)  bottom = Math.Max(bottom, l.Rect.Bottom);
        foreach (var v in barrels) bottom = Math.Max(bottom, v.GripBottom.Bottom);

        var scene = new StackupScene
        {
            Width                 = width,
            Height                = bottom + FooterPad,
            Bands                 = bands,
            Barrels               = barrels,
            Labels                = labels,
            Hits                  = ordered,
            ConductorsCompressed  = conductorScale.Compressed,
            DielectricsCompressed = dielectricScale.Compressed,
            LabelColumnDropped    = dropLabels,
        };
        foreach (var band in bands)     scene._rectByName.TryAdd(band.Name,   band.Rect);
        foreach (var barrel in barrels) scene._rectByName.TryAdd(barrel.Name, barrel.Rect);
        return scene;
    }

    /// <inheritdoc cref="Build(Technology, float, StackupSceneOptions)"/>
    public static StackupScene Build(Technology tech, float width)
        => Build(tech, width, StackupSceneOptions.Default);

    // ── Pieces ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A band's spec, as SEVERAL labels (R-stk1-10). Every number is formatted <b>exactly as the card
    /// formats it</b> — <c>LayoutUnits.Format</c> in the technology's display unit for a thickness,
    /// <c>StackupLayerRowViewModel</c>'s own <c>"0.###e+0"</c> for σ, invariant culture throughout —
    /// so opening an editor over a value seeds the same string the card shows. A drawing that rounded
    /// differently from the field it edits would be an edit that changed a value the user did not
    /// touch.
    /// </summary>
    private static void AddSpecPieces(LabelGroup g, StackupLayer layer, Technology tech, SKFont font)
    {
        // Short, because the picture is the point and every word here is drawn on every band
        // (owner, 2026-09-13). "thick" is gone: a length with a unit on a band of a cross-section is
        // its thickness, and the word said so nine times on a seven-band stack.
        g.Add(ThicknessText(layer.ThicknessDbu, tech), StackupField.Thickness, StackupLabelStyle.Spec, font, QuantityGap);
        g.Add(UnitText(tech), StackupField.None, StackupLabelStyle.Spec, font, PieceGap);

        if (layer.Kind == StackupKind.Conductor)
        {
            g.Add("σ =", StackupField.None,  StackupLabelStyle.Spec, font, QuantityGap);
            g.Add(layer.SigmaSm.ToString("0.###e+0", Inv), StackupField.Sigma, StackupLabelStyle.Spec, font, PieceGap);
            g.Add("S/m",      StackupField.None,  StackupLabelStyle.Spec, font, PieceGap);

            // The ground reference is the one band a reader has to be able to FIND. The heavy edge
            // says it in the picture; this says it in words, on the same line — a second line would
            // be a second thing to keep from overlapping for no extra information.
            if (layer.IsGroundReference)
                g.Add("ground ref", StackupField.None, StackupLabelStyle.Accent, font, QuantityGap);
            return;
        }

        g.Add("εr =", StackupField.None, StackupLabelStyle.Spec, font, QuantityGap);
        g.Add(layer.Epsr.ToString("0.####", Inv), StackupField.Epsr, StackupLabelStyle.Spec, font, PieceGap);
        g.Add("tanδ =", StackupField.None, StackupLabelStyle.Spec, font, QuantityGap);
        g.Add(layer.TanD.ToString("0.######", Inv), StackupField.TanD, StackupLabelStyle.Spec, font, PieceGap);
        // µr is printed only when it is NOT 1, which is the engineering convention and saves a whole
        // quantity on very nearly every dielectric anyone draws. The consequence is real and is the
        // trade: a non-magnetic dielectric has no µr label, so it cannot be edited FROM THE DRAWING
        // (brief 4) — only from the card, which always shows the field. A magnetic one has both.
        if (Math.Abs(layer.Mur - 1.0) > 1e-9)
        {
            g.Add("µr =", StackupField.None, StackupLabelStyle.Spec, font, QuantityGap);
            g.Add(layer.Mur.ToString("0.####", Inv), StackupField.Mur, StackupLabelStyle.Spec, font, PieceGap);
        }

        // MIM-7's tie: the one thing about this band a reader cannot infer from the picture, because
        // it is drawn as a layer of the sandwich like any other and is the only one that is not
        // always there.
        if (layer.PresentWithLayer is { Length: > 0 } plate)
            g.Add($"patterned: {plate}", StackupField.None, StackupLabelStyle.Accent, font, QuantityGap);
    }

    private static void AddViaSpecPieces(
        LabelGroup g, StackupLayer via, StackupBarrel barrel, Technology tech, SKFont font)
    {
        // Three pieces rather than one sentence, so a narrow label column can break BETWEEN the two
        // conductor names instead of running the pair off the edge. All three carry Span: a hit
        // anywhere in it means the same thing, and brief 5 drags it as one.
        // Both are non-null here by construction — this runs only for a via whose span RESOLVED to
        // two bands — but Add ignores an empty piece anyway, so the guard costs nothing.
        g.Add(via.SpanFromLayer ?? "", StackupField.Span, StackupLabelStyle.Spec, font, QuantityGap);
        g.Add("→",               StackupField.Span, StackupLabelStyle.Spec, font, PieceGap);
        g.Add(via.SpanToLayer   ?? "", StackupField.Span, StackupLabelStyle.Spec, font, PieceGap);
        g.Add(barrel.Look switch
        {
            StackupViaLook.PlatedBarrel => "plated",
            StackupViaLook.SolidFill    => "solid",
            _                           => "unplated",
        }, StackupField.None, StackupLabelStyle.Spec, font, QuantityGap);

        // WallThicknessDbu is the WALL, not the hole radius — the confusion the model field's own
        // doc comment already warns about, and one a drawing that got it backwards would make
        // permanent. It is editable from the drawing on a plated-FILL via only, which is the only
        // state in which it means anything.
        if (barrel.Look != StackupViaLook.PlatedBarrel) return;
        g.Add("wall", StackupField.None, StackupLabelStyle.Spec, font, QuantityGap);
        g.Add(ThicknessText(via.WallThicknessDbu ?? 0L, tech), StackupField.WallThickness, StackupLabelStyle.Spec, font, PieceGap);
        g.Add(UnitText(tech), StackupField.None, StackupLabelStyle.Spec, font, PieceGap);
    }

    private static string UnresolvedSpanText(StackupLayer via)
    {
        string from = via.SpanFromLayer is { Length: > 0 } f ? $"\"{f}\"" : "(unset)";
        string to   = via.SpanToLayer   is { Length: > 0 } t ? $"\"{t}\"" : "(unset)";
        return $"span does not resolve: {from} → {to}";
    }

    internal static string ThicknessText(long dbu, Technology tech)
        => LayoutUnits.Format(dbu, tech.DefaultDisplayUnit, LayoutUnits.DefaultDbuPerMicron);

    internal static string UnitText(Technology tech) => LayoutUnits.Suffix(tech.DefaultDisplayUnit);

    // ── Colour, span and lane resolution ──────────────────────────────────────────────────────────

    /// <summary>R-stk1-5: a conductor (and a via barrel) takes its fill from the first drawing layer
    /// it is bound to that resolves against the technology, so the cross-section and the layout
    /// editor agree about which metal is which.</summary>
    private static Rgba MetalOf(StackupLayer layer, Technology tech)
    {
        foreach (var key in layer.DrawingLayers)
            foreach (var def in tech.Layers)
                if (def.Key.Equals(key)) return def.Color;
        return MetalFallback;
    }

    /// <summary>
    /// The three drawn via states, from two independent model fields (R-stk1-7).
    ///
    /// <para><b>A null <c>Fill</c> draws SOLID</b>, per the brief's own table — a hollow barrel needs
    /// a wall thickness, and an entry with no fill model stated has none, so drawing one would mean
    /// inventing the number the look exists to show. Note that <c>StackupLayerRowViewModel</c>'s
    /// combo box defaults a null <c>Fill</c> to <c>Plated</c> instead; see
    /// <c>src/Render/RESOLVED.md</c>.</para>
    /// </summary>
    private static StackupViaLook LookOf(StackupLayer via)
        => via.Plated == false               ? StackupViaLook.UnplatedHole
         : via.Fill == ViaFillKind.Plated    ? StackupViaLook.PlatedBarrel
         :                                     StackupViaLook.SolidFill;

    private static StackupBand? FindBand(List<StackupBand> bands, string? name)
    {
        if (name is not { Length: > 0 }) return null;
        foreach (var band in bands)
            if (band.Kind == StackupKind.Conductor && string.Equals(band.Name, name, StringComparison.Ordinal))
                return band;
        return null;
    }

    /// <summary>
    /// R-stk1-8. Lanes spread across the right-hand fraction of the band column and WRAP to a second
    /// pass, shifted left by rather more than a barrel width, rather than running off the edge when
    /// there are more vias than lanes. An explicit per-via lane from
    /// <see cref="StackupSceneOptions.ViaLanes"/> wins — which is all brief 5 has to supply.
    /// </summary>
    private static float LaneCentre(
        StackupLayer via, int slot, float bandLeft, float bandWidth, bool dropLabels,
        StackupSceneOptions options)
    {
        float half = BarrelWidth * 0.5f;
        float lo   = bandLeft + half + 1f;
        float hi   = bandLeft + bandWidth - half - 1f;

        if (options.ViaLanes is { } lanes && lanes.TryGetValue(via.Name, out float lane))
            return Math.Clamp(bandLeft + bandWidth * Math.Clamp(lane, 0f, 1f), lo, hi);

        // With the specs on the bands there is no left-hand caption strip to stay out of and no room
        // to spread: the lanes crowd into the gutter reserved for them at the right edge instead.
        float x = dropLabels
            ? hi - slot * (BarrelWidth + 2f)
            : bandLeft + bandWidth * (LaneStartFraction - LaneStepFraction * (slot % LaneCount))
              - (slot / LaneCount) * BarrelWidth * 1.25f;
        return Math.Clamp(x, lo, hi);
    }

    /// <summary>
    /// The widest piece of text the scene can emit that it is not allowed to break.
    ///
    /// <para><b>It measures NAMES and formatted VALUES, and deliberately not the static words.</b>
    /// Those two are what can be arbitrarily long — a layer called "Inner 1 (Ground Plane)", a
    /// thickness printed in mil to four places — while every static word here is under a dozen
    /// characters, and no column wide enough for a name will fail to hold "tan&#948; =". A static
    /// word that ever grew long enough to matter shows up as a label past the right edge in
    /// <c>StackupSceneTests</c>' own extent gate rather than as a silent overflow.</para>
    ///
    /// <para><b>It is not an absolute guarantee, and cannot be</b>: a layer name longer than the
    /// whole pane has nowhere to go, and the alternative — truncating it — would print a value that
    /// is not the value. It holds for every shipped technology at every width the gate covers.</para>
    /// </summary>
    private static float WidestToken(Technology tech, SKFont nameFont, SKFont specFont)
    {
        float widest = 0f;
        foreach (var layer in tech.Stackup.Layers)
        {
            Token(layer.Name, nameFont);
            Token(layer.Name, specFont);
            Token(ThicknessText(layer.ThicknessDbu, tech), specFont);
            Token(UnitText(tech), specFont);
            Token(layer.SigmaSm.ToString("0.###e+0", Inv), specFont);
            Token(layer.Epsr.ToString("0.####",   Inv), specFont);
            Token(layer.TanD.ToString("0.######", Inv), specFont);
            Token(layer.Mur .ToString("0.####",   Inv), specFont);
            Token(layer.SpanFromLayer, specFont);
            Token(layer.SpanToLayer,   specFont);
            if (layer.WallThicknessDbu is { } w) Token(ThicknessText(w, tech), specFont);
            if (layer.PresentWithLayer is { Length: > 0 } plate)
                Token($"patterned: {plate}", specFont);
        }
        return widest;

        void Token(string? text, SKFont font)
        {
            if (text is not { Length: > 0 }) return;
            widest = Math.Max(widest, font.MeasureText(text) + 2 * LabelPadX);
        }
    }

    private static float BottomOfColumn(List<StackupLabel> labels, float x0, float x1)
    {
        float bottom = float.NegativeInfinity;
        foreach (var l in labels)
            if (l.Rect.Left < x1 && x0 < l.Rect.Right) bottom = Math.Max(bottom, l.Rect.Bottom);
        return float.IsNegativeInfinity(bottom) ? 0f : bottom;
    }

    private static float FaceHeight(SKFont font) => font.Metrics.Descent - font.Metrics.Ascent;

    private static StackupScene TooNarrowScene(float width, SKFont noteFont)
    {
        var run = new PieceRun(4f);
        run.Add("Pane too narrow to draw the stackup.", StackupField.None, StackupLabelStyle.Note, noteFont, 0f);
        var labels = new List<StackupLabel>();
        var hits   = new List<StackupHit>();
        run.Place(TopPad, "", labels, hits);
        return new StackupScene
        {
            Width       = width,
            Height      = TopPad + run.Height + FooterPad,
            Labels      = labels,
            IsTooNarrow = true,
        };
    }

    // ── R-stk1-3's height function ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a thickness within ONE kind onto that kind's own band-height range.
    ///
    /// <para><b>Proportional while the kind's dynamic range fits the budget</b>, so two conductors at
    /// a 2:1 thickness ratio draw at exactly 2:1 — which is what was asked for. <b>Monotonically
    /// compressed when it does not</b>: the thickest still draws tallest, the thinnest still draws
    /// shortest, every ordering is preserved and nothing is a hairline.</para>
    ///
    /// <para><b>The proportional branch is anchored at the THICKEST</b>, <c>h = hMax·t/tMax</c>, not
    /// at the thinnest. Both anchors are exactly proportional and both satisfy the range; this one
    /// uses the whole budget, so the commonest stack in the application — a two-layer board whose two
    /// copper layers are the same weight and whose one substrate has nothing to be proportional
    /// against — draws its substrate at full height instead of at the floor. Anchoring at the
    /// thinnest would have drawn 1.6 mm of FR-4 four pixels taller than 35 µm of copper.</para>
    /// </summary>
    private readonly struct HeightScale
    {
        private readonly float _hMin, _hMax, _tMin, _tMax;
        public  readonly bool  Compressed;

        private HeightScale(float hMin, float hMax, float tMin, float tMax, bool compressed)
        { _hMin = hMin; _hMax = hMax; _tMin = tMin; _tMax = tMax; Compressed = compressed; }

        public static HeightScale For(IEnumerable<long> thicknesses, float hMin, float hMax)
        {
            float tMin = float.PositiveInfinity, tMax = 0f;
            foreach (long dbu in thicknesses)
            {
                float t = Math.Max(dbu, 1L);
                tMin = Math.Min(tMin, t);
                tMax = Math.Max(tMax, t);
            }
            if (tMax <= 0f) return new HeightScale(hMin, hMax, 1f, 1f, false);
            bool compressed = tMax / tMin > hMax / hMin + 1e-4f;
            return new HeightScale(hMin, hMax, tMin, tMax, compressed);
        }

        public float Map(long dbu)
        {
            float t = Math.Max(dbu, 1L);
            if (!Compressed) return Math.Clamp(_hMax * t / _tMax, _hMin, _hMax);
            double span = Math.Log(_tMax / _tMin);
            double f    = span <= 0 ? 0 : Math.Log(t / _tMin) / span;
            return (float)Math.Clamp(_hMin + (_hMax - _hMin) * f, _hMin, _hMax);
        }
    }

    // ── Text runs and the no-overlap guarantee (R-stk1-9) ─────────────────────────────────────────

    private sealed record PieceSpec(
        string Text, StackupField Field, StackupLabelStyle Style, float Width, float Ascent,
        float Descent, float GapBefore);

    /// <summary>
    /// Measured, padded pieces at a fixed x, wrapped to a stated width.
    ///
    /// <para><b>The wrap is not cosmetic.</b> A dielectric's spec is nine pieces long and a via's
    /// names two conductors; on a 560-pixel pane either runs past the right margin, and a label that
    /// runs off the edge is a value the user cannot read and brief 4 cannot edit. Wrapping keeps
    /// every piece inside the scene, and because successive lines are separated by
    /// <see cref="LineGap"/> it keeps R-stk1-9 true as well.</para>
    /// </summary>
    private class PieceRun
    {
        private readonly List<PieceSpec> _pieces = [];
        private readonly List<int>       _lineStart = [];
        private readonly float           _maxWidth;
        private float _width;
        private float _ascent  = float.PositiveInfinity;
        private float _descent;

        public PieceRun(float left, float maxWidth = float.PositiveInfinity)
        { Left = left; _maxWidth = maxWidth; }

        public float Left  { get; set; }
        public float Width => _width;
        public float LineHeight => _pieces.Count == 0 ? 0f : (_descent - _ascent) + 2 * LabelPadY;
        public int   PieceCount => _pieces.Count;
        public float Height =>
            _lineStart.Count == 0 ? 0f : _lineStart.Count * LineHeight + (_lineStart.Count - 1) * LineGap;

        public void Add(string text, StackupField field, StackupLabelStyle style, SKFont font, float gapBefore)
        {
            if (text is null or "") return;
            var m = font.Metrics;
            _pieces.Add(new PieceSpec(text, field, style, font.MeasureText(text), m.Ascent, m.Descent, gapBefore));
            _ascent  = Math.Min(_ascent,  m.Ascent);
            _descent = Math.Max(_descent, m.Descent);
            Reflow();
        }

        /// <summary>Greedy: a piece that would cross <c>_maxWidth</c> starts a new line. The FIRST
        /// piece of a line never carries a leading gap — <see cref="Left"/> is what the leader lines
        /// and the x-overlap partition are stated in terms of, so it has to be where the ink starts —
        /// and a piece wider than the whole width still gets its own line rather than none.</summary>
        private void Reflow()
        {
            _lineStart.Clear();
            _width = 0f;
            float cursor = 0f;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                bool first = _lineStart.Count == 0 || cursor <= 0f;
                float advance = first ? 0f : PieceGap + p.GapBefore;
                float right   = cursor + advance + p.Width + 2 * LabelPadX;

                if (!first && right > _maxWidth)
                {
                    _lineStart.Add(i);
                    cursor = p.Width + 2 * LabelPadX;
                }
                else
                {
                    if (_lineStart.Count == 0) _lineStart.Add(0);
                    cursor = right;
                }
                _width = Math.Max(_width, cursor);
            }
        }

        /// <summary>Emits every piece at <paramref name="top"/>. A piece's rect is its measured width
        /// inflated by the padding, and consecutive pieces are separated by <see cref="PieceGap"/> on
        /// top of that, so no two of them intersect.</summary>
        public void Place(float top, string layerName, List<StackupLabel> labels, List<StackupHit> hits)
        {
            float lineH = LineHeight;
            for (int line = 0; line < _lineStart.Count; line++)
            {
                int from = _lineStart[line];
                int to   = line + 1 < _lineStart.Count ? _lineStart[line + 1] : _pieces.Count;

                float lineTop  = top + line * (lineH + LineGap);
                float baseline = lineTop + LabelPadY - _ascent;
                float cursor   = Left;

                for (int i = from; i < to; i++)
                {
                    var p = _pieces[i];
                    if (i > from) cursor += p.GapBefore;
                    var rect = new SKRect(cursor, lineTop, cursor + p.Width + 2 * LabelPadX, lineTop + lineH);
                    labels.Add(new StackupLabel(p.Text, layerName, p.Field, p.Style, rect, cursor + LabelPadX, baseline));
                    hits.Add(new StackupHit(StackupHitKind.Label, layerName, p.Field, rect));
                    cursor = rect.Right + PieceGap;
                }
            }
        }
    }

    /// <summary>A <see cref="PieceRun"/> that has not been placed yet: it knows where it WANTS to be
    /// (its band's vertical centre) and the separator decides where it lands.</summary>
    private sealed class LabelGroup : PieceRun
    {
        public LabelGroup(float left, float maxWidth) : base(left, maxWidth) { }

        public string LayerName { get; init; } = "";
        public int    Index     { get; init; }
        public float  AnchorY   { get; init; }
        public float  Top       { get; set; }

        public void Emit(List<StackupLabel> labels, List<StackupHit> hits) => Place(Top, LayerName, labels, hits);
    }

    /// <summary>
    /// <b>R-stk1-9's guarantee, made structural.</b> Every group wants its band's centre line; when
    /// two would collide — which happens the moment two thin bands are adjacent — they are pushed
    /// apart SYMMETRICALLY about their midpoint, which is what keeps each one the nearest label to
    /// its own band with no callout line needed to say so.
    ///
    /// <para>Groups that cannot overlap horizontally are never pushed against each other: the list is
    /// partitioned into components by x-interval overlap first, which is what keeps a via's name in
    /// the band column from displacing a spec in the label column. Within a component the classic
    /// cluster merge runs — place each group at its ideal centre, merge any two that touch, re-centre
    /// the merged cluster about the MEAN of its members' ideal centres, and repeat — then one
    /// downward-only pass keeps the whole thing below <paramref name="minY"/>.</para>
    ///
    /// <para><b>The test is the property, not this algorithm</b> (R-stk1-9): over every shipped
    /// technology and over a generated stack of the thinnest bands the unit can express, no two
    /// entries in <see cref="Labels"/> intersect. That is what makes any future change to the
    /// placement safe.</para>
    /// </summary>
    private static void Separate(List<LabelGroup> groups, float minY, float gap)
    {
        if (groups.Count == 0) return;
        foreach (var g in groups) g.Top = g.AnchorY - g.Height * 0.5f;

        var byLeft = groups.OrderBy(g => g.Left).ThenBy(g => g.Index).ToList();
        int i = 0;
        while (i < byLeft.Count)
        {
            var component = new List<LabelGroup> { byLeft[i] };
            float right = byLeft[i].Left + byLeft[i].Width;
            int j = i + 1;
            while (j < byLeft.Count && byLeft[j].Left < right)
            {
                component.Add(byLeft[j]);
                right = Math.Max(right, byLeft[j].Left + byLeft[j].Width);
                j++;
            }
            SeparateComponent(component, minY, gap);
            i = j;
        }
    }

    private static void SeparateComponent(List<LabelGroup> component, float minY, float gap)
    {
        if (component.Count < 2)
        {
            if (component.Count == 1) component[0].Top = Math.Max(component[0].Top, minY);
            return;
        }

        var ordered = component.OrderBy(g => g.AnchorY).ThenBy(g => g.Index).ToList();
        var clusters = new List<List<LabelGroup>>();
        foreach (var g in ordered)
        {
            clusters.Add([g]);
            Reposition(clusters[^1], gap);
            while (clusters.Count >= 2 && Top(clusters[^2]) + TotalHeight(clusters[^2], gap) + gap > Top(clusters[^1]))
            {
                var merged = clusters[^2];
                merged.AddRange(clusters[^1]);
                clusters.RemoveAt(clusters.Count - 1);
                Reposition(merged, gap);
            }
        }

        // Downward-only, so it cannot re-introduce an overlap it has just resolved.
        float cursor = minY;
        foreach (var cluster in clusters)
        {
            float top = Top(cluster);
            if (top < cursor) Shift(cluster, cursor - top);
            cursor = Top(cluster) + TotalHeight(cluster, gap) + gap;
        }
    }

    private static float TotalHeight(List<LabelGroup> cluster, float gap)
        => cluster.Sum(g => g.Height) + gap * (cluster.Count - 1);

    private static float Top(List<LabelGroup> cluster) => cluster[0].Top;

    private static void Reposition(List<LabelGroup> cluster, float gap)
    {
        float centre = 0f;
        foreach (var g in cluster) centre += g.AnchorY;
        centre /= cluster.Count;

        float top = centre - TotalHeight(cluster, gap) * 0.5f;
        foreach (var g in cluster) { g.Top = top; top += g.Height + gap; }
    }

    private static void Shift(List<LabelGroup> cluster, float dy)
    {
        foreach (var g in cluster) g.Top += dy;
    }
}
