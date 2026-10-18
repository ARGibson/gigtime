using System.Reflection;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class InstallScriptTests(PublishedFixture published)
{
	private static readonly string script = Path.GetFullPath(Path.Combine(typeof(InstallScriptTests)
			.Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == "RepositoryRoot")
			.Value!,
		"scripts/install.sh"));

	[Fact]
	public async Task Install_upgrade_noop_and_force_keep_stable_units_links_and_data()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var data = (await day.GigAsync("export")).Output;
		var sentinel = Path.Combine(day.Environment["XDG_DATA_HOME"], "sentinel");
		await File.WriteAllTextAsync(sentinel, "preserved", TestContext.Current.CancellationToken);
		var first = Pair(day, "aaaaaaa");
		var installed = await InstallAsync(day, first);
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);
		installed.Error.ShouldContain("not on PATH");
		installed.Output.ShouldContain("Health check passed");
		var root = Root(day);
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/aaaaaaa");
		new FileInfo(Path.Combine(Bin(day), "gig")).LinkTarget.ShouldBe(Path.Combine(root, "current/gig"));

		var linked = await CommandProcess.RunAsync(Path.Combine(Bin(day), "gig"),
			["status", "--json"],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(10));

		linked.ExitCode.ShouldBe(0, linked.Output + linked.Error);
		var units = Units(day);
		var service = units.Single(item => item.Key.EndsWith(".service", StringComparison.Ordinal)).Value;
		service.Content.ShouldContain(Path.Combine(root, "current/gigtimed"));

		var second = Pair(day, "bbbbbbb");
		var upgraded = await InstallAsync(day, second);
		upgraded.ExitCode.ShouldBe(0, upgraded.Output + upgraded.Error);
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/bbbbbbb");
		Units(day).OrderBy(item => item.Key).ShouldBe(units.OrderBy(item => item.Key));
		var manager = await ManagerLogAsync(day);
		manager.Split("--user try-restart gigtimed.service").Length.ShouldBe(3);
		manager.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldAllBe(line => line.StartsWith("--user ", StringComparison.Ordinal));
		var releaseTime = Directory.GetLastWriteTimeUtc(Path.Combine(root, "current"));
		var repeated = await InstallAsync(day, second);
		repeated.ExitCode.ShouldBe(0, repeated.Output + repeated.Error);
		repeated.Output.ShouldContain("nothing to do");
		(await ManagerLogAsync(day)).ShouldBe(manager);
		Directory.GetLastWriteTimeUtc(Path.Combine(root, "current")).ShouldBe(releaseTime);
		var folderTime = Directory.GetLastWriteTimeUtc(Path.Combine(root, "releases/bbbbbbb"));
		var forced = await InstallAsync(day, second, "--force");
		forced.ExitCode.ShouldBe(0, forced.Output + forced.Error);
		Directory.GetLastWriteTimeUtc(Path.Combine(root, "releases/bbbbbbb")).ShouldBe(folderTime);
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/bbbbbbb");
		Units(day).OrderBy(item => item.Key).ShouldBe(units.OrderBy(item => item.Key));
		(await ManagerLogAsync(day)).Split("--user try-restart gigtimed.service").Length.ShouldBe(4);
		(await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken)).ShouldBe("preserved");
		(await day.GigAsync("export")).Output.ShouldBe(data);
		Directory.GetDirectories(Path.Combine(root, "releases"), ".*").ShouldBeEmpty();
	}

	[Fact]
	public async Task Custom_paths_and_pruning_keep_current_previous_selection_and_newest_alternative()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var root = Path.Combine(day.Root, "install space %");
		var bin = Path.Combine(day.Root, "custom bin");
		day.Environment["PATH"] = bin + Path.PathSeparator + day.Environment["PATH"];
		var labels = new[] { "1000001", "1000002", "1000003", "1000004", "1000005" };

		for (var index = 0; index < labels.Length; index++)
		{
			var result = await InstallAsync(day, Pair(day, labels[index]), "--prefix", root, "--bin-dir", bin);
			result.ExitCode.ShouldBe(0, result.Output + result.Error);
			result.Error.ShouldBeEmpty();
			Directory.SetLastWriteTimeUtc(Path.Combine(root, "releases", labels[index]), new DateTime(2026, 3, 2, 0, index, 0, DateTimeKind.Utc));
		}

		Directory.GetDirectories(Path.Combine(root, "releases")).Select(Path.GetFileName).Order().ShouldBe(labels.Skip(2));
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/1000005");
		new FileInfo(Path.Combine(bin, "gig")).LinkTarget.ShouldBe(Path.Combine(root, "current/gig"));

		Units(day)
			.Single(item => item.Key.EndsWith(".service", StringComparison.Ordinal))
			.Value.Content.ShouldContain("install space %%/current/gigtimed");

		var rollback = await CommandProcess.RunAsync("ln",
			["-sfn", "releases/1000003", Path.Combine(root, "current")],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(10));

		rollback.ExitCode.ShouldBe(0, rollback.Error);

		var version = await CommandProcess.RunAsync(Path.Combine(bin, "gig"),
			["--version"],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(10));

		version.ExitCode.ShouldBe(0, version.Error);
		version.Output.Trim().ShouldBe("1.0.0+1000003");

		var installed = await InstallAsync(day, Pair(day, "1000006"), "--prefix", root, "--bin-dir", bin);
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);

		Directory
			.GetDirectories(Path.Combine(root, "releases"))
			.Select(Path.GetFileName)
			.Order()
			.ShouldBe(["1000003", "1000005", "1000006"]);

		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/1000006");
		File.Exists(Path.Combine(root, "releases/1000003/gig")).ShouldBeTrue();
	}

	[Fact]
	public async Task Published_commit_labels_use_seven_characters_and_changed_files_get_a_timestamp()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var source = Pair(day, "abcdef0123456789abcdef0123456789abcdef01");
		var first = await InstallAsync(day, source);
		first.ExitCode.ShouldBe(0, first.Output + first.Error);
		new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget.ShouldBe("releases/abcdef0");
		await File.WriteAllTextAsync(Path.Combine(source, "changed-file"), "different publish", TestContext.Current.CancellationToken);
		var changed = await InstallAsync(day, source);
		changed.ExitCode.ShouldBe(0, changed.Output + changed.Error);
		var target = new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget!;
		target.ShouldStartWith("releases/abcdef0-");
		target["releases/abcdef0-".Length..].Length.ShouldBe(14);
		File.Exists(Path.Combine(Root(day), "current/changed-file")).ShouldBeTrue();
		var repeated = await InstallAsync(day, source);
		repeated.ExitCode.ShouldBe(0, repeated.Output + repeated.Error);
		repeated.Output.ShouldContain($"Already current: {target}; nothing to do.");
		new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget.ShouldBe(target);
	}

	[Fact]
	public async Task Version_without_a_commit_suffix_gets_a_timestamp_label()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var installed = await InstallAsync(day, Pair(day, "development"));
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);
		var target = new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget!;
		target.ShouldStartWith("releases/version-");
		var timestamp = target["releases/version-".Length..];
		timestamp.Length.ShouldBe(14);
		timestamp.All(char.IsAsciiDigit).ShouldBeTrue();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Missing_pair_or_failed_version_check_leaves_the_working_install_untouched(bool missing)
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var good = await InstallAsync(day, Pair(day, "aaaaaaa"));
		good.ExitCode.ShouldBe(0, good.Output + good.Error);
		var units = Units(day);
		var manager = await ManagerLogAsync(day);
		var bad = Pair(day, "bbbbbbb", badVersion: !missing);
		if (missing) File.Delete(Path.Combine(bad, "gigtimed"));
		var failed = await InstallAsync(day, bad);
		failed.ExitCode.ShouldNotBe(0);
		failed.Error.ShouldContain(missing ? "requires executable" : "Version check failed");
		new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget.ShouldBe("releases/aaaaaaa");
		new FileInfo(Path.Combine(Bin(day), "gig")).LinkTarget.ShouldBe(Path.Combine(Root(day), "current/gig"));
		Units(day).OrderBy(item => item.Key).ShouldBe(units.OrderBy(item => item.Key));
		(await ManagerLogAsync(day)).ShouldBe(manager);
		Directory.GetDirectories(Path.Combine(Root(day), "releases")).Select(Path.GetFileName).ShouldBe(["aaaaaaa"]);
	}

	[Fact]
	public async Task Failed_current_swap_during_force_preserves_the_existing_release()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Install tests require Linux.");

		await using var day = new DaySession(published);
		await day.StartAsync();
		var source = Pair(day, "aaaaaaa");
		var good = await InstallAsync(day, source);
		good.ExitCode.ShouldBe(0, good.Output + good.Error);
		var marker = Path.Combine(Root(day), "current/old-marker");
		await File.WriteAllTextAsync(marker, "preserved", TestContext.Current.CancellationToken);
		var units = Units(day);
		var manager = await ManagerLogAsync(day);
		day.Environment["GIGTIME_TEST_FAIL_DEST"] = Path.Combine(Root(day), "current");
		var mv = Path.Combine(day.Root, "bin/mv");

		await File.WriteAllTextAsync(mv,
			"#!/bin/sh\nfor last do :; done\n"
			+ "if [ \"$last\" = \"$GIGTIME_TEST_FAIL_DEST\" ]; then exit 23; fi\nexec /usr/bin/mv \"$@\"\n",
			TestContext.Current.CancellationToken);

		File.SetUnixFileMode(mv, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		await File.WriteAllTextAsync(Path.Combine(source, "changed-file"), "different publish", TestContext.Current.CancellationToken);
		var failed = await InstallAsync(day, source, "--force");
		failed.ExitCode.ShouldBe(23, failed.Output + failed.Error);
		(await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken)).ShouldBe("preserved");
		new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget.ShouldBe("releases/aaaaaaa");
		Units(day).OrderBy(item => item.Key).ShouldBe(units.OrderBy(item => item.Key));
		(await ManagerLogAsync(day)).ShouldBe(manager);
		Directory.GetDirectories(Path.Combine(Root(day), "releases")).Select(Path.GetFileName).ShouldBe(["aaaaaaa"]);
		Directory.GetFileSystemEntries(Root(day), ".current-*").ShouldBeEmpty();
		Directory.GetFileSystemEntries(Bin(day), ".gig-*").ShouldBeEmpty();
	}

	[Fact]
	public async Task Failed_health_check_reports_failure_and_retains_the_new_current()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var good = await InstallAsync(day, Pair(day, "aaaaaaa"));
		good.ExitCode.ShouldBe(0, good.Output + good.Error);
		var units = Units(day);

		foreach (var label in new[] { "0000001", "0000002", "0000003" })
		{
			var old = Path.Combine(Root(day), "releases", label);
			Directory.CreateDirectory(old);
			Directory.SetLastWriteTimeUtc(old, new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));
		}

		var failed = await InstallAsync(day, Pair(day, "bbbbbbb", badHealth: true));
		failed.ExitCode.ShouldBe(6, failed.Output + failed.Error);
		failed.Error.ShouldContain("Health check failed; current remains bbbbbbb");
		Directory.GetDirectories(Path.Combine(Root(day), "releases")).Length.ShouldBe(3);
		new DirectoryInfo(Path.Combine(Root(day), "current")).LinkTarget.ShouldBe("releases/bbbbbbb");
		Units(day).OrderBy(item => item.Key).ShouldBe(units.OrderBy(item => item.Key));
		(await ManagerLogAsync(day)).ShouldContain("--user try-restart gigtimed.service");
		Directory.GetDirectories(Path.Combine(Root(day), "releases"), ".*").ShouldBeEmpty();
	}

	[Fact]
	public async Task Help_usage_and_data_directory_refusals_leave_no_installation()
	{
		await using var day = new DaySession(published);
		var help = await CommandProcess.RunAsync("bash", [script, "--help"], day.Root, day.Environment, TimeSpan.FromSeconds(10));
		help.ExitCode.ShouldBe(0);
		help.Output.ShouldContain("--from DIR");

		foreach (var args in new[] { new[] { "--unknown" }, new[] { "--prefix" } })
		{
			var invalid = await CommandProcess.RunAsync("bash", [script, .. args], day.Root, day.Environment, TimeSpan.FromSeconds(10));
			invalid.ExitCode.ShouldBe(2);
		}

		var refused = await InstallAsync(day, Pair(day, "aaaaaaa"), "--prefix", Path.Combine(day.Environment["XDG_DATA_HOME"], "gigtime"));
		refused.ExitCode.ShouldNotBe(0);
		refused.Error.ShouldContain("data directory");
		Directory.GetFileSystemEntries(day.Environment["XDG_DATA_HOME"]).ShouldBeEmpty();
		Directory.Exists(Root(day)).ShouldBeFalse();
	}

	[Fact]
	public async Task Named_instance_reuses_shared_binaries_and_sets_up_only_its_units()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		var source = Pair(day, "aaaaaaa");
		var installed = await InstallAsync(day, source);
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);
		var originalUnits = Units(day);
		var releaseTime = Directory.GetLastWriteTimeUtc(Path.Combine(Root(day), "current"));
		day.Environment["GIGTIME_INSTANCE"] = "dev";
		await day.RestartAsync();
		var named = await InstallAsync(day, source, "--instance", "dev");
		named.ExitCode.ShouldBe(0, named.Output + named.Error);
		named.Output.ShouldContain("Health check passed");
		Directory.GetLastWriteTimeUtc(Path.Combine(Root(day), "current")).ShouldBe(releaseTime);
		Directory.GetDirectories(Path.Combine(Root(day), "releases")).Select(Path.GetFileName).ShouldBe(["aaaaaaa"]);
		var units = Units(day);
		foreach (var (path, expected) in originalUnits) units[path].ShouldBe(expected);
		var service = Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd/user/gigtimed-dev.service");
		units[service].Content.ShouldContain("Environment=\"GIGTIME_INSTANCE=dev\"");
		units[service].Content.ShouldContain(Path.Combine(Root(day), "current/gigtimed"));

		units[Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd/user/gigtimed-dev.socket")]
			.Content
			.ShouldContain(Path.Combine(day.Environment["XDG_RUNTIME_DIR"], "gigtime-dev/gigtimed.sock"));

		(await ManagerLogAsync(day)).ShouldContain("--user try-restart gigtimed-dev.service");
	}

	[Theory]
	[InlineData("dev")]
	[InlineData("../invalid")]
	public async Task Install_without_instance_flag_ignores_inherited_instance(string inherited)
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		day.Environment["GIGTIME_INSTANCE"] = inherited;
		var installed = await InstallAsync(day, Pair(day, "aaaaaaa"));
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);
		installed.Output.ShouldContain("Health check passed");
		var units = Units(day);
		units.Keys.Select(Path.GetFileName).Order().ShouldBe(["gigtimed.service", "gigtimed.socket"]);
		units.Values.ShouldAllBe(unit => !unit.Content.Contains("GIGTIME_INSTANCE", StringComparison.Ordinal));
		(await ManagerLogAsync(day)).ShouldContain("--user try-restart gigtimed.service");
	}

	[Theory]
	[InlineData("--instance")]
	[InlineData("--instance", "")]
	[InlineData("--instance", "--force")]
	[InlineData("--instance", "Dev")]
	[InlineData("--instance", "dév")]
	[InlineData("--instance", "../dev")]
	[InlineData("--instance", "dev_name")]
	[InlineData("--instance", "1dev")]
	[InlineData("--instance", "abcdefghijklmnopqrstuvwxyzabcdefg")]
	[InlineData("--instance", "dev", "--unknown")]
	public async Task Invalid_instance_arguments_leave_the_sandbox_untouched(params string[] arguments)
	{
		await using var day = new DaySession(published);
		day.Environment["LC_ALL"] = "en_US.utf8";
		var before = Directory.GetFileSystemEntries(day.Root, "*", SearchOption.AllDirectories).Order().ToArray();
		var invalid = await CommandProcess.RunAsync("bash", [script, .. arguments], day.Root, day.Environment, TimeSpan.FromSeconds(10));
		invalid.ExitCode.ShouldBe(2, invalid.Output + invalid.Error);
		Directory.GetFileSystemEntries(day.Root, "*", SearchOption.AllDirectories).Order().ShouldBe(before);
	}

	private string Pair(DaySession day, string label, bool badVersion = false, bool badHealth = false)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Install tests require Linux.");

		var directory = Path.Combine(day.Root, "published-" + label);

		foreach (var file in Directory.EnumerateFiles(published.Application, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(directory, Path.GetRelativePath(published.Application, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}

		var gig = Path.Combine(directory, "gig");
		File.Move(gig, Path.Combine(directory, "gig.real"));

		File.WriteAllText(gig,
			"#!/usr/bin/env bash\nset -euo pipefail\nif [[ ${1-} == --version ]]; then "
			+ (badVersion ? "exit 23" : "printf '%s\\n' '1.0.0+" + label + "'") + "; exit 0; fi\n"
			+ (badHealth ? "if [[ ${1-} == setup && ${2-} == --check ]]; then exit 6; fi\n" : "")
			+ "exec \"$(dirname -- \"$(readlink -f -- \"$0\")\")/gig.real\" \"$@\"\n");

		File.SetUnixFileMode(gig, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		return directory;
	}

	private static Task<CommandResult> InstallAsync(DaySession day, string source, params string[] arguments) =>
		CommandProcess.RunAsync("bash", [script, "--from", source, .. arguments], day.Root, day.Environment, TimeSpan.FromSeconds(20));

	private static string Root(DaySession day) => Path.Combine(day.Environment["HOME"], ".local/lib/gigtime");
	private static string Bin(DaySession day) => Path.Combine(day.Environment["HOME"], ".local/bin");
	private static Task<string> ManagerLogAsync(DaySession day) => File.ReadAllTextAsync(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"]);

	private static Dictionary<string, (string Content, DateTime Modified)> Units(DaySession day) =>
		Directory
			.GetFiles(Path.Combine(day.Environment["XDG_CONFIG_HOME"], "systemd/user"))
			.ToDictionary(path => path, path => (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));
}
