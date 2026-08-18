using IndependentVehicles.Items;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace IndependentVehicles.Blocks;

public sealed class BlockVehicleControlSeat : Block
{
    private const string FacingVariant = "side";

    public static int HeadingTurns(Block block, float fallbackYaw)
    {
        if (block.Variant is not null &&
            block.Variant.TryGetValue(FacingVariant, out string? facing))
        {
            return facing switch
            {
                "west" => 1,
                "south" => 2,
                "east" => 3,
                _ => 0
            };
        }

        // Compatibilité avec le siège sans variante des versions 0.1 à 0.3.4.
        // Sa première activation reprend le quart de tour regardé par le joueur,
        // puis la rematérialisation le convertit en variante orientée persistante.
        return Core.QuarterTurn.FromYaw(fallbackYaw);
    }

    public override bool DoPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ItemStack byItemStack)
    {
        BlockFacing facing = Block.SuggestedHVOrientation(byPlayer, blockSel)[0];
        Block? oriented = world.GetBlock(CodeWithVariant(FacingVariant, facing.Code));
        if (oriented is null || oriented.Id <= 0)
            return base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);

        world.BlockAccessor.SetBlock(oriented.BlockId, blockSel.Position, byItemStack);
        return true;
    }

    public override AssetLocation GetRotatedBlockCode(int angle)
    {
        BlockFacing facing = BlockFacing.NORTH;
        if (Variant is not null &&
            Variant.TryGetValue(FacingVariant, out string? facingCode))
        {
            facing = BlockFacing.FromCode(facingCode) ?? BlockFacing.NORTH;
        }

        return new AssetLocation(
            IndependentVehiclesSystem.Domain,
            $"vehiclecontrolseat-{facing.GetHorizontalRotated(angle).Code}");
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        bool glueIsHeld = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is ItemStructureGlue;
        if (glueIsHeld)
        {
            // Laisse l’objet tenu démarrer son interaction continue, comme sur un bloc vanilla.
            return false;
        }

        if (world.Side != EnumAppSide.Server) return true;

        IndependentVehiclesSystem? system = world.Api.ModLoader.GetModSystem<IndependentVehiclesSystem>();
        system?.TryActivate(blockSel.Position, byPlayer);
        return true;
    }
}
