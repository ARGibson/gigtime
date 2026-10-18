using System.Diagnostics;
using System.Text.Json;
using Shouldly;

namespace Gigtime.E2E.Tests;

internal sealed class DaySession : IAsyncDisposable
{
	private Process? server;
	private Task<string>? serverOutput;
	private Task<string>? serverError;
	private readonly PublishedFixture published;
	internal string Root { get; } = Path.Combine(Path.GetTempPath(), "gig-day-" + Guid.NewGuid().ToString("N"));
	internal int ServerProcessId => server!.Id;
	internal Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);

	internal DaySession(PublishedFixture published)
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("End-to-end tests require Linux.");

		this.published = published;

		foreach (var (key, leaf) in new[]
						{
							("HOME", "home"), ("TMPDIR", "tmp"), ("XDG_DATA_HOME", "data"), ("XDG_RUNTIME_DIR", "runtime"),
							("XDG_CONFIG_HOME", "config")
						})
		{
			var path = Path.Combine(Root, leaf);
			Directory.CreateDirectory(path);
			Environment[key] = path;
		}

		Environment["NO_COLOR"] = "1";
		Environment["TERM"] = "dumb";
		// #5: even an accidental setup call can reach only this harmless replacement.
		var bin = Path.Combine(Root, "bin");
		Directory.CreateDirectory(bin);
		var systemctl = Path.Combine(bin, "systemctl");

		File.WriteAllText(systemctl,
			"#!/bin/sh\nprintf '%s\\n' \"$*\" >> \"$GIGTIME_TEST_SYSTEMCTL_LOG\"\n"
			+ "if [ \"$2\" = enable ] && [ \"$4\" = gigtimed.socket ] && [ \"$GIGTIME_TEST_NO_ACTIVATION\" = 1 ]; then exit 1; fi\nexit 0\n");

		File.SetUnixFileMode(systemctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		Environment["PATH"] = bin + Path.PathSeparator + System.Environment.GetEnvironmentVariable("PATH");
		Environment["GIGTIME_TEST_SYSTEMCTL_LOG"] = Path.Combine(Root, "systemctl.log");
	}

	internal Task StartAsync() => StartAsync(published.Application);

	internal async Task StartAsync(string workingDirectory)
	{
		var start = new ProcessStartInfo(Path.Combine(published.Application, "gigtimed"))
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		foreach (var (key, value) in Environment) start.Environment[key] = value;
		start.Environment.Remove("DBUS_SESSION_BUS_ADDRESS");
		start.Environment.Remove("LISTEN_PID");
		start.Environment.Remove("LISTEN_FDS");
		server = Process.Start(start)!;
		serverOutput = server.StandardOutput.ReadToEndAsync();
		serverError = server.StandardError.ReadToEndAsync();
		var elapsed = Stopwatch.StartNew();

		while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
		{
			if (server.HasExited) throw new InvalidOperationException(await serverOutput + await serverError);

			if ((await GigAsync("setup", "--check", "--json")).ExitCode == 0) return;

			await Task.Delay(30);
		}

		throw new TimeoutException("The isolated daemon did not answer health within 30 seconds.");
	}

	internal async Task RestartAsync()
	{
		// #5: restart like a service manager; a forced kill bypasses Unix listener cleanup.
		var signal = await CommandProcess.RunAsync("/bin/kill",
			["-TERM", server!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)],
			Root,
			Environment,
			TimeSpan.FromSeconds(5));

		signal.ExitCode.ShouldBe(0, signal.Error);
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		await server.WaitForExitAsync(deadline.Token);
		await Task.WhenAll(serverOutput!, serverError!);
		server.Dispose();
		server = null;
		await StartAsync();
	}

	internal Task<CommandResult> GigAsync(params string[] arguments) => CommandProcess.RunAsync(
		Path.Combine(published.Application, "gig"),
		arguments,
		Root,
		Environment,
		TimeSpan.FromSeconds(20));

	internal async Task<CommandResult> HumanAsync(params string[] arguments)
	{
		var result = await GigAsync(arguments);
		result.ExitCode.ShouldBe(0, result.Output + result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldNotBeEmpty();
		result.Output.ShouldNotContain("\u001b");
		result.Output.ShouldNotStartWith("{");

		return result;
	}

	internal async Task<JsonElement> SuccessAsync(params string[] arguments)
	{
		var result = await GigAsync([.. arguments, "--json"]);
		result.ExitCode.ShouldBe(0, result.Output + result.Error);
		result.Error.ShouldBeEmpty();
		result.Output.ShouldEndWith("\n");
		result.Output.ShouldNotContain("\u001b");
		using var json = JsonDocument.Parse(result.Output);
		json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();

		return json.RootElement.GetProperty("data").Clone();
	}

	internal async Task RefusalAsync(int exit, string code, params string[] arguments)
	{
		var result = await GigAsync([.. arguments, "--json"]);
		result.ExitCode.ShouldBe(exit, result.Output + result.Error);
		result.Error.ShouldBeEmpty();
		using var json = JsonDocument.Parse(result.Output);
		json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
		json.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe(code);
	}

	internal async Task CatalogueAsync()
	{
		await SuccessAsync("defaults", "set", "--timezone", "UTC", "--week-starts", "Monday", "--rounding", "nearest-minute");
		await SuccessAsync("client", "add", "acme", "--name", "Acme", "--currency", "GBP");
		await SuccessAsync("project", "add", "acme", "work", "--name", "Work", "--rate", "20");
	}

	public async ValueTask DisposeAsync()
	{
		if (server is not null)
		{
			if (!server.HasExited) server.Kill(true);
			await server.WaitForExitAsync();
			await Task.WhenAll(serverOutput!, serverError!);
			server.Dispose();
		}

		Directory.Delete(Root, true);
	}
}
