namespace GameLauncher.Services
{
    internal sealed class NullTemperatureReader : IOptionalTemperatureReader
    {
        public float? TryReadCpuTemperature() => null;

        public float? TryReadGpuTemperature() => null;
    }
}
