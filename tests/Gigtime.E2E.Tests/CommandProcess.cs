using System.Diagnostics;

namespace Gigtime.E2E.Tests;

internal static class CommandProcess
{
	private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

	private static readonly (string Variable, string Directory)[] privateRoots =
	[
		("HOME", "home"), ("XDG_DATA_HOME", "data"), ("XDG_STATE_HOME", "state"), ("XDG_CONFIG_HOME", "config"),
		("XDG_CACHE_HOME", "cache"), ("XDG_RUNTIME_DIR", "runtime"), ("TMPDIR", "tmp"),
	];

	internal static async Task<CommandResult> RunAsync(string file,
		IEnumerable<string> arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environment,
		TimeSpan timeout,
		string? omittedVariable = null)
	{
		// #33: use /tmp directly so an inherited TMPDIR cannot choose the launch root.
		var launchRoot = Path.Combine("/tmp", $"gig-command-{Guid.NewGuid():N}");

		try
		{
			var start = StartInfo(file, arguments, workingDirectory, environment, launchRoot, omittedVariable);
			using var process = Process.Start(start)!;
			using var deadline = new CancellationTokenSource(timeout);
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();

			try
			{
				await process.WaitForExitAsync(deadline.Token);

				return new(process.ExitCode, await output, await error);
			}
			finally
			{
				if (!process.HasExited) process.Kill(true);
				await process.WaitForExitAsync();
			}
		}
		finally
		{
			if (Directory.Exists(launchRoot)) Directory.Delete(launchRoot, true);
		}
	}

	internal static ProcessStartInfo StartInfo(string file,
		IEnumerable<string> arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environment,
		string launchRoot,
		string? omittedVariable)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("End-to-end tests require Linux.");

		var start = new ProcessStartInfo(file)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		foreach (var key in new[] { "GIGTIME_INSTANCE", "LISTEN_FDS", "LISTEN_PID", "LISTEN_FDNAMES" })
			start.Environment.Remove(key);
		foreach (var (key, value) in environment) start.Environment[key] = value;

		Directory.CreateDirectory(launchRoot, PrivateDirectory);
		foreach (var (key, directory) in privateRoots)
		{
			if (environment.ContainsKey(key) || key == omittedVariable) continue;

			var path = Path.Combine(launchRoot, directory);
			Directory.CreateDirectory(path, PrivateDirectory);
			start.Environment[key] = path;
		}

		if (omittedVariable is not null) start.Environment.Remove(omittedVariable);
		start.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
		foreach (var argument in arguments) start.ArgumentList.Add(argument);

		return start;
	}
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
