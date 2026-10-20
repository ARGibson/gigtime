using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Reporting;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class ReportCliTests
{
	[Theory]
	[InlineData("report")]
	[InlineData("timesheet")]
	public async Task Both_reports_render_default_and_filtered_queries(string command)
	{
		foreach (var (arguments, query) in new (string[] Arguments, string Query)[]
						{
							([], ""), (["--week"], "?week=true"), (["--month", "--client", "example"], "?month=true&client=example"),
							(["--range", "2026-03-01..2026-03-31", "--project", "work"], "?range=2026-03-01..2026-03-31&project=work"),
						})
		{
			await using var server = await FakeServer.StartAsync(CommandCases.Response(command));
			var paths = server.Paths;
			var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => paths);

			(await app.RunAsync([command, .. arguments], TestContext.Current.CancellationToken)).ShouldBe(0);
			server.Requests.ShouldHaveSingleItem().Path.ShouldBe("/v1/" + command + query);
		}
	}

	[Theory]
	[InlineData("--week --month")]
	[InlineData("--week --range 2026-03-01..2026-03-31")]
	[InlineData("--range 2026-03-10..2026-03-01")]
	[InlineData("--range bad")]
	public async Task Both_reports_share_period_usage_errors_before_resolving_paths(string options)
	{
		var errors = new List<string>();

		foreach (var command in new[] { "report", "timesheet" })
		{
			using var output = new StringWriter(CultureInfo.InvariantCulture);

			var app = new CliApplication(output,
				TextWriter.Null,
				false,
				false,
				() => throw new InvalidOperationException("Resolved paths for invalid period."));

			(await app.RunAsync([command, .. options.Split(' '), "--json"], TestContext.Current.CancellationToken)).ShouldBe(2);
			using var json = JsonDocument.Parse(output.ToString());
			errors.Add(json.RootElement.GetProperty("error").GetProperty("message").GetString()!);
		}

		errors[0].ShouldBe(errors[1]);
	}

	[Fact]
	public async Task Human_timesheet_has_day_task_and_period_totals_without_money_or_rounding()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("timesheet"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["timesheet"], TestContext.Current.CancellationToken)).ShouldBe(0);
		var text = output.ToString();
		text.ShouldContain("2026-03-02");
		text.ShouldContain("01:01:01");
		text.ShouldContain("01JTASK");
		text.ShouldContain("ticket-7");
		text.ShouldContain("Period time");
		text.ShouldContain("Client total");
		text.ShouldNotContain("Rounded");
		text.ShouldNotContain("Amount");
		text.ShouldNotContain("GBP");
	}

	[Fact]
	public async Task Human_settlement_separates_submitted_totals_and_provisional_tasks()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("report"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["report"], TestContext.Current.CancellationToken)).ShouldBe(0);
		var text = output.ToString();
		text.ShouldContain("12.50");
		text.ShouldContain("Unsubmitted (provisional)");
		text.ShouldContain("open-task");
		text.ShouldContain("paused");
		text.ShouldContain("ref-open");
		text.ShouldContain("0.50 GBP");
	}

	[Theory]
	[InlineData(false, "not rated")]
	[InlineData(true, "unpriced")]
	public async Task Null_provisional_amount_distinguishes_own_rate_from_unrated(bool hasOwnRate, string expected)
	{
		var body = JsonSerializer.Serialize(Envelopes.Success("report",
				new ReportDto([],
					[],
					[
						new("a1b2c3",
							new(CommandCases.ClientId, "example"),
							new(CommandCases.ProjectId, "work"),
							null,
							"running",
							60,
							60,
							"00:01:00",
							null,
							Rate: hasOwnRate ? new("45", "GBP") : null)
					])),
			GigtimeJsonContext.Default.ReportResponse);

		await using var server = await FakeServer.StartAsync(body);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["report"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain("running");
		output.ToString().ShouldContain(expected);
		output.ToString().ShouldNotContain(hasOwnRate ? "not rated" : "unpriced");
	}
}
