using IndependentVehicles.Core;
using IndependentVehicles.Rider;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace IndependentVehicles.Entities;

public sealed class EntityVehicleStructure : Entity, IMountable
{
    public const string SnapshotAttribute = "independentvehicles:snapshot";
    private const float MoveSpeed = 3f;
    private const float TurnSpeed = 1.6f;
    private const double MaxTranslationPerCollisionStep = 0.1;
    private const float MaxRotationPerCollisionStep = 0.04f;

    private VehicleControlSeat? seat;
    private VehicleSnapshot? snapshot;
    private readonly VehicleMaterializationGuard materialization = new();

    public VehicleSnapshot? Snapshot => snapshot;
    public VehicleControlSeat? ControlSeat => seat;
    public IMountableSeat[] Seats => seat is null ? [] : [seat];
    public EntityPos Position => Pos;
    public double StepPitch => 0;
    // Le prototype est strictement autoritaire côté serveur. Renvoyer le passager
    // ici classerait la structure comme monture prédite : l'interpolation vanilla
    // ignorerait alors ses paquets de position sur le client local.
    public Entity? Controller => null;
    public Entity OnEntity => this;
    public EntityControls? ControllingControls => null;
    public override double FrustumSphereRadius => Math.Max(3, SelectionBox is null ? 3 : Math.Max(SelectionBox.XSize, Math.Max(SelectionBox.YSize, SelectionBox.ZSize)));

    public override void Initialize(EntityProperties properties, ICoreAPI api, long inChunkIndex3d)
    {
        base.Initialize(properties, api, inChunkIndex3d);
        seat = new VehicleControlSeat(this);
        ReadSnapshot();
        UpdateBounds();
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (Api.Side == EnumAppSide.Server) SimulateMovement(dt);
    }

    private void SimulateMovement(float dt)
    {
        if (materialization.Phase != VehicleMaterializationPhase.Mobile || seat is null) return;

        VehicleRiderAttachmentSystem riderSystem =
            Api.ModLoader.GetModSystem<VehicleRiderAttachmentSystem>();
        if (!riderSystem.TryGetDriverInput(
                this,
                out bool forward,
                out bool backward,
                out bool left,
                out bool right))
        {
            StopCompletely();
            return;
        }

        float turn = VehicleControlMath.TurnInput(left, right);
        float drive = VehicleControlMath.DriveInput(forward, backward);
        if (turn == 0 && drive == 0)
        {
            StopCompletely();
            return;
        }

        IndependentVehiclesSystem system = Api.ModLoader.GetModSystem<IndependentVehiclesSystem>();
        double translation = Math.Abs(drive * MoveSpeed * dt);
        float rotation = Math.Abs(turn * TurnSpeed * dt);
        int collisionSteps = Math.Clamp(
            (int)Math.Ceiling(Math.Max(
                translation / MaxTranslationPerCollisionStep,
                rotation / MaxRotationPerCollisionStep)),
            1,
            256);
        float stepTime = dt / collisionSteps;

        for (int step = 0; step < collisionSteps; step++)
        {
            float oldYaw = Pos.Yaw;
            double oldX = Pos.X;
            double oldY = Pos.Y;
            double oldZ = Pos.Z;

            Pos.Yaw += turn * TurnSpeed * stepTime;
            if (drive != 0)
            {
                (double forwardX, double forwardZ) = VehicleControlMath.ForwardVector(Pos.Yaw);
                Pos.X += forwardX * MoveSpeed * drive * stepTime;
                Pos.Z += forwardZ * MoveSpeed * drive * stepTime;
            }

            if (TryResolveGroundPose(system, drive != 0, oldY)) continue;

            Pos.X = oldX;
            Pos.Y = oldY;
            Pos.Z = oldZ;
            Pos.Yaw = oldYaw;
            break;
        }

        StopCompletely();
    }

