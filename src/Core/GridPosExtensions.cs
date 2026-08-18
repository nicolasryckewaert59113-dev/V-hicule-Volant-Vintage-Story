using Vintagestory.API.MathTools;

namespace IndependentVehicles.Core;

public static class GridPosExtensions
{
    public static GridPos ToGridPos(this BlockPos pos) => new(pos.X, pos.Y, pos.Z, pos.dimension);

    public static BlockPos ToBlockPos(this GridPos pos) => new(pos.X, pos.Y, pos.Z, pos.Dimension);
}

