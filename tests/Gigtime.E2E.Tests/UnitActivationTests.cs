using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class UnitActivationTests(PublishedFixture published)
{
	[Fact]
	public async Task First_status_activates_the_daemon_using_generated_unit_paths_and_environment()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Socket activation requires Linux.");

		await using var day = new DaySession(published);
		var application = Path.Combine(day.Root, "application space %");

		foreach (var file in Directory.EnumerateFiles(published.Application, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(application, Path.GetRelativePath(published.Application, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target);
		}

		var installed = await day.SuccessAsync("setup", "--gigtimed", Path.Combine(application, "gigtimed"));
		var service = await File.ReadAllLinesAsync(installed.GetProperty("service_unit").GetString()!, TestContext.Current.CancellationToken);
		var socketUnit = await File.ReadAllLinesAsync(installed.GetProperty("socket_unit").GetString()!, TestContext.Current.CancellationToken);
		var executable = Decode(service.Single(line => line.StartsWith("ExecStart=", StringComparison.Ordinal))[10..]);
		var address = Decode(socketUnit.Single(line => line.StartsWith("ListenStream=", StringComparison.Ordinal))[13..]);
		var database = installed.GetProperty("database_path").GetString()!;
		File.Exists(database).ShouldBeFalse();

		Directory.CreateDirectory(Path.GetDirectoryName(address)!,
			(UnixFileMode)Convert.ToInt32(socketUnit.Single(line => line.StartsWith("DirectoryMode=", StringComparison.Ordinal))[14..], 8));

		var activator = Environment.GetEnvironmentVariable("PATH")!
			.Split(Path.PathSeparator)
			.Select(directory => Path.Combine(directory, "systemd-socket-activate"))
			.First(File.Exists);

		var start = new ProcessStartInfo(activator)
		{
			WorkingDirectory = day.Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		// #5: only the generated unit's environment reaches the daemon; no user-manager credentials survive.
		start.Environment.Clear();
		start.ArgumentList.Add("-l");
		start.ArgumentList.Add(address);

		foreach (var line in service.Where(line => line.StartsWith("Environment=", StringComparison.Ordinal)))
		{
			start.ArgumentList.Add("-E");
			start.ArgumentList.Add(Decode(line[12..]));
		}

		start.ArgumentList.Add(executable);
		using var process = Process.Start(start)!;
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var error = ReadActivationLogAsync(process.StandardError, ready);

		try
		{
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
			File.Exists(database).ShouldBeFalse();

			File.SetUnixFileMode(address,
				(UnixFileMode)Convert.ToInt32(socketUnit.Single(line => line.StartsWith("SocketMode=", StringComparison.Ordinal))[11..], 8));

			var first = await day.GigAsync("status", "--json");
			first.ExitCode.ShouldBe(0, first.Output + first.Error);
			first.Error.ShouldBeEmpty();
			using var response = JsonDocument.Parse(first.Output);
			response.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
			response.RootElement.GetProperty("data").GetProperty("task").ValueKind.ShouldBe(JsonValueKind.Null);
			File.Exists(database).ShouldBeTrue();
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			await Task.WhenAll(output, error);
		}

		(await error).ShouldContain("Communication attempt");
	}

	private static string Decode(string value) => value.Trim('"').Replace("%%", "%", StringComparison.Ordinal);

	private static async Task<string> ReadActivationLogAsync(StreamReader reader, TaskCompletionSource ready)
	{
		var log = new StringBuilder();

		while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
		{
			log.AppendLine(line);
			if (line.StartsWith("Listening on ", StringComparison.Ordinal)) ready.TrySetResult();
		}

		if (!ready.Task.IsCompleted) ready.TrySetException(new IOException(log.ToString()));

		return log.ToString();
	}
}
