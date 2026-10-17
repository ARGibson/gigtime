using System.Diagnostics;
using Gigtime.Platform;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests.Platform;

public sealed class PlatformPathsTests
{
	[Fact]
	public void Linux_paths_resolve_configured_and_fallback_roots_without_real_XDG_access()
	{
		var root = Path.Combine(Path.GetTempPath(), $"gigtime-path-test-{Guid.NewGuid():N}");
		var paths = new LinuxPlatformPaths(Path.Combine(root, "data"), Path.Combine(root, "runtime"), Path.Combine(root, "config"), root, root);
		paths.DataDirectory.ShouldBe(Path.Combine(root, "data"));
		paths.RuntimeDirectory.ShouldBe(Path.Combine(root, "runtime"));
		paths.ConfigDirectory.ShouldBe(Path.Combine(root, "config"));
	}

	[Fact]
	public void Inspecting_paths_does_not_create_an_absent_runtime_fallback()
	{
		var root = Directory.CreateTempSubdirectory("gigtime-inspect-").FullName;

		try
		{
			var paths = new LinuxPlatformPaths(null, null, null, root, root, false);
			paths.RuntimeDirectory.ShouldBe(Path.Combine(root, $"gigtime-{Environment.UserName}"));
			Directory.GetFileSystemEntries(root).ShouldBeEmpty();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void Missing_runtime_fallback_is_created_private_and_reused()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux directory ownership test.");

		var root = Directory.CreateTempSubdirectory("gigtime-path-test-").FullName;

		try
		{
			var paths = new LinuxPlatformPaths(null, null, null, root, root);
			var fallback = Path.Combine(root, $"gigtime-{Environment.UserName}");
			paths.RuntimeDirectory.ShouldBe(fallback);
			File.GetUnixFileMode(fallback).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			new LinuxPlatformPaths(null, null, null, root, root).RuntimeDirectory.ShouldBe(fallback);
			paths.DataDirectory.ShouldBe(Path.Combine(root, ".local", "share"));
			paths.ConfigDirectory.ShouldBe(Path.Combine(root, ".config"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void Runtime_fallback_with_public_permissions_is_refused_without_chmod()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux directory ownership test.");

		var root = Directory.CreateTempSubdirectory("gigtime-path-test-").FullName;
		var fallback = Path.Combine(root, $"gigtime-{Environment.UserName}");

		try
		{
			Directory.CreateDirectory(fallback);

			var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
				| UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

			File.SetUnixFileMode(fallback, mode);
			Should.Throw<IOException>(() => new LinuxPlatformPaths(null, null, null, root, root)).Message.ShouldContain("mode 0700");
			File.GetUnixFileMode(fallback).ShouldBe(mode);
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task Server_refuses_to_start_with_a_public_runtime_fallback()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux directory ownership test.");

		var root = Directory.CreateTempSubdirectory("gigtime-path-test-").FullName;

		try
		{
			var fallback = Path.Combine(root, $"gigtime-{Environment.UserName}");
			Directory.CreateDirectory(fallback);

			File.SetUnixFileMode(fallback,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
				| UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

			var start = new ProcessStartInfo("dotnet")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			start.ArgumentList.Add(ServerExecutable.Path);
			start.Environment["TMPDIR"] = root;
			start.Environment["XDG_DATA_HOME"] = Path.Combine(root, "data");
			start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
			start.Environment.Remove("XDG_RUNTIME_DIR");
			start.Environment.Remove("LISTEN_PID");
			start.Environment.Remove("LISTEN_FDS");
			using var process = Process.Start(start)!;
			var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
			var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(30));

			try
			{
				await process.WaitForExitAsync(timeout.Token);
			}
			finally
			{
				if (!process.HasExited) process.Kill(true);
				await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			}

			process.ExitCode.ShouldNotBe(0);
			(await output + await errors).ShouldContain("owned by the current user with mode 0700");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void Runtime_fallback_cannot_be_a_symbolic_link()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux directory ownership test.");

		var root = Directory.CreateTempSubdirectory("gigtime-path-test-").FullName;

		try
		{
			var target = Path.Combine(root, "target");
			Directory.CreateDirectory(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			Directory.CreateSymbolicLink(Path.Combine(root, $"gigtime-{Environment.UserName}"), target);
			Should.Throw<IOException>(() => new LinuxPlatformPaths(null, null, null, root, root)).Message.ShouldContain("mode 0700");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
