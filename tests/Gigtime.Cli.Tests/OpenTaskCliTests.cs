using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class OpenTaskCliTests
{
	[Theory]
	[InlineData("pause abcdef", "/v1/tasks/pause?task=abcdef")]
	[InlineData("resume abcdef --switch", "/v1/tasks/resume?task=abcdef&switch=true")]
	[InlineData("stop abcdef --submit", "/v1/tasks/stop?task=abcdef&submit=true")]
	public async Task Explicit_lifecycle_targets_use_bodyless_queries(string command, string path)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("resume"));
		var paths = server.Paths;
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => paths);
		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		var request = server.Requests.ShouldHaveSingleItem();
		request.Path.ShouldBe(path);
		request.Body.ShouldBeEmpty();
	}

	[Fact]
	public async Task Existing_task_add_sends_only_interval_input()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("add"));
		var paths = server.Paths;
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => paths);
		(await app.RunAsync(["add", "abcdef", "--from", "09:00", "--to", "10:00"], TestContext.Current.CancellationToken)).ShouldBe(0);
		var request = server.Requests.ShouldHaveSingleItem();
		request.Path.ShouldBe("/v1/tasks/abcdef/add");
		using var body = JsonDocument.Parse(request.Body);
		body.RootElement.GetProperty("start").GetString().ShouldBe("09:00");
		body.RootElement.GetProperty("end").GetString().ShouldBe("10:00");
	}

	[Theory]
	[InlineData("--ref")]
	[InlineData("--rate")]
	public async Task Existing_task_add_rejects_reference_and_rate_before_paths(string option)
	{
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => throw new InvalidOperationException());

		(await app.RunAsync(["add", "abcdef", "--from", "09:00", "--to", "10:00", option, "42"],
			TestContext.Current.CancellationToken)).ShouldBe(8);
	}

	[Theory]
	[InlineData(true, true, true)]
	[InlineData(false, true, false)]
	[InlineData(true, false, false)]
	public async Task Switch_prompts_require_both_terminals_and_repost_the_start_body(bool inputTerminal, bool outputTerminal, bool retries)
	{
		var count = 0;

		await using var server = await FakeServer.StartAsync(async context =>
		{
			context.Response.StatusCode = ++count == 1 ? 409 : 200;
			await context.Response.WriteAsync(count == 1 ? RunningRefusal("start") : CommandCases.Response("start"));
		});

		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, output, error, "yes\n", inputTerminal, outputTerminal);

		(await app.RunAsync(["start", "example", "work", "--ref", "opaque", "--rate", "45"],
			TestContext.Current.CancellationToken)).ShouldBe(retries ? 0 : 3);

		server.Requests.Count.ShouldBe(retries ? 2 : 1);
		error.ToString().Contains("[y/N]", StringComparison.Ordinal).ShouldBe(retries);

		if (retries)
		{
			var requests = server.Requests.ToArray();
			using var first = JsonDocument.Parse(requests[0].Body);
			using var second = JsonDocument.Parse(requests[1].Body);
			first.RootElement.GetProperty("switch").GetBoolean().ShouldBeFalse();
			second.RootElement.GetProperty("switch").GetBoolean().ShouldBeTrue();
			second.RootElement.GetProperty("switch_from").GetString().ShouldBe("aaaaaa");
			first.RootElement.GetProperty("switch_from").ValueKind.ShouldBe(JsonValueKind.Null);
			second.RootElement.GetProperty("ref").GetString().ShouldBe("opaque");
			second.RootElement.GetProperty("rate").GetString().ShouldBe("45");
		}
	}

	[Theory]
	[InlineData("")]
	[InlineData("\n")]
	[InlineData("n\n")]
	public async Task Declined_switch_returns_original_refusal_without_retry(string answer)
	{
		await using var server = await FakeServer.StartAsync(RunningRefusal("start"), 409);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, TextWriter.Null, error, answer, true, true);
		(await app.RunAsync(["start", "example", "work"], TestContext.Current.CancellationToken)).ShouldBe(3);
		server.Requests.Count.ShouldBe(1);
		error.ToString().ShouldContain("Running blocker");
	}

	[Fact]
	public async Task Json_never_prompts_even_on_two_terminals()
	{
		var refusal = RunningRefusal("start");
		await using var server = await FakeServer.StartAsync(refusal, 409);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, output, error, "yes\n", true, true);
		(await app.RunAsync(["start", "example", "work", "--json"], TestContext.Current.CancellationToken)).ShouldBe(3);
		server.Requests.Count.ShouldBe(1);
		output.ToString().Trim().ShouldBe(refusal);
		error.ToString().ShouldBeEmpty();
	}

	[Theory]
	[InlineData("resume", "2\n", "/v1/tasks/resume?task=bbbbbb", 0)]
	[InlineData("stop --submit", "1\n", "/v1/tasks/stop?submit=true&task=aaaaaa", 0)]
	[InlineData("resume", "\n", null, 3)]
	[InlineData("stop", "9\n", null, 3)]
	public async Task Ambiguity_offers_ordered_choices_and_retries_only_a_valid_selection(string command,
		string answer,
		string? retry,
		int exit)
	{
		var refusal = JsonSerializer.Serialize(Envelopes.Failure<TaskDto>(command.Split(' ')[0],
				new("invalid_transition", "Choose task", new(Tasks: ["aaaaaa", "bbbbbb"]))),
			GigtimeJsonContext.Default.TaskResponse);

		var count = 0;

		await using var server = await FakeServer.StartAsync(async context =>
		{
			context.Response.StatusCode = ++count == 1 ? 409 : 200;
			await context.Response.WriteAsync(count == 1 ? refusal : CommandCases.Response("resume"));
		});

		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, TextWriter.Null, error, answer, true, true);
		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(exit);
		server.Requests.Count.ShouldBe(retry is null ? 1 : 2);
		error.ToString().ShouldContain("1) aaaaaa");
		error.ToString().ShouldContain("2) bbbbbb");
		if (retry is not null) server.Requests.Last().Path.ShouldBe(retry);
	}

	[Theory]
	[InlineData("resume")]
	[InlineData("resume aaaaaa")]
	public async Task Bare_resume_and_resuming_the_running_task_do_not_offer_a_switch(string command)
	{
		await using var server = await FakeServer.StartAsync(RunningRefusal("resume"), 409);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, TextWriter.Null, error, "yes\n", true, true);
		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(3);
		server.Requests.Count.ShouldBe(1);
		error.ToString().ShouldNotContain("[y/N]");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Successful_warnings_use_stderr_only_in_human_mode(bool asJson)
	{
		var body = JsonSerializer.Serialize(Envelopes.Success("start", CommandCases.Task) with
		{
			Warnings = [new("repeated_ref", "Open task aaaaaa repeats the reference.")],
		},
			GigtimeJsonContext.Default.TaskResponse);

		await using var server = await FakeServer.StartAsync(body);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, output, error, "", false, false);
		string[] args = ["start", "example", "work"];
		if (asJson) args = [.. args, "--json"];
		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(0);

		if (asJson) error.ToString().ShouldBeEmpty();
		else error.ToString().ShouldContain("repeated_ref: Open task aaaaaa repeats the reference.");
	}

	[Fact]
	public async Task Resume_switch_retries_bodyless_once_and_preserves_a_racing_refusal()
	{
		await using var server = await FakeServer.StartAsync(RunningRefusal("resume"), 409);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, TextWriter.Null, error, "yes\nyes\n", true, true);
		(await app.RunAsync(["resume", "bbbbbb"], TestContext.Current.CancellationToken)).ShouldBe(3);
		var requests = server.Requests.ToArray();
		requests.Length.ShouldBe(2);
		requests[1].Path.ShouldBe("/v1/tasks/resume?task=bbbbbb&switch=true&switch_from=aaaaaa");
		requests[1].Body.ShouldBeEmpty();
		error.ToString().Split("[y/N]").Length.ShouldBe(2);
	}

	[Fact]
	public async Task Status_renders_running_then_paused_in_service_order_and_one_instance_heading()
	{
		var running = CommandCases.Task with
		{
			Id = "running-id",
			State = "running"
		};

		var recent = CommandCases.Task with
		{
			Id = "recent-id",
			State = "paused"
		};

		var older = CommandCases.Task with
		{
			Id = "older-id",
			State = "paused"
		};

		var body = JsonSerializer.Serialize(Envelopes.Success("status", new StatusDto(running, [recent, older], "dev")),
			GigtimeJsonContext.Default.StatusResponse);

		await using var server = await FakeServer.StartAsync(body);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var app = App(server, output, TextWriter.Null, "", false, false);
		(await app.RunAsync(["status"], TestContext.Current.CancellationToken)).ShouldBe(0);
		var text = output.ToString();
		text.IndexOf("running-id", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("recent-id", StringComparison.Ordinal));
		text.IndexOf("recent-id", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("older-id", StringComparison.Ordinal));
		text.Split("Instance: dev").Length.ShouldBe(2);
	}

	private static CliApplication App(FakeServer server,
		TextWriter output,
		TextWriter error,
		string answer,
		bool inputTerminal,
		bool outputTerminal)
	{
		var paths = server.Paths;

		return new(output,
			error,
			outputTerminal,
			true,
			() => paths,
			_ => throw new InvalidOperationException(),
			() => "/unused/gig",
			new FixedClock(CommandCases.Start),
			new StringReader(answer),
			inputTerminal);
	}

	private static string RunningRefusal(string command) => JsonSerializer.Serialize(Envelopes.Failure<TaskDto>(command,
			new("invalid_transition", "Running blocker", new("aaaaaa", "running", command))),
		GigtimeJsonContext.Default.TaskResponse);
}
