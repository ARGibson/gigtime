using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class TimesheetSettlementTests(PublishedFixture published)
{
	[Fact]
	public async Task Published_day_splits_local_midnight_and_separates_running_work_from_settlement()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.SuccessAsync("client", "add", "ny", "--name", "New York", "--currency", "USD", "--timezone", "America/New_York");
		await day.SuccessAsync("project", "add", "ny", "work", "--rate", "40");
		var acme = await day.SuccessAsync("add", "acme", "work", "--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T10:00:00Z");
		var acmeId = acme.GetProperty("id").GetString()!;
		var ny = await day.SuccessAsync("add", "ny", "work", "--from", "2026-03-03T04:30:00Z", "--to", "2026-03-03T05:30:00Z");
		var nyId = ny.GetProperty("id").GetString()!;
		var timesheet = await day.SuccessAsync("timesheet", "--range", "2026-03-02..2026-03-03");
		var days = timesheet.GetProperty("days").EnumerateArray().ToArray();
		days.Select(d => d.GetProperty("date").GetString()).ShouldBe(["2026-03-02", "2026-03-03"]);
		days.Select(d => d.GetProperty("raw_seconds").GetInt64()).ShouldBe([5400L, 1800L]);

		days[0]
			.GetProperty("tasks")
			.EnumerateArray()
			.Single(t => t.GetProperty("task_id").GetString() == nyId)
			.GetProperty("raw_seconds")
			.GetInt64()
			.ShouldBe(1800);

		days[1].GetProperty("tasks").EnumerateArray().Single().GetProperty("raw_seconds").GetInt64().ShouldBe(1800);
		timesheet.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("raw_seconds").GetInt64()).ShouldBe([3600L, 3600L]);
		timesheet.GetProperty("clients").EnumerateArray().Select(c => c.GetProperty("raw_seconds").GetInt64()).ShouldBe([3600L, 3600L]);
		timesheet.GetRawText().ShouldNotContain("rounded_seconds");
		timesheet.GetRawText().ShouldNotContain("amount");
		var filtered = await day.SuccessAsync("timesheet", "--range", "2026-03-02..2026-03-03", "--client", "ny", "--project", "work");
		filtered.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("raw_seconds").GetInt64()).ShouldBe([1800L, 1800L]);
		(await day.HumanAsync("timesheet", "--range", "2026-03-02..2026-03-03")).Output.ShouldContain("2026-03-02");

		var submitted = await day.SuccessAsync("submit", acmeId);
		var settlement = await day.SuccessAsync("report");

		settlement
			.GetProperty("totals")
			.EnumerateArray()
			.Single()
			.GetProperty("amount")
			.GetProperty("rounded")
			.GetString()
			.ShouldBe("20.00");

		settlement.GetProperty("unsubmitted").EnumerateArray().Single().GetProperty("task_id").GetString().ShouldBe(nyId);
		var running = await day.SuccessAsync("start", "acme", "work");
		var runningId = running.GetProperty("id").GetString()!;
		var elapsed = Stopwatch.StartNew();
		long seconds;

		do
		{
			seconds = (await day.SuccessAsync("status")).GetProperty("task").GetProperty("raw_seconds").GetInt64();
			if (seconds < 1) await Task.Delay(25, TestContext.Current.CancellationToken);
		} while (seconds < 1 && elapsed.Elapsed < TimeSpan.FromSeconds(3));

		seconds.ShouldBeGreaterThanOrEqualTo(1);
		var current = await day.SuccessAsync("timesheet");

		current
			.GetProperty("days")
			.EnumerateArray()
			.SelectMany(d => d.GetProperty("tasks").EnumerateArray())
			.Single(t => t.GetProperty("task_id").GetString() == runningId)
			.GetProperty("raw_seconds")
			.GetInt64()
			.ShouldBeGreaterThanOrEqualTo(seconds);

		var pending = await day.SuccessAsync("report", "--range", "2000-01-01..2000-01-01");
		pending.GetProperty("totals").GetArrayLength().ShouldBe(0);

		pending
			.GetProperty("unsubmitted")
			.EnumerateArray()
			.Select(t => t.GetProperty("task_id").GetString())
			.Order()
			.ShouldBe(new[] { nyId, runningId }.Order());

		pending
			.GetProperty("unsubmitted")
			.EnumerateArray()
			.Single(t => t.GetProperty("task_id").GetString() == runningId)
			.GetProperty("state")
			.GetString()
			.ShouldBe("running");

		await day.SuccessAsync("unsubmit", acmeId);
		var unsubmitted = await day.SuccessAsync("report");
		unsubmitted.GetProperty("totals").GetArrayLength().ShouldBe(0);
		unsubmitted.GetProperty("unsubmitted").GetArrayLength().ShouldBe(3);
		var resubmitted = await day.SuccessAsync("submit", acmeId);

		resubmitted
			.GetProperty("submission")
			.GetProperty("submitted_at")
			.GetDateTimeOffset()
			.ShouldBeGreaterThanOrEqualTo(submitted.GetProperty("submission").GetProperty("submitted_at").GetDateTimeOffset());

		var settledAgain = await day.SuccessAsync("report");
		settledAgain.GetProperty("totals").GetArrayLength().ShouldBe(1);
		settledAgain.GetProperty("unsubmitted").GetArrayLength().ShouldBe(2);
		var nySettlement = await day.SuccessAsync("report", "--client", "ny", "--project", "work");
		nySettlement.GetProperty("totals").GetArrayLength().ShouldBe(0);
		nySettlement.GetProperty("unsubmitted").EnumerateArray().Single().GetProperty("task_id").GetString().ShouldBe(nyId);
		(await day.HumanAsync("report")).Output.ShouldContain("Unsubmitted (provisional)");
	}
}
