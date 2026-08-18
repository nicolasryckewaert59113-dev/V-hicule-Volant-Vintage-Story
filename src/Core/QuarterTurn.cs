namespace IndependentVehicles.Core;

public static class QuarterTurn
{
    public const float Radians = MathF.PI / 2f;

    public static int Normalize(int turns) => ((turns % 4) + 4) % 4;

    public static int FromYaw(float yaw) => Normalize((int)MathF.Round(yaw / Radians));

    public static float SnapYaw(float yaw) => FromYaw(yaw) * Radians;
}

