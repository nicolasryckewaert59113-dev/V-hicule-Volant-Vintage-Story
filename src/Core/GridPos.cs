namespace IndependentVehicles.Core;

public readonly record struct GridPos(int X, int Y, int Z, int Dimension = 0)
{
    public bool IsFaceAdjacent(GridPos other)
    {
        if (Dimension != other.Dimension) return false;
        return Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z) == 1;
    }

    public GridPos Offset(int x, int y, int z) => new(X + x, Y + y, Z + z, Dimension);
}

