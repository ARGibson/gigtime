namespace Gigtime.Domain.Tasks;

public sealed record Interval(int Number, DateTimeOffset Start, DateTimeOffset? End)
{
	public long Seconds => End is { } end ? (end - Start).Ticks / TimeSpan.TicksPerSecond : 0;
}
