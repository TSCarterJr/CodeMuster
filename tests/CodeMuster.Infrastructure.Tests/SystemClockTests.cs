using CodeMuster.Domain;

namespace CodeMuster.Infrastructure.Tests;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_is_within_five_seconds_of_now_and_has_zero_offset()
    {
        IClock clock = new SystemClock();

        var now = clock.UtcNow;

        Assert.True((DateTimeOffset.UtcNow - now).Duration() < TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}
