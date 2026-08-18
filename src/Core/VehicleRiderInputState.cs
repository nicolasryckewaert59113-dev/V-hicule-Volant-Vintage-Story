namespace IndependentVehicles.Core;

public static class VehicleRiderInputBits
{
    public const int Forward = 1 << 0;
    public const int Backward = 1 << 1;
    public const int Left = 1 << 2;
    public const int Right = 1 << 3;
    public const int All = Forward | Backward | Left | Right;
}

public sealed class VehicleRiderInputState
{
    public int LastSequence { get; private set; } = -1;
    public int ControlBits { get; private set; }
    public long LastReceivedMilliseconds { get; private set; }

    public bool Accept(int sequence, int controlBits, long receivedMilliseconds)
    {
        if (sequence <= LastSequence) return false;

        LastSequence = sequence;
        ControlBits = controlBits & VehicleRiderInputBits.All;
        LastReceivedMilliseconds = receivedMilliseconds;
        return true;
    }

    public int FreshControlBits(long nowMilliseconds, long timeoutMilliseconds) =>
        LastSequence >= 0 && nowMilliseconds - LastReceivedMilliseconds <= timeoutMilliseconds
            ? ControlBits
            : 0;

    public void Stop() => ControlBits = 0;
}
