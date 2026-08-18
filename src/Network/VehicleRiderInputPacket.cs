using ProtoBuf;

namespace IndependentVehicles.Network;

[ProtoContract]
public sealed class VehicleRiderInputPacket
{
    [ProtoMember(1)] public long VehicleEntityId { get; set; }
    [ProtoMember(2)] public int Sequence { get; set; }
    [ProtoMember(3)] public int ControlBits { get; set; }
    [ProtoMember(4)] public bool RequestDetach { get; set; }
    [ProtoMember(5)] public long AttachmentId { get; set; }
    [ProtoMember(6)] public bool RequestState { get; set; }
}
