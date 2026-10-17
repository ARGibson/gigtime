using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class CorrectionCliTests
{
	[Theory]
	[InlineData("amend")]
	[InlineData("amend abcdef")]
	[InlineData("amend abcdef --start 09:00")]
	[InlineData("amend abcdef --interval 1")]
	[InlineData("amend abcdef --interval 0 --end 10:00")]
	[InlineData("amend abcdef --interval bad --end 10:00")]
	[InlineData("amend abcdef --client a")]
	[InlineData("amend abcdef --ref x --clear-ref")]
	[InlineData("add a b --from bad --to 10:00")]
	[InlineData("add a b --from 09:00")]
	[InlineData("void")]
	[InlineData("report --week --month")]
	[InlineData("report --range 2026-03-10..2026-03-01")]
	[InlineData("report --range bad")]
	[InlineData("export --from bad")]
	public async Task Invalid_corrections_and_periods_are_usage_without_path_resolution(string command)
	{
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = new CliApplication(output, error, false, false, () => throw new InvalidOperationException("Resolved paths for bad args."));
		(await app.RunAsync([.. command.Split(' '), "--json"], TestContext.Current.CancellationToken)).ShouldBe(2);
		JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!.Error!.Code.ShouldBe("usage");
	}

	[Fact]
	public async Task Amend_can_clear_a_reference_without_editing_intervals()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("amend"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		(await app.RunAsync(["amend", "abcdef", "--clear-ref"], TestContext.Current.CancellationToken)).ShouldBe(0);
		using var body = JsonDocument.Parse(server.Requests.ShouldHaveSingleItem().Body);
		body.RootElement.GetProperty("clear_ref").GetBoolean().ShouldBeTrue();
	}

	[Theory]
	[InlineData("report", "/v1/report")]
	[InlineData("report --week", "/v1/report?week=true")]
	[InlineData("report --month --client example", "/v1/report?month=true&client=example")]
	public async Task Report_period_arguments_preserve_default_and_filters(string command, string path)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("report"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		server.Requests.ShouldHaveSingleItem().Path.ShouldBe(path);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Export_is_JSON_Lines_in_both_output_modes(bool asJson)
	{
		using var data = JsonDocument.Parse("{\"reason\":\"audit\"}");
		var one = new ExportEventDto(1, "task_voided", "abcdef", 3, CommandCases.Start, "correlation", data.RootElement.Clone());

		var two = one with
		{
			Sequence = 2,
			Version = 4
		};

		var lines = JsonSerializer.Serialize(one, GigtimeJsonContext.Default.ExportEventDto) + "\n"
			+ JsonSerializer.Serialize(two, GigtimeJsonContext.Default.ExportEventDto) + "\n";

		await using var server = await FakeServer.StartAsync(lines);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		string[] args = ["export", "--from", "2026-03-01", "--to", "2026-03-31"];
		if (asJson) args = [.. args, "--json"];
		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldBe(lines);
		server.Requests.ShouldHaveSingleItem().Path.ShouldBe("/v1/export?from=2026-03-01&to=2026-03-31");

		output
			.ToString()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Select(line => JsonSerializer.Deserialize(line, GigtimeJsonContext.Default.ExportEventDto)!.Sequence)
			.ShouldBe([1, 2]);
	}

	[Theory]
	[InlineData("internal", 1)]
	[InlineData("usage", 2)]
	[InlineData("invalid_transition", 3)]
	[InlineData("not_found", 4)]
	[InlineData("config", 5)]
	[InlineData("unavailable", 6)]
	[InlineData("conflict", 7)]
	[InlineData("validation", 8)]
	public async Task Export_maps_service_refusals(string code, int exit)
	{
		var body = JsonSerializer.Serialize(Envelopes.Failure<AcknowledgementDto>("export", new(code, "refused")),
			GigtimeJsonContext.Default.AcknowledgementResponse);

		await using var server = await FakeServer.StartAsync(body, 409);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		(await app.RunAsync(["export", "--json"], TestContext.Current.CancellationToken)).ShouldBe(exit);
		output.ToString().ShouldBe(body + Environment.NewLine);
	}

	[Fact]
	public async Task Json_adds_only_a_missing_trailing_newline()
	{
		var body = CommandCases.Response("status");
		await using var server = await FakeServer.StartAsync(body);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		(await app.RunAsync(["status", "--json"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldBe(body + Environment.NewLine);
	}
}
