using System.Diagnostics;

namespace Gigtime.Platform;

public sealed class LinuxServiceInstaller(IPlatformPaths paths, Func<IReadOnlyList<string>, CancellationToken, Task<int>> systemctl)
	: IServiceInstaller
{
	public LinuxServiceInstaller(IPlatformPaths paths) : this(paths, RunSystemctlAsync) { }

	public async Task<ServiceInstallation> InstallAsync(string executable, CancellationToken cancellationToken = default)
	{
		var directory = Path.Combine(paths.ConfigDirectory, "systemd", "user");
		Directory.CreateDirectory(directory);
		var socket = Path.Combine(paths.RuntimeDirectory, "gigtime", "gigtimed.sock");

		var service = "[Unit]\nDescription=gigtime time tracking\n\n[Service]\nExecStart="
			+ Quote(executable.Replace("$", "$$", StringComparison.Ordinal))
			+ "\nEnvironment=" + Quote("XDG_RUNTIME_DIR=" + paths.RuntimeDirectory)
			+ "\nEnvironment=" + Quote("XDG_DATA_HOME=" + paths.DataDirectory)
			+ "\nEnvironment=" + Quote("XDG_CONFIG_HOME=" + paths.ConfigDirectory) + "\n\n[Install]\nWantedBy=default.target\n";

		var socketUnit = "[Unit]\nDescription=gigtime socket\n\n[Socket]\nListenStream="
			+ socket.Replace("%", "%%", StringComparison.Ordinal)
			+ "\nSocketMode=0600\nDirectoryMode=0700\n\n[Install]\nWantedBy=sockets.target\n";

		var servicePath = Path.Combine(directory, "gigtimed.service");
		var socketPath = Path.Combine(directory, "gigtimed.socket");
		var fallback = "# Socket activation unavailable; login-started service.\n" + service;

		if (!File.Exists(socketPath) && File.Exists(servicePath) && await File.ReadAllTextAsync(servicePath, cancellationToken) == fallback)
		{
			await RequireAsync(["enable", "--now", "gigtimed.service"], cancellationToken);

			return new(false, false);
		}

		var changed = await WriteAsync(servicePath, service, cancellationToken);
		changed |= await WriteAsync(Path.Combine(directory, "gigtimed.socket"), socketUnit, cancellationToken);
		if (changed) await RequireAsync(["daemon-reload"], cancellationToken);

		if (await systemctl(["enable", "--now", "gigtimed.socket"], cancellationToken) == 0)
			return new(changed, true);

		// #5: remove the failed activation unit before enabling the login-started service.
		await systemctl(["disable", "--now", "gigtimed.socket"], cancellationToken);
		File.Delete(socketPath);
		await WriteAsync(servicePath, fallback, cancellationToken);
		await RequireAsync(["daemon-reload"], cancellationToken);
		await RequireAsync(["enable", "--now", "gigtimed.service"], cancellationToken);

		return new(true, false);
	}

	private async Task RequireAsync(string[] arguments, CancellationToken cancellationToken)
	{
		if (await systemctl(arguments, cancellationToken) != 0)
			throw new IOException("systemctl --user " + string.Join(' ', arguments) + " failed.");
	}

	private static async Task<bool> WriteAsync(string path, string content, CancellationToken cancellationToken)
	{
		if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken) == content) return false;

		await File.WriteAllTextAsync(path, content, cancellationToken);

		return true;
	}

	private static string Quote(string value)
	{
		if (value.Any(c => char.IsControl(c))) throw new IOException("Unit paths cannot contain control characters.");

		return "\"" + value
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("%", "%%", StringComparison.Ordinal) + "\"";
	}

	private static async Task<int> RunSystemctlAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(10));
		cancellationToken = deadline.Token;

		var start = new ProcessStartInfo("systemctl")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		start.ArgumentList.Add("--user");
		foreach (var argument in arguments) start.ArgumentList.Add(argument);
		using var process = Process.Start(start)!;
		var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var error = process.StandardError.ReadToEndAsync(cancellationToken);

		try
		{
			await process.WaitForExitAsync(cancellationToken);
			await Task.WhenAll(output, error);

			return process.ExitCode;
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
		}
	}
}
