# Renderers — local conventions for `src/Render/Renderers/`

Read with root `CLAUDE.md`. Moved here from `src/Ui/Renderers/` with the code it describes
(`docs/sonnet-briefs/brief-render-1-render-layer-below-the-firewall.md`); the convention is unchanged.

## Symbol stroke join / cap — single switch point

Symbol rendering uses round stroke joins and caps everywhere **except** wires.

The constants live on `SchematicRenderer`:

```csharp
public const SKStrokeJoin SymbolStrokeJoinStyle = SKStrokeJoin.Round;
public const SKStrokeCap  SymbolStrokeCapStyle  = SKStrokeCap.Round;
```

Both `SchematicRenderer` (schematic view) and `SymbolEditorRenderer` (symbol editor) must read these
constants — they are the **single switch point** for join style across both rendering contexts. When
you add a new paint for symbol geometry, include these two properties. Do NOT apply them to
`wirePaint` — wires stay miter-joined.
