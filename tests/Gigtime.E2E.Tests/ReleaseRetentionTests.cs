using System.Reflection;
using System.Security.Cryptography;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class ReleaseRetentionTests(PublishedFixture published)
{
	[Theory]
	[InlineData("executable")]
	[InlineData("working-directory")]
	[InlineData("dotnet")]
	[InlineData("dotnet-release")]
	[InlineData("dotnet-relative")]
	[InlineData("dotnet-current")]
	public async Task Upgrades_and_rollbacks_preserve_another_instances_running_release(string location)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Release retention requires Linux procfs.");

		await using var day = new DaySession(published);
		await day.StartAsync();
		var source = Pair(day, "aaaaaaa");
		var installed = await InstallAsync(day, source);
		installed.ExitCode.ShouldBe(0, installed.Output + installed.Error);
		var root = Path.Combine(day.Environment["HOME"], ".local/lib/gigtime");
		var release = Path.Combine(root, "releases/aaaaaaa");
		await using var other = new DaySession(published);
		other.Environment.Clear();
		foreach (var (key, value) in day.Environment) other.Environment[key] = value;
		other.Environment["GIGTIME_INSTANCE"] = "other";

		if (location.StartsWith("dotnet", StringComparison.Ordinal))
		{
			var dll = location switch
			{
				"dotnet" => Path.Combine(published.Application, "gigtimed.dll"),
				"dotnet-current" => Path.Combine(root, "current/gigtimed.dll"),
				"dotnet-relative" => Path.GetRelativePath(day.Root, Path.Combine(release, "gigtimed.dll")),
				_ => Path.Combine(release, "gigtimed.dll"),
			};

			await other.StartAsync("dotnet", location == "dotnet" ? release : day.Root, dll);
		}
		else
		{
			await other.StartAsync(
				Path.Combine(location == "executable" ? release : published.Application, "gigtimed"),
				location == "executable" ? day.Root : release);
		}

		var processRoot = $"/proc/{other.ServerProcessId}";
		var executable = File.ResolveLinkTarget(Path.Combine(processRoot, "exe"), true)!.FullName;
		var workingDirectory = File.ResolveLinkTarget(Path.Combine(processRoot, "cwd"), true)!.FullName;

		if (location == "executable")
		{
			executable.ShouldBe(Path.Combine(release, "gigtimed"));
			workingDirectory.ShouldBe(day.Root);
		}
		else
		{
			executable.ShouldNotStartWith(release + Path.DirectorySeparatorChar);
			workingDirectory.ShouldBe(location is "dotnet" or "working-directory" ? release : day.Root);
		}

		await other.CatalogueAsync();
		await other.SuccessAsync("start", "acme", "work");
		var data = (await other.GigAsync("export")).Output;
		var originalFiles = Files(release);
		var originalInode = await InodeAsync(day, Path.Combine(release, "gigtimed"));
		var latestSource = "";

		foreach (var label in new[] { "bbbbbbb", "ccccccc", "ddddddd" })
		{
			latestSource = Pair(day, label);
			var upgraded = await InstallAsync(day, latestSource);
			upgraded.ExitCode.ShouldBe(0, upgraded.Output + upgraded.Error);
			Directory.Exists(release).ShouldBeTrue();
			Files(release).OrderBy(item => item.Key).ShouldBe(originalFiles.OrderBy(item => item.Key));
			(await other.GigAsync("export")).Output.ShouldBe(data);
		}

		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/ddddddd");
		var link = Path.Combine(day.Environment["HOME"], ".local/bin/gig");
		var binTarget = new FileInfo(link).LinkTarget;

		foreach (var force in new[] { false, true })
		{
			var rollback = await InstallAsync(day, source, force ? ["--force"] : []);
			rollback.ExitCode.ShouldBe(0, rollback.Output + rollback.Error);
			new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe("releases/aaaaaaa");
			new FileInfo(link).LinkTarget.ShouldBe(binTarget);
			Files(release).OrderBy(item => item.Key).ShouldBe(originalFiles.OrderBy(item => item.Key));
			(await InodeAsync(day, Path.Combine(release, "gigtimed"))).ShouldBe(originalInode);
			(await other.GigAsync("export")).Output.ShouldBe(data);
			var upgrade = await InstallAsync(day, latestSource);
			upgrade.ExitCode.ShouldBe(0, upgrade.Output + upgrade.Error);
		}

		await File.WriteAllTextAsync(Path.Combine(source, "changed-file"), "changed publish", TestContext.Current.CancellationToken);
		var changed = await InstallAsync(day, source, "--force");
		changed.ExitCode.ShouldBe(0, changed.Output + changed.Error);
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldStartWith("releases/aaaaaaa-");
		File.Exists(Path.Combine(root, "current/changed-file")).ShouldBeTrue();
		Files(release).OrderBy(item => item.Key).ShouldBe(originalFiles.OrderBy(item => item.Key));
		(await InodeAsync(day, Path.Combine(release, "gigtimed"))).ShouldBe(originalInode);
		(await other.GigAsync("export")).Output.ShouldBe(data);
		Directory.GetDirectories(Path.Combine(root, "releases"), ".*").ShouldBeEmpty();
	}

	private string Pair(DaySession day, string label)
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
			"#!/usr/bin/env bash\nset -euo pipefail\n"
			+ "if [[ ${1-} == --version ]]; then printf '%s\\n' '1.0.0+" + label + "'; exit 0; fi\n"
			+ "exec \"$(dirname -- \"$(readlink -f -- \"$0\")\")/gig.real\" \"$@\"\n");

		File.SetUnixFileMode(gig, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		return directory;
	}

	private static Task<CommandResult> InstallAsync(DaySession day, string source, params string[] arguments)
	{
		var repository = typeof(ReleaseRetentionTests)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(attribute => attribute.Key == "RepositoryRoot")
			.Value!;

		return CommandProcess.RunAsync("bash",
			[Path.Combine(repository, "scripts/install.sh"), "--from", source, .. arguments],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(20));
	}

	private static async Task<string> InodeAsync(DaySession day, string path)
	{
		var result = await CommandProcess.RunAsync("stat", ["-c", "%i", path], day.Root, day.Environment, TimeSpan.FromSeconds(10));
		result.ExitCode.ShouldBe(0, result.Error);

		return result.Output;
	}

	private static Dictionary<string, string> Files(string directory)
	{
		return Directory
			.GetFiles(directory, "*", SearchOption.AllDirectories)
			.ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
	}
}
