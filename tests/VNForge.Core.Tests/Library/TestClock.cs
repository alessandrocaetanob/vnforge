namespace VNForge.Core.Tests.Library;

/// <summary>
/// Um <see cref="TimeProvider"/> à mão: o relógio de parede e o monotônico andam juntos em <see cref="Advance"/>, mas a parede também pode ser acertada para trás
/// (<see cref="SetWall"/>), como quando a pessoa muda a hora do PC. Com intervalo zero o <c>Task.Delay</c> volta na hora, então o tempo só anda pelo teste.
/// </summary>
internal sealed class TestClock(DateTimeOffset wall) : TimeProvider
{
    private DateTimeOffset _wall = wall;
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by)
    {
        _ticks += by.Ticks;
        _wall += by;
    }

    public void SetWall(DateTimeOffset value) => _wall = value;

    public override DateTimeOffset GetUtcNow() => _wall;

    public override long GetTimestamp() => _ticks;
}
