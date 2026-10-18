using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class LocalTimeRenderingTests
{
	[Theory]
	[InlineData("status")]
	[InlineData("show")]
	[InlineData("start")]
	[InlineData("stop")]
	[InlineData("pause")]
	[InlineData("log")]
	public async Task Each_task_command_uses_the_client_day_for_today_and_minute_precision(string command)
	{
		var task = TimedTask("America/New_York", "2026-03-03T00:30:45Z", "2026-03-03T01:15:59Z");
		var rendered = await RenderAsync(command, [task], "2026-03-03T02:00:00Z");
		rendered.ShouldContain("Times in America/New_York");
		rendered.Split("Times in ").Length.ShouldBe(2);
		rendered.ShouldContain("19:30");
		rendered.ShouldContain("20:15");
		rendered.ShouldNotContain("2026-");
		rendered.ShouldNotContain("+00:00");
		rendered.ShouldNotContain("19:30:45");
		rendered.ShouldContain("01:15:00");
	}

	[Theory]
	[InlineData("America/New_York", "2026-03-03T04:30:00Z", "2026-03-03T05:30:00Z", "2026-03-03T12:00:00Z", "2026-03-02 23:30", "00:30")]
	[InlineData("Europe/London", "2026-03-28T23:30:00Z", "2026-03-29T00:30:00Z", "2026-03-29T12:00:00Z", "2026-03-28 23:30", "00:30")]
	[InlineData("Europe/London", "2026-03-29T00:30:00Z", "2026-03-29T01:30:00Z", "2026-03-29T12:00:00Z", "00:30", "02:30")]
	[InlineData("America/New_York", "2026-03-08T06:30:00Z", "2026-03-08T07:30:00Z", "2026-03-08T12:00:00Z", "01:30", "03:30")]
	[InlineData("America/New_York",
		"2026-11-01T05:30:00Z",
		"2026-11-01T06:30:00Z",
		"2026-11-02T12:00:00Z",
		"2026-11-01 01:30",
		"2026-11-01 01:30")]
	public async Task Midnight_and_DST_convert_each_instant_with_the_offset_that_applied_then(
		string zone,
		string start,
		string end,
		string now,
		string expectedStart,
		string expectedEnd)
	{
		var rendered = await RenderAsync("show", [TimedTask(zone, start, end)], now);
		rendered.ShouldContain("Started: " + expectedStart + "  Stopped: " + expectedEnd + Environment.NewLine);
		rendered.ShouldNotContain("T0");
		rendered.ShouldNotContain("(UTC)");
	}

	[Fact]
	public async Task Log_keeps_newest_first_order_and_labels_each_contiguous_zone_block()
	{
		var first = TimedTask("America/New_York", "2026-06-02T10:00:00Z", "2026-06-02T11:00:00Z") with
		{
			Id = "first"
		};

		var second = TimedTask("Europe/London", "2026-06-02T08:00:00Z", "2026-06-02T09:00:00Z") with
		{
			Id = "second"
		};

		var third = TimedTask("America/New_York", "2026-06-01T10:00:00Z", "2026-06-01T11:00:00Z") with
		{
			Id = "third"
		};

		var rendered = await RenderAsync("log", [first, second, third], "2026-06-02T12:00:00Z");
		rendered.IndexOf("first", StringComparison.Ordinal).ShouldBeLessThan(rendered.IndexOf("second", StringComparison.Ordinal));
		rendered.IndexOf("second", StringComparison.Ordinal).ShouldBeLessThan(rendered.IndexOf("third", StringComparison.Ordinal));
		rendered.Split("Times in America/New_York").Length.ShouldBe(3);
		rendered.Split("Times in Europe/London").Length.ShouldBe(2);
		rendered.ShouldContain("06:00");
		rendered.ShouldContain("09:00");
		rendered.ShouldContain("2026-06-01 06:00");
	}

	[Fact]
	public async Task Open_task_and_running_interval_keep_distinct_markers()
	{
		var task = TimedTask("Europe/London", "2026-06-02T10:00:00Z", "2026-06-02T11:00:00Z") with
		{
			State = "running",
			StoppedAt = null,
			Intervals = [new(1, Parse("2026-06-02T10:00:00Z"), null)]
		};

		var rendered = await RenderAsync("status", [task], "2026-06-02T12:00:00Z");
		rendered.ShouldContain("Started: 11:00  Stopped: open");
		rendered.ShouldContain("running");
		(await RenderAsync("log", [task], "2026-06-02T12:00:00Z")).ShouldContain("open");
	}

	private static TaskDto TimedTask(string zone, string start, string end) => CommandCases.Task with
	{
		Timezone = zone,
		StartedAt = Parse(start),
		StoppedAt = Parse(end),
		Intervals = [new(1, Parse(start), Parse(end))]
	};

	private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

	private static async Task<string> RenderAsync(string command, TaskDto[] tasks, string now)
	{
		var response = command switch
		{
			"status" => JsonSerializer.Serialize(Envelopes.Success(command, new StatusDto(tasks[0])), GigtimeJsonContext.Default.StatusResponse),
			"log" => JsonSerializer.Serialize(Envelopes.Success(command, new TasksDto(tasks)), GigtimeJsonContext.Default.TasksResponse),
			_ => JsonSerializer.Serialize(Envelopes.Success(command, tasks[0]), GigtimeJsonContext.Default.TaskResponse),
		};

		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);

		var paths = server.Paths;

		var app = new CliApplication(output,
			error,
			false,
			false,
			() => paths,
			_ => throw new InvalidOperationException(),
			() => "/unused/gig",
			new FixedClock(Parse(now)));

		(await app.RunAsync(CommandCases.Args(command), TestContext.Current.CancellationToken)).ShouldBe(0);
		error.ToString().ShouldBeEmpty();

		return output.ToString();
	}
}
