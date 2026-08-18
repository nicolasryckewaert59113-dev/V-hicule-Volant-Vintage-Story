namespace IndependentVehicles.Core;

public static class VehicleControlMath
{
    public static float DriveInput(bool forward, bool backward) =>
        (forward ? 1f : 0f) - (backward ? 1f : 0f);

    public static float TurnInput(bool left, bool right) =>
        (left ? 1f : 0f) - (right ? 1f : 0f);

    /// <summary>
    /// Même convention que EntityPos.GetViewVector() à pitch nul.
    /// À yaw zéro, l'avant vanilla pointe vers Z négatif.
    /// </summary>
    public static (double X, double Z) ForwardVector(float yaw) =>
        (-Math.Sin(yaw), -Math.Cos(yaw));
}
