namespace GameLauncher.Services
{
    internal interface IHardwareTelemetrySource : System.IDisposable
    {
        float? TryReadCpuTemperature();
        float? TryReadGpuTemperature();
        float? TryReadGpuMemoryTotalGb();
    }
}
