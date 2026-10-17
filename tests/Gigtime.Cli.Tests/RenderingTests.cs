using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class RenderingTests
{
	[Theory]
	[InlineData(false, false, false)]
	[InlineData(false, true, false)]
	[InlineData(true, true, false)]
	[InlineData(true, false, true)]
	public async Task Color_requires_a_terminal_and_no_NO_COLOR(bool terminal, bool noColor, bool expectedAnsi)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("client list"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, terminal, noColor, () => paths);

		(await app.RunAsync(["client", "list"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().Contains('\u001b', StringComparison.Ordinal).ShouldBe(expectedAnsi);
		output.ToString().ShouldContain("Example [blue]");
	}

	[Fact]
	public async Task Status_without_an_open_task_prints_none()
	{
		var response = JsonSerializer.Serialize(Envelopes.Success("status", new StatusDto(null)), GigtimeJsonContext.Default.StatusResponse);
		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["status"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().Trim().ShouldBe("none");
	}

	[Theory]
	[InlineData("running")]
	[InlineData("paused")]
	public async Task Status_shows_current_intervals_and_service_supplied_running_totals(string state)
	{
		var task = CommandCases.Task with
		{
			State = state,
			StoppedAt = null,
			RawSeconds = 90,
			RoundedSeconds = 900,
			Display = "00:15:00",
			Intervals =
			[
				new(1, CommandCases.Start, CommandCases.Start.AddSeconds(30)),
				new(2, CommandCases.Start.AddSeconds(60), state == "running" ? null : CommandCases.Start.AddSeconds(120)),
			],
		};

		var response = JsonSerializer.Serialize(Envelopes.Success("status", new StatusDto(task)), GigtimeJsonContext.Default.StatusResponse);
		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["status"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain(state);
		output.ToString().ShouldContain("00:01:30");
		output.ToString().ShouldContain("00:15:00");
		output.ToString().ShouldContain("Stopped: open");
		output.ToString().ShouldContain("2026-03-02T09:01:00");
		if (state == "paused") output.ToString().ShouldContain("2026-03-02T09:02:00");
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"ok\":true,\"command\":\"status\"}")]
	public async Task Malformed_responses_are_internal_errors(string response)
	{
		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["status", "--json"], TestContext.Current.CancellationToken)).ShouldBe(1);
		JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!.Error!.Code.ShouldBe("internal");
	}
}
