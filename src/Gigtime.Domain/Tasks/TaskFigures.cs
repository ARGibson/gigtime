using Gigtime.Domain.Catalogue;

namespace Gigtime.Domain.Tasks;

public sealed record TaskFigures(long RawSeconds, long RoundedSeconds, string Rounding, decimal? Rate, string Currency, decimal? Earnings)
{
	public static TaskFigures Resolve(TaskView task, CatalogueView catalogue, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(catalogue);
		var raw = task.RawSecondsAt(now);

		if (task.Submission is { } submission)
		{
			return new(raw,
				submission.RoundedSeconds,
				submission.Rounding,
				submission.Rate,
				submission.Currency,
				submission.Rate * submission.RoundedSeconds / 3600m);
		}

		var policy = catalogue.Resolve(task.ProjectId);
		var rounded = TaskMath.Round(raw, policy.Rounding);
		var rate = task.Rate?.Amount ?? policy.Rate;

		return new(raw, rounded, policy.Rounding, rate, policy.Currency, rate * rounded / 3600m);
	}
}