    private bool TryResolveGroundPose(IndependentVehiclesSystem system, bool mayChangeLevel, double previousY)
    {
        if (system.IsMobilePoseClear(this))
        {
            if (system.HasGroundSupport(this)) return true;
            if (!mayChangeLevel) return false;

            // Descente d’une seule marche. Sans sol à ce niveau, on refuse le
            // déplacement afin qu’un véhicule terrestre ne flotte pas au-dessus d’un vide.
            Pos.Y = previousY - 1;
            if (system.IsMobilePoseClear(this) && system.HasGroundSupport(this)) return true;
            Pos.Y = previousY;
            return false;
        }

        if (!mayChangeLevel) return false;

        // Montée d’une seule marche. Un mur de deux blocs reste donc infranchissable,
        // tout comme une marche dont l’espace supérieur est occupé.
        Pos.Y = previousY + 1;
        if (system.IsMobilePoseClear(this) && system.HasGroundSupport(this)) return true;
        Pos.Y = previousY;
        return false;
    }

    public void SetSnapshot(VehicleSnapshot value)
    {
        snapshot = value;
        WatchedAttributes.SetString(SnapshotAttribute, VehicleJson.Serialize(value));
        WatchedAttributes.MarkPathDirty(SnapshotAttribute);
        UpdateBounds();
        UpdateLight();
    }

    public void ReadSnapshot()
    {
        snapshot = VehicleJson.Deserialize<VehicleSnapshot>(WatchedAttributes.GetString(SnapshotAttribute));
        UpdateLight();
    }

    public bool AnyMounted() => seat?.Passenger is not null;

    public IMountableSeat? FindSeat(string? seatId) => seatId == "control" ? seat : null;

    public void OnDriverMounted(EntityAgent entityAgent)
    {
        StopCompletely();
    }

    public void OnDriverUnmounted(EntityAgent entityAgent)
    {
        StopCompletely();
        if (Api.Side != EnumAppSide.Server) return;
        Api.ModLoader.GetModSystem<IndependentVehiclesSystem>().Materialize(this, entityAgent);
    }

    public void OnDriverLost()
    {
        StopCompletely();
        if (Api.Side != EnumAppSide.Server) return;
        Api.ModLoader.GetModSystem<IndependentVehiclesSystem>().MaterializeAbandoned(this);
    }

    public bool TryBeginMaterialization() => materialization.TryBegin();

    public bool CompleteMaterialization() => materialization.TryComplete();

    public bool CancelMaterialization()
    {
        StopCompletely();
        return materialization.TryCancel();
    }

    public bool CancelMaterializationAndRemount(EntityAgent entityAgent)
    {
        if (!CancelMaterialization()) return false;
        return Api.ModLoader
            .GetModSystem<VehicleRiderAttachmentSystem>()
            .TryAttach(this, entityAgent);
    }

    public void DiscardWithoutMaterializing()
    {
        materialization.TryDiscard();
        StopCompletely();
    }

    public void StopCompletely()
    {
        Pos.Motion.Set(0, 0, 0);
    }

    private void UpdateBounds()
    {
        if (snapshot?.Blocks.Count is not > 0) return;

        float minX = snapshot.Blocks.Min(block => block.Offset.X) - 0.5f;
        float minY = snapshot.Blocks.Min(block => block.Offset.Y);
        float minZ = snapshot.Blocks.Min(block => block.Offset.Z) - 0.5f;
        float maxX = snapshot.Blocks.Max(block => block.Offset.X) + 0.5f;
        float maxY = snapshot.Blocks.Max(block => block.Offset.Y) + 1f;
        float maxZ = snapshot.Blocks.Max(block => block.Offset.Z) + 0.5f;

        Cuboidf box = new(minX, minY, minZ, maxX, maxY, maxZ);
        CollisionBox = box;
        OriginCollisionBox = box.Clone();
        SelectionBox = box.Clone();
        OriginSelectionBox = box.Clone();
    }

    private void UpdateLight()
    {
        byte[]? merged = null;
        if (snapshot is not null)
        {
            foreach (byte[] light in snapshot.Blocks
                         .Select(block => block.LightHsv)
                         .Where(light => light is { Length: >= 3 })
                         .Cast<byte[]>())
            {
                if (light[2] == 0) continue;
                merged = merged is null
                    ? light.ToArray()
                    : ColorUtil.MergeLightHSV(merged, light);
            }
        }
        LightHsv = merged;
    }
}
