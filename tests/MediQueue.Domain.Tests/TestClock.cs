using MediQueue.Domain.Abstractions;

namespace MediQueue.Domain.Tests;

/// <summary>A clock the test drives by hand.</summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; private set; }

    public void Advance(TimeSpan by) => Now = Now.Add(by);

    public void Set(DateTimeOffset to) => Now = to;
}
