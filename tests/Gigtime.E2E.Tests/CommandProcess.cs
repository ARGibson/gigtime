using System.Diagnostics;

namespace Gigtime.E2E.Tests;

internal static class CommandProcess
{
	private static readonly string[] isolatedVariables =
	[
		"XDG_RUNTIME_DIR", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME",
		"HOME", "TMPDIR", "GIGTIME_INSTANCE", "LISTEN_FDS", "LISTEN_PID", "LISTEN_FDNAMES",
	];

	internal static async Task<CommandResult> RunAsync(string file,
		IEnumerable<string> arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environment,
		TimeSpan timeout)
	{
		var start = StartInfo(file, arguments, workingDirectory, environment);
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

	internal static ProcessStartInfo StartInfo(string file,
		IEnumerable<string> arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environment)
	{
		var start = new ProcessStartInfo(file)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		// #33: callers opt into their own roots and activation environment.
		foreach (var key in isolatedVariables) start.Environment.Remove(key);
		foreach (var (key, value) in environment) start.Environment[key] = value;
		start.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
		foreach (var argument in arguments) start.ArgumentList.Add(argument);

		return start;
	}
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
