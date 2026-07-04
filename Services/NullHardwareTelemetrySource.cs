namespace GameLauncher.Services
{
    internal sealed class NullHardwareTelemetrySource : IHardwareTelemetrySource
    {
        public float? TryReadCpuTemperature() => null;

        public float? TryReadGpuTemperature() => null;

        public float? TryReadGpuMemoryTotalGb() => null;

        public void Dispose()
        {
        }
    }
}
