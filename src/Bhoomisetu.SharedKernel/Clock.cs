namespace Bhoomisetu.SharedKernel;

/// <summary>Source of the current time. Inject this instead of calling DateTimeOffset.UtcNow so rules are testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
