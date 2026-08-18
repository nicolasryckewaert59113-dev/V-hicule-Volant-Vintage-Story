namespace IndependentVehicles.Core;

public readonly record struct LocalPos(int X, int Y, int Z)
{
    public LocalPos RotateQuarterTurns(int turns)
    {
        return QuarterTurn.Normalize(turns) switch
        {
            1 => new LocalPos(Z, Y, -X),
            2 => new LocalPos(-X, Y, -Z),
            3 => new LocalPos(-Z, Y, X),
            _ => this
        };
    }
}

