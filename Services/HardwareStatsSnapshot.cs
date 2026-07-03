namespace GameLauncher.Services
{
    public readonly record struct HardwareStatsSnapshot(
        float? CpuTemp,
        float? CpuUsage,
        float? GpuTemp,
        float? GpuUsage,
        float? RamUsedGb,
        float? RamTotalGb,
        float? RamLoad,
        float? VramUsedGb,
        float? VramTotalGb,
        float? VramLoad);
}
