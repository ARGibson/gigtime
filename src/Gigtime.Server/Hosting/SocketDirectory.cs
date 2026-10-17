using Gigtime.Platform;

namespace Gigtime.Server.Hosting;

public sealed class SocketDirectory(IPlatformPaths paths) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("gigtimed requires Unix sockets.");

		var directory = Path.Combine(paths.RuntimeDirectory, "gigtime");
		Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
