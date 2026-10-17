using System.CommandLine;
using System.Globalization;
using Gigtime.Contracts;
using Gigtime.Contracts.Reporting;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class ReportCommands
{
	private static readonly string[] bounds = ["--from", "--to"];

	internal static void Configure(RootCommand root, CliExchange exchange)
	{
		var report = Add(root, "report", "Totals by project and client/currency; defaults to this week.");
		Flag(report, "week");
		Flag(report, "month");
		Text(report, "range");
		Text(report, "client");
		Text(report, "project");

		report.Validators.Add(result =>
		{
			var range = result.GetValue<string>("--range");

			if ((result.GetValue<bool>("--week") ? 1 : 0) + (result.GetValue<bool>("--month") ? 1 : 0) + (range is null ? 0 : 1) > 1
					|| !ReportDates.TryParse(range, out _, out _))
			{
				result.AddError("Choose --week, --month or --range yyyy-MM-dd..yyyy-MM-dd (inclusive local dates).");
			}
		});

		report.SetAction((r, ct) => exchange.GetAsync(r,
			"report",
			"/v1/report" + Query(
				("week", r.GetValue<bool>("--week") ? "true" : null),
				("month", r.GetValue<bool>("--month") ? "true" : null),
				("range", r.GetValue<string>("--range")),
				("client", r.GetValue<string>("--client")),
				("project", r.GetValue<string>("--project"))),
			GigtimeJsonContext.Default.ReportResponse,
			ct));

		var export = Add(root, "export", "Stream every event as JSON Lines; date-only bounds are inclusive UTC dates.");
		Text(export, "from");
		Text(export, "to");

		export.Validators.Add(result =>
		{
			foreach (var name in bounds)
			{
				if (result.GetValue<string>(name) is { } text && !InputTime.IsValid(text)
						&& !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
				{
					result.AddError(name + " expects an ISO time or UTC date.");
				}
			}
		});

		export.SetAction((r, ct) => exchange.ExportAsync(r,
			"/v1/export" + Query(
				("from", r.GetValue<string>("--from")),
				("to", r.GetValue<string>("--to"))),
			ct));
	}
}
