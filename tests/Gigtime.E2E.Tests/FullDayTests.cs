using System.Globalization;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class FullDayTests(PublishedFixture published)
{
	[Fact]
	public async Task Published_native_cli_tracks_a_day_corrections_report_export_and_restart()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		(await day.SuccessAsync("defaults", "show")).GetProperty("timezone").GetString().ShouldBe("UTC");
		var task = await day.SuccessAsync("start", "acme", "work", "--ref", "opaque-42");
		var id = task.GetProperty("id").GetString()!;
		(await day.SuccessAsync("status")).GetProperty("task").GetProperty("id").GetString().ShouldBe(id);
		(await day.SuccessAsync("pause")).GetProperty("state").GetString().ShouldBe("paused");
		var resumed = await day.SuccessAsync("resume");
		resumed.GetProperty("state").GetString().ShouldBe("running");
		resumed.GetProperty("intervals").EnumerateArray().Last().GetProperty("number").GetInt32().ShouldBe(2);
		var stopped = await day.SuccessAsync("stop");
		stopped.GetProperty("state").GetString().ShouldBe("stopped");
		(await day.SuccessAsync("status")).GetProperty("task").ValueKind.ShouldBe(JsonValueKind.Null);
		await day.SuccessAsync("project", "set", "acme", "work", "--rate", "99");

		var amended = await day.SuccessAsync("amend",
			id,
			"--interval",
			"1",
			"--start",
			"2026-03-02T09:00:00Z",
			"--end",
			"2026-03-02T10:00:00Z");

		amended.GetProperty("raw_seconds").GetInt64().ShouldBeGreaterThanOrEqualTo(3600);
		amended.GetProperty("snapshot").GetProperty("rate").GetProperty("amount").GetString().ShouldBe("20");
		(await day.SuccessAsync("show", id)).GetRawText().ShouldBe(amended.GetRawText());

		(await day.SuccessAsync("log", "--client", "acme", "--project", "work", "--last", "20"))
			.GetProperty("tasks")
			.EnumerateArray()
			.Select(t => t.GetProperty("id").GetString())
			.ShouldContain(id);

		var date = stopped.GetProperty("stopped_at").GetDateTimeOffset().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		var report = await day.SuccessAsync("report", "--range", date + ".." + date, "--client", "acme", "--project", "work");
		report.GetProperty("totals")[0].GetProperty("raw_seconds").GetInt64().ShouldBe(amended.GetProperty("raw_seconds").GetInt64());
		var human = await day.GigAsync("report", "--range", date + ".." + date);
		human.ExitCode.ShouldBe(0);
		human.Output.ShouldContain("GBP");
		human.Output.ShouldNotContain("\u001b");
		await day.SuccessAsync("void", id, "--reason", "duplicate");
		(await day.SuccessAsync("report", "--range", date + ".." + date)).GetProperty("totals").GetArrayLength().ShouldBe(0);
		(await day.SuccessAsync("log")).GetProperty("tasks").GetArrayLength().ShouldBe(0);
		(await day.SuccessAsync("log", "--all")).GetProperty("tasks").GetArrayLength().ShouldBe(1);

		foreach (var args in new[] { new[] { "export" }, new[] { "export", "--json" } })
		{
			var export = await day.GigAsync(args);
			export.ExitCode.ShouldBe(0, export.Error);
			export.Error.ShouldBeEmpty();
			var events = export.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();

			try
			{
				events.Length.ShouldBeGreaterThan(10);

				events
					.Select(e => e.RootElement.GetProperty("sequence").GetInt64())
					.ShouldBe(Enumerable.Range(1, events.Length).Select(n => (long)n));

				events
					.Any(e => e.RootElement.GetProperty("data").TryGetProperty("reason", out var reason) && reason.GetString() == "duplicate")
					.ShouldBeTrue();
			}
			finally
			{
				foreach (var item in events) item.Dispose();
			}
		}

		await day.RestartAsync();
		(await day.SuccessAsync("show", id)).GetProperty("state").GetString().ShouldBe("voided");
		(await day.SuccessAsync("log", "--all")).GetProperty("tasks").GetArrayLength().ShouldBe(1);
		File.Exists(Path.Combine(day.Environment["XDG_DATA_HOME"], "gigtime", "gigtime.db")).ShouldBeTrue();
	}
}
