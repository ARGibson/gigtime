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
		var report = Add(root, "report", "Show submitted totals and provisional unsubmitted tasks; defaults to this week.");
		ConfigurePeriod(report);

		report.SetAction((r, ct) => exchange.GetAsync(r,
			"report",
			"/v1/report" + PeriodQuery(r),
			GigtimeJsonContext.Default.ReportResponse,
			ct));

		var timesheet = Add(root, "timesheet", "Show raw time by local day and task; defaults to this week.");
		ConfigurePeriod(timesheet);

		timesheet.SetAction((r, ct) => exchange.GetAsync(r,
			"timesheet",
			"/v1/timesheet" + PeriodQuery(r),
			GigtimeJsonContext.Default.TimesheetResponse,
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

	private static void ConfigurePeriod(Command command)
	{
		Flag(command, "week");
		Flag(command, "month");
		Text(command, "range");
		Text(command, "client");
		Text(command, "project");

		command.Validators.Add(result =>
		{
			var range = result.GetValue<string>("--range");

			if ((result.GetValue<bool>("--week") ? 1 : 0) + (result.GetValue<bool>("--month") ? 1 : 0) + (range is null ? 0 : 1) > 1
					|| !ReportDates.TryParse(range, out _, out _))
			{
				result.AddError("Choose --week, --month or --range yyyy-MM-dd..yyyy-MM-dd (inclusive local dates).");
			}
		});
	}

	private static string PeriodQuery(ParseResult result)
	{
		return Query(
			("week", result.GetValue<bool>("--week") ? "true" : null),
			("month", result.GetValue<bool>("--month") ? "true" : null),
			("range", result.GetValue<string>("--range")),
			("client", result.GetValue<string>("--client")),
			("project", result.GetValue<string>("--project")));
	}
}
