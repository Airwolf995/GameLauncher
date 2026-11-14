using GameLauncher.Services;

namespace GameLauncher.Tests;

public class PlayTimeSessionClockTests
{
    [Fact]
    public void NeueSitzungBuchtKeineZeitVorDerErkennung()
    {
        var time = new ManualPlayTimeProvider();
        var clock = new PlayTimeSessionClock(time);
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(clock.Update(new HashSet<string> { "spiel" }));
    }

    [Fact]
    public void UnregelmäßigeDurchläufeMessenDieDauerUndBewahrenBruchteile()
    {
        var time = new ManualPlayTimeProvider();
        var clock = new PlayTimeSessionClock(time);
        var running = new HashSet<string> { "spiel" };
        clock.Update(running);
        time.Advance(TimeSpan.FromMilliseconds(2750));
        Assert.Equal(2, clock.Update(running)["spiel"]);
        time.Advance(TimeSpan.FromMilliseconds(3250));
        Assert.Equal(4, clock.Update(running)["spiel"]);
    }

    [Fact]
    public void SitzungsendeBuchtDenRestEinmalUndNeustartZähltKeinePause()
    {
        var time = new ManualPlayTimeProvider();
        var clock = new PlayTimeSessionClock(time);
        var running = new HashSet<string> { "spiel" };
        clock.Update(running);
        time.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(7, clock.Update(new HashSet<string>())["spiel"]);
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Empty(clock.Update(new HashSet<string>()));
        Assert.Empty(clock.Update(running));
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, clock.Update(running)["spiel"]);
    }

    [Fact]
    public void ParalleleSpieleBekommenNurIhreEigeneSitzungsdauer()
    {
        var time = new ManualPlayTimeProvider();
        var clock = new PlayTimeSessionClock(time);
        clock.Update(new HashSet<string> { "a" });
        time.Advance(TimeSpan.FromSeconds(3));
        var first = clock.Update(new HashSet<string> { "a", "b" });
        Assert.Equal(3, first["a"]);
        Assert.False(first.ContainsKey("b"));
        time.Advance(TimeSpan.FromSeconds(2));
        var next = clock.Update(new HashSet<string> { "b" });
        Assert.Equal(2, next["a"]);
        Assert.Equal(2, next["b"]);
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(4, clock.Update(new HashSet<string> { "b" })["b"]);
    }

    [Fact]
    public void StandbyUndÄnderungenDerSystemuhrErzeugenKeineZusätzlicheSpielzeit()
    {
        var time = new ManualPlayTimeProvider();
        var clock = new PlayTimeSessionClock(time);
        var running = new HashSet<string> { "spiel" };
        clock.Update(running);
        time.Advance(TimeSpan.FromHours(2));
        Assert.Empty(clock.Update(running, resetBaseline: true));
        time.UtcNow = time.UtcNow.AddDays(-1);
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(3, clock.Update(running)["spiel"]);
    }
}

internal sealed class ManualPlayTimeProvider : TimeProvider
{
    private long _timestamp;
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => UtcNow;
    public void Advance(TimeSpan elapsed)
    {
        _timestamp += elapsed.Ticks;
        UtcNow += elapsed;
    }
}
