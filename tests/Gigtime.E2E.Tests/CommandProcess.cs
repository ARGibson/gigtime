using System.Diagnostics;

namespace Gigtime.E2E.Tests;

internal static class CommandProcess
{
	internal static async Task<CommandResult> RunAsync(string file,
		IEnumerable<string> arguments,
		string workingDirectory,
		IReadOnlyDictionary<string, string> environment,
		TimeSpan timeout)
	{
		var start = new ProcessStartInfo(file)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		start.Environment.Remove("GIGTIME_INSTANCE");
		foreach (var argument in arguments) start.ArgumentList.Add(argument);
		foreach (var (key, value) in environment) start.Environment[key] = value;
		start.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
		start.Environment.Remove("LISTEN_PID");
		start.Environment.Remove("LISTEN_FDS");
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
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
