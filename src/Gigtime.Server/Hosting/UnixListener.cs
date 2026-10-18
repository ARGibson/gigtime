using System.Globalization;
using System.Net.Sockets;
using Gigtime.Platform;

namespace Gigtime.Server.Hosting;

public static class UnixListener
{
	public static void ListenOnGigtimeSocket(this WebApplicationBuilder builder)
	{
		builder.Services.AddHostedService<SocketDirectory>();

		builder.WebHost.ConfigureKestrel((_, options) =>
		{
			if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("gigtimed requires Unix sockets.");

			var paths = options.ApplicationServices.GetRequiredService<IPlatformPaths>();
			var path = paths.Instance.SocketPath(paths);
			// #2: environment/configured TCP endpoints cannot widen the daemon's Unix-only listener.
			options.Configure(new ConfigurationBuilder().Build());

			if (Environment.GetEnvironmentVariable("LISTEN_PID") == Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
			{
				if (Environment.GetEnvironmentVariable("LISTEN_FDS") != "1")
					throw new InvalidOperationException("gigtimed accepts exactly one activation socket.");

				using var inherited = new Socket(new SafeSocketHandle(3, false));

				if (inherited.AddressFamily != AddressFamily.Unix || inherited.LocalEndPoint?.ToString() != path ||
						(int)inherited.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.AcceptConnection)! != 1)
				{
					throw new InvalidOperationException("The activation socket must listen on the configured Unix path.");
				}

				options.ListenHandle(3);
			}
			else
			{
				options.ListenUnixSocket(path);
			}
		});
	}

	public static void RestrictSocket(this WebApplication app)
	{
		app.Lifetime.ApplicationStarted.Register(() =>
		{
			if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("gigtimed requires Unix sockets.");

			var paths = app.Services.GetRequiredService<IPlatformPaths>();
			var socket = paths.Instance.SocketPath(paths);
			if (File.Exists(socket)) File.SetUnixFileMode(socket, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		});
	}
}
