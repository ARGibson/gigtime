using Gigtime.Platform;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class LinuxServiceInstallerTests
{
	[Fact]
	public async Task Installer_quotes_paths_preserves_unchanged_files_and_only_calls_the_replacement_manager()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-units-" + Guid.NewGuid().ToString("N"));
		var paths = new FakePaths(Path.Combine(root, "data %"), Path.Combine(root, "run space"), Path.Combine(root, "config"));
		var calls = new List<string>();

		var installer = new LinuxServiceInstaller(paths,
			(args, _) =>
			{
				calls.Add(string.Join(' ', args));

				return Task.FromResult(0);
			});

		try
		{
			var executable = Path.Combine(root, "bin $% space", "gigtimed");
			(await installer.InstallAsync(executable, TestContext.Current.CancellationToken)).Changed.ShouldBeTrue();
			var service = Path.Combine(paths.ConfigDirectory, "systemd/user/gigtimed.service");
			var content = await File.ReadAllTextAsync(service, TestContext.Current.CancellationToken);
			content.ShouldContain("bin $$%% space/gigtimed\"");
			content.ShouldContain("data %%");
			var stamp = File.GetLastWriteTimeUtc(service);
			(await installer.InstallAsync(executable, TestContext.Current.CancellationToken)).Changed.ShouldBeFalse();
			File.GetLastWriteTimeUtc(service).ShouldBe(stamp);
			calls.ShouldBe(["daemon-reload", "enable --now gigtimed.socket", "enable --now gigtimed.socket"]);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task Manager_failure_is_reported_instead_of_claiming_installation_succeeded()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-units-" + Guid.NewGuid().ToString("N"));
		var paths = new FakePaths(root, root, root);
		var installer = new LinuxServiceInstaller(paths, (_, _) => Task.FromResult(1));

		try
		{
			await Should.ThrowAsync<IOException>(async () =>
				await installer.InstallAsync("/example/gigtimed", TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
