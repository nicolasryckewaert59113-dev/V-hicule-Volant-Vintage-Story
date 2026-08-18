using IndependentVehicles.Core;
using IndependentVehicles.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace IndependentVehicles.Client;

public sealed class VehicleStructureRenderer : EntityRenderer
{
    private MultiTextureMeshRef? meshRef;

    public VehicleStructureRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
    }

    public override void OnEntityLoaded()
    {
        BuildMesh();
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        // The shadow pass already owns the chunk-shadow shader. Starting the standard
        // shader here crashes the client. The prototype simply skips casting a shadow.
        if (isShadowPass) return;

        if (meshRef is null) BuildMesh();
        if (meshRef is null) return;

        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            (int)entity.Pos.X,
            (int)entity.Pos.Y,
            (int)entity.Pos.Z,
            null!);

        shader.ModelMatrix = new Matrixf()
            .Identity()
            .Translate(
                entity.Pos.X - capi.World.Player.Entity.CameraPos.X,
                entity.Pos.Y - capi.World.Player.Entity.CameraPos.Y,
                entity.Pos.Z - capi.World.Player.Entity.CameraPos.Z)
            .RotateY(entity.Pos.Yaw)
            .Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;

        capi.Render.RenderMultiTextureMesh(meshRef, "tex");
        shader.Stop();
    }

    public override void Dispose()
    {
        meshRef?.Dispose();
        meshRef = null;
    }

    private void BuildMesh()
    {
        if (entity is not EntityVehicleStructure vehicle) return;
        vehicle.ReadSnapshot();
        VehicleSnapshot? snapshot = vehicle.Snapshot;
        if (snapshot is null) return;

        MeshData? combined = null;
        foreach (VehicleBlockSnapshot entry in snapshot.Blocks)
        {
            Block? block = capi.World.GetBlock(new AssetLocation(entry.BlockCode));
            if (block is null || block.Id <= 0) continue;

            MeshData? blockMesh = null;
            bool skipDefault = false;
            if (entry.BlockEntity is not null)
            {
                try
                {
                    (blockMesh, skipDefault) = BuildBlockEntityMesh(
                        snapshot,
                        entry,
                        block,
                        entry.BlockEntity);
                }
                catch (Exception error)
                {
                    capi.Logger.Warning(
                        "[IndependentVehicles] Rendu mobile des données de {0} ignoré : {1}",
                        entry.BlockCode,
                        error.Message);
                }
            }

            if (skipDefault && blockMesh is null) skipDefault = false;

            if (!skipDefault)
            {
                MeshData defaultMesh = capi.TesselatorManager.GetDefaultBlockMesh(block).Clone();
                if (blockMesh is null) blockMesh = defaultMesh;
                else blockMesh.AddMeshData(defaultMesh);
            }
            if (blockMesh is null) continue;

            blockMesh.Translate(entry.Offset.X - 0.5f, entry.Offset.Y, entry.Offset.Z - 0.5f);
            if (combined is null) combined = blockMesh;
            else combined.AddMeshData(blockMesh);
        }

        if (combined is not null) meshRef = capi.Render.UploadMultiTextureMesh(combined);
    }

    private (MeshData? Mesh, bool SkipDefault) BuildBlockEntityMesh(
        VehicleSnapshot snapshot,
        VehicleBlockSnapshot entry,
        Block block,
        VehicleBlockEntitySnapshot source)
    {
        string className = block.EntityClass ?? source.ClassName;
        BlockEntity blockEntity = capi.World.ClassRegistry.CreateBlockEntity(className)
            ?? throw new InvalidOperationException($"classe BlockEntity {className} introuvable");
        BlockPos virtualPosition = new(
            (int)Math.Floor(entity.Pos.X) + entry.Offset.X,
            (int)Math.Floor(entity.Pos.Y) + entry.Offset.Y,
            (int)Math.Floor(entity.Pos.Z) + entry.Offset.Z,
            entity.Pos.Dimension);
        blockEntity.Pos = virtualPosition;
        blockEntity.CreateBehaviors(block, capi.World);

        TreeAttribute tree = TreeAttribute.CreateFromBytes(source.DecodeTreeData());
        tree.SetInt("posx", virtualPosition.X);
        tree.SetInt("posy", virtualPosition.InternalY);
        tree.SetInt("posz", virtualPosition.Z);
        tree.SetString("blockCode", block.Code.ToShortString());

        Dictionary<int, AssetLocation> blockMappings = DecodeMappings(snapshot.BlockIdMappings);
        Dictionary<int, AssetLocation> itemMappings = DecodeMappings(snapshot.ItemIdMappings);
        bool initialized = false;
        try
        {
            blockEntity.FromTreeAttributes(tree, capi.World);
            blockEntity.OnLoadCollectibleMappings(
                capi.World,
                blockMappings,
                itemMappings,
                snapshot.CollectibleMappingSeed,
                resolveImports: false);
            blockEntity.Initialize(capi);
            initialized = true;

            var pool = new CapturedTerrainMeshPool();
            bool skipDefault = blockEntity.OnTesselation(pool, capi.Tesselator);
            return (pool.Mesh, skipDefault);
        }
        finally
        {
            if (initialized)
            {
                try
                {
                    blockEntity.OnBlockUnloaded();
                }
                catch (Exception error)
                {
                    capi.Logger.Warning(
                        "[IndependentVehicles] Nettoyage du rendu temporaire de {0} incomplet : {1}",
                        entry.BlockCode,
                        error.Message);
                }
            }
        }
    }

    private static Dictionary<int, AssetLocation> DecodeMappings(
        Dictionary<int, string>? mappings) =>
        mappings?.ToDictionary(
            entry => entry.Key,
            entry => new AssetLocation(entry.Value)) ?? [];

    private sealed class CapturedTerrainMeshPool : ITerrainMeshPool
    {
        public MeshData? Mesh { get; private set; }

        public void AddMeshData(MeshData data, int lodLevel = 1) => Add(data.Clone());

        public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1) =>
            Add(data.Clone().MatrixTransform(tfMatrix));

        public void AddMeshData(MeshData data, ColorMapData colorMapData, int lodLevel = 1) =>
            Add(data.Clone());

        private void Add(MeshData data)
        {
            if (Mesh is null) Mesh = data;
            else Mesh.AddMeshData(data);
        }
    }
}
