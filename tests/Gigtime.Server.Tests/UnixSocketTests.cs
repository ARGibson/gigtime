using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class UnixSocketTests
{
	[Fact]
	public async Task Real_Kestrel_uses_a_private_Unix_socket_and_no_TCP_listener()
	{
		if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This acceptance test requires Unix sockets.");

		var directory = Path.Combine(Path.GetTempPath(), $"gigtime-socket-test-{Guid.NewGuid():N}");

		var start = new ProcessStartInfo("dotnet")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		start.ArgumentList.Add(typeof(Program).Assembly.Location);
		start.Environment["XDG_DATA_HOME"] = Path.Combine(directory, "data");
		start.Environment["XDG_RUNTIME_DIR"] = Path.Combine(directory, "runtime");
		start.Environment["XDG_CONFIG_HOME"] = Path.Combine(directory, "config");
		start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
		start.Environment["Kestrel__Endpoints__Injected__Url"] = "http://127.0.0.1:0";
		start.Environment.Remove("LISTEN_PID");
		start.Environment.Remove("LISTEN_FDS");
		using var process = Process.Start(start)!;
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		var socketPath = Path.Combine(directory, "runtime", "gigtime", "gigtimed.sock");

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
		logs.ShouldContain("Now listening on: http://unix:");
		logs.ShouldNotContain("Now listening on: http://127.0.0.1");
	}
}
