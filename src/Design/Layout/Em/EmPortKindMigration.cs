// The one-way door between where a port's TYPE used to live and where it lives now.

using CircuitRF.Engine.Mom;

namespace CircuitRF.Design.Layout.Em;

/// <summary>
/// <b>Carries a legacy <c>.cem</c>'s <c>PortKinds</c> onto the port labels that are now the type's
/// only home</b> (2026-09-14 — see <see cref="LabelShape.PortKind"/> for why it moved).
///
/// <para><b>Why a migration at all, rather than reading both.</b> Reading the label first and falling
/// back to the setup would leave the value with two homes indefinitely, and the drawing would go on
/// disagreeing with the run for exactly the files this change exists to fix: the renderer knows
/// nothing about a <c>.cem</c>, so a legacy stated type would be driven by the engine and not drawn.
/// One home, and one moment at which an old file arrives in it.</para>
///
/// <para><b>It is applied by BOTH the editor and the headless run, from here, so they cannot
/// migrate differently.</b> The editor does it as a real, undoable layout edit and says so; the run
/// does it in memory and writes nothing, so <c>circuitrf em</c> over an un-migrated pair produces the
/// bytes Simulate produces.</para>
///
/// <para><b>A label that states its own type always wins, and is never overwritten.</b> The setup's
/// list is consulted only where the drawing is silent — so migrating twice is a no-op, and a type
/// changed in the layout after the setup was written is not reverted by it.</para>
/// </summary>
public static class EmPortKindMigration
{
    /// <summary>
    /// Stamps <paramref name="legacyPortKinds"/> onto <paramref name="shapes"/>' port labels, by the
    /// port NUMBER the setup addressed them with (slot = number − 1 — the rule that list was always
    /// read under). Returns the labels actually changed, empty when there is nothing to do.
    ///
    /// <para>Addressed through <see cref="EmPortExtraction.NumberPorts"/> rather than by document
    /// order, because that is the numbering the slot always meant: a label whose text names a number
    /// keeps it, and everything else takes the lowest free one.</para>
    /// </summary>
    public static IReadOnlyList<LabelShape> Pending(IReadOnlyList<LayoutShape> shapes,
                                                    IReadOnlyList<PlanarPortKind> legacyPortKinds)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        if (legacyPortKinds is null || legacyPortKinds.Count == 0) return [];

        var pending = new List<LabelShape>();
        foreach (var (number, label) in EmPortExtraction.NumberPorts(shapes))
        {
            int slot = number - 1;
            if (slot < 0 || slot >= legacyPortKinds.Count) continue;
            if (label.PortKind is not null) continue;              // the drawing already states it
            if (legacyPortKinds[slot] == PlanarPortKind.Edge) continue;  // the value silence infers anyway
            pending.Add(label);
        }
        return pending;
    }

    /// <summary>The type <paramref name="label"/> should be stamped with — call only for a label
    /// <see cref="Pending"/> returned.</summary>
    public static PlanarPortKind KindFor(IReadOnlyList<LayoutShape> shapes,
                                         IReadOnlyList<PlanarPortKind> legacyPortKinds, LabelShape label)
    {
        foreach (var (number, l) in EmPortExtraction.NumberPorts(shapes))
            if (ReferenceEquals(l, label)) return legacyPortKinds[number - 1];
        return PlanarPortKind.Edge;
    }

    /// <summary>
    /// The in-memory form, for a headless run: stamps every pending label in place.
    /// <b>Writes no file</b> — the caller's <c>.clay</c> and <c>.cem</c> are untouched on disk, so a
    /// build machine analysing an un-migrated pair gets the same answer the application does without
    /// modifying the tree it was handed.
    /// </summary>
    public static int ApplyInMemory(IReadOnlyList<LayoutShape> shapes,
                                    IReadOnlyList<PlanarPortKind> legacyPortKinds)
    {
        var pending = Pending(shapes, legacyPortKinds);
        foreach (var label in pending) label.PortKind = KindFor(shapes, legacyPortKinds, label);
        return pending.Count;
    }
}
