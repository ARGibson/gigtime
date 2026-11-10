using System.Diagnostics;
using System.Reflection;
using System.Text;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class HerdrActivationTests(PublishedFixture published, ITestOutputHelper output)
{
	[Theory]
	[InlineData("0.05", false)]
	[InlineData("2.2", true)]
	public async Task Direct_poll_triggers_inherited_listener_and_recovers_after_delayed_start(string delay, bool tooSlow)
	{
		await using var day = new DaySession(published);
		var address = Path.Combine(day.Environment["XDG_RUNTIME_DIR"], "gigtime/gigtimed.sock");
		Directory.CreateDirectory(Path.GetDirectoryName(address)!);
		var script = Path.GetFullPath(Path.Combine(typeof(HerdrActivationTests).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryRoot").Value!,
			"integrations/herdr/gig-tab-status"));
		var start = new ProcessStartInfo("systemd-socket-activate")
		{
			WorkingDirectory = day.Root,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		start.Environment.Clear();
		start.ArgumentList.Add("-l");
		start.ArgumentList.Add(address);

		foreach (var (key, value) in day.Environment)
		{
			start.ArgumentList.Add("-E");
			start.ArgumentList.Add(key + "=" + value);
		}

		foreach (var argument in new[] { "/bin/sh", "-c", "sleep " + delay + "; exec \"$@\"", "activation",
				Path.Combine(published.Application, "gigtimed") })
		{
			start.ArgumentList.Add(argument);
		}

		using var process = Process.Start(start)!;
		var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stderr = ReadListeningAsync(process.StandardError, ready);

		try
		{
			await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
			var first = await CommandProcess.RunAsync(script, [], day.Root, day.Environment, TimeSpan.FromSeconds(4));
			first.Output.ShouldBeEmpty();

			if (tooSlow)
			{
				first.ExitCode.ShouldBe(1, first.Error);
				first.Error.ShouldStartWith("gig-tab-status: ");
				await Task.Delay(1500, TestContext.Current.CancellationToken);
			}
			else
			{
				first.ExitCode.ShouldBe(0, first.Error);
			}

			var next = await CommandProcess.RunAsync(script, [], day.Root, day.Environment, TimeSpan.FromSeconds(4));
			next.ExitCode.ShouldBe(0, next.Error);
			next.Error.ShouldBeEmpty();
			next.Output.ShouldBeEmpty();
			output.WriteLine($"Inherited socket: delay={delay}; first={first.ExitCode}; next={next.ExitCode}");
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			await Task.WhenAll(stdout, stderr);
		}

		(await stderr).ShouldContain("Communication attempt");
	}

	private static async Task<string> ReadListeningAsync(StreamReader reader, TaskCompletionSource ready)
	{
		var text = new StringBuilder();

		while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
		{
			text.AppendLine(line);
			if (line.StartsWith("Listening on ", StringComparison.Ordinal)) ready.TrySetResult();
		}

		if (!ready.Task.IsCompleted) ready.TrySetException(new IOException(text.ToString()));

		return text.ToString();
	}
}
