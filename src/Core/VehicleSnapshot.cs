namespace IndependentVehicles.Core;

public sealed class VehicleSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public GridPos OriginalController { get; set; }
    public int CollectibleMappingSeed { get; set; }
    public Dictionary<int, string> BlockIdMappings { get; set; } = [];
    public Dictionary<int, string> ItemIdMappings { get; set; } = [];
    public List<VehicleBlockSnapshot> Blocks { get; set; } = [];
    public List<LocalBond> Bonds { get; set; } = [];
}

public sealed class VehicleBlockSnapshot
{
    public LocalPos Offset { get; set; }
    public string BlockCode { get; set; } = string.Empty;
    public VehicleBlockEntitySnapshot? BlockEntity { get; set; }
    public List<VehicleCuboidSnapshot> CollisionBoxes { get; set; } = [];
    public byte[]? LightHsv { get; set; }
}

public sealed class VehicleBlockEntitySnapshot
{
    public string ClassName { get; set; } = string.Empty;
    public string TreeDataBase64 { get; set; } = string.Empty;

    public byte[] DecodeTreeData() => Convert.FromBase64String(TreeDataBase64);

    public static VehicleBlockEntitySnapshot FromBytes(string className, byte[] data) => new()
    {
        ClassName = className,
        TreeDataBase64 = Convert.ToBase64String(data)
    };
}

public sealed class VehicleCuboidSnapshot
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float Z1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float Z2 { get; set; }

    public VehicleCuboidSnapshot RotateQuarterTurns(int quarterTurns)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        float minX = X1;
        float maxX = X2;
        float minZ = Z1;
        float maxZ = Z2;
        for (int turn = 0; turn < turns; turn++)
        {
            (minX, maxX, minZ, maxZ) =
                (minZ, maxZ, 1 - maxX, 1 - minX);
        }
        return new VehicleCuboidSnapshot
        {
            X1 = minX,
            Y1 = this.Y1,
            Z1 = minZ,
            X2 = maxX,
            Y2 = this.Y2,
            Z2 = maxZ
        };
    }
}
