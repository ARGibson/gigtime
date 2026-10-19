using System.Globalization;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class TaskResponses
{
	public static TaskDto From(TaskView task, CatalogueView catalogue, DateTimeOffset now)
	{
		var p = catalogue.Projects[task.ProjectId];
		var c = catalogue.Clients[p.ClientId];
		var figures = TaskFigures.Resolve(task, catalogue, now);

		var submission = task.Submission is { } fixedFigure
			? new SubmissionDto(fixedFigure.SubmittedAt,
				fixedFigure.RoundedSeconds,
				fixedFigure.Rounding,
				fixedFigure.Rate is { } rate ? MoneyDto.From(rate, fixedFigure.Currency) : null,
				fixedFigure.Currency,
				Amount(figures.Earnings, fixedFigure.Currency))
			: null;

		return new(task.Id,
			new(c.Id, c.Key),
			new(p.Id, p.Key),
			task.Ref,
			task.State.ToString().ToLowerInvariant(),
			task.Intervals.Select(i => new IntervalDto(i.Number, i.Start, i.End)).ToArray(),
			figures.RawSeconds,
			figures.RoundedSeconds,
			DurationText.Format(figures.RoundedSeconds),
			submission,
			task.StartedAt!.Value,
			task.StoppedAt,
			c.Timezone ?? catalogue.Defaults.Timezone,
			task.Rate is { } own ? MoneyDto.From(own.Amount, own.Currency) : null);
	}

	private static EarningsDto? Amount(decimal? amount, string currency)
	{
		return amount is { } value
			? new(currency,
				value.ToString(CultureInfo.InvariantCulture),
				CurrencyRounding.Round(value, currency).ToString("F" + CurrencyRounding.Digits(currency), CultureInfo.InvariantCulture))
			: null;
	}
}
