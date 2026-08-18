namespace IndependentVehicles.Core;

public static class VehicleCollisionMath
{
    private const double ContactTolerance = 0.000001;

    public static bool IntersectsOrientedPrismWithAabb(
        double mobileCenterX,
        double mobileMinY,
        double mobileCenterZ,
        double mobileHalfX,
        double mobileMaxY,
        double mobileHalfZ,
        double yaw,
        double obstacleMinX,
        double obstacleMinY,
        double obstacleMinZ,
        double obstacleMaxX,
        double obstacleMaxY,
        double obstacleMaxZ)
    {
        if (mobileMaxY <= obstacleMinY + ContactTolerance ||
            mobileMinY >= obstacleMaxY - ContactTolerance)
        {
            return false;
        }

        double cos = Math.Cos(yaw);
        double sin = Math.Sin(yaw);
        double absCos = Math.Abs(cos);
        double absSin = Math.Abs(sin);

        double obstacleCenterX = (obstacleMinX + obstacleMaxX) * 0.5;
        double obstacleCenterZ = (obstacleMinZ + obstacleMaxZ) * 0.5;
        double obstacleHalfX = (obstacleMaxX - obstacleMinX) * 0.5;
        double obstacleHalfZ = (obstacleMaxZ - obstacleMinZ) * 0.5;
        double deltaX = obstacleCenterX - mobileCenterX;
        double deltaZ = obstacleCenterZ - mobileCenterZ;

        if (Separated(
                Math.Abs(deltaX),
                obstacleHalfX + mobileHalfX * absCos + mobileHalfZ * absSin)) return false;

        if (Separated(
                Math.Abs(deltaZ),
                obstacleHalfZ + mobileHalfX * absSin + mobileHalfZ * absCos)) return false;

        // Axe X local du bloc mobile après rotation : (cos, -sin).
        if (Separated(
                Math.Abs(deltaX * cos - deltaZ * sin),
                mobileHalfX + obstacleHalfX * absCos + obstacleHalfZ * absSin)) return false;

        // Axe Z local du bloc mobile après rotation : (sin, cos).
        if (Separated(
                Math.Abs(deltaX * sin + deltaZ * cos),
                mobileHalfZ + obstacleHalfX * absSin + obstacleHalfZ * absCos)) return false;

        return true;
    }

    private static bool Separated(double centerDistance, double projectedRadii)
        => centerDistance >= projectedRadii - ContactTolerance;
}
