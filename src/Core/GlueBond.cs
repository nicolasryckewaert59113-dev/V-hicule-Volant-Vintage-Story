namespace IndependentVehicles.Core;

public readonly record struct GlueBond
{
    public GridPos A { get; init; }
    public GridPos B { get; init; }

    public GlueBond(GridPos a, GridPos b)
    {
        if (Compare(a, b) <= 0)
        {
            A = a;
            B = b;
        }
        else
        {
            A = b;
            B = a;
        }
    }

    private static int Compare(GridPos a, GridPos b)
    {
        int value = a.Dimension.CompareTo(b.Dimension);
        if (value != 0) return value;
        value = a.X.CompareTo(b.X);
        if (value != 0) return value;
        value = a.Y.CompareTo(b.Y);
        return value != 0 ? value : a.Z.CompareTo(b.Z);
    }
}

public readonly record struct LocalBond(LocalPos A, LocalPos B);

