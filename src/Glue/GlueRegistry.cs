using IndependentVehicles.Core;

namespace IndependentVehicles.Glue;

public sealed class GlueRegistry
{
    private readonly HashSet<GlueBond> bonds = [];

    public int Count => bonds.Count;

    public bool Add(GridPos a, GridPos b) => Add(new GlueBond(a, b));

    public bool Add(GlueBond bond)
    {
        return bond.A.IsFaceAdjacent(bond.B) && bonds.Add(bond);
    }

    public bool Remove(GridPos a, GridPos b) => bonds.Remove(new GlueBond(a, b));

    public HashSet<GridPos> GetConnectedComponent(GridPos origin, int maximumBlocks)
    {
        var result = new HashSet<GridPos> { origin };
        var queue = new Queue<GridPos>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            GridPos current = queue.Dequeue();
            foreach (GlueBond bond in bonds)
            {
                GridPos? next = bond.A == current ? bond.B : bond.B == current ? bond.A : null;
                if (next is null || result.Contains(next.Value)) continue;
                result.Add(next.Value);
                if (result.Count > maximumBlocks) return result;
                queue.Enqueue(next.Value);
            }
        }

        return result;
    }

    public List<GlueBond> TakeInternalBonds(IReadOnlySet<GridPos> positions)
    {
        List<GlueBond> taken = GetInternalBonds(positions);
        foreach (GlueBond bond in taken) bonds.Remove(bond);
        return taken;
    }

    public List<GlueBond> GetInternalBonds(IReadOnlySet<GridPos> positions) =>
        bonds.Where(bond => positions.Contains(bond.A) && positions.Contains(bond.B)).ToList();

    public IEnumerable<GlueBond> All() => bonds;

    public void ReplaceAll(IEnumerable<GlueBond> newBonds)
    {
        bonds.Clear();
        foreach (GlueBond bond in newBonds) Add(bond);
    }
}
