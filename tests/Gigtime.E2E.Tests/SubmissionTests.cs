using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class SubmissionTests(PublishedFixture published)
{
	[Fact]
	public async Task Published_day_fixes_own_rate_at_submission_then_unsubmits_amends_and_resubmits()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		var started = await day.SuccessAsync("start", "acme", "work", "--rate", "30");
		var id = started.GetProperty("id").GetString()!;
		started.GetProperty("rate").GetProperty("amount").GetString().ShouldBe("30");
		await day.SuccessAsync("pause");
		var stopped = await day.SuccessAsync("stop");
		stopped.GetProperty("state").GetString().ShouldBe("stopped");
		stopped.GetProperty("submission").ValueKind.ShouldBe(JsonValueKind.Null);

		await day.SuccessAsync("amend",
			id,
			"--interval",
			"1",
			"--start",
			"2026-03-02T09:00:00Z",
			"--end",
			"2026-03-02T10:00:00Z");

		await day.SuccessAsync("project", "set", "acme", "work", "--rate", "99");
		var submitted = await day.SuccessAsync("submit", id);
		submitted.GetProperty("state").GetString().ShouldBe("submitted");
		var first = submitted.GetProperty("submission");
		first.GetProperty("rounded_seconds").GetInt64().ShouldBe(3600);
		first.GetProperty("rate").GetProperty("amount").GetString().ShouldBe("30");
		first.GetProperty("amount").GetProperty("rounded").GetString().ShouldBe("30.00");
		await day.SuccessAsync("project", "set", "acme", "work", "--rate", "111");
		var shown = await day.SuccessAsync("show", id);
		shown.GetProperty("submission").GetRawText().ShouldBe(first.GetRawText());
		var logged = (await day.SuccessAsync("log")).GetProperty("tasks").EnumerateArray().Single();
		logged.GetProperty("state").GetString().ShouldBe("submitted");
		logged.GetProperty("submission").GetRawText().ShouldBe(first.GetRawText());
		var report = await day.SuccessAsync("report", "--range", "2026-03-02..2026-03-02");
		report.GetProperty("totals")[0].GetProperty("estimated_earnings").GetProperty("rounded").GetString().ShouldBe("30.00");
		(await day.HumanAsync("show", id)).Output.ShouldContain("Submission amount: 30.00 GBP");

		var beforeRefusal = await day.GigAsync("export");
		beforeRefusal.ExitCode.ShouldBe(0, beforeRefusal.Error);
		var refused = await day.GigAsync("amend", id, "--rate", "50", "--json");
		refused.ExitCode.ShouldBe(3, refused.Output + refused.Error);

		using (var json = JsonDocument.Parse(refused.Output))
		{
			var error = json.RootElement.GetProperty("error");
			error.GetProperty("code").GetString().ShouldBe("invalid_transition");
			error.GetProperty("state").GetProperty("task_id").GetString().ShouldBe(id);
			error.GetProperty("hint").GetString()!.ShouldContain("gig unsubmit " + id);
		}

		var humanRefusal = await day.GigAsync("amend", id, "--clear-rate");
		humanRefusal.ExitCode.ShouldBe(3, humanRefusal.Output + humanRefusal.Error);
		humanRefusal.Error.ShouldContain("gig unsubmit " + id);
		var afterRefusal = await day.GigAsync("export");
		afterRefusal.ExitCode.ShouldBe(0, afterRefusal.Error);
		afterRefusal.Output.ShouldBe(beforeRefusal.Output);
		var unsubmitted = await day.SuccessAsync("unsubmit", id);
		unsubmitted.GetProperty("state").GetString().ShouldBe("stopped");
		unsubmitted.GetProperty("submission").ValueKind.ShouldBe(JsonValueKind.Null);

		var amended = await day.SuccessAsync("amend",
			id,
			"--clear-rate",
			"--interval",
			"1",
			"--end",
			"2026-03-02T11:00:00Z");

		amended.GetProperty("rate").ValueKind.ShouldBe(JsonValueKind.Null);
		amended.GetProperty("raw_seconds").GetInt64().ShouldBe(7200);
		var resubmitted = await day.SuccessAsync("submit", id);
		var second = resubmitted.GetProperty("submission");
		second.GetProperty("rate").GetProperty("amount").GetString().ShouldBe("111");
		second.GetProperty("rounded_seconds").GetInt64().ShouldBe(7200);
		second.GetProperty("amount").GetProperty("rounded").GetString().ShouldBe("222.00");

		second
			.GetProperty("submitted_at")
			.GetDateTimeOffset()
			.ShouldBeGreaterThanOrEqualTo(first.GetProperty("submitted_at").GetDateTimeOffset());

		var export = await day.GigAsync("export");
		export.ExitCode.ShouldBe(0, export.Error);
		var events = export.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();

		try
		{
			var taskEvents = events.Where(e => e.RootElement.GetProperty("stream").GetString() == id).ToArray();
			var types = taskEvents.Select(e => e.RootElement.GetProperty("type").GetString()).ToArray();
			types.Count(type => type == "task_submitted").ShouldBe(2);
			types.Count(type => type == "task_unsubmitted").ShouldBe(1);
			types.Count(type => type == "task_rate_set").ShouldBe(2);

			taskEvents
				.Last(e => e.RootElement.GetProperty("type").GetString() == "task_rate_set")
				.RootElement.GetProperty("data")
				.GetProperty("rate")
				.ValueKind.ShouldBe(JsonValueKind.Null);
		}
		finally
		{
			foreach (var item in events) item.Dispose();
		}
	}
}
