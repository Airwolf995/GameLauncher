namespace GameLauncher.Services
{
    internal interface IOptionalTemperatureReader
    {
        float? TryReadCpuTemperature();
        float? TryReadGpuTemperature();
    }
}
