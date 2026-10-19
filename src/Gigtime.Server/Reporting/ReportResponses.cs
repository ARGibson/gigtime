using System.Globalization;
using Gigtime.Contracts.Reporting;
using Gigtime.Domain.Reporting;

namespace Gigtime.Server.Reporting;

public static class ReportResponses
{
	public static ReportDto From(SettlementResult result, CatalogueView catalogue)
	{
		var projects = result
			.Totals
			.Select(t => new ReportTotalDto(new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				new(t.ProjectId, catalogue.Projects[t.ProjectId].Key),
				t.Currency,
				t.RawSeconds,
				t.RoundedSeconds,
				DurationText.Format(t.RoundedSeconds),
				Money(t.Earnings, t.Currency)))
			.ToArray();

		var clients = result
			.Clients.Select(t => new ClientReportTotalDto(
				new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				t.Currency,
				t.RawSeconds,
				t.RoundedSeconds,
				DurationText.Format(t.RoundedSeconds),
				Money(t.Earnings, t.Currency)))
			.ToArray();

		var unsubmitted = result
			.Unsubmitted.Select(t => new UnsubmittedTaskDto(t.TaskId,
				new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				new(t.ProjectId, catalogue.Projects[t.ProjectId].Key),
				t.Ref,
				t.State.ToString().ToLowerInvariant(),
				t.Figures.RawSeconds,
				t.Figures.RoundedSeconds,
				DurationText.Format(t.Figures.RoundedSeconds),
				Money(t.Figures.Earnings, t.Figures.Currency)))
			.ToArray();

		return new(projects, clients, unsubmitted);
	}

	private static EarningsDto? Money(decimal? amount, string currency)
	{
		return amount is { } value
			? new(currency,
				value.ToString(CultureInfo.InvariantCulture),
				CurrencyRounding.Round(value, currency).ToString("F" + CurrencyRounding.Digits(currency), CultureInfo.InvariantCulture))
			: null;
	}
}
