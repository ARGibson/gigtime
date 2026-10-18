using System.Diagnostics;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class InstallActivationTests(PublishedFixture published, ITestOutputHelper output)
{
	[Theory]
	[InlineData(null)]
	[InlineData("dev")]
	public async Task Install_and_upgrade_wait_for_a_cold_socket_activated_daemon(string? instance)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Socket activation requires Linux.");

		await using var day = new DaySession(published);
		if (instance is not null) day.Environment["GIGTIME_INSTANCE"] = instance;
		var instanceArguments = instance is null ? Array.Empty<string>() : ["--instance", instance];
		var service = instance is null ? "gigtimed.service" : $"gigtimed-{instance}.service";
		var root = Path.Combine(day.Environment["HOME"], ".local/lib/gigtime");
		day.Environment["GIGTIME_TEST_INSTALL_ROOT"] = root;
		// #22: restart the executable reached through current, rather than leaving a warm fixture daemon alive.
		var manager = Path.Combine(day.Root, "bin/systemctl");

		await File.WriteAllTextAsync(manager,
			"""
			#!/usr/bin/env bash
			set -euo pipefail
			printf '%s\n' "$*" >> "$GIGTIME_TEST_SYSTEMCTL_LOG"
			pidfile=$HOME/activation.pid
			socket=$XDG_RUNTIME_DIR/gigtime${GIGTIME_INSTANCE:+-$GIGTIME_INSTANCE}/gigtimed.sock
			if [[ ${2-} != try-restart && ${2-} != stop ]]; then exit 0; fi
			if [[ -f $pidfile ]]; then
				kill -KILL -- "-$(cat "$pidfile")" 2>/dev/null || true
				rm -f -- "$pidfile"
			fi
			rm -f -- "$socket"
			if [[ ${2-} == stop ]]; then exit 0; fi
			mkdir -p -- "$(dirname -- "$socket")"
			chmod 700 -- "$(dirname -- "$socket")"
			setsid bash -c '
				cd -- "$1"
				exec systemd-socket-activate -E HOME -E TMPDIR -E XDG_DATA_HOME -E XDG_CONFIG_HOME -E XDG_RUNTIME_DIR \
					${GIGTIME_INSTANCE:+-E GIGTIME_INSTANCE} -l "$2" ./gigtimed
			' bash \
				"$GIGTIME_TEST_INSTALL_ROOT/current" "$socket" >"$HOME/activation.log" 2>&1 </dev/null &
			printf '%s\n' "$!" > "$pidfile"
			for ((i=0; i<100; i++)); do
				[[ ! -S $socket ]] || exit 0
				sleep 0.01
			done
			exit 1
			""",
			TestContext.Current.CancellationToken);

		File.SetUnixFileMode(manager, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		var repository = typeof(InstallActivationTests)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == "RepositoryRoot")
			.Value!;

		var script = Path.Combine(repository, "scripts/install.sh");

		try
		{
			var installed = await CommandProcess.RunAsync("bash",
				[script, "--from", published.Application, .. instanceArguments],
				day.Root,
				day.Environment,
				TimeSpan.FromSeconds(20));

			installed.ExitCode.ShouldBe(0,
				installed.Output + installed.Error
				+ await File.ReadAllTextAsync(Path.Combine(day.Environment["HOME"], "activation.log"),
					TestContext.Current.CancellationToken));

			await day.CatalogueAsync();
			var before = (await day.GigAsync("export")).Output;

			var units = Directory
				.GetFiles(Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd/user"))
				.ToDictionary(path => path, path => (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));

			var firstPid = await File.ReadAllTextAsync(Path.Combine(day.Environment["HOME"], "activation.pid"),
				TestContext.Current.CancellationToken);

			var upgraded = await CommandProcess.RunAsync("bash",
				[script, "--from", published.Application, "--force", .. instanceArguments],
				day.Root,
				day.Environment,
				TimeSpan.FromSeconds(20));

			upgraded.ExitCode.ShouldBe(0, upgraded.Output + upgraded.Error);
			upgraded.Output.ShouldContain("Health check passed");

			(await File.ReadAllTextAsync(Path.Combine(day.Environment["HOME"], "activation.pid"), TestContext.Current.CancellationToken))
				.ShouldNotBe(firstPid);

			foreach (var (path, expected) in units) (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)).ShouldBe(expected);
			(await day.GigAsync("export")).Output.ShouldBe(before);

			// #22: ordinary commands retain their ten-second timeout through another cold activation.
			var restarted = await CommandProcess.RunAsync(manager,
				["--user", "try-restart", service],
				day.Root,
				day.Environment,
				TimeSpan.FromSeconds(10));

			restarted.ExitCode.ShouldBe(0, restarted.Error);
			var elapsed = Stopwatch.StartNew();
			var coldStatus = await day.GigAsync("status", "--json");
			coldStatus.ExitCode.ShouldBe(0, coldStatus.Output + coldStatus.Error);
			output.WriteLine($"Cold socket-activated status completed in {elapsed.Elapsed.TotalMilliseconds:F0} ms.");
		}
		finally
		{
			await CommandProcess.RunAsync(manager,
				["--user", "stop", service],
				day.Root,
				day.Environment,
				TimeSpan.FromSeconds(10));
		}
	}
}
