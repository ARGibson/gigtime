using System.Globalization;
using Gigtime.Contracts.Reporting;
using Gigtime.Domain.Reporting;

namespace Gigtime.Server.Reporting;

public static class ReportResponses
{
	public static ReportDto From(IReadOnlyList<ReportTotal> totals, CatalogueView catalogue)
	{
		var projects = totals
			.Select(t => new ReportTotalDto(new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				new(t.ProjectId, catalogue.Projects[t.ProjectId].Key),
				t.Currency,
				t.RawSeconds,
				t.RoundedSeconds,
				DurationText.Format(t.RoundedSeconds),
				Money(t.Earnings, t.Currency)))
			.ToArray();

		var clients = totals
			.GroupBy(t => (t.ClientId, t.Currency))
			.Select(g => new ClientReportTotalDto(
				new(g.Key.ClientId, catalogue.Clients[g.Key.ClientId].Key),
				g.Key.Currency,
				g.Sum(t => t.RawSeconds),
				g.Sum(t => t.RoundedSeconds),
				DurationText.Format(g.Sum(t => t.RoundedSeconds)),
				Money(g.Any(t => t.Earnings is null) ? null : g.Sum(t => t.Earnings), g.Key.Currency)))
			.ToArray();

		return new(projects, clients);
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
