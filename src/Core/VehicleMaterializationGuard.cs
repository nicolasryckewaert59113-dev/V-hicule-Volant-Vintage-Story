using System.Threading;

namespace IndependentVehicles.Core;

public enum VehicleMaterializationPhase
{
    Mobile,
    Materializing,
    Materialized,
    Discarded
}

/// <summary>
/// Verrou monotone du passage entité mobile -> blocs fixes.
/// Une même entité ne peut commencer cette transition qu'une seule fois à la fois
/// et ne peut plus la recommencer après validation ou abandon définitif.
/// </summary>
public sealed class VehicleMaterializationGuard
{
    private int phase = (int)VehicleMaterializationPhase.Mobile;

    public VehicleMaterializationPhase Phase =>
        (VehicleMaterializationPhase)Volatile.Read(ref phase);

    public bool TryBegin() =>
        Interlocked.CompareExchange(
            ref phase,
            (int)VehicleMaterializationPhase.Materializing,
            (int)VehicleMaterializationPhase.Mobile) == (int)VehicleMaterializationPhase.Mobile;

    public bool TryCancel() =>
        Interlocked.CompareExchange(
            ref phase,
            (int)VehicleMaterializationPhase.Mobile,
            (int)VehicleMaterializationPhase.Materializing) == (int)VehicleMaterializationPhase.Materializing;

    public bool TryComplete() =>
        Interlocked.CompareExchange(
            ref phase,
            (int)VehicleMaterializationPhase.Materialized,
            (int)VehicleMaterializationPhase.Materializing) == (int)VehicleMaterializationPhase.Materializing;

    public bool TryDiscard() =>
        Interlocked.CompareExchange(
            ref phase,
            (int)VehicleMaterializationPhase.Discarded,
            (int)VehicleMaterializationPhase.Mobile) == (int)VehicleMaterializationPhase.Mobile;
}
