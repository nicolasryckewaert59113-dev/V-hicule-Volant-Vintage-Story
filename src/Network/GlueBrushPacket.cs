using ProtoBuf;

namespace IndependentVehicles.Network;

[ProtoContract]
public sealed class GlueBrushPacket
{
    [ProtoMember(1)] public bool HasTarget { get; set; }
    [ProtoMember(2)] public bool Paint { get; set; }
    [ProtoMember(3)] public bool Remove { get; set; }
    [ProtoMember(4)] public int FromX { get; set; }
    [ProtoMember(5)] public int FromY { get; set; }
    [ProtoMember(6)] public int FromZ { get; set; }
    [ProtoMember(7)] public int ToX { get; set; }
    [ProtoMember(8)] public int ToY { get; set; }
    [ProtoMember(9)] public int ToZ { get; set; }
    [ProtoMember(10)] public int Dimension { get; set; }
}
