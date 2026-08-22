using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using IndependentVehicles.Core;
using IndependentVehicles.Rider;

namespace IndependentVehicles.Entities;

public sealed class VehicleControlSeat : IMountableSeat
{
    private readonly EntityVehicleStructure vehicle;
    private readonly EntityControls controls = new();
    private readonly EntityPos seatPosition = new();
    private AngleConstraint? bodyYawConstraint;
    private bool warnedUnsafeClientAnchor;

    public VehicleControlSeat(EntityVehicleStructure vehicle)
    {
        this.vehicle = vehicle;
        Config = new SeatConfig
        {
            SeatId = "control",
            // La conduite est interprétée par EntityVehicleStructure sur le serveur.
            // Déclarer ce siège comme une monture prédite ferait volontairement
            // ignorer au client les positions autoritaires envoyées par le serveur.
            Controllable = false,
            RiderOffset = new Vec3f(
                (float)VehicleRiderMath.DefaultLocalX,
                (float)VehicleRiderMath.DefaultLocalY,
                (float)VehicleRiderMath.DefaultLocalZ),
            EyeHeight = 1.4f,
            AngleMode = EnumMountAngleMode.FixateYaw,
            Animation = "sitflooridle"
        };
    }

    public SeatConfig Config { get; set; }
    public string SeatId { get => Config.SeatId; set => Config.SeatId = value; }
    public long PassengerEntityIdForInit { get; set; }
    public bool DoTeleportOnUnmount { get; set; } = false;
    /// <summary>
    /// Siège volontairement virtuel. Vanilla sait que le joueur est monté, mais
    /// ne traite jamais la structure comme une monture dont le client pourrait
    /// envoyer la position. La structure reste autoritaire côté serveur.
    /// </summary>
    public Entity Entity => null!;
    public Entity? Passenger { get; private set; }
    public EntityVehicleStructure Vehicle => vehicle;
    public IMountable MountSupplier => vehicle;
    public bool CanControl => false;
    public EnumMountAngleMode AngleMode => EnumMountAngleMode.FixateYaw;
    public AnimationMetaData SuggestedAnimation => new()
    {
        Animation = "sitflooridle",
        Code = "sitflooridle"
    };
    public bool SkipIdleAnimation => true;
    public float FpHandPitchFollow => 0;
    public Vec3f LocalEyePos => new(0, 1.4f, 0);
    public EntityPos SeatPosition
    {
        get
        {
            VehicleRiderAnchor anchor = VehicleRiderMath.LocalToWorld(
                vehicle.Pos.X,
                vehicle.Pos.Y,
                vehicle.Pos.Z,
                vehicle.Pos.Yaw,
                VehicleRiderMath.DefaultLocalX,
                VehicleRiderMath.DefaultLocalY,
                VehicleRiderMath.DefaultLocalZ);

            if (vehicle.Api is ICoreClientAPI clientApi &&
                Passenger is EntityAgent passenger &&
                clientApi.World.Player?.Entity?.EntityId == passenger.EntityId &&
                (passenger.Pos.Dimension != vehicle.Pos.Dimension ||
                 !VehicleRiderMath.IsPlausibleCorrection(
                     passenger.Pos.X,
                     passenger.Pos.Y,
                     passenger.Pos.Z,
                     anchor,
                     VehicleRiderAttachmentSystem.MaximumClientAnchorCorrectionDistance)))
            {
                if (!warnedUnsafeClientAnchor)
                {
                    warnedUnsafeClientAnchor = true;
                    clientApi.Logger.Warning(
                        "[Mobilis Core] Ancre client dangereuse ignorée pour le véhicule {0}: joueur ({1:0.00}, {2:0.00}, {3:0.00}), ancre ({4:0.00}, {5:0.00}, {6:0.00}). Position actuelle conservée en attendant Vanilla.",
                        vehicle.EntityId,
                        passenger.Pos.X,
                        passenger.Pos.Y,
                        passenger.Pos.Z,
                        anchor.X,
                        anchor.Y,
                        anchor.Z);
                }

                seatPosition.SetFrom(passenger.Pos);
                seatPosition.Motion.Set(0, 0, 0);
                return seatPosition;
            }

            seatPosition.SetFrom(vehicle.Pos);
            seatPosition.SetPos(anchor.X, anchor.Y, anchor.Z);
            seatPosition.Pitch = 0;
            seatPosition.Roll = 0;
            seatPosition.Yaw = vehicle.Pos.Yaw;
            seatPosition.Motion.Set(0, 0, 0);
            return seatPosition;
        }
    }
    public Matrixf RenderTransform => new Matrixf().Identity();
    public EntityControls Controls => controls;

    public void MountableToTreeAttributes(TreeAttribute tree)
    {
        tree.SetString("className", IndependentVehiclesSystem.MountableClassName);
        tree.SetLong("entityId", vehicle.EntityId);
        tree.SetString("seatId", SeatId);
    }

    public void DidUnmount(EntityAgent entityAgent)
    {
        entityAgent.Api.ModLoader
            .GetModSystem<VehicleRiderAttachmentSystem>()
            .OnSeatUnmounted(vehicle, entityAgent);
        ReleaseRiderOrientation(entityAgent);
        Passenger = null;
        PassengerEntityIdForInit = 0;
        controls.StopAllMovement();
        vehicle.OnDriverUnmounted(entityAgent);
    }

    public void DidMount(EntityAgent entityAgent)
    {
        if (Passenger is EntityAgent previous && previous != entityAgent) previous.TryUnmount();
        Passenger = entityAgent;
        PassengerEntityIdForInit = entityAgent.EntityId;
        controls.StopAllMovement();
        ApplyRiderOrientation(entityAgent);

        // Comme EntityRideableSeat vanilla : l'angle de regard qui précédait
        // le clic ne doit pas devenir le repère initial de la monture.
        if (entityAgent.Api is ICoreClientAPI clientApi &&
            clientApi.World.Player?.Entity?.EntityId == entityAgent.EntityId)
        {
            clientApi.Input.MouseYaw = vehicle.Pos.Yaw;
        }

        entityAgent.Api.ModLoader
            .GetModSystem<VehicleRiderAttachmentSystem>()
            .OnSeatMounted(vehicle, entityAgent);
        vehicle.OnDriverMounted(entityAgent);
    }

    public bool CanUnmount(EntityAgent entityAgent) => true;

    public bool CanMount(EntityAgent entityAgent) => Passenger is null || Passenger == entityAgent;

    public bool TryReleaseStalePassenger()
    {
        if (Passenger is not EntityAgent passenger || passenger.MountedOn == this) return false;

        ReleaseRiderOrientation(passenger);
        Passenger = null;
        PassengerEntityIdForInit = 0;
        controls.StopAllMovement();
        vehicle.OnDriverLost();
        return true;
    }

    public void ResetControls() => controls.StopAllMovement();

    internal void ApplyRiderOrientation(EntityAgent rider)
    {
        float yaw = vehicle.Pos.Yaw;
        if (rider is EntityPlayer player)
        {
            bodyYawConstraint ??= new AngleConstraint(yaw, 0);
            bodyYawConstraint.X = yaw;
            bodyYawConstraint.Y = 0;
            player.BodyYawLimits = bodyYawConstraint;
        }

        rider.BodyYaw = yaw;
        rider.BodyYawServer = yaw;
    }

    private void ReleaseRiderOrientation(EntityAgent rider)
    {
        if (rider is EntityPlayer player &&
            ReferenceEquals(player.BodyYawLimits, bodyYawConstraint))
        {
            player.BodyYawLimits = null;
        }
        bodyYawConstraint = null;
    }

}
