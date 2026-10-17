using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class SetupCompletionTests(PublishedFixture published)
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Setup_installs_only_temporary_units_is_idempotent_and_check_never_writes(bool fallback)
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_TEST_NO_ACTIVATION"] = fallback ? "1" : "0";
		var check = await day.GigAsync("setup", "--check", "--json");
		check.ExitCode.ShouldBe(6);

		using (var json = JsonDocument.Parse(check.Output))
		{
			json.RootElement.GetProperty("data").GetProperty("service_installed").GetBoolean().ShouldBeFalse();
			json.RootElement.GetProperty("data").GetProperty("healthy").GetBoolean().ShouldBeFalse();
		}

		File.Exists(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"]).ShouldBeFalse();
		Directory.Exists(Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd")).ShouldBeFalse();
		var installed = await day.SuccessAsync("setup");
		installed.GetProperty("changed").GetBoolean().ShouldBeTrue();
		installed.GetProperty("socket_activation").GetBoolean().ShouldBe(!fallback);
		var service = installed.GetProperty("service_unit").GetString()!;
		var content = await File.ReadAllTextAsync(service, TestContext.Current.CancellationToken);
		content.ShouldContain("ExecStart=\"" + Path.Combine(published.Application, "gigtimed") + "\"");
		content.ShouldContain(day.Environment["XDG_RUNTIME_DIR"]);
		var written = File.GetLastWriteTimeUtc(service);
		var socket = installed.GetProperty("socket_unit").GetString()!;

		if (!fallback)
		{
			(await File.ReadAllTextAsync(socket, TestContext.Current.CancellationToken)).ShouldContain("ListenStream="
				+ installed.GetProperty("socket_path").GetString() + "\n");
		}
		else
		{
			File.Exists(socket).ShouldBeFalse();
		}

		var second = await day.SuccessAsync("setup");
		second.GetProperty("changed").GetBoolean().ShouldBeFalse();
		File.GetLastWriteTimeUtc(service).ShouldBe(written);
		var human = await day.GigAsync("setup");
		human.ExitCode.ShouldBe(0);
		human.Output.ShouldContain("unchanged");
		if (fallback) human.Output.ShouldContain("service starts at login");
		var commands = await File.ReadAllTextAsync(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"], TestContext.Current.CancellationToken);
		commands.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldAllBe(line => line.StartsWith("--user ", StringComparison.Ordinal));
		commands.ShouldContain(fallback ? "enable --now gigtimed.service" : "enable --now gigtimed.socket");
		await day.StartAsync();

		var before = Directory
			.GetFiles(Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd/user"))
			.ToDictionary(p => p, File.GetLastWriteTimeUtc);

		var log = await File.ReadAllTextAsync(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"], TestContext.Current.CancellationToken);
		var healthy = await day.SuccessAsync("setup", "--check");
		healthy.GetProperty("healthy").GetBoolean().ShouldBeTrue();
		healthy.GetProperty("socket_exists").GetBoolean().ShouldBeTrue();
		(await File.ReadAllTextAsync(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"], TestContext.Current.CancellationToken)).ShouldBe(log);
		foreach (var (path, stamp) in before) File.GetLastWriteTimeUtc(path).ShouldBe(stamp);
	}

	[Fact]
	public async Task Setup_missing_override_is_config_error_and_does_not_write_units()
	{
		await using var day = new DaySession(published);
		var missing = Path.Combine(day.Root, "missing-gigtimed");
		await day.RefusalAsync(5, "config", "setup", "--gigtimed", missing);
		var human = await day.GigAsync("setup", "--gigtimed", missing);
		human.ExitCode.ShouldBe(5);
		human.Error.ShouldContain(missing);
		Directory.Exists(Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd")).ShouldBeFalse();
		await day.SuccessAsync("setup", "--gigtimed", Path.Combine(published.Application, "gigtimed"));
	}

	[Fact]
	public async Task Live_completion_filters_scopes_and_published_latency_stays_below_fifty_milliseconds()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.SuccessAsync("client", "add", "globex", "--name", "Globex", "--currency", "JPY");
		await day.SuccessAsync("project", "add", "globex", "other");

		var id = (await day.SuccessAsync("add", "acme", "work", "--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T10:00:00Z"))
			.GetProperty("id")
			.GetString()!;

		(await day.GigAsync("__complete", "clients", "ac")).Output.ShouldBe("acme\n");
		(await day.GigAsync("__complete", "projects", "", "--client", "acme")).Output.ShouldBe("work\n");
		(await day.GigAsync("__complete", "tasks", id[..2])).Output.ShouldBe(id + "\n");
		(await day.GigAsync("__complete", "clients", "no-match")).Output.ShouldBeEmpty();
		(await day.GigAsync("__complete", "unknown")).Output.ShouldBeEmpty();
		var samples = new List<double>();

		for (var i = 0; i < 25; i++)
		{
			var elapsed = Stopwatch.StartNew();
			var result = await day.GigAsync("__complete", "clients", "ac");
			elapsed.Stop();
			result.ExitCode.ShouldBe(0);
			result.Output.ShouldBe("acme\n");
			result.Error.ShouldBeEmpty();
			samples.Add(elapsed.Elapsed.TotalMilliseconds);
		}

		var median = samples.Order().ElementAt(samples.Count / 2);

		TestContext.Current.TestOutputHelper!.WriteLine("Published __complete: 25 runs, median "
			+ median.ToString("F3", CultureInfo.InvariantCulture) + " ms");

		median.ShouldBeLessThan(50);
		await day.SuccessAsync("client", "archive", "globex");
		(await day.GigAsync("__complete", "clients", "glo")).Output.ShouldBeEmpty();
	}

	[Fact]
	public async Task Zsh_script_completes_commands_flags_and_live_keys_in_zsh()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var script = await day.GigAsync("completion", "zsh");
		script.ExitCode.ShouldBe(0);
		var path = Path.Combine(day.Root, "_gig");
		await File.WriteAllTextAsync(path, script.Output, TestContext.Current.CancellationToken);
		day.Environment["PATH"] = published.Application + Path.PathSeparator + day.Environment["PATH"];

		// #5: replace completion UI primitives, preserving the actual zsh function and live subprocesses.
		var driver = "compdef() { :; }; source \"$1\"; _describe() { print -rl -- $commands; }; "
			+ "compadd() { [[ $1 == -- ]] && shift; print -rl -- $@; }; "
			+ "_arguments() { if [[ $PREFIX == --* ]]; then print -rl -- $flags; else state=live; fi; }; "
			+ "words=(gig ''); CURRENT=2; PREFIX=''; _gig; "
			+ "words=(gig report --); CURRENT=3; PREFIX=--; _gig; "
			+ "words=(gig start ac); CURRENT=3; PREFIX=ac; _gig; "
			+ "words=(gig start acme wo); CURRENT=4; PREFIX=wo; _gig;";

		var result = await CommandProcess.RunAsync("zsh",
			["-f", "-c", driver, "zsh", path],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(10));

		result.ExitCode.ShouldBe(0, result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldContain("start:start a task");
		result.Output.ShouldContain("--range[");
		result.Output.ShouldContain("acme\n");
		result.Output.ShouldContain("work\n");
	}
}
