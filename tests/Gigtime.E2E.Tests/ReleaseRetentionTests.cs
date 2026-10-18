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
	public async Task Upgrades_retain_another_instances_running_release_and_force_refuses_to_replace_it(string location)
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

		if (location == "dotnet")
		{
			await other.StartAsync("dotnet", release, Path.Combine(published.Application, "gigtimed.dll"));
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
			workingDirectory.ShouldBe(release);
		}

		await other.CatalogueAsync();
		await other.SuccessAsync("start", "acme", "work");
		var data = (await other.GigAsync("export")).Output;
		var originalFiles = Files(release);

		foreach (var label in new[] { "bbbbbbb", "ccccccc", "ddddddd" })
		{
			var upgraded = await InstallAsync(day, Pair(day, label));
			upgraded.ExitCode.ShouldBe(0, upgraded.Output + upgraded.Error);
			Directory.Exists(release).ShouldBeTrue();
			Files(release).OrderBy(item => item.Key).ShouldBe(originalFiles.OrderBy(item => item.Key));
			(await other.GigAsync("export")).Output.ShouldBe(data);
		}

		var current = new DirectoryInfo(Path.Combine(root, "current")).LinkTarget;
		current.ShouldBe("releases/ddddddd");
		var link = Path.Combine(day.Environment["HOME"], ".local/bin/gig");
		var binTarget = new FileInfo(link).LinkTarget;
		var managerLog = day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"];
		var manager = await File.ReadAllTextAsync(managerLog, TestContext.Current.CancellationToken);
		var refused = await InstallAsync(day, source, "--force");
		refused.ExitCode.ShouldNotBe(0, refused.Output + refused.Error);
		refused.Error.ShouldContain("running gigtimed process");
		refused.Error.ShouldContain("Cannot replace release");
		new DirectoryInfo(Path.Combine(root, "current")).LinkTarget.ShouldBe(current);
		new FileInfo(link).LinkTarget.ShouldBe(binTarget);
		Files(release).OrderBy(item => item.Key).ShouldBe(originalFiles.OrderBy(item => item.Key));
		(await File.ReadAllTextAsync(managerLog, TestContext.Current.CancellationToken)).ShouldBe(manager);
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

	private static Dictionary<string, string> Files(string directory)
	{
		return Directory
			.GetFiles(directory, "*", SearchOption.AllDirectories)
			.ToDictionary(path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
	}
}
