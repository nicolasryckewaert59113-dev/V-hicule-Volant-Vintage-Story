namespace IndependentVehicles.Core;

public readonly record struct VehicleRiderAnchor(double X, double Y, double Z);

public static class VehicleRiderMath
{
    public const double DefaultLocalX = 0;
    public const double DefaultLocalY = 0.64;
    public const double DefaultLocalZ = 0;

    /// <summary>
    /// Convertit un point du repère local de la structure vers le monde.
    /// La convention est la même que le rendu de la structure : à yaw positif,
    /// l'avant local -Z tourne vers -X.
    /// </summary>
    public static VehicleRiderAnchor LocalToWorld(
        double originX,
        double originY,
        double originZ,
        float yaw,
        double localX,
        double localY,
        double localZ)
    {
        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        return new VehicleRiderAnchor(
            originX + localX * cos + localZ * sin,
            originY + localY,
            originZ - localX * sin + localZ * cos);
    }

    /// <summary>
    /// Refuse une correction locale non finie ou assez grande pour révéler un
    /// mélange entre coordonnées monde et coordonnées décalées du client.
    /// </summary>
    public static bool IsPlausibleCorrection(
        double currentX,
        double currentY,
        double currentZ,
        VehicleRiderAnchor anchor,
        double maximumDistance)
    {
        if (!double.IsFinite(currentX) || !double.IsFinite(currentY) ||
            !double.IsFinite(currentZ) || !double.IsFinite(anchor.X) ||
            !double.IsFinite(anchor.Y) || !double.IsFinite(anchor.Z) ||
            !double.IsFinite(maximumDistance) || maximumDistance < 0)
        {
            return false;
        }

        double dx = currentX - anchor.X;
        double dy = currentY - anchor.Y;
        double dz = currentZ - anchor.Z;
        return dx * dx + dy * dy + dz * dz <= maximumDistance * maximumDistance;
    }
}
