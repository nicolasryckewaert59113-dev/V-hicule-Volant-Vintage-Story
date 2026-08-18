using ProtoBuf;

namespace IndependentVehicles.Network;

[ProtoContract]
public sealed class VehicleRiderStatePacket
{
    [ProtoMember(1)] public long VehicleEntityId { get; set; }
    [ProtoMember(2)] public long RiderEntityId { get; set; }
    [ProtoMember(3)] public bool Attached { get; set; }
    [ProtoMember(4)] public double LocalX { get; set; }
    [ProtoMember(5)] public double LocalY { get; set; }
    [ProtoMember(6)] public double LocalZ { get; set; }
    [ProtoMember(7)] public int LastAcceptedInputSequence { get; set; }
    [ProtoMember(8)] public long AttachmentId { get; set; }
}
