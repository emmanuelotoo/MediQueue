namespace MediQueue.Domain.Abstractions;

/// <summary>
/// Supplies the current time. Injected everywhere time is read so that
/// time-dependent queue behaviour is deterministic under test.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>
/// Reads the real clock in UTC. Ghana observes GMT year-round with no daylight
/// saving, so UTC and hospital-local time are the same wall clock; storing UTC
/// keeps ordering and date grouping correct regardless of where a server runs.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
