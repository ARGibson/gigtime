using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class HerdrTabStatusTests(PublishedFixture published, ITestOutputHelper output)
{
	private static readonly string script = Path.GetFullPath(Path.Combine(typeof(HerdrTabStatusTests)
		.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
		.Single(a => a.Key == "RepositoryRoot").Value!, "integrations/herdr/gig-tab-status"));

	[Fact]
	public async Task A_failing_command_still_emits_its_error_log()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var database = (await day.SuccessAsync("setup", "--check")).GetProperty("database_path").GetString()!;

		// #33: break only this sandbox's projection table to exercise the real exception logger.
		var broken = await CommandProcess.RunAsync("python3",
			[
				"-I",
				"-c",
				"import sqlite3, sys\nwith sqlite3.connect(sys.argv[1]) as db:\n\tdb.execute('DROP TABLE fi_doc_catalogueview')",
				database,
			],
			day.Root, day.Environment, TimeSpan.FromSeconds(5));

		broken.ExitCode.ShouldBe(0, broken.Error);
		await day.RefusalAsync(1, "internal", "status");
		var logs = await day.StopAsync();
		logs.ShouldContain("Command request failed with internal");
		logs.ShouldContain("fail: Gigtime.Server.Hosting.CommandExceptions");
	}

	[Fact]
	public async Task Real_status_shows_running_or_most_recently_paused_and_clears_when_stopped()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		day.Environment["PATH"] = published.Application + Path.PathSeparator + day.Environment["PATH"];
		await AssertLineAsync(day, "");
		var first = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		await AssertLineAsync(day, @"acme/work \d{2,}:\d{2}:\d{2}\n");
		await day.SuccessAsync("pause");
		await AssertLineAsync(day, @"acme/work paused \d{2,}:\d{2}:\d{2}\n");
		await day.SuccessAsync("project", "add", "acme", "other", "--name", "Other", "--rate", "20");
		var second = (await day.SuccessAsync("start", "acme", "other")).GetProperty("id").GetString()!;
		await day.SuccessAsync("pause");
		var third = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		await day.SuccessAsync("pause");
		await AssertPausedAsync(day, 2);
		await day.SuccessAsync("stop", third);
		await AssertPausedAsync(day, 1);
		await day.SuccessAsync("resume", first);
		await AssertLineAsync(day, @"acme/work \d{2,}:\d{2}:\d{2}\n");
		await day.SuccessAsync("stop", first);
		await day.SuccessAsync("stop", second);
		await AssertLineAsync(day, "");
	}

	[Fact]
	public async Task Selected_instance_is_read_instead_of_default_on_shared_XDG_roots()
	{
		await using var one = new DaySession(published);
		await using var two = new DaySession(published);
		foreach (var key in new[] { "XDG_DATA_HOME", "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME" })
			two.Environment[key] = one.Environment[key];
		two.Environment["GIGTIME_INSTANCE"] = "dev";
		await one.StartAsync();
		await two.StartAsync();
		await two.CatalogueAsync();
		await two.SuccessAsync("start", "acme", "work");
		one.Environment["PATH"] = published.Application + Path.PathSeparator + one.Environment["PATH"];
		two.Environment["PATH"] = published.Application + Path.PathSeparator + two.Environment["PATH"];
		await AssertLineAsync(one, "");
		await AssertLineAsync(two, @"acme/work \d{2,}:\d{2}:\d{2}\n");
	}

	[Fact]
	public async Task Running_duration_advances_and_paused_duration_stays_fixed()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		day.Environment["PATH"] = published.Application + Path.PathSeparator + day.Environment["PATH"];
		await day.SuccessAsync("start", "acme", "work");
		var initial = await RunAsync(day);
		initial.ExitCode.ShouldBe(0, initial.Error);
		var next = initial;
		var elapsed = Stopwatch.StartNew();

		while (next.Output == initial.Output && elapsed.Elapsed < TimeSpan.FromSeconds(5))
		{
			await Task.Delay(30, TestContext.Current.CancellationToken);
			next = await RunAsync(day);
			next.ExitCode.ShouldBe(0, next.Error);
		}

		next.Output.ShouldNotBe(initial.Output);
		next.Output.ShouldMatch(@"\Aacme/work \d{2,}:\d{2}:\d{2}\n\z");
		output.WriteLine("running advanced: " + JsonSerializer.Serialize(initial.Output) + " -> " + JsonSerializer.Serialize(next.Output));
		await day.SuccessAsync("pause");
		var paused = await RunAsync(day);
		paused.ExitCode.ShouldBe(0, paused.Error);
		paused.Output.ShouldMatch(@"\Aacme/work paused \d{2,}:\d{2}:\d{2}\n\z");
		elapsed.Restart();

		while (elapsed.Elapsed < TimeSpan.FromSeconds(1.2))
		{
			var unchanged = await RunAsync(day);
			unchanged.ExitCode.ShouldBe(0, unchanged.Error);
			unchanged.Output.ShouldBe(paused.Output);
			await Task.Delay(30, TestContext.Current.CancellationToken);
		}

		output.WriteLine("paused stayed fixed: " + JsonSerializer.Serialize(paused.Output));
	}

	[Fact]
	public async Task Repeated_status_reads_are_quiet_during_polling_despite_startup_URL_warning()
	{
		await using var day = new DaySession(published);
		day.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "Information";
		day.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:1";
		await day.StartAsync();
		await Task.Delay(100, TestContext.Current.CancellationToken);
		day.Logs.ShouldContain("Overriding address");
		var before = day.Logs.Length;

		for (var i = 0; i < 20; i++)
		{
			var poll = await RunAsync(day);
			poll.ExitCode.ShouldBe(0, poll.Error);
			poll.Error.ShouldBeEmpty();
		}

		await Task.Delay(100, TestContext.Current.CancellationToken);
		day.Logs[before..].ShouldNotContain("Microsoft.AspNetCore");
		await day.StopAsync();
	}

	[Fact]
	public async Task Backfill_amend_reassign_and_key_changes_reach_the_line_unchanged()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var added = await day.SuccessAsync("add", "acme", "work",
			"--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T10:00:00Z");
		await AssertLineAsync(day, "");
		var id = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		await day.SuccessAsync("pause");
		await day.SuccessAsync("amend", id, "--interval", "1",
			"--start", "2026-03-02T09:00:00Z", "--end", "2026-03-02T10:01:01Z");
		await AssertLineAsync(day, "acme/work paused 01:01:01\n");
		await day.SuccessAsync("client", "add", "other", "--name", "Other", "--currency", "GBP");
		await day.SuccessAsync("project", "add", "other", "else", "--rate", "20");
		await day.SuccessAsync("amend", id, "--client", "other", "--project", "else");
		await AssertLineAsync(day, "other/else paused 01:01:01\n");
		await day.SuccessAsync("client", "set", "other", "--key", "renamed");
		await day.SuccessAsync("project", "set", "renamed", "else", "--key", "new");
		await AssertLineAsync(day, "renamed/new paused 01:01:01\n");
		await day.RestartAsync();
		await AssertLineAsync(day, "renamed/new paused 01:01:01\n");
		await day.StopAsync();
		AssertFailure(await RunAsync(day));
		await day.StartAsync();
		await AssertLineAsync(day, "renamed/new paused 01:01:01\n");
		added.GetProperty("raw_seconds").GetInt64().ShouldBe(3600);
	}

	[Fact]
	public async Task Many_paused_tasks_with_long_real_interval_histories_fit_the_resource_cap()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();

		for (var task = 0; task < 8; task++)
		{
			var id = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
			await day.SuccessAsync("pause");

			for (var interval = 0; interval < 32; interval++)
			{
				var from = DateTimeOffset.Parse("2026-03-02T09:00:00Z", CultureInfo.InvariantCulture).AddMinutes(interval * 2);
				await day.SuccessAsync("add",
					id,
					"--from",
					from.ToString("O", CultureInfo.InvariantCulture),
					"--to",
					from.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture));
			}
		}

		var result = await day.GigAsync("status", "--json");
		result.ExitCode.ShouldBe(0, result.Error);
		using var json = JsonDocument.Parse(result.Output);
		var paused = json.RootElement.GetProperty("data").GetProperty("paused");
		paused.GetArrayLength().ShouldBe(8);
		foreach (var task in paused.EnumerateArray()) task.GetProperty("intervals").GetArrayLength().ShouldBeGreaterThanOrEqualTo(32);
		var bytes = System.Text.Encoding.UTF8.GetByteCount(result.Output.TrimEnd('\n'));
		bytes.ShouldBeLessThan(1048576 / 4);
		output.WriteLine($"Large real status: {bytes} UTF-8 bytes; 8 paused tasks, at least 32 intervals each; cap 1048576.");
		await AssertPausedAsync(day, 7);
	}

	[Fact]
	public async Task A_crash_clears_the_line_and_restart_reads_persisted_state()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.SuccessAsync("start", "acme", "work");
		await day.SuccessAsync("pause");
		var before = await RunAsync(day);
		before.ExitCode.ShouldBe(0, before.Error);
		await day.CrashAsync();

		AssertFailure(await RunAsync(day));
		var socket = Path.Combine(day.Environment["XDG_RUNTIME_DIR"], "gigtime/gigtimed.sock");
		File.Exists(socket).ShouldBeTrue();
		// #33: the sandbox owner cleans up after SIGKILL; the polling reader never repairs the socket.
		File.Delete(socket);
		await day.StartAsync();
		var restarted = await RunAsync(day);
		restarted.ExitCode.ShouldBe(0, restarted.Error);
		restarted.Output.ShouldBe(before.Output);
	}

	private async Task AssertPausedAsync(DaySession day, int others)
	{
		// #33: pauses can share a second; use the contract's first entry, including its tie-break order.
		var paused = (await day.SuccessAsync("status")).GetProperty("paused");
		paused.GetArrayLength().ShouldBe(others + 1);
		var first = paused[0];
		var client = first.GetProperty("client").GetProperty("key").GetString();
		var project = first.GetProperty("project").GetProperty("key").GetString();
		var seconds = first.GetProperty("raw_seconds").GetInt64();
		var duration = $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";
		await AssertLineAsync(day, Regex.Escape($"{client}/{project} paused {duration} +{others}\n"));
	}

	private async Task AssertLineAsync(DaySession day, string pattern)
	{
		var result = await RunAsync(day);
		result.ExitCode.ShouldBe(0, result.Error);
		result.Error.ShouldBeEmpty();
		if (pattern.Length == 0) result.Output.ShouldBeEmpty();
		else result.Output.ShouldMatch("\\A" + pattern + "\\z");
		output.WriteLine("status: " + JsonSerializer.Serialize(result.Output));
	}

	private static Task<CommandResult> RunAsync(DaySession day) => CommandProcess.RunAsync("python3",
		["-I", script], day.Root, day.Environment, TimeSpan.FromSeconds(5));

	private static void AssertFailure(CommandResult result)
	{
		result.ExitCode.ShouldNotBe(0);
		result.Output.ShouldBeEmpty();
		result.Error.ShouldEndWith("\n");
		result.Error.Split('\n').Length.ShouldBe(2);
		result.Error.ShouldStartWith("gig-tab-status: ");
		result.Error.ShouldNotContain("Traceback");
	}
}
