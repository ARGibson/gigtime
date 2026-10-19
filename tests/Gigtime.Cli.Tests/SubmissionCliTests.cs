using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class SubmissionCliTests
{
	[Theory]
	[InlineData("stop --submit", "/v1/tasks/stop?submit=true")]
	[InlineData("submit 01JTASK", "/v1/tasks/01JTASK/submit")]
	[InlineData("unsubmit 01JTASK", "/v1/tasks/01JTASK/unsubmit")]
	public async Task Submission_commands_send_bodyless_posts(string command, string path)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("submit"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		var request = server.Requests.ShouldHaveSingleItem();
		request.Method.ShouldBe("POST");
		request.Path.ShouldBe(path);
		request.Body.ShouldBeEmpty();
	}

	[Theory]
	[InlineData("start example work --rate 42.50")]
	[InlineData("add example work --from 09:00 --to 10:00 --rate 42.50")]
	[InlineData("amend 01JTASK --rate 42.50")]
	public async Task Own_rate_is_sent_without_converting_the_decimal_string(string command)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("start"));
		var paths = server.Paths;
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		using var body = JsonDocument.Parse(server.Requests.ShouldHaveSingleItem().Body);
		body.RootElement.GetProperty("rate").GetString().ShouldBe("42.50");
	}

	[Fact]
	public async Task Amend_can_clear_only_the_own_rate()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("amend"));
		var paths = server.Paths;
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["amend", "01JTASK", "--clear-rate"], TestContext.Current.CancellationToken)).ShouldBe(0);
		using var body = JsonDocument.Parse(server.Requests.ShouldHaveSingleItem().Body);
		body.RootElement.GetProperty("rate").ValueKind.ShouldBe(JsonValueKind.Null);
		body.RootElement.GetProperty("clear_rate").GetBoolean().ShouldBeTrue();
	}

	[Theory]
	[InlineData("submit")]
	[InlineData("unsubmit")]
	[InlineData("start example work --rate")]
	[InlineData("add example work --from 09:00 --to 10:00 --rate")]
	[InlineData("amend 01JTASK --rate 42 --clear-rate")]
	[InlineData("stop 01JTASK")]
	[InlineData("pause 01JTASK")]
	[InlineData("resume 01JTASK")]
	public async Task Invalid_submission_arguments_refuse_before_resolving_paths(string command)
	{
		var app = new CliApplication(TextWriter.Null,
			TextWriter.Null,
			false,
			false,
			() => throw new InvalidOperationException("Resolved paths for bad arguments."));

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(2);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Submitted_amend_refusal_preserves_the_real_unsubmit_hint(bool asJson)
	{
		var response = JsonSerializer.Serialize(Envelopes.Failure<TaskDto>("amend",
				new("invalid_transition", "Task is submitted.", new("a1b2c3", "submitted", "amend"), "gig unsubmit a1b2c3")),
			GigtimeJsonContext.Default.TaskResponse);

		await using var server = await FakeServer.StartAsync(response, 409);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		string[] args = ["amend", "a1b2c3", "--rate", "42"];
		if (asJson) args = [.. args, "--json"];

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(3);
		(asJson ? output.ToString() : error.ToString()).ShouldContain("gig unsubmit a1b2c3");
	}

	[Fact]
	public async Task Human_submission_shows_the_fixed_amount_and_own_rate_without_a_submit_prompt()
	{
		var task = CommandCases.Task with
		{
			State = "submitted",
			Rate = new("30.00", "GBP"),
			Submission = new(CommandCases.Start.AddHours(2),
				4500,
				"nearest-minute",
				new("30.00", "GBP"),
				"GBP",
				new("GBP", "37.50", "37.50")),
		};

		var response = JsonSerializer.Serialize(Envelopes.Success("stop", task), GigtimeJsonContext.Default.TaskResponse);
		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["stop", "--submit"], TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain("submitted");
		output.ToString().ShouldContain("Own rate: 30.00 GBP");
		output.ToString().ShouldContain("Submission amount: 37.50 GBP");
		output.ToString().ShouldContain("Submitted:");
		output.ToString().ShouldNotContain("Submit the rounded figure.");
	}

	[Theory]
	[InlineData("show")]
	[InlineData("log")]
	public async Task Show_and_log_mark_submitted_tasks(string command)
	{
		var task = CommandCases.Task with
		{
			State = "submitted",
			Submission = new(CommandCases.Start.AddHours(2), 4500, "nearest-minute", null, "GBP", null),
		};

		var response = command == "log"
			? JsonSerializer.Serialize(Envelopes.Success("log", new TasksDto([task])), GigtimeJsonContext.Default.TasksResponse)
			: JsonSerializer.Serialize(Envelopes.Success("show", task), GigtimeJsonContext.Default.TaskResponse);

		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);
		string[] args = command == "log" ? ["log"] : ["show", "01JTASK"];

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain("submitted");
	}

	[Theory]
	[InlineData("stopped")]
	[InlineData("submitted")]
	public async Task Task_completion_requests_the_state_filter(string state)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("log"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, TextWriter.Null, false, false, () => paths);

		(await app.RunAsync(["__complete", "tasks", "01", "--state", state], TestContext.Current.CancellationToken)).ShouldBe(0);
		server.Requests.ShouldHaveSingleItem().Path.ShouldBe("/v1/tasks?last=20&state=" + state);
		output.ToString().Trim().ShouldBe("01JTASK");
	}
}
