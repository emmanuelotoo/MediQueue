namespace MediQueue.Domain.Abstractions;

/// <summary>
/// Supplies the current time. Injected everywhere time is read so that
/// time-dependent queue behaviour is deterministic under test.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
