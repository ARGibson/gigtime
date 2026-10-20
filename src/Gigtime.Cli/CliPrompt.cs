using System.Globalization;
using Gigtime.Contracts;

namespace Gigtime.Cli;

internal static class CliPrompt
{
	internal static async Task<string?> RetryPathAsync(string command,
		string path,
		ErrorDto refusal,
		TextReader input,
		TextWriter error,
		CancellationToken token)
	{
		if (refusal.State?.Tasks is { Count: > 1 } tasks && command is "resume" or "stop")
		{
			for (var i = 0; i < tasks.Count; i++) error.WriteLine((i + 1).ToString(CultureInfo.InvariantCulture) + ") " + tasks[i]);
			error.Write("Choose a task [1-" + tasks.Count.ToString(CultureInfo.InvariantCulture) + "] (Enter to cancel): ");
			await error.FlushAsync(token);
			var answer = await input.ReadLineAsync(token);

			return int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var choice) && choice >= 1 && choice <= tasks.Count
				? path + (path.Contains('?') ? "&" : "?") + "task=" + Uri.EscapeDataString(tasks[choice - 1])
				: null;
		}

		if (refusal.State is not { TaskId: { } running, State: "running" } || command is not ("start" or "resume")) return null;
		if (path.Contains("switch=true", StringComparison.Ordinal)) return null;

		var target = path.Split('?').Last().Split('&').FirstOrDefault(pair => pair.StartsWith("task=", StringComparison.Ordinal));

		if (command == "resume"
				&& (target is null || running.StartsWith(Uri.UnescapeDataString(target[5..]), StringComparison.Ordinal)))
		{
			return null;
		}

		error.Write("Pause running task " + running + " and switch? [y/N] ");
		await error.FlushAsync(token);
		var confirmation = await input.ReadLineAsync(token);

		if (!string.Equals(confirmation?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(confirmation?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return command == "start" ? path : path + (path.Contains('?') ? "&" : "?") + "switch=true";
	}
}
