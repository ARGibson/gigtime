using System.Diagnostics;
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
	public async Task Piped_output_stays_plain_under_CI_environment_enrichers()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("client list"));

		var start = new ProcessStartInfo("dotnet")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};

		start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
		start.ArgumentList.Add("client");
		start.ArgumentList.Add("list");
		start.Environment.Remove("GIGTIME_INSTANCE");
		start.Environment["CI"] = "true";
		start.Environment["GITHUB_ACTIONS"] = "true";
		start.Environment["TF_BUILD"] = "True";
		start.Environment["TERM"] = "dumb";
		start.Environment.Remove("NO_COLOR");
		start.Environment["XDG_DATA_HOME"] = server.Paths.DataDirectory;
		start.Environment["XDG_RUNTIME_DIR"] = server.Paths.RuntimeDirectory;
		start.Environment["XDG_CONFIG_HOME"] = server.Paths.ConfigDirectory;
		using var process = Process.Start(start)!;
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

		try
		{
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			process.ExitCode.ShouldBe(0, await error);
			var rendered = await output;
			rendered.ShouldContain("Example [blue]");
			rendered.ShouldNotContain("\u001b");
			server.Requests.Count.ShouldBe(1);
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
		}
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
		output.ToString().ShouldContain("2026-03-02 09:01");
		if (state == "paused") output.ToString().ShouldContain("2026-03-02 09:02");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Json_output_preserves_the_original_envelope_including_extra_fields(bool ok)
	{
		var response = ok
			? " { \"ok\": true, \"command\": \"status\", \"data\": { \"task\": null }, \"extra\": { \"version\": 2 } }\n"
			: " { \"ok\": false, \"command\": \"status\", \"error\": { \"code\": \"conflict\", \"message\": \"busy\" }, \"extra\": 2 }\n";

		await using var server = await FakeServer.StartAsync(response, ok ? 200 : 409);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["status", "--json"], TestContext.Current.CancellationToken)).ShouldBe(ok ? 0 : 7);
		output.ToString().ShouldBe(response);
		output.ToString().ShouldContain("extra");
	}

	[Fact]
	public async Task Stop_keeps_raw_and_rounded_with_a_submission_note()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("stop"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["stop"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain("Raw");
		output.ToString().ShouldContain("Rounded");
		output.ToString().ShouldContain("01:01:01");
		output.ToString().ShouldContain("01:15:00");
		output.ToString().ShouldContain("Submit the rounded figure.");

		output
			.ToString()
			.Split(Environment.NewLine)
			.Single(line => line.Contains("Rounded", StringComparison.Ordinal))
			.ShouldNotContain("Submit");
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("null")]
	[InlineData("{}")]
	[InlineData("{\"ok\":true,\"data\":{\"task\":null}}")]
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
