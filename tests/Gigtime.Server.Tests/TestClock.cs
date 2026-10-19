namespace Gigtime.Server.Tests;

public sealed class TestClock : TimeProvider
{
	public DateTimeOffset Now { get; set; } =
		DateTimeOffset.Parse("2026-03-02T09:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);

	public Exception? Failure { get; set; }
	public TimeZoneInfo HostTimezone { get; set; } = TimeZoneInfo.Local;
	public override TimeZoneInfo LocalTimeZone => HostTimezone;

	public override DateTimeOffset GetUtcNow()
	{
		if (Failure is { } error) throw error;

		return Now;
	}
}
