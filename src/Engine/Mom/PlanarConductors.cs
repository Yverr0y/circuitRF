// PCAL2 — WHICH PIECE OF METAL IS WHICH, ON THE MESH RATHER THAN ON THE ARTWORK.
//
// The port-calibration clearance check has to answer two questions about a cell of metal near a
// port: is it the port's OWN conductor, and if it is not, does it carry a port of its own. PCAL1
// measured that the second answer is worth a factor of 2-3 in the clearance a port needs
// (`src/Engine/Mom/RESOLVED.md`, "PCAL1"), and the first is what keeps the check off a taper's own
// flare — which is R-fed-1's job and not a neighbour at all.
//
// **The connectivity comes from the BASES, not from the polygons.** A rooftop basis exists for
// exactly those pairs of adjacent cells that are both metal, so the basis list already IS the
// conduction graph of the meshed structure — including a via's ẑ basis, which joins two levels.
// Re-deriving it from the drawn polygons would be a second answer to a question the mesh has
// already answered, and the two would disagree wherever the mesher staircased, cut or dropped a
// sliver. What the solve drives is cells, so what "the same conductor" means is cells — the same
// rule `PlanarPortCrossSection` states for the port profile one file over.

namespace CircuitRF.Engine.Mom;

/// <summary>
/// Connected runs of metal on a <see cref="PlanarMesh"/>: which cells conduct to which, and which of
/// those runs carry a port.
/// </summary>
public sealed class PlanarConductors
{
    private readonly int[]  _parent;
    private readonly bool[] _inBasis;

    /// <summary>
    /// <b>Does this cell carry current at all?</b> A cell that appears in no rooftop basis is not
    /// in the solve: no unknown is attached to it, nothing flows through it, and it contributes
    /// nothing to any answer.
    ///
    /// <para><b>This is not an edge case, it is the ordinary conformal mesh.</b> The Klopfenstein
    /// taper the ceiling tests run on meshes to 3,504 cells of which <b>32 are single cut cells with
    /// no basis</b> — slivers of the taper's OWN metal that R-cut-4 declines to drive, and which the
    /// basis graph therefore shows as 32 separate one-cell "conductors". Counting those as
    /// neighbours made PCAL2's clearance check refuse the taper at 0.13 substrate heights from its
    /// own artwork. A piece of metal that carries no current cannot make a calibration wrong, so it
    /// is not a neighbour.</para>
    /// </summary>
    public bool CarriesCurrent(int cellIndex)
        => (uint)cellIndex < (uint)_inBasis.Length && _inBasis[cellIndex];

    /// <summary>One label per cell of <see cref="PlanarMesh.Cells"/>: cells with the same label are
    /// joined by a chain of rooftop bases. Labels are arbitrary, dense over nothing in particular,
    /// and only ever compared for equality.</summary>
    public int LabelOf(int cellIndex) => Find(cellIndex);

    /// <summary>Do these two cells belong to one conductor?</summary>
    public bool SameConductor(int cellA, int cellB) => Find(cellA) == Find(cellB);

    private PlanarConductors(int cellCount)
    {
        _parent  = new int[cellCount];
        _inBasis = new bool[cellCount];
    }

    /// <summary>
    /// Label every cell of <paramref name="mesh"/>. O(N·α) over the basis list, which on the largest
    /// mesh this kernel will accept is a few thousand entries — this is asked once per solve, before
    /// the first fill, and never inside the frequency loop.
    /// </summary>
    public static PlanarConductors Of(PlanarMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var c = new PlanarConductors(mesh.Cells.Count);
        for (int i = 0; i < c._parent.Length; i++) c._parent[i] = i;

        foreach (var b in mesh.Bases)
        {
            // A ground-attachment basis has one meshed foot and names it twice; the union is a
            // no-op there rather than a special case.
            if ((uint)b.CellA >= (uint)c._parent.Length || (uint)b.CellB >= (uint)c._parent.Length)
                continue;
            c._inBasis[b.CellA] = true;
            c._inBasis[b.CellB] = true;
            c.Union(b.CellA, b.CellB);
        }
        return c;
    }

    /// <summary>
    /// The labels of the conductors <paramref name="ports"/> stand on — one per port, deduplicated.
    /// <b>Every port, including the one being asked about</b>: the caller separates "mine" from
    /// "someone else's" by comparing against that port's own label, which it has.
    /// </summary>
    public HashSet<int> LabelsCarryingAPort(PlanarMesh mesh, IEnumerable<PlanarPortResolution> ports)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(ports);
        var labels = new HashSet<int>();
        foreach (var p in ports)
            foreach (int label in LabelsOf(mesh, p))
                labels.Add(label);
        return labels;
    }

    /// <summary>The labels of the conductor(s) one port's own bases stand on. More than one only for
    /// a conductor-referenced port, whose two terminals are by construction not connected.</summary>
    public IEnumerable<int> LabelsOf(PlanarMesh mesh, PlanarPortResolution port)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(port);
        var seen = new HashSet<int>();
        foreach (int bi in port.BasisIndices)
        {
            if ((uint)bi >= (uint)mesh.Bases.Count) continue;
            var b = mesh.Bases[bi];
            if ((uint)b.CellA < (uint)_parent.Length && seen.Add(Find(b.CellA))) yield return Find(b.CellA);
            if ((uint)b.CellB < (uint)_parent.Length && seen.Add(Find(b.CellB))) yield return Find(b.CellB);
        }
    }

    private int Find(int i)
    {
        while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
        return i;
    }

    private void Union(int a, int b)
    {
        a = Find(a); b = Find(b);
        if (a != b) _parent[b] = a;
    }
}
