using IndependentVehicles.Core;
using IndependentVehicles.Entities;
using IndependentVehicles.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace IndependentVehicles.Rider;

/// <summary>
/// Couche d'attachement indépendante de la physique de la structure.
///
/// Le montage public de Vintage Story est conservé uniquement pour que la
/// physique vanilla sache que le joueur est assis. Le siège est virtuel
/// (VehicleControlSeat.Entity == null) : la structure ne devient jamais une
/// monture pilotée par les paquets vanilla. Les paquets du mod ne transportent
/// que les commandes et l'identité de l'attachement, jamais une position monde.
/// </summary>
public sealed class VehicleRiderAttachmentSystem : ModSystem
{
    private const string NetworkChannel = "independentvehicles:rider";
    private const long InputHeartbeatMilliseconds = 150;
    private const long InputTimeoutMilliseconds = 600;
    private const long StateRequestIntervalMilliseconds = 250;
    private const long ClientMountMismatchGraceMilliseconds = 1500;
    private const long ClientMountMismatchAbandonMilliseconds = 5000;
    internal const double MaximumClientAnchorCorrectionDistance = 16;
    private const double CorrectionWarningDistance = 4;
    private const long CorrectionWarningIntervalMilliseconds = 5000;

    private readonly Dictionary<long, ServerAttachment> serverAttachments = [];

    private ICoreServerAPI? sapi;
    private ICoreClientAPI? capi;
    private IServerNetworkChannel? serverChannel;
    private IClientNetworkChannel? clientChannel;

