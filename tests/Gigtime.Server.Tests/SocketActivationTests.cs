using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class SocketActivationTests
{
	public static bool HasSocketActivator => FindExecutable("systemd-socket-activate") is not null;

	[Fact(SkipUnless = nameof(HasSocketActivator), Skip = "systemd-socket-activate is not on PATH.")]
	public async Task Systemd_socket_activation_serves_the_temporary_Unix_socket()
	{
		if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This acceptance test requires Unix sockets.");

		var directory = Path.Combine(Path.GetTempPath(), $"gigtime-activation-test-{Guid.NewGuid():N}");

		var socketPath = Path.Combine(directory, "runtime", "gigtime", "gigtimed.sock");

		Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		var start = new ProcessStartInfo(FindExecutable("systemd-socket-activate")!)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		start.ArgumentList.Add("--listen=" + socketPath);

		foreach (var (variable, child) in new[] { ("XDG_DATA_HOME", "data"), ("XDG_RUNTIME_DIR", "runtime"), ("XDG_CONFIG_HOME", "config") })
		{
			start.ArgumentList.Add("-E");
			start.ArgumentList.Add(variable + "=" + Path.Combine(directory, child));
		}

		start.ArgumentList.Add(FindExecutable("dotnet")!);
		start.ArgumentList.Add(ServerExecutable.Path);
		start.Environment.Remove("LISTEN_PID");
		start.Environment.Remove("LISTEN_FDS");
		using var process = Process.Start(start)!;
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(30));

			using var transport = new SocketsHttpHandler
			{
				ConnectCallback = async (_, token) =>
				{
					var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

					try
					{
						await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);

						return new NetworkStream(socket, true);
					}
					catch
					{
						socket.Dispose();

						throw;
					}
				},
			};

			using var client = new HttpClient(transport);
			string body;

			while (true)
			{
				if (process.HasExited) throw new InvalidOperationException(await output + await errors);

				try
				{
					body = await client.GetStringAsync("http://localhost/v1/health", timeout.Token);

					break;
				}
				catch (HttpRequestException)
				{
					await Task.Delay(50, timeout.Token);
				}
			}

			JsonSerializer.Deserialize(body, GigtimeJsonContext.Default.HealthResponse)!.Ok.ShouldBeTrue();
			File.GetUnixFileMode(socketPath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);

			File
				.GetUnixFileMode(Path.Combine(directory, "data", "gigtime", "gigtime.db"))
				.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			Directory.Delete(directory, true);
		}

		var logs = await output + await errors;
		logs.ShouldContain("Now listening on:");
		logs.ShouldNotContain("Now listening on: http://127.0.0.1");
	}

	private static string? FindExecutable(string name)
	{
		return (Environment.GetEnvironmentVariable("PATH") ?? "")
			.Split(Path.PathSeparator)
			.Where(path => !string.IsNullOrEmpty(path))
			.Select(path => Path.Combine(path, name))
			.FirstOrDefault(File.Exists);
	}
}
