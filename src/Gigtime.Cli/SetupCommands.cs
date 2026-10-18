using System.CommandLine;
using System.Net.Sockets;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Platform;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class SetupCommands
{
	internal static void Configure(RootCommand root,
		CliExchange exchange,
		TextWriter output,
		Func<IPlatformPaths> paths,
		Func<IPlatformPaths, IServiceInstaller> installers,
		Func<string> executable,
		Option<bool> json)
	{
		var setup = Add(root, "setup", "Install user units, or inspect them without changes with --check.");
		Flag(setup, "check");
		Text(setup, "gigtimed");

		setup.SetAction(async (r, ct) =>
		{
			try
			{
				var p = paths();

				var installation =
					new ServiceInstallation(false, File.Exists(Path.Combine(p.ConfigDirectory, "systemd/user", p.Instance.SocketUnit)));

				if (!r.GetValue<bool>("--check"))
				{
					var daemon = Path.GetFullPath(r.GetValue<string>("--gigtimed") ?? Path.Combine(Path.GetDirectoryName(executable())!, "gigtimed"));

					LinuxServiceInstaller.ValidateExecutablePath(daemon);

					if (!File.Exists(daemon))
						return exchange.Fail("setup", ErrorCodes.Config, "gigtimed executable is missing: " + daemon, r.GetValue(json));

					if (!OperatingSystem.IsLinux() || (File.GetUnixFileMode(daemon) &
								(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
					{
						return exchange.Fail("setup", ErrorCodes.Config, "gigtimed is not executable: " + daemon, r.GetValue(json));
					}

					installation = await installers(p).InstallAsync(daemon, ct);
				}

				var socket = p.Instance.SocketPath(p);
				var serviceUnit = Path.Combine(p.ConfigDirectory, "systemd/user", p.Instance.ServiceUnit);
				var socketUnit = Path.Combine(p.ConfigDirectory, "systemd/user", p.Instance.SocketUnit);

				var data = new SetupDto(serviceUnit,
					File.Exists(serviceUnit),
					socketUnit,
					File.Exists(socketUnit),
					socket,
					Path.Exists(socket),
					p.Instance.DatabasePath(p),
					await HealthyAsync(socket, ct),
					installation.Changed,
					installation.SocketActivation,
					p.Instance.Name);

				if (r.GetValue(json))
				{
					output.WriteLine(JsonSerializer.Serialize(Envelopes.Success("setup", data), GigtimeJsonContext.Default.SetupResponse));
				}
				else
				{
					if (p.Instance.Name is { } name) output.WriteLine("Instance: " + name);
					output.WriteLine($"Service unit: {data.ServiceUnit} ({(data.ServiceInstalled ? "installed" : "missing")})");
					output.WriteLine($"Socket unit: {data.SocketUnit} ({(data.SocketInstalled ? "installed" : "missing")})");
					output.WriteLine($"Socket: {data.SocketPath} ({(data.SocketExists ? "present" : "absent")})");
					output.WriteLine("Database: " + data.DatabasePath);
					output.WriteLine("Health: " + (data.Healthy ? "ok" : "unavailable"));
					if (!r.GetValue<bool>("--check")) output.WriteLine(data.Changed ? "Installed." : "unchanged");

					if (!installation.SocketActivation && !r.GetValue<bool>("--check"))
						output.WriteLine("Socket activation unavailable; service starts at login.");
				}

				return r.GetValue<bool>("--check") && !data.Healthy ? ExitCodes.Unavailable : ExitCodes.Success;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
																	or OperationCanceledException)
			{
				return exchange.Fail("setup", ErrorCodes.Config, ex.Message, r.GetValue(json));
			}
		});
	}

	private static async Task<bool> HealthyAsync(string path, CancellationToken cancellationToken)
	{
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromMilliseconds(250));

			using var transport = new SocketsHttpHandler
			{
				ConnectCallback = async (_, ct) =>
				{
					var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

					try
					{
						await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);

						return new NetworkStream(socket, true);
					}
					catch
					{
						socket.Dispose();

						throw;
					}
				}
			};

			using var client = new HttpClient(transport);
			var body = await client.GetStringAsync("http://localhost/v1/health", timeout.Token);
			var health = JsonSerializer.Deserialize(body, GigtimeJsonContext.Default.HealthResponse);

			return health is { Ok: true, Data.Status: "ok" };
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException) { return false; }
	}
}
