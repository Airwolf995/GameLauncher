namespace GameLauncher.Services
{
    internal interface IHardwareTelemetrySource : System.IDisposable
    {
        float? TryReadCpuTemperature();
        (float? Temperature, float? Usage) TryReadGpu(uint vendorId);
    }
}
