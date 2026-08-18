using Vintagestory.API.Common;

namespace IndependentVehicles.Items;

public sealed class ItemStructureGlue : Item
{
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling)
    {
        if (blockSel is null) return;

        handHandling = EnumHandHandling.PreventDefault;
        IndependentVehiclesSystem? system = byEntity.Api.ModLoader.GetModSystem<IndependentVehiclesSystem>();
        system?.BeginGlueStroke(byEntity, blockSel);
    }

    public override bool OnHeldInteractStep(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel)
    {
        IndependentVehiclesSystem? system = byEntity.Api.ModLoader.GetModSystem<IndependentVehiclesSystem>();
        system?.ContinueGlueStroke(byEntity, blockSel);
        return true;
    }

    public override void OnHeldInteractStop(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel)
    {
        byEntity.Api.ModLoader.GetModSystem<IndependentVehiclesSystem>()?.EndGlueStroke();
    }

    public override bool OnHeldInteractCancel(
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        EnumItemUseCancelReason cancelReason)
    {
        byEntity.Api.ModLoader.GetModSystem<IndependentVehiclesSystem>()?.EndGlueStroke();
        return true;
    }
}
