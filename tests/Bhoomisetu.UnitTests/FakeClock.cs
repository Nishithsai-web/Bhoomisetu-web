using Bhoomisetu.SharedKernel;

namespace Bhoomisetu.UnitTests;

internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
