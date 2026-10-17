namespace Gigtime.Domain.Tasks;

public static class TaskMath
{
	public static DateTimeOffset Instant(DateTimeOffset value) => DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

	public static long Round(long seconds, string rounding = "nearest-minute")
	{
		ArgumentOutOfRangeException.ThrowIfNegative(seconds);

		if (rounding != "nearest-minute") throw new ArgumentException("Unsupported rounding.", nameof(rounding));

		return checked((seconds / 60 + (seconds % 60 >= 30 ? 1 : 0)) * 60);
	}

	public static decimal? Earnings(TaskView task)
	{
		ArgumentNullException.ThrowIfNull(task);

		return task.Snapshot?.Rate * task.RoundedSeconds / 3600m;
	}
}
