using System.Diagnostics;
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
	public async Task Unavailable_sandbox_daemon_returns_one_error_and_no_status()
	{
		await using var day = new DaySession(published);
		day.Environment["PATH"] = published.Application + Path.PathSeparator + day.Environment["PATH"];
		AssertFailure(await RunAsync(day));
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

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Unrounded_seconds_win_when_the_rounded_display_disagrees(bool paused)
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		await WriteExecutableAsync(Path.Combine(bin, "gig"), PrintStatus("acme", "3661", paused));
		var result = await RunAsync(day);
		result.ExitCode.ShouldBe(0, result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldBe(paused ? "acme/work paused 01:01:01\n" : "acme/work 01:01:01\n");
	}

	[Theory]
	[InlineData("0", "00:00:00")]
	[InlineData("59", "00:00:59")]
	[InlineData("3599", "00:59:59")]
	[InlineData("3600", "01:00:00")]
	[InlineData("360000", "100:00:00")]
	public async Task Duration_formats_second_minute_and_hour_boundaries(string seconds, string expected)
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		await WriteExecutableAsync(Path.Combine(bin, "gig"), PrintStatus("acme", seconds));
		var result = await RunAsync(day);
		result.ExitCode.ShouldBe(0, result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldBe("acme/work " + expected + "\n");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("\"3661\"")]
	[InlineData("true")]
	[InlineData("1.5")]
	[InlineData("-1")]
	public async Task Missing_or_invalid_raw_seconds_are_an_error(string? seconds)
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		await WriteExecutableAsync(Path.Combine(bin, "gig"), PrintStatus("acme", seconds));
		AssertFailure(await RunAsync(day));
	}

	[Theory]
	[InlineData("exit 7")]
	[InlineData("printf '%s' 'not json'")]
	[InlineData("printf '%s' '{\"ok\":false}'")]
	[InlineData("printf '%s' '{\"ok\":true}'")]
	[InlineData("printf '%s' '{\"ok\":true,\"data\":{\"running\":null}}'")]
	[InlineData("printf '%s' '{\"ok\":true,\"data\":{\"running\":{},\"paused\":[]}}'")]
	[InlineData("printf '%s' '{\"ok\":true,\"data\":{\"running\":null,\"paused\":[{}]}}'")]
	[InlineData("printf '%s' '{\"ok\":true,\"data\":{\"running\":null,\"paused\":{}}}'")]
	[InlineData("printf '%s' 'null'")]
	[InlineData("exec /bin/sleep 3")]
	public async Task Failed_or_unusable_status_has_no_stdout_and_no_traceback(string body)
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		await WriteExecutableAsync(Path.Combine(bin, "gig"), body);
		AssertFailure(await RunAsync(day));
	}

	[Fact]
	public async Task Missing_gig_is_an_error_and_home_install_is_the_fallback()
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		AssertFailure(await RunAsync(day));
		var fallback = Path.Combine(day.Environment["HOME"], ".local/bin/gig");
		Directory.CreateDirectory(Path.GetDirectoryName(fallback)!);
		await WriteExecutableAsync(fallback, PrintStatus("home"));
		var installed = await RunAsync(day);
		installed.ExitCode.ShouldBe(0, installed.Error);
		installed.Error.ShouldBeEmpty();
		installed.Output.ShouldBe("home/work 123:45:06\n");
		await WriteExecutableAsync(Path.Combine(bin, "gig"), PrintStatus("path"));
		var preferred = await RunAsync(day);
		preferred.ExitCode.ShouldBe(0, preferred.Error);
		preferred.Error.ShouldBeEmpty();
		preferred.Output.ShouldBe("path/work 123:45:06\n");
	}

	[Fact]
	public async Task Executable_ignores_Python_modules_in_the_active_pane_directory_and_environment()
	{
		await using var day = new DaySession(published);
		var bin = IsolatedPath(day);
		await WriteExecutableAsync(Path.Combine(bin, "gig"), PrintStatus("acme"));
		var marker = Path.Combine(day.Root, "imported");
		var planted = "open(" + JsonSerializer.Serialize(marker) + ", 'w').write('imported')\n";
		await File.WriteAllTextAsync(Path.Combine(day.Root, "json.py"), planted, TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(day.Root, "sitecustomize.py"), planted, TestContext.Current.CancellationToken);
		day.Environment["PYTHONPATH"] = day.Root;
		day.Environment["PYTHONHOME"] = Path.Combine(day.Root, "invalid-python-home");
		var result = await CommandProcess.RunAsync(script, [], day.Root, day.Environment, TimeSpan.FromSeconds(5));
		result.ExitCode.ShouldBe(0, result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldBe("acme/work 123:45:06\n");
		File.Exists(marker).ShouldBeFalse();
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
		result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
		result.Error.ShouldStartWith("gig-tab-status: ");
		result.Error.ShouldNotContain("Traceback");
	}

	private static string IsolatedPath(DaySession day)
	{
		var bin = Path.Combine(day.Root, "status-bin");
		Directory.CreateDirectory(bin);
		File.CreateSymbolicLink(Path.Combine(bin, "python3"), "/usr/bin/python3");
		day.Environment["PATH"] = bin;

		return bin;
	}

	private static string PrintStatus(string client, string? seconds = "445506", bool paused = false)
	{
		var task = new Dictionary<string, object>
		{
			["client"] = new { key = client },
			["project"] = new { key = "work" },
			["display"] = "01:02:00",
		};

		if (seconds is not null) task["raw_seconds"] = JsonSerializer.Deserialize<JsonElement>(seconds);

		var envelope = new { ok = true, data = new { running = paused ? null : task, paused = paused ? new[] { task } : [] } };

		return "printf '%s' '" + JsonSerializer.Serialize(envelope) + "'";
	}

	private static async Task WriteExecutableAsync(string path, string body)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("End-to-end tests require Linux.");

		await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body + "\n", TestContext.Current.CancellationToken);
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}
}
