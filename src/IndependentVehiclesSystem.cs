using System.Text;
using IndependentVehicles.Blocks;
using IndependentVehicles.Client;
using IndependentVehicles.Core;
using IndependentVehicles.Entities;
using IndependentVehicles.Glue;
using IndependentVehicles.Items;
using IndependentVehicles.Network;
using IndependentVehicles.Rider;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace IndependentVehicles;

public sealed class IndependentVehiclesSystem : ModSystem
{
    public const string ProductName = "Mobilis - Core";
    public const string Domain = "independentvehicles";
    public const string ModVersion = "0.5.0";
    public const string MountableClassName = "independentvehicles:structureseat";
    private const string GlueSaveKey = "independentvehicles:gluebonds";
    private const string GlueNetworkChannel = "independentvehicles:gluebrush";
    private const int GlueHighlightSlot = 7314;
    private const int MaxBlocks = 64;
    private const int MaxWidth = 9;
    private const int MaxHeight = 5;
    private const int MaxBlockEntityDataBytes = 256 * 1024;
    private const int MaxTotalBlockEntityDataBytes = 1024 * 1024;
    private const long ReactivationCooldownMilliseconds = 1000;

    private readonly GlueRegistry glue = new();
    private readonly Dictionary<string, long> reactivationBlockedUntil = [];
    private ICoreServerAPI? sapi;
    private ICoreClientAPI? capi;
    private IServerNetworkChannel? serverGlueChannel;
    private IClientNetworkChannel? clientGlueChannel;
    private GridPos? clientStrokeLast;
    private GridPos? clientQueryTarget;
    private bool clientGlueWasHeld;

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("VehicleControlSeat", typeof(BlockVehicleControlSeat));
        api.RegisterItemClass("StructureGlue", typeof(ItemStructureGlue));
        api.RegisterEntity("IndependentVehicleStructure", typeof(EntityVehicleStructure));
        api.RegisterMountable(MountableClassName, RestoreMountable);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Logger.Notification(
            "[Mobilis Core] Version {0} chargée — attachement conducteur virtuel, conduite serveur.",
            ModVersion);
        serverGlueChannel = api.Network
            .RegisterChannel(GlueNetworkChannel)
            .RegisterMessageType<GlueBrushPacket>()
            .SetMessageHandler<GlueBrushPacket>(OnGlueBrushPacket);
        api.ChatCommands
            .Create("iv")
            .WithDescription("Commandes de secours de Mobilis - Core")
            .RequiresPlayer()
            .BeginSubCommand("recover")
                .WithDescription("Rematérialise la structure mobile inoccupée la plus proche")
                .HandleWith(RecoverNearestVehicle)
            .EndSubCommand()
            .BeginSubCommand("dismount")
                .WithDescription("Force une descente serveur du véhicule actuellement conduit")
                .HandleWith(DismountCurrentVehicle)
            .EndSubCommand();
        api.Event.SaveGameLoaded += LoadGlue;
        api.Event.GameWorldSave += SaveGlue;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        clientGlueChannel = api.Network
            .RegisterChannel(GlueNetworkChannel)
            .RegisterMessageType<GlueBrushPacket>();
        api.Event.RegisterGameTickListener(OnClientGlueTick, 75);
        api.RegisterEntityRendererClass("IndependentVehicleStructureRenderer", typeof(VehicleStructureRenderer));
    }

    public void BeginGlueStroke(EntityAgent byEntity, BlockSelection? blockSelection)
    {
        if (capi is null || byEntity.World.Side != EnumAppSide.Client || blockSelection is null) return;
        GridPos target = blockSelection.Position.ToGridPos();
        clientStrokeLast = target;
        SendGlueQuery(target);
    }

    public void ContinueGlueStroke(EntityAgent byEntity, BlockSelection? blockSelection)
    {
        if (capi is null || byEntity.World.Side != EnumAppSide.Client || blockSelection is null) return;
        GridPos target = blockSelection.Position.ToGridPos();

        if (clientStrokeLast is GridPos previous && previous != target && previous.IsFaceAdjacent(target))
        {
            SendGluePaint(previous, target, byEntity.Controls.Sneak);
        }

        clientStrokeLast = target;
        SendGlueQuery(target);
    }

    public void EndGlueStroke() => clientStrokeLast = null;

    public void TryActivate(BlockPos controllerPos, IPlayer player)
    {
        if (sapi is null) return;
        long now = sapi.World.ElapsedMilliseconds;
        if (reactivationBlockedUntil.TryGetValue(player.PlayerUID, out long blockedUntil))
        {
            if (now < blockedUntil)
            {
                Notify(player, "Attendez un instant que la descente précédente soit synchronisée.");
                return;
            }
            reactivationBlockedUntil.Remove(player.PlayerUID);
        }
        if (player.Entity.MountedOn is not null)
        {
            Notify(player, "Descendez de votre monture actuelle avant d'activer ce siège.");
            return;
        }

        GridPos controller = controllerPos.ToGridPos();
        HashSet<GridPos> positions = glue.GetConnectedComponent(controller, MaxBlocks);

        if (positions.Count < 2)
        {
            Notify(player, "Collez le siège à au moins un autre bloc avant le départ.");
            return;
        }
        if (positions.Count > MaxBlocks)
        {
            Notify(player, $"Prototype limité à {MaxBlocks} blocs.");
            return;
        }
        if (!DimensionsAreValid(positions))
        {
            Notify(player, $"Prototype limité à {MaxWidth}×{MaxWidth} blocs au sol et {MaxHeight} blocs de haut.");
            return;
        }

        var blockMappings = new Dictionary<int, AssetLocation>();
        var itemMappings = new Dictionary<int, AssetLocation>();
        List<CapturedWorldBlock> capturedBlocks = [];
        int totalBlockEntityBytes = 0;
        try
        {
            foreach (GridPos position in positions)
            {
                CapturedWorldBlock captured = CaptureWorldBlock(
                    position.ToBlockPos(),
                    blockMappings,
                    itemMappings);
                totalBlockEntityBytes += captured.BlockEntity?.DecodeTreeData().Length ?? 0;
                if (totalBlockEntityBytes > MaxTotalBlockEntityDataBytes)
                    throw new InvalidOperationException(
                        $"les données des blocs dépassent {MaxTotalBlockEntityDataBytes / 1024 / 1024} Mio");
                capturedBlocks.Add(captured);
            }
        }
        catch (Exception error)
        {
            sapi.Logger.Error("[Mobilis Core] Capture de la structure refusée : {0}", error);
            Notify(player, $"Activation refusée : impossible de sauvegarder toutes les données ({error.Message}).");
            return;
        }

        ExpandRotatableBlockMappings(blockMappings);

        Block controllerBlock = sapi.World.BlockAccessor.GetBlock(controllerPos);
        int headingTurns = BlockVehicleControlSeat.HeadingTurns(controllerBlock, player.Entity.Pos.Yaw);
        List<GlueBond> vehicleBonds = glue.GetInternalBonds(positions);
        VehicleSnapshot snapshot;
        try
        {
            snapshot = CreateSnapshot(
                controller,
                capturedBlocks,
                vehicleBonds,
                headingTurns,
                blockMappings,
                itemMappings);
        }
        catch (Exception error)
        {
            sapi.Logger.Error("[Mobilis Core] Normalisation de la structure refusée : {0}", error);
            Notify(player, $"Activation refusée : un bloc à données ne peut pas être orienté proprement ({error.Message}).");
            return;
        }

        List<GlueBond> takenBonds = glue.TakeInternalBonds(positions);
        var capturedByPosition = capturedBlocks.ToDictionary(
            entry => entry.Position.ToGridPos(),
            entry => entry);
        List<CapturedWorldBlock> removed = [];
        EntityVehicleStructure? spawnedEntity = null;
        bool didSpawn = false;

        try
        {
            foreach (GridPos gridPos in positions.OrderByDescending(pos => pos.Y))
            {
                BlockPos pos = gridPos.ToBlockPos();
                removed.Add(capturedByPosition[gridPos]);
                sapi.World.BlockAccessor.SetBlock(0, pos);
                if (sapi.World.BlockAccessor.GetBlock(pos).Id != 0 ||
                    sapi.World.BlockAccessor.GetBlockEntity(pos) is not null)
                {
                    throw new InvalidOperationException($"le bloc {gridPos} n’a pas pu être retiré proprement");
                }
            }

            EntityProperties? type = sapi.World.GetEntityType(new AssetLocation(Domain, "structure"));
            if (type is null) throw new InvalidOperationException("Type d’entité structure introuvable.");
            if (sapi.World.ClassRegistry.CreateEntity(type) is not EntityVehicleStructure entity)
                throw new InvalidOperationException("Classe d’entité structure introuvable.");
            spawnedEntity = entity;

            entity.Pos.SetPos(controller.X + 0.5, controller.Y, controller.Z + 0.5);
            entity.Pos.Dimension = controller.Dimension;
            entity.Pos.Yaw = headingTurns * QuarterTurn.Radians;
            entity.SetSnapshot(snapshot);
            sapi.World.SpawnEntity(entity);
            didSpawn = true;

            if (player.Entity is not EntityAgent agent ||
                !sapi.ModLoader.GetModSystem<VehicleRiderAttachmentSystem>().TryAttach(entity, agent))
                throw new InvalidOperationException("Impossible d’asseoir le joueur sur la structure.");

            sapi.Logger.Audit(
                "[Mobilis Core] {0} monte sur la structure {1} à {2}; position joueur {3}.",
                agent.GetName(), entity.EntityId, entity.Pos.AsBlockPos, agent.Pos.AsBlockPos);

            Notify(player, $"{ProductName} {ModVersion} — structure mobile. L’avant est celui du siège ; accroupissez-vous pour l’arrêter.");
        }
        catch (Exception error)
        {
            if (didSpawn && spawnedEntity is not null)
            {
                spawnedEntity.DiscardWithoutMaterializing();
                spawnedEntity.Die(EnumDespawnReason.Removed);
            }
            try
            {
                RestoreCapturedBlocks(
                    removed,
                    blockMappings,
                    itemMappings,
                    snapshot.CollectibleMappingSeed);
            }
            catch (Exception restoreError)
            {
                sapi.Logger.Fatal(
                    "[Mobilis Core] Restauration d’activation incomplète; intervention requise : {0}",
                    restoreError);
                Notify(player, "ERREUR : la restauration complète des blocs à données a échoué. N’utilisez pas cette zone avant vérification du journal serveur.");
            }
            foreach (GlueBond bond in takenBonds) glue.Add(bond);
            sapi.Logger.Error("[Mobilis Core] Activation annulée : {0}", error);
            Notify(player, "Activation annulée : la plateforme a été restaurée.");
        }
    }

    public bool IsMobilePoseClear(EntityVehicleStructure entity)
    {
        if (entity.Snapshot is null) return false;
        IWorldAccessor world = entity.World;
        double sin = Math.Sin(entity.Pos.Yaw);
        double cos = Math.Cos(entity.Pos.Yaw);
        double absSin = Math.Abs(sin);
        double absCos = Math.Abs(cos);

        foreach (VehicleBlockSnapshot entry in entity.Snapshot.Blocks)
        {
            Block? mobileBlock = world.GetBlock(new AssetLocation(entry.BlockCode));
            if (mobileBlock is null) continue;
            Cuboidf[] mobileBoxes = entity.Snapshot.SchemaVersion >= 2
                ? entry.CollisionBoxes.Select(ToCuboidf).ToArray()
                : mobileBlock.CollisionBoxes ?? [];
            if (mobileBoxes.Length == 0) continue;

            foreach (Cuboidf mobileBox in mobileBoxes)
            {
                double localCenterX = entry.Offset.X - 0.5 + (mobileBox.X1 + mobileBox.X2) * 0.5;
                double localCenterZ = entry.Offset.Z - 0.5 + (mobileBox.Z1 + mobileBox.Z2) * 0.5;
                double mobileCenterX = entity.Pos.X + localCenterX * cos + localCenterZ * sin;
                double mobileCenterZ = entity.Pos.Z - localCenterX * sin + localCenterZ * cos;
                double mobileHalfX = (mobileBox.X2 - mobileBox.X1) * 0.5;
                double mobileHalfZ = (mobileBox.Z2 - mobileBox.Z1) * 0.5;
                double mobileMinY = entity.Pos.Y + entry.Offset.Y + mobileBox.Y1;
                double mobileMaxY = entity.Pos.Y + entry.Offset.Y + mobileBox.Y2;
                double radiusX = mobileHalfX * absCos + mobileHalfZ * absSin;
                double radiusZ = mobileHalfX * absSin + mobileHalfZ * absCos;

                int minX = (int)Math.Floor(mobileCenterX - radiusX);
                int maxX = (int)Math.Floor(mobileCenterX + radiusX - 0.000001);
                int minY = (int)Math.Floor(mobileMinY);
                int maxY = (int)Math.Floor(mobileMaxY - 0.000001);
                int minZ = (int)Math.Floor(mobileCenterZ - radiusZ);
                int maxZ = (int)Math.Floor(mobileCenterZ + radiusZ - 0.000001);

                for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    BlockPos worldPos = new(x, y, z, entity.Pos.Dimension);
                    Block worldBlock = world.BlockAccessor.GetBlock(worldPos);
                    Cuboidf[] worldBoxes = worldBlock.GetCollisionBoxes(world.BlockAccessor, worldPos);
                    if (worldBlock.Id == 0 || worldBoxes is not { Length: > 0 }) continue;

                    foreach (Cuboidf worldBox in worldBoxes)
                    {
                        if (VehicleCollisionMath.IntersectsOrientedPrismWithAabb(
                                mobileCenterX,
                                mobileMinY,
                                mobileCenterZ,
                                mobileHalfX,
                                mobileMaxY,
                                mobileHalfZ,
                                entity.Pos.Yaw,
                                x + worldBox.X1,
                                y + worldBox.Y1,
                                z + worldBox.Z1,
                                x + worldBox.X2,
                                y + worldBox.Y2,
                                z + worldBox.Z2))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    public bool HasGroundSupport(EntityVehicleStructure entity)
    {
        const double supportProbeDepth = 0.05;
        double originalY = entity.Pos.Y;
        try
        {
            entity.Pos.Y = originalY - supportProbeDepth;
            return !IsMobilePoseClear(entity);
        }
        finally
        {
            entity.Pos.Y = originalY;
        }
    }

    public void Materialize(EntityVehicleStructure entity, EntityAgent rider)
    {
        if (sapi is not null && rider is EntityPlayer playerEntity &&
            playerEntity.Player is IPlayer player)
        {
            reactivationBlockedUntil[player.PlayerUID] =
                sapi.World.ElapsedMilliseconds + ReactivationCooldownMilliseconds;
        }
        TryMaterialize(entity, rider);
    }

    public void MaterializeAbandoned(EntityVehicleStructure entity)
        => TryMaterialize(entity, null);

    private bool TryMaterialize(EntityVehicleStructure entity, EntityAgent? rider)
    {
        if (sapi is null || !entity.TryBeginMaterialization()) return false;
        if (entity.Snapshot is null)
        {
            CancelMaterialization(entity, rider);
            sapi.Logger.Error("[Mobilis Core] Rematérialisation annulée : snapshot absent pour l'entité {0}.", entity.EntityId);
            return false;
        }
        entity.StopCompletely();

        int turns = QuarterTurn.FromYaw(entity.Pos.Yaw);
        GridPos snapped = new(
            (int)Math.Round(entity.Pos.X - 0.5),
            (int)Math.Round(entity.Pos.Y),
            (int)Math.Round(entity.Pos.Z - 0.5),
            entity.Pos.Dimension);

        if (!TryFindMaterializationPose(entity.Snapshot, snapped, turns, out GridPos anchor, out int safeTurns))
        {
            CancelMaterialization(entity, rider);
            if (rider is EntityPlayer playerEntity && playerEntity.Player is IPlayer player)
                Notify(player, "Arrêt impossible ici : aucun espace libre pour rematérialiser la plateforme. Le siège reste actif et la vélocité est nulle.");
            return false;
        }

        Dictionary<int, AssetLocation> blockMappings = DecodeMappings(entity.Snapshot.BlockIdMappings);
        Dictionary<int, AssetLocation> itemMappings = DecodeMappings(entity.Snapshot.ItemIdMappings);
        List<(GridPos Position, Block Block, VehicleBlockSnapshot Snapshot)> placements = [];
        foreach (VehicleBlockSnapshot entry in entity.Snapshot.Blocks)
        {
            LocalPos offset = entry.Offset.RotateQuarterTurns(safeTurns);
            GridPos position = anchor.Offset(offset.X, offset.Y, offset.Z);
            Block? block = sapi.World.GetBlock(new AssetLocation(entry.BlockCode));
            if (block is null || block.Id <= 0)
            {
                CancelMaterialization(entity, rider);
                if (rider is EntityPlayer missingPlayerEntity && missingPlayerEntity.Player is IPlayer missingPlayer)
                    Notify(missingPlayer, $"Arrêt annulé : le bloc {entry.BlockCode} n’existe plus dans cette installation.");
                return false;
            }
            // Le yaw positif de l'entité tourne le repère local vers l'ouest,
            // tandis que GetRotatedBlockCode(+90) tourne les variantes vers l'est.
            // Le signe opposé conserve donc l'orientation visuelle de chaque bloc.
            AssetLocation? rotatedCode = block.GetRotatedBlockCode(-safeTurns * 90);
            Block? rotatedBlock = rotatedCode is null ? null : sapi.World.GetBlock(rotatedCode);
            if (rotatedBlock is not null && rotatedBlock.Id > 0) block = rotatedBlock;
            if (entry.BlockEntity is not null &&
                string.IsNullOrWhiteSpace(block.EntityClass) &&
                string.IsNullOrWhiteSpace(entry.BlockEntity.ClassName))
            {
                CancelMaterialization(entity, rider);
                if (rider is EntityPlayer incompatibleEntity && incompatibleEntity.Player is IPlayer incompatiblePlayer)
                    Notify(incompatiblePlayer, $"Arrêt annulé : la classe de données de {entry.BlockCode} n’est plus disponible.");
                return false;
            }
            if (entry.BlockEntity is not null)
            {
                try
                {
                    ValidateBlockEntityData(
                        entry.BlockEntity,
                        block,
                        position.ToBlockPos(),
                        -safeTurns * 90,
                        blockMappings,
                        itemMappings,
                        entity.Snapshot.CollectibleMappingSeed);
                }
                catch (Exception error)
                {
                    CancelMaterialization(entity, rider);
                    sapi.Logger.Error(
                        "[Mobilis Core] Données de {0} incompatibles avec la rematérialisation : {1}",
                        entry.BlockCode,
                        error);
                    if (rider is EntityPlayer invalidEntity && invalidEntity.Player is IPlayer invalidPlayer)
                        Notify(invalidPlayer, $"Arrêt annulé : les données de {entry.BlockCode} ne peuvent pas être restaurées sans risque.");
                    return false;
                }
            }
            placements.Add((position, block, entry));
        }

        List<(GridPos Position, int BlockId)> placed = [];
        List<GlueBond> addedBonds = [];
        try
        {
            foreach ((GridPos position, Block block, _) in placements.OrderBy(entry => entry.Position.Y))
            {
                sapi.World.BlockAccessor.SetBlock(block.Id, position.ToBlockPos());
                if (sapi.World.BlockAccessor.GetBlock(position.ToBlockPos()).Id != block.Id)
                    throw new InvalidOperationException($"la pose de {block.Code} a échoué en {position}");
                placed.Add((position, block.Id));
            }

            foreach ((GridPos position, Block block, VehicleBlockSnapshot snapshotBlock) in
                     placements.Where(entry => entry.Snapshot.BlockEntity is not null))
            {
                RestoreBlockEntityData(
                    snapshotBlock.BlockEntity!,
                    block,
                    position.ToBlockPos(),
                    -safeTurns * 90,
                    blockMappings,
                    itemMappings,
                    entity.Snapshot.CollectibleMappingSeed);
            }

            foreach (LocalBond localBond in entity.Snapshot.Bonds)
            {
                LocalPos a = localBond.A.RotateQuarterTurns(safeTurns);
                LocalPos b = localBond.B.RotateQuarterTurns(safeTurns);
                GlueBond bond = new(anchor.Offset(a.X, a.Y, a.Z), anchor.Offset(b.X, b.Y, b.Z));
                if (glue.Add(bond)) addedBonds.Add(bond);
            }
        }
        catch (Exception error)
        {
            foreach (GlueBond bond in addedBonds) glue.Remove(bond.A, bond.B);
            foreach ((GridPos position, int blockId) in placed.AsEnumerable().Reverse())
            {
                BlockPos pos = position.ToBlockPos();
                if (sapi.World.BlockAccessor.GetBlock(pos).Id == blockId)
                    sapi.World.BlockAccessor.SetBlock(0, pos);
            }
            CancelMaterialization(entity, rider);
            sapi.Logger.Error("[Mobilis Core] Rematérialisation annulée et restaurée : {0}", error);
            if (rider is EntityPlayer failedPlayerEntity && failedPlayerEntity.Player is IPlayer failedPlayer)
                Notify(failedPlayer, "Arrêt annulé : la pose des blocs a échoué. La structure mobile a été conservée.");
            return false;
        }

        // Le verrou passe à l'état terminal avant la téléportation et la mort de
        // l'entité. Un second événement de descente ne peut donc plus reposer les blocs.
        if (!entity.CompleteMaterialization())
        {
            sapi.Logger.Error("[Mobilis Core] État de rematérialisation incohérent pour l'entité {0}; les blocs ne seront pas posés une seconde fois.", entity.EntityId);
            return false;
        }

        // VehicleRiderAttachmentSystem a déjà placé le joueur sur la dernière
        // ancre monde valide avant cette transition. Ne pas émettre une seconde
        // téléportation pendant la disparition de la structure.
        if (rider is not null)
        {
            rider.Pos.Motion.Set(0, 0, 0);
            sapi.Logger.Audit(
                "[Mobilis Core] Structure {0} rematérialisée à {1}; {2} reste en {3} sans téléportation.",
                entity.EntityId, anchor.ToBlockPos(), rider.GetName(), rider.Pos.AsBlockPos);
        }
        entity.Die(EnumDespawnReason.Removed);
        return true;
    }

    private static void CancelMaterialization(EntityVehicleStructure entity, EntityAgent? rider)
    {
        if (rider is null) entity.CancelMaterialization();
        else entity.CancelMaterializationAndRemount(rider);
    }

    private TextCommandResult RecoverNearestVehicle(TextCommandCallingArgs args)
    {
        if (sapi is null || args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Cette commande doit être utilisée par un joueur.");

        EntityVehicleStructure? nearest = sapi.World
            .GetEntitiesAround(player.Entity.Pos.XYZ, 16, 16, entity => entity is EntityVehicleStructure)
            .OfType<EntityVehicleStructure>()
            .Where(vehicle => !vehicle.AnyMounted())
            .OrderBy(vehicle => HorizontalDistanceSquared(vehicle.Pos, player.Entity.Pos))
            .FirstOrDefault();

        if (nearest is null)
            return TextCommandResult.Error("Aucune structure mobile inoccupée trouvée dans un rayon de 16 blocs.");

        return TryMaterialize(nearest, null)
            ? TextCommandResult.Success("La structure mobile a été rematérialisée en blocs solides.")
            : TextCommandResult.Error("Aucun emplacement sûr n’a été trouvé. La structure est restée intacte et immobile.");
    }

    private TextCommandResult DismountCurrentVehicle(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Cette commande doit être utilisée par un joueur.");

        if (player.Entity.MountedOn is not VehicleControlSeat)
            return TextCommandResult.Error("Vous n'êtes pas monté sur un véhicule Mobilis - Core.");

        return player.Entity.TryUnmount()
            ? TextCommandResult.Success("Descente forcée côté serveur.")
            : TextCommandResult.Error("Le serveur n'a pas pu libérer ce siège.");
    }

    private static double HorizontalDistanceSquared(EntityPos a, EntityPos b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    private IMountableSeat? RestoreMountable(IWorldAccessor world, TreeAttribute tree)
    {
        long entityId = tree.GetLong("entityId");
        string seatId = tree.GetString("seatId");
        return (world.GetEntityById(entityId) as EntityVehicleStructure)?.FindSeat(seatId);
    }

    private VehicleSnapshot CreateSnapshot(
        GridPos controller,
        IEnumerable<CapturedWorldBlock> capturedBlocks,
        IEnumerable<GlueBond> bonds,
        int headingTurns,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings)
    {
        if (sapi is null) throw new InvalidOperationException();
        var snapshot = new VehicleSnapshot
        {
            SchemaVersion = 2,
            OriginalController = controller,
            CollectibleMappingSeed = sapi.World.Rand.Next(),
            BlockIdMappings = EncodeMappings(blockMappings),
            ItemIdMappings = EncodeMappings(itemMappings)
        };
        foreach (CapturedWorldBlock captured in capturedBlocks)
        {
            GridPos position = captured.Position.ToGridPos();
            Block block = captured.Block;
            if (position == controller &&
                !block.Variant.ContainsKey("side"))
            {
                // L'ancien bloc sans orientation devient le siège nord canonique.
                Block? migrated = sapi.World.GetBlock(new AssetLocation(Domain, "vehiclecontrolseat-north"));
                if (migrated is not null && migrated.Id > 0) block = migrated;
            }

            AssetLocation localBlockCode =
                block.GetRotatedBlockCode(headingTurns * 90) ?? block.Code;
            Block? localBlock = sapi.World.GetBlock(localBlockCode);
            if (localBlock is null || localBlock.Id <= 0)
                throw new InvalidOperationException(
                    $"la variante orientée {localBlockCode} du bloc {block.Code} n’existe pas");

            LocalPos worldOffset = new(
                position.X - controller.X,
                position.Y - controller.Y,
                position.Z - controller.Z);
            VehicleBlockEntitySnapshot? localEntityData = TransformBlockEntityData(
                captured.BlockEntity,
                localBlock,
                captured.Position,
                headingTurns * 90,
                blockMappings,
                itemMappings);
            if (localEntityData is not null)
            {
                ValidateBlockEntityData(
                    localEntityData,
                    localBlock,
                    captured.Position,
                    0,
                    blockMappings,
                    itemMappings,
                    snapshot.CollectibleMappingSeed);
            }
            snapshot.Blocks.Add(new VehicleBlockSnapshot
            {
                Offset = worldOffset.RotateQuarterTurns(-headingTurns),
                BlockCode = localBlockCode.ToString(),
                BlockEntity = localEntityData,
                CollisionBoxes = captured.CollisionBoxes
                    .Select(box => box.RotateQuarterTurns(headingTurns))
                    .ToList(),
                LightHsv = captured.LightHsv?.ToArray()
            });
        }
        foreach (GlueBond bond in bonds)
        {
            LocalPos worldA = new(
                bond.A.X - controller.X,
                bond.A.Y - controller.Y,
                bond.A.Z - controller.Z);
            LocalPos worldB = new(
                bond.B.X - controller.X,
                bond.B.Y - controller.Y,
                bond.B.Z - controller.Z);
            snapshot.Bonds.Add(new LocalBond(
                worldA.RotateQuarterTurns(-headingTurns),
                worldB.RotateQuarterTurns(-headingTurns)));
        }
        return snapshot;
    }

    private CapturedWorldBlock CaptureWorldBlock(
        BlockPos position,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings)
    {
        if (sapi is null) throw new InvalidOperationException();
        Block block = sapi.World.BlockAccessor.GetBlock(position);
        if (block.Id == 0 || block.IsLiquid())
            throw new InvalidOperationException($"la structure contient de l’air ou un liquide en {position}");

        VehicleBlockEntitySnapshot? entityData = null;
        if (sapi.World.BlockAccessor.GetBlockEntity(position) is BlockEntity blockEntity)
        {
            if (blockEntity is IBlockEntityContainer container &&
                sapi.World.AllOnlinePlayers.Any(container.Inventory.HasOpened))
            {
                throw new InvalidOperationException(
                    $"fermez l’inventaire de {block.Code} avant d’activer le véhicule");
            }

            string className = sapi.World.ClassRegistry.GetBlockEntityClass(blockEntity.GetType());
            if (string.IsNullOrWhiteSpace(className))
                throw new InvalidOperationException(
                    $"la classe de données {blockEntity.GetType().FullName} du bloc {block.Code} n’est pas enregistrée");

            var tree = new TreeAttribute();
            blockEntity.ToTreeAttributes(tree);
            byte[] bytes = tree.ToBytes();
            if (bytes.Length > MaxBlockEntityDataBytes)
                throw new InvalidOperationException(
                    $"les données de {block.Code} dépassent {MaxBlockEntityDataBytes / 1024} Kio");

            blockEntity.OnStoreCollectibleMappings(blockMappings, itemMappings);
            entityData = VehicleBlockEntitySnapshot.FromBytes(className, bytes);
        }

        Cuboidf[]? sourceBoxes = block.GetCollisionBoxes(sapi.World.BlockAccessor, position);
        List<VehicleCuboidSnapshot> collisionBoxes = sourceBoxes?
            .Select(ToCuboidSnapshot)
            .ToList() ?? [];
        byte[]? light = block.GetLightHsv(sapi.World.BlockAccessor, position);

        return new CapturedWorldBlock(
            position.Copy(),
            block,
            entityData,
            collisionBoxes,
            light?.ToArray());
    }

    private VehicleBlockEntitySnapshot? TransformBlockEntityData(
        VehicleBlockEntitySnapshot? source,
        Block transformedBlock,
        BlockPos position,
        int degrees,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings)
    {
        if (sapi is null || source is null) return null;

        TreeAttribute tree = TreeAttribute.CreateFromBytes(source.DecodeTreeData());
        string className = transformedBlock.EntityClass ?? source.ClassName;
        if (degrees != 0)
        {
            BlockEntity transformer = sapi.World.ClassRegistry.CreateBlockEntity(className)
                ?? throw new InvalidOperationException($"classe BlockEntity {className} introuvable");
            transformer.Pos = position.Copy();
            transformer.CreateBehaviors(transformedBlock, sapi.World);
            if (transformer is IRotatable rotatable)
            {
                rotatable.OnTransformed(
                    sapi.World,
                    tree,
                    degrees,
                    blockMappings,
                    itemMappings,
                    null);
            }
        }

        tree.SetString("blockCode", transformedBlock.Code.ToShortString());
        return VehicleBlockEntitySnapshot.FromBytes(className, tree.ToBytes());
    }

    private void RestoreCapturedBlocks(
        IEnumerable<CapturedWorldBlock> capturedBlocks,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings,
        int mappingSeed)
    {
        if (sapi is null) throw new InvalidOperationException();
        CapturedWorldBlock[] blocks = capturedBlocks.ToArray();
        foreach (CapturedWorldBlock captured in blocks.OrderBy(entry => entry.Position.Y))
        {
            sapi.World.BlockAccessor.SetBlock(captured.Block.Id, captured.Position);
            if (sapi.World.BlockAccessor.GetBlock(captured.Position).Id != captured.Block.Id)
                throw new InvalidOperationException($"le bloc {captured.Block.Code} n’a pas été restauré en {captured.Position}");
        }

        foreach (CapturedWorldBlock captured in blocks.Where(entry => entry.BlockEntity is not null))
        {
            RestoreBlockEntityData(
                captured.BlockEntity!,
                captured.Block,
                captured.Position,
                0,
                blockMappings,
                itemMappings,
                mappingSeed);
        }
    }

    private void ValidateBlockEntityData(
        VehicleBlockEntitySnapshot source,
        Block block,
        BlockPos position,
        int rotationDegrees,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings,
        int mappingSeed)
    {
        if (sapi is null) throw new InvalidOperationException();
        string className = block.EntityClass ?? source.ClassName;
        BlockEntity candidate = sapi.World.ClassRegistry.CreateBlockEntity(className)
            ?? throw new InvalidOperationException($"classe BlockEntity {className} introuvable");
        candidate.Pos = position.Copy();
        candidate.CreateBehaviors(block, sapi.World);

        TreeAttribute tree = TreeAttribute.CreateFromBytes(source.DecodeTreeData());
        if (rotationDegrees != 0 && candidate is IRotatable rotatable)
        {
            rotatable.OnTransformed(
                sapi.World,
                tree,
                rotationDegrees,
                blockMappings,
                itemMappings,
                null);
        }
        tree.SetInt("posx", position.X);
        tree.SetInt("posy", position.InternalY);
        tree.SetInt("posz", position.Z);
        tree.SetString("blockCode", block.Code.ToShortString());
        candidate.FromTreeAttributes(tree, sapi.World);
        candidate.OnLoadCollectibleMappings(
            sapi.World,
            blockMappings,
            itemMappings,
            mappingSeed,
            resolveImports: false);
    }

    private void RestoreBlockEntityData(
        VehicleBlockEntitySnapshot source,
        Block block,
        BlockPos position,
        int rotationDegrees,
        Dictionary<int, AssetLocation> blockMappings,
        Dictionary<int, AssetLocation> itemMappings,
        int mappingSeed)
    {
        if (sapi is null) throw new InvalidOperationException();
        string className = block.EntityClass ?? source.ClassName;
        BlockEntity restored = sapi.World.ClassRegistry.CreateBlockEntity(className)
            ?? throw new InvalidOperationException($"classe BlockEntity {className} introuvable");
        restored.Pos = position.Copy();
        restored.CreateBehaviors(block, sapi.World);

        TreeAttribute tree = TreeAttribute.CreateFromBytes(source.DecodeTreeData());
        if (rotationDegrees != 0 && restored is IRotatable rotatable)
        {
            rotatable.OnTransformed(
                sapi.World,
                tree,
                rotationDegrees,
                blockMappings,
                itemMappings,
                null);
        }
        tree.SetInt("posx", position.X);
        tree.SetInt("posy", position.InternalY);
        tree.SetInt("posz", position.Z);
        tree.SetString("blockCode", block.Code.ToShortString());

        bool initialized = false;
        bool spawned = false;
        try
        {
            restored.FromTreeAttributes(tree, sapi.World);
            restored.OnLoadCollectibleMappings(
                sapi.World,
                blockMappings,
                itemMappings,
                mappingSeed,
                resolveImports: false);
            restored.Initialize(sapi);
            initialized = true;

            sapi.World.BlockAccessor.RemoveBlockEntity(position);
            sapi.World.BlockAccessor.SpawnBlockEntity(restored);
            spawned = true;
            restored.MarkDirty(redrawOnClient: true);
            sapi.World.BlockAccessor.MarkBlockModified(position);
        }
        catch
        {
            if (initialized && !spawned)
            {
                try
                {
                    restored.OnBlockRemoved();
                }
                catch
                {
                    // Conserver l’exception de restauration originale.
                }
            }
            throw;
        }
    }

    private static Dictionary<int, string> EncodeMappings(
        Dictionary<int, AssetLocation> mappings) =>
        mappings.ToDictionary(entry => entry.Key, entry => entry.Value.ToString());

    private void ExpandRotatableBlockMappings(Dictionary<int, AssetLocation> mappings)
    {
        if (sapi is null) return;
        foreach (AssetLocation code in mappings.Values.Distinct().ToArray())
        {
            Block? block = sapi.World.GetBlock(code);
            if (block is null || block.Id <= 0) continue;
            mappings[block.Id] = block.Code;
            for (int turns = 1; turns < 4; turns++)
            {
                AssetLocation? rotatedCode = block.GetRotatedBlockCode(turns * 90);
                Block? rotated = rotatedCode is null ? null : sapi.World.GetBlock(rotatedCode);
                if (rotated is not null && rotated.Id > 0)
                    mappings[rotated.Id] = rotated.Code;
            }
        }
    }

    private static Dictionary<int, AssetLocation> DecodeMappings(
        Dictionary<int, string>? mappings) =>
        mappings?.ToDictionary(
            entry => entry.Key,
            entry => new AssetLocation(entry.Value)) ?? [];

    private static VehicleCuboidSnapshot ToCuboidSnapshot(Cuboidf box) => new()
    {
        X1 = box.X1,
        Y1 = box.Y1,
        Z1 = box.Z1,
        X2 = box.X2,
        Y2 = box.Y2,
        Z2 = box.Z2
    };

    private static Cuboidf ToCuboidf(VehicleCuboidSnapshot box) => new(
        box.X1,
        box.Y1,
        box.Z1,
        box.X2,
        box.Y2,
        box.Z2);

    private sealed record CapturedWorldBlock(
        BlockPos Position,
        Block Block,
        VehicleBlockEntitySnapshot? BlockEntity,
        List<VehicleCuboidSnapshot> CollisionBoxes,
        byte[]? LightHsv);

    private bool TryFindMaterializationPose(VehicleSnapshot snapshot, GridPos preferred, int preferredTurns, out GridPos anchor, out int turns)
    {
        List<GridPos> candidates = [preferred, snapshot.OriginalController];
        for (int radius = 1; radius <= 8; radius++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                candidates.Add(preferred.Offset(x, 0, -radius));
                candidates.Add(preferred.Offset(x, 0, radius));
            }
            for (int z = -radius + 1; z < radius; z++)
            {
                candidates.Add(preferred.Offset(-radius, 0, z));
                candidates.Add(preferred.Offset(radius, 0, z));
            }
        }
        for (int y = 1; y <= 4; y++)
        {
            candidates.Add(preferred.Offset(0, y, 0));
        }

        foreach (GridPos candidate in candidates)
        {
            int candidateTurns = candidate == snapshot.OriginalController ? 0 : preferredTurns;
            if (CanMaterialize(snapshot, candidate, candidateTurns))
            {
                anchor = candidate;
                turns = candidateTurns;
                return true;
            }
        }

        anchor = default;
        turns = 0;
        return false;
    }

    private bool CanMaterialize(VehicleSnapshot snapshot, GridPos anchor, int turns)
    {
        if (sapi is null) return false;
        foreach (VehicleBlockSnapshot entry in snapshot.Blocks)
        {
            LocalPos offset = entry.Offset.RotateQuarterTurns(turns);
            if (sapi.World.BlockAccessor.GetBlock(anchor.Offset(offset.X, offset.Y, offset.Z).ToBlockPos()).Id != 0) return false;
        }
        return true;
    }

    private static bool DimensionsAreValid(IEnumerable<GridPos> positions)
    {
        int minX = positions.Min(pos => pos.X);
        int maxX = positions.Max(pos => pos.X);
        int minY = positions.Min(pos => pos.Y);
        int maxY = positions.Max(pos => pos.Y);
        int minZ = positions.Min(pos => pos.Z);
        int maxZ = positions.Max(pos => pos.Z);
        return maxX - minX + 1 <= MaxWidth && maxZ - minZ + 1 <= MaxWidth && maxY - minY + 1 <= MaxHeight;
    }

    private void OnClientGlueTick(float deltaTime)
    {
        if (capi is null) return;
        IPlayer? player = capi.World.Player;
        if (player is null) return;

        bool glueIsHeld = player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is ItemStructureGlue;
        if (!glueIsHeld)
        {
            if (clientGlueWasHeld)
            {
                clientGlueWasHeld = false;
                clientStrokeLast = null;
                clientQueryTarget = null;
                ClearClientGlueHighlight();
                SendGlueClear();
            }
            return;
        }

        clientGlueWasHeld = true;
        BlockSelection? selection = player.CurrentBlockSelection;
        if (selection is null)
        {
            if (clientQueryTarget is not null)
            {
                clientQueryTarget = null;
                ClearClientGlueHighlight();
                SendGlueClear();
            }
            return;
        }

        SendGlueQuery(selection.Position.ToGridPos());
    }

    private void SendGlueQuery(GridPos target)
    {
        if (clientGlueChannel is null || !clientGlueChannel.Connected || clientQueryTarget == target) return;
        clientQueryTarget = target;
        clientGlueChannel.SendPacket(new GlueBrushPacket
        {
            HasTarget = true,
            ToX = target.X,
            ToY = target.Y,
            ToZ = target.Z,
            Dimension = target.Dimension
        });
    }

    private void SendGluePaint(GridPos from, GridPos to, bool remove)
    {
        if (clientGlueChannel is null || !clientGlueChannel.Connected) return;
        clientGlueChannel.SendPacket(new GlueBrushPacket
        {
            HasTarget = true,
            Paint = true,
            Remove = remove,
            FromX = from.X,
            FromY = from.Y,
            FromZ = from.Z,
            ToX = to.X,
            ToY = to.Y,
            ToZ = to.Z,
            Dimension = to.Dimension
        });
    }

    private void SendGlueClear()
    {
        if (clientGlueChannel?.Connected == true)
            clientGlueChannel.SendPacket(new GlueBrushPacket());
    }

    private void ClearClientGlueHighlight()
    {
        if (capi?.World.Player is not IPlayer player) return;
        capi.World.HighlightBlocks(
            player,
            GlueHighlightSlot,
            [],
            [],
            EnumHighlightBlocksMode.Absolute,
            EnumHighlightShape.Arbitrary,
            1.02f);
    }

    private void OnGlueBrushPacket(IServerPlayer player, GlueBrushPacket packet)
    {
        if (sapi is null) return;
        if (!packet.HasTarget)
        {
            ClearServerGlueHighlight(player);
            return;
        }

        if (player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is not ItemStructureGlue) return;

        GridPos target = new(packet.ToX, packet.ToY, packet.ToZ, packet.Dimension);
        if (!IsValidGlueTarget(player, target))
        {
            ClearServerGlueHighlight(player);
            return;
        }

        if (packet.Paint)
        {
            GridPos from = new(packet.FromX, packet.FromY, packet.FromZ, packet.Dimension);
            if (!from.IsFaceAdjacent(target) || !IsValidGlueTarget(player, from))
            {
                UpdateServerGlueHighlight(player, target);
                return;
            }

            if (packet.Remove) glue.Remove(from, target);
            else glue.Add(from, target);
        }

        UpdateServerGlueHighlight(player, target);
    }

    private bool IsValidGlueTarget(IServerPlayer player, GridPos position)
    {
        if (sapi is null || position.Dimension != player.Entity.Pos.Dimension) return false;
        double dx = position.X + 0.5 - player.Entity.Pos.X;
        double dy = position.Y + 0.5 - player.Entity.Pos.Y;
        double dz = position.Z + 0.5 - player.Entity.Pos.Z;
        if (dx * dx + dy * dy + dz * dz > 64) return false;

        Block block = sapi.World.BlockAccessor.GetBlock(position.ToBlockPos());
        return block.Id != 0 && !block.IsLiquid();
    }

    private void UpdateServerGlueHighlight(IServerPlayer player, GridPos target)
    {
        if (sapi is null) return;
        HashSet<GridPos> component = glue.GetConnectedComponent(target, MaxBlocks);
        List<BlockPos> blocks = component.Select(position => position.ToBlockPos()).ToList();
        List<int> colors = component
            .Select(position => position == target
                ? ColorUtil.ToRgba(150, 255, 175, 35)
                : ColorUtil.ToRgba(85, 35, 215, 255))
            .ToList();

        sapi.World.HighlightBlocks(
            player,
            GlueHighlightSlot,
            blocks,
            colors,
            EnumHighlightBlocksMode.Absolute,
            EnumHighlightShape.Arbitrary,
            1.02f);
    }

    private void ClearServerGlueHighlight(IServerPlayer player)
    {
        sapi?.World.HighlightBlocks(
            player,
            GlueHighlightSlot,
            [],
            [],
            EnumHighlightBlocksMode.Absolute,
            EnumHighlightShape.Arbitrary,
            1.02f);
    }

    private void LoadGlue()
    {
        if (sapi is null) return;
        byte[]? bytes = sapi.WorldManager.SaveGame.GetData(GlueSaveKey);
        string? json = bytes is null ? null : Encoding.UTF8.GetString(bytes);
        glue.ReplaceAll(VehicleJson.Deserialize<List<GlueBond>>(json) ?? []);
    }

    private void SaveGlue()
    {
        if (sapi is null) return;
        byte[] bytes = Encoding.UTF8.GetBytes(VehicleJson.Serialize(glue.All().ToList()));
        sapi.WorldManager.SaveGame.StoreData(GlueSaveKey, bytes);
    }

    private void Notify(IPlayer player, string message)
    {
        sapi?.SendMessage(player, GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
    }
}
