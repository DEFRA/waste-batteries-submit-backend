using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Time;

public class TimeProviderTests
{
    [Fact]
    public void AsUtc_WhenInstantIsUnspecified_LabelsItUtcWithoutShifting()
    {
        // Arrange
        ITimeProvider time = new FixedTime(new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));
        var unspecified = new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Unspecified);

        // Act
        var labelled = time.AsUtc(unspecified);

        // Assert
        Assert.NotNull(labelled);
        Assert.Equal(DateTimeKind.Utc, labelled.Value.Kind);
        Assert.Equal(unspecified.Ticks, labelled.Value.Ticks);
    }

    [Fact]
    public void AsUtc_WhenInstantIsUtc_ReturnsSameInstant()
    {
        // Arrange
        ITimeProvider time = new FixedTime(new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));
        var utc = new DateTime(2026, 7, 31, 23, 0, 0, DateTimeKind.Utc);

        // Act
        var labelled = time.AsUtc(utc);

        // Assert
        Assert.Equal(utc, labelled);
    }

    [Fact]
    public void AsUtc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        ITimeProvider time = new FixedTime(new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc));

        // Act
        var labelled = time.AsUtc(null);

        // Assert
        Assert.Null(labelled);
    }

    [Fact]
    public void UtcNow_WhenRead_ReturnsUtcInstant()
    {
        // Arrange
        var before = DateTime.UtcNow;
        var time = new SystemTimeProvider();

        // Act
        var now = time.UtcNow;

        // Assert
        Assert.Equal(DateTimeKind.Utc, now.Kind);
        Assert.InRange(now, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }
}