    private long clientVehicleEntityId;
    private long clientAttachmentId;
    private int clientSequence;
    private int clientLastControlBits = -1;
    private long clientLastSentMilliseconds;
    private long clientLastStateRequestMilliseconds;
    private bool clientDetachHeld;
    private long clientMountMismatchVehicleEntityId;
    private long clientMountMismatchStartedMilliseconds;
    private bool clientMountMismatchDetachSent;
    private VehicleRiderStatePacket? pendingClientState;
    private long nextServerAttachmentId;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        serverChannel = api.Network
            .RegisterChannel(NetworkChannel)
            .RegisterMessageType<VehicleRiderInputPacket>()
            .RegisterMessageType<VehicleRiderStatePacket>()
            .SetMessageHandler<VehicleRiderInputPacket>(OnRiderInputPacket);
        api.Event.RegisterGameTickListener(OnServerTick, 20);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        clientChannel = api.Network
            .RegisterChannel(NetworkChannel)
            .RegisterMessageType<VehicleRiderInputPacket>()
            .RegisterMessageType<VehicleRiderStatePacket>()
            .SetMessageHandler<VehicleRiderStatePacket>(OnRiderStatePacket);
        api.Event.RegisterGameTickListener(OnClientTick, 20);
    }

    public bool TryAttach(EntityVehicleStructure vehicle, EntityAgent rider)
    {
        VehicleControlSeat? seat = vehicle.ControlSeat;
        if (seat is null) return false;

        seat.ResetControls();
        bool attached = rider.TryMount(seat);
        if (attached) ApplyAnchor(rider, vehicle);
        return attached;
    }

    public void OnSeatMounted(EntityVehicleStructure vehicle, EntityAgent rider)
    {
        ApplyAnchor(rider, vehicle);

        if (rider.Api.Side == EnumAppSide.Server)
        {
            var attachment = NewServerAttachment(rider.EntityId);
            serverAttachments[vehicle.EntityId] = attachment;
            SendState(rider, vehicle.EntityId, attached: true, attachment);
            return;
        }

        if (capi?.World.Player?.Entity?.EntityId == rider.EntityId)
        {
            // L'état réseau peut arriver juste avant l'attribut vanilla mountedOn.
            // Dans ce cas, conserver le jeton déjà validé par le serveur.
            if (clientVehicleEntityId != vehicle.EntityId)
                ResetClientInput(vehicle.EntityId);
        }
    }

    public void OnSeatUnmounted(EntityVehicleStructure vehicle, EntityAgent rider)
    {
        // La dernière position valide est celle de l'ancre du siège. Elle est
        // appliquée avant que la rematérialisation existante ne soit appelée.
        ApplyAnchor(rider, vehicle);

        if (rider.Api.Side == EnumAppSide.Server)
        {
            ServerAttachment? detachedAttachment = null;
            if (serverAttachments.TryGetValue(vehicle.EntityId, out ServerAttachment? attachment) &&
                attachment.RiderEntityId == rider.EntityId)
            {
                serverAttachments.Remove(vehicle.EntityId);
                attachment.Input.Stop();
                detachedAttachment = attachment;
            }
            SendState(rider, vehicle.EntityId, attached: false, detachedAttachment);
            return;
        }

        if (capi?.World.Player?.Entity?.EntityId == rider.EntityId)
        {
            if (clientVehicleEntityId == vehicle.EntityId && clientAttachmentId != 0)
            {
                // Une descente locale peut précéder l'état détaché du serveur,
                // ou être la désynchronisation que nous devons arrêter. Garder
                // le jeton permet encore d'envoyer la demande de descente sûre.
                BeginClientMountMismatch(vehicle.EntityId, capi.World.ElapsedMilliseconds);
            }
            else
            {
                ResetClientInput(0);
            }
        }
    }

    public bool TryGetDriverInput(
        EntityVehicleStructure vehicle,
        out bool forward,
        out bool backward,
        out bool left,
        out bool right)
    {
        forward = backward = left = right = false;
        if (sapi is null || vehicle.ControlSeat is not VehicleControlSeat seat ||
            seat.Passenger is not EntityAgent rider)
        {
            return false;
        }
        if (rider.MountedOn != seat)
        {
            serverAttachments.Remove(vehicle.EntityId);
            seat.TryReleaseStalePassenger();
            return false;
        }

        if (!serverAttachments.TryGetValue(vehicle.EntityId, out ServerAttachment? attachment) ||
            attachment.RiderEntityId != rider.EntityId)
        {
            attachment = NewServerAttachment(rider.EntityId);
            serverAttachments[vehicle.EntityId] = attachment;
            SendState(rider, vehicle.EntityId, attached: true, attachment);
        }

        int bits = attachment.Input.FreshControlBits(
            sapi.World.ElapsedMilliseconds,
            InputTimeoutMilliseconds);
        forward = (bits & VehicleRiderInputBits.Forward) != 0;
        backward = (bits & VehicleRiderInputBits.Backward) != 0;
        left = (bits & VehicleRiderInputBits.Left) != 0;
        right = (bits & VehicleRiderInputBits.Right) != 0;
        return true;
    }

    private void OnClientTick(float dt)
    {
        if (capi?.World.Player?.Entity is not EntityAgent rider)
        {
            return;
        }

        if (pendingClientState is VehicleRiderStatePacket pending)
        {
            pendingClientState = null;
            ApplyClientState(rider, pending);
        }

        long now = capi.World.ElapsedMilliseconds;
        VehicleControlSeat? seat = rider.MountedOn as VehicleControlSeat;

        // Ne jamais appeler TryMount côté client pour réparer un mountedOn
        // manquant. À grande distance de l'origine, l'entité interpolée peut
        // momentanément employer le repère décalé du client : forcer la monture
        // avec cette position téléporterait uniquement le client. Si Vanilla ne
        // rétablit pas naturellement le même siège, demander au serveur la
        // descente normale est la seule issue sûre offerte par l'API publique.
        if (clientVehicleEntityId != 0 && clientAttachmentId != 0 &&
            (seat is null || seat.Vehicle.EntityId != clientVehicleEntityId))
        {
            if (clientMountMismatchVehicleEntityId != clientVehicleEntityId)
            {
                BeginClientMountMismatch(clientVehicleEntityId, now);
            }

            if (!clientMountMismatchDetachSent &&
                now - clientMountMismatchStartedMilliseconds >= ClientMountMismatchGraceMilliseconds &&
                clientChannel?.Connected == true)
            {
                clientChannel.SendPacket(new VehicleRiderInputPacket
                {
                    VehicleEntityId = clientVehicleEntityId,
                    Sequence = ++clientSequence,
                    ControlBits = 0,
                    RequestDetach = true,
                    AttachmentId = clientAttachmentId
                });
                clientMountMismatchDetachSent = true;
                clientLastControlBits = 0;
                clientLastSentMilliseconds = now;
                capi.Logger.Warning(
                    "[IndependentVehicles] État de siège incohérent pour le véhicule {0}; descente serveur sûre demandée sans correction de position client.",
                    clientVehicleEntityId);
            }

            if (clientMountMismatchDetachSent &&
                now - clientMountMismatchStartedMilliseconds >= ClientMountMismatchAbandonMilliseconds)
            {
                // Le canal de jeu est fiable. Sans nouvel état après ce délai,
                // le serveur a déjà refusé la requête parce qu'il avait lui-même
                // terminé la descente : oublier seulement l'état client périmé.
                ResetClientInput(0);
            }

            return;
        }

        ResetClientMountMismatch();

        if (seat is null) return;

        EntityVehicleStructure vehicle = seat.Vehicle;
        if (clientVehicleEntityId != vehicle.EntityId) ResetClientInput(vehicle.EntityId);

        // Filet public contre les corrections normales : la physique vanilla lit
        // la même SeatPosition juste avant chacun de ses paquets automatiques.
        // Cette application supplémentaire garde aussi la caméra/rendu entre deux
        // pas physiques, sans toucher à une méthode interne du jeu.
        ApplyAnchor(rider, vehicle);

        // Le jeton vient exclusivement du serveur. Tant que l'état d'attachement
        // correspondant n'est pas arrivé, aucune ancienne commande ne peut être
        // réutilisée pour ce siège.
        if (clientAttachmentId == 0)
        {
            // OnSeatMounted côté serveur précède la réplication de l'attribut
            // Vanilla mountedOn. Le premier paquet d'état peut donc arriver au
            // client avant que le siège existe localement, puis être oublié par
            // le tick non monté. Redemander l'état rend cet ordre inoffensif.
            if (clientChannel?.Connected == true &&
                (clientLastStateRequestMilliseconds == 0 ||
                 now - clientLastStateRequestMilliseconds >= StateRequestIntervalMilliseconds))
            {
                clientChannel.SendPacket(new VehicleRiderInputPacket
                {
                    VehicleEntityId = vehicle.EntityId,
                    RequestState = true
                });
                clientLastStateRequestMilliseconds = now;
            }
            return;
        }

        EntityControls controls = seat.Controls;
        int bits = 0;
        if (controls.Forward) bits |= VehicleRiderInputBits.Forward;
        if (controls.Backward) bits |= VehicleRiderInputBits.Backward;
        if (controls.Left) bits |= VehicleRiderInputBits.Left;
        if (controls.Right) bits |= VehicleRiderInputBits.Right;

        bool detachPressed = controls.Sneak && !clientDetachHeld;
        clientDetachHeld = controls.Sneak;
        bool heartbeatDue = now - clientLastSentMilliseconds >= InputHeartbeatMilliseconds;
        if (!detachPressed && bits == clientLastControlBits && !heartbeatDue) return;

        if (clientChannel?.Connected == true)
        {
            clientChannel.SendPacket(new VehicleRiderInputPacket
            {
                VehicleEntityId = vehicle.EntityId,
                Sequence = ++clientSequence,
                ControlBits = detachPressed ? 0 : bits,
                RequestDetach = detachPressed,
                AttachmentId = clientAttachmentId
            });
            clientLastControlBits = detachPressed ? 0 : bits;
            clientLastSentMilliseconds = now;
        }
    }

    private void OnServerTick(float dt)
    {
        if (sapi is null || serverAttachments.Count == 0) return;

        foreach ((long vehicleId, ServerAttachment attachment) in serverAttachments.ToArray())
        {
            if (sapi.World.GetEntityById(vehicleId) is not EntityVehicleStructure vehicle)
            {
                serverAttachments.Remove(vehicleId);
                continue;
            }

            VehicleControlSeat? seat = vehicle.ControlSeat;
            if (seat?.Passenger is not EntityAgent rider ||
                rider.EntityId != attachment.RiderEntityId)
            {
                serverAttachments.Remove(vehicleId);
                continue;
            }
            if (rider.MountedOn != seat)
            {
                serverAttachments.Remove(vehicleId);
                seat.TryReleaseStalePassenger();
                continue;
            }

            // Le serveur reprend l'autorité après traitement des paquets vanilla.
            // Le client normal envoie déjà cette même ancre grâce au siège virtuel ;
            // une valeur obsolète ou falsifiée ne survit donc pas au tick serveur.
            CorrectServerAnchor(rider, vehicle, attachment);
        }
    }

    private void OnRiderInputPacket(IServerPlayer player, VehicleRiderInputPacket packet)
    {
        if (sapi is null ||
            sapi.World.GetEntityById(packet.VehicleEntityId) is not EntityVehicleStructure vehicle ||
            vehicle.ControlSeat?.Passenger != player.Entity ||
            player.Entity.MountedOn != vehicle.ControlSeat)
        {
            return;
        }

        if (!serverAttachments.TryGetValue(vehicle.EntityId, out ServerAttachment? attachment) ||
            attachment.RiderEntityId != player.Entity.EntityId)
        {
            attachment = NewServerAttachment(player.Entity.EntityId);
            serverAttachments[vehicle.EntityId] = attachment;
            SendState(player.Entity, vehicle.EntityId, attached: true, attachment);
        }

        // Requête de resynchronisation sans commande. Elle n'est acceptée
        // qu'après les contrôles ci-dessus : le demandeur doit être le passager
        // réellement monté sur ce véhicule.
        if (packet.RequestState)
        {
            SendState(player.Entity, vehicle.EntityId, attached: true, attachment);
            CorrectServerAnchor(player.Entity, vehicle, attachment);
            return;
        }

        if (packet.AttachmentId != attachment.AttachmentId) return;

        if (!attachment.Input.Accept(
                packet.Sequence,
                packet.ControlBits,
                sapi.World.ElapsedMilliseconds))
        {
            return;
        }

        CorrectServerAnchor(player.Entity, vehicle, attachment);
        if (packet.RequestDetach)
        {
            attachment.Input.Stop();
            player.Entity.TryUnmount();
        }
    }

    private void OnRiderStatePacket(VehicleRiderStatePacket packet)
    {
        // Un état sauvegardé peut arriver pendant l'écran de connexion. Garder
        // seulement le plus récent jusqu'à la création du joueur évite de
        // dépendre de l'ordre de réplication Vanilla de mountedOn.
        if (capi?.World.Player?.Entity is not EntityAgent rider)
        {
            pendingClientState = packet;
            return;
        }

        ApplyClientState(rider, packet);
    }

    private void ApplyClientState(EntityAgent rider, VehicleRiderStatePacket packet)
    {
        if (rider.EntityId != packet.RiderEntityId) return;

        if (packet.Attached)
        {
            if (clientVehicleEntityId != packet.VehicleEntityId)
                ResetClientInput(packet.VehicleEntityId);
            clientAttachmentId = packet.AttachmentId;
            clientLastStateRequestMilliseconds = 0;
            clientSequence = Math.Max(clientSequence, packet.LastAcceptedInputSequence);
        }
        else if (clientVehicleEntityId == packet.VehicleEntityId &&
                 (clientAttachmentId == 0 || clientAttachmentId == packet.AttachmentId))
        {
            ResetClientInput(0);
        }
    }

    private void SendState(
        EntityAgent rider,
        long vehicleEntityId,
        bool attached,
        ServerAttachment? attachment)
    {
        if (serverChannel is null || rider is not EntityPlayer playerEntity ||
            playerEntity.Player is not IServerPlayer player)
        {
            return;
        }

        serverChannel.SendPacket(new VehicleRiderStatePacket
        {
            VehicleEntityId = vehicleEntityId,
            RiderEntityId = rider.EntityId,
            Attached = attached,
            LocalX = attachment?.LocalX ?? VehicleRiderMath.DefaultLocalX,
            LocalY = attachment?.LocalY ?? VehicleRiderMath.DefaultLocalY,
            LocalZ = attachment?.LocalZ ?? VehicleRiderMath.DefaultLocalZ,
            LastAcceptedInputSequence = attachment?.Input.LastSequence ?? -1,
            AttachmentId = attachment?.AttachmentId ?? 0
        }, player);
    }

    internal static void ApplyAnchor(EntityAgent rider, EntityVehicleStructure vehicle)
    {
        VehicleRiderAnchor anchor = GetAnchor(
            vehicle,
            VehicleRiderMath.DefaultLocalX,
            VehicleRiderMath.DefaultLocalY,
            VehicleRiderMath.DefaultLocalZ);
        ApplyAnchor(rider, vehicle, anchor);
    }

    private void CorrectServerAnchor(
        EntityAgent rider,
        EntityVehicleStructure vehicle,
        ServerAttachment attachment)
    {
        if (sapi is null) return;

        VehicleRiderAnchor anchor = GetAnchor(
            vehicle,
            attachment.LocalX,
            attachment.LocalY,
            attachment.LocalZ);
        double dx = rider.Pos.X - anchor.X;
        double dy = rider.Pos.Y - anchor.Y;
        double dz = rider.Pos.Z - anchor.Z;
        double warningDistanceSquared = CorrectionWarningDistance * CorrectionWarningDistance;
        long now = sapi.World.ElapsedMilliseconds;
        if (dx * dx + dy * dy + dz * dz > warningDistanceSquared &&
            (attachment.LastCorrectionWarningMilliseconds == 0 ||
             now - attachment.LastCorrectionWarningMilliseconds >= CorrectionWarningIntervalMilliseconds))
        {
            attachment.LastCorrectionWarningMilliseconds = now;
            sapi.Logger.Warning(
                "[IndependentVehicles] Position Vanilla du conducteur {0} corrigée vers l’ancre du véhicule {1} (écart {2:0.00} blocs).",
                rider.GetName(),
                vehicle.EntityId,
                Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        ApplyAnchor(rider, vehicle, anchor);
    }

    private static VehicleRiderAnchor GetAnchor(
        EntityVehicleStructure vehicle,
        double localX,
        double localY,
        double localZ) =>
        VehicleRiderMath.LocalToWorld(
            vehicle.Pos.X,
            vehicle.Pos.Y,
            vehicle.Pos.Z,
            vehicle.Pos.Yaw,
            localX,
            localY,
            localZ);

    private static void ApplyAnchor(
        EntityAgent rider,
        EntityVehicleStructure vehicle,
        VehicleRiderAnchor anchor)
    {
        if (rider.Api.Side == EnumAppSide.Client &&
            (rider.Pos.Dimension != vehicle.Pos.Dimension ||
             !VehicleRiderMath.IsPlausibleCorrection(
                 rider.Pos.X,
                 rider.Pos.Y,
                 rider.Pos.Z,
                 anchor,
                 MaximumClientAnchorCorrectionDistance)))
        {
            // La SeatPosition possède le même garde-fou. Ne jamais copier ici
            // une position d'entité encore exprimée dans le repère client local.
            vehicle.ControlSeat?.ApplyRiderOrientation(rider);
            return;
        }

        rider.Pos.SetPos(anchor.X, anchor.Y, anchor.Z);
        rider.Pos.Dimension = vehicle.Pos.Dimension;
        rider.Pos.Motion.Set(0, 0, 0);
        rider.PositionBeforeFalling.Set(anchor.X, anchor.Y, anchor.Z);
        vehicle.ControlSeat?.ApplyRiderOrientation(rider);
    }

    private void ResetClientInput(long vehicleEntityId)
    {
        clientVehicleEntityId = vehicleEntityId;
        clientAttachmentId = 0;
        clientSequence = 0;
        clientLastControlBits = -1;
        clientLastSentMilliseconds = 0;
        clientLastStateRequestMilliseconds = 0;
        clientDetachHeld = false;
        ResetClientMountMismatch();
    }

    private void ResetClientMountMismatch()
    {
        clientMountMismatchVehicleEntityId = 0;
        clientMountMismatchStartedMilliseconds = 0;
        clientMountMismatchDetachSent = false;
    }

    private void BeginClientMountMismatch(long vehicleEntityId, long now)
    {
        clientMountMismatchVehicleEntityId = vehicleEntityId;
        clientMountMismatchStartedMilliseconds = now;
        clientMountMismatchDetachSent = false;
    }

    private ServerAttachment NewServerAttachment(long riderEntityId) =>
        new(
            ++nextServerAttachmentId,
            riderEntityId,
            VehicleRiderMath.DefaultLocalX,
            VehicleRiderMath.DefaultLocalY,
            VehicleRiderMath.DefaultLocalZ);

    private sealed class ServerAttachment(
        long attachmentId,
        long riderEntityId,
        double localX,
        double localY,
        double localZ)
    {
        public long AttachmentId { get; } = attachmentId;
        public long RiderEntityId { get; } = riderEntityId;
        public double LocalX { get; } = localX;
        public double LocalY { get; } = localY;
        public double LocalZ { get; } = localZ;
        public long LastCorrectionWarningMilliseconds { get; set; }
        public VehicleRiderInputState Input { get; } = new();
    }
}
