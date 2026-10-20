using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class OpenTasksTests(PublishedFixture published)
{
	private static readonly string[] promptScript =
		new[]
		{
			"import os, pty, select, signal, sys, time", "answer = sys.argv[1]", "command = sys.argv[2:]", "pid, master = pty.fork()",
			"if pid == 0:", "    os.execv(command[0], command)", "output = b''", "sent = False", "deadline = time.monotonic() + 15",
			"while time.monotonic() < deadline:", "    if select.select([master], [], [], 0.1)[0]:",
			"        try: chunk = os.read(master, 65536)", "        except OSError: break", "        if not chunk: break",
			"        output += chunk", "        if not sent and b'[y/N]' in output:", "            os.write(master, (answer + '\\n').encode())",
			"            sent = True", "else:", "    os.kill(pid, signal.SIGKILL)", "_, status = os.waitpid(pid, 0)", "os.close(master)",
			"sys.stdout.write(output.decode(errors='replace'))", "sys.exit(os.waitstatus_to_exitcode(status))",
		};

	[Fact]
	public async Task Published_switch_stop_reopen_add_and_warning_day_preserves_open_tasks()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var a = (await day.SuccessAsync("start", "acme", "work", "--ref", "repeat")).GetProperty("id").GetString()!;
		var b = (await day.SuccessAsync("start", "acme", "work", "--switch")).GetProperty("id").GetString()!;
		var status = await day.SuccessAsync("status");
		status.GetProperty("running").GetProperty("id").GetString().ShouldBe(b);
		status.GetProperty("paused").EnumerateArray().Single().GetProperty("id").GetString().ShouldBe(a);
		(await day.SuccessAsync("stop")).GetProperty("id").GetString().ShouldBe(b);
		(await day.SuccessAsync("stop")).GetProperty("id").GetString().ShouldBe(a);
		await day.SuccessAsync("resume", a);
		await day.SuccessAsync("pause");
		await day.SuccessAsync("resume", b);
		await day.SuccessAsync("pause");
		var ambiguity = await day.GigAsync("resume", "--json");
		ambiguity.ExitCode.ShouldBe(3);

		using (var json = JsonDocument.Parse(ambiguity.Output))
		{
			json.RootElement.GetProperty("error").GetProperty("state").GetProperty("tasks").GetArrayLength().ShouldBe(2);
		}

		var c = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		status = await day.SuccessAsync("status");
		status.GetProperty("running").GetProperty("id").GetString().ShouldBe(c);
		status.GetProperty("paused").GetArrayLength().ShouldBe(2);
		var piped = await day.GigAsync("start", "acme", "work");
		piped.ExitCode.ShouldBe(3);
		piped.Error.ShouldNotContain("[y/N]");
		await day.RefusalAsync(3, "invalid_transition", "start", "acme", "work");
		await day.SuccessAsync("stop", c);
		await day.SuccessAsync("resume", a);
		await day.SuccessAsync("resume", b, "--switch");
		var added = await day.SuccessAsync("add", b, "--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T09:01:00Z");
		added.GetProperty("intervals").EnumerateArray().First().GetProperty("number").GetInt32().ShouldBe(4);
		await day.RefusalAsync(8, "validation", "add", b, "--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T09:01:00Z");
		await day.SuccessAsync("stop", b, "--submit");
		await day.RefusalAsync(3, "invalid_transition", "resume", b);
		await day.RefusalAsync(3, "invalid_transition", "add", b, "--from", "2026-03-02T07:00:00Z", "--to", "2026-03-02T07:01:00Z");

		var warning = await day.GigAsync("add",
			"acme",
			"work",
			"--from",
			"2026-03-02T07:00:00Z",
			"--to",
			"2026-03-02T07:01:00Z",
			"--ref",
			"repeat");

		warning.ExitCode.ShouldBe(0);
		warning.Error.ShouldContain("repeated_ref");

		var jsonWarning = await day.GigAsync("add",
			"acme",
			"work",
			"--from",
			"2026-03-02T06:00:00Z",
			"--to",
			"2026-03-02T06:01:00Z",
			"--ref",
			"repeat",
			"--json");

		jsonWarning.ExitCode.ShouldBe(0);
		using var envelope = JsonDocument.Parse(jsonWarning.Output);
		envelope.RootElement.GetProperty("warnings").EnumerateArray().Single().GetProperty("code").GetString().ShouldBe("repeated_ref");
		var export = await day.GigAsync("export");
		export.Output.ShouldContain("task_reopened");
		export.Output.ShouldContain("interval_added");
	}

	[Fact]
	public async Task A_real_terminal_confirms_a_switch_and_defaults_to_no()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var a = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		var script = Path.Combine(day.Root, "prompt.py");

		// #36: Python3 and its stdlib pty module are present on the Ubuntu 24.04 runner.
		await File.WriteAllTextAsync(script,
			string.Join('\n', promptScript),
			TestContext.Current.CancellationToken);

		var native = Path.Combine(published.Application, "gig");

		var decline = await CommandProcess.RunAsync("python3",
			[script, "", native, "start", "acme", "work"],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(20));

		decline.ExitCode.ShouldBe(3, decline.Output + decline.Error);
		decline.Output.ShouldContain("[y/N]");
		(await day.SuccessAsync("status")).GetProperty("running").GetProperty("id").GetString().ShouldBe(a);

		var accept = await CommandProcess.RunAsync("python3",
			[script, "y", native, "start", "acme", "work"],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(20));

		accept.ExitCode.ShouldBe(0, accept.Output + accept.Error);
		accept.Output.ShouldContain("[y/N]");
		var status = await day.SuccessAsync("status");
		status.GetProperty("running").GetProperty("id").GetString().ShouldNotBe(a);
		status.GetProperty("paused").EnumerateArray().Single().GetProperty("id").GetString().ShouldBe(a);
	}
}
