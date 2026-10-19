using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class TimesheetTests
{
	[Fact]
	public async Task Timesheet_splits_local_days_clips_ranges_and_counts_running_time_without_money()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("ny", "New York", "USD", "America/New_York"));
		await app.PostAsync<ProjectDto>("/v1/clients/ny/projects", new AddProjectRequest("work", Rate: "120"));

		var task = (await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("ny", "work", "2026-03-02T04:30:00Z", "2026-03-02T05:30:00Z", "night"))).Data!;

		var sheet = (await app.GetAsync<TimesheetDto>("/v1/timesheet?range=2026-03-01..2026-03-02")).Data!;
		sheet.Days.Select(d => d.Date).ShouldBe(["2026-03-01", "2026-03-02"]);
		sheet.Days.Select(d => d.RawSeconds).ShouldBe([1800L, 1800L]);
		sheet.Days.ShouldAllBe(d => d.Tasks.Single().TaskId == task.Id);
		sheet.Days[0].Tasks[0].Ref.ShouldBe("night");
		sheet.Projects.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
		sheet.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);

		(await app.GetAsync<TimesheetDto>("/v1/timesheet?range=2026-03-02..2026-03-02")).Data!
			.Days
			.ShouldHaveSingleItem()
			.RawSeconds.ShouldBe(1800);

		await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/void", new VoidTaskRequest());
		(await app.GetAsync<TimesheetDto>("/v1/timesheet?range=2026-03-01..2026-03-02")).Data!.Days.ShouldBeEmpty();
		await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("ny", "work"));
		app.Clock.Now = app.Clock.Now.AddSeconds(89);
		var running = (await app.GetAsync<TimesheetDto>("/v1/timesheet")).Data!;
		running.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(89);
		var response = await app.Host.Scenario(s => s.Get.Url("/v1/timesheet"));
		var json = await response.ReadAsTextAsync();
		json.ShouldNotContain("rounded_seconds");
		json.ShouldNotContain("amount");
		json.ShouldNotContain("rate");
	}

	[Fact]
	public async Task Backfilled_time_at_a_backwards_date_transition_appears_in_each_one_day_range()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("sj", "St Johns", "CAD", "America/St_Johns"));
		await app.PostAsync<ProjectDto>("/v1/clients/sj/projects", new AddProjectRequest("work"));

		var task = (await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("sj", "work", "2010-11-07T02:30:00Z", "2010-11-07T02:32:00Z"))).Data!;

		var both = (await app.GetAsync<TimesheetDto>("/v1/timesheet?range=2010-11-06..2010-11-07")).Data!;
		both.Days.Select(day => day.Date).ShouldBe(["2010-11-06", "2010-11-07"]);
		both.Days.Select(day => day.RawSeconds).ShouldBe([60L, 60L]);

		foreach (var date in new[] { "2010-11-06", "2010-11-07" })
		{
			var sheet = (await app.GetAsync<TimesheetDto>($"/v1/timesheet?range={date}..{date}")).Data!;
			var day = sheet.Days.ShouldHaveSingleItem();
			day.Date.ShouldBe(date);
			day.RawSeconds.ShouldBe(60);
			day.Tasks.ShouldHaveSingleItem().TaskId.ShouldBe(task.Id);
			day.Tasks[0].RawSeconds.ShouldBe(60);
			sheet.Projects.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
			sheet.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
		}
	}

	[Theory]
	[InlineData("timesheet")]
	[InlineData("report")]
	public async Task Reports_share_filters_and_validation(string command)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("other", "Other", "USD"));
		await app.PostAsync<ProjectDto>("/v1/clients/other/projects", new AddProjectRequest("project"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("example", "project", "08:00", "08:01"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("other", "project", "08:00", "08:02"));
		var response = await app.Host.Scenario(s => s.Get.Url($"/v1/{command}?client=example&project=project"));
		using var json = JsonDocument.Parse(await response.ReadAsTextAsync());
		var data = json.RootElement.GetProperty("data");
		var tasks = command == "report" ? data.GetProperty("unsubmitted") : data.GetProperty("days")[0].GetProperty("tasks");
		tasks.GetArrayLength().ShouldBe(1);
		tasks[0].GetProperty("client").GetProperty("key").GetString().ShouldBe("example");
		(await app.GetAsync<TimesheetDto>($"/v1/{command}?client=missing", 404)).Error!.Code.ShouldBe("not_found");
		(await app.GetAsync<TimesheetDto>($"/v1/{command}?project=missing", 404)).Error!.Code.ShouldBe("not_found");

		foreach (var query in new[] { "week=true&month=true", "range=bad", "range=2026-03-03..2026-03-01" })
		{
			var error = (await app.GetAsync<TimesheetDto>($"/v1/{command}?{query}", 422)).Error!;
			error.Code.ShouldBe("validation");
			error.Message.ShouldBe("Choose week, month, or an ordered inclusive date range yyyy-MM-dd..yyyy-MM-dd.");
		}
	}

	[Fact]
	public async Task Unsubmit_and_resubmit_move_a_task_between_sections_and_submission_periods()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("example", "project", "08:00", "08:01"))).Data!.Id;
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit");
		(await app.GetAsync<ReportDto>("/v1/report?range=2026-03-02..2026-03-02")).Data!.Totals.Count.ShouldBe(1);
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/unsubmit");
		var pending = (await app.GetAsync<ReportDto>("/v1/report?range=2020-01-01..2020-01-01")).Data!;
		pending.Totals.ShouldBeEmpty();
		pending.Unsubmitted.ShouldHaveSingleItem().TaskId.ShouldBe(id);
		pending.Unsubmitted[0].State.ShouldBe("stopped");
		app.Clock.Now = DateTimeOffset.Parse("2026-04-02T09:00:00Z", CultureInfo.InvariantCulture);
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit");
		(await app.GetAsync<ReportDto>("/v1/report?range=2026-03-01..2026-03-31")).Data!.Totals.ShouldBeEmpty();
		var april = (await app.GetAsync<ReportDto>("/v1/report?range=2026-04-01..2026-04-30")).Data!;
		april.Unsubmitted.ShouldBeEmpty();
		april.Totals.ShouldHaveSingleItem().RoundedSeconds.ShouldBe(60);

		(await app.GetAsync<TimesheetDto>("/v1/timesheet?range=2026-03-02..2026-03-02")).Data!
			.Days
			.ShouldHaveSingleItem()
			.RawSeconds.ShouldBe(60);
	}
}
