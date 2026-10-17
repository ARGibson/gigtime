using System.Globalization;
using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;
using Gigtime.Domain.Tests.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Reporting;

public sealed class ReportTests
{
	[Fact]
	public void Stop_time_selects_the_clients_week_across_a_utc_week_boundary()
	{
		var s = new TaskScenario();
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Timezone: Change.Set<string?>("America/New_York")));
		var sunday = Recorded(s, "111111", "2026-03-02T04:59:59Z");
		var monday = Recorded(s, "222222", "2026-03-02T05:00:00Z");
		var week = new ReportQuery(At("2026-03-02T06:00:00Z"));

		ReportMath.Calculate([sunday, monday], s.Catalogue.State, week).Single().RawSeconds.ShouldBe(30);

		var previous = new ReportQuery(At("2026-03-01T12:00:00Z"));

		ReportMath.Calculate([sunday, monday], s.Catalogue.State, previous).Single().RawSeconds.ShouldBe(30);

		var local = ReportPeriod.For(week, s.Catalogue.State.Resolve(s.Catalogue.ProjectId));

		local.From.ShouldBe(new(2026, 3, 2));
		local.Contains(sunday.StoppedAt!.Value, "America/New_York").ShouldBeFalse();
		local.Contains(monday.StoppedAt!.Value, "America/New_York").ShouldBeTrue();
	}

	[Fact]
	public void Amending_the_stop_time_moves_report_membership_across_the_clients_week_boundary()
	{
		var s = new TaskScenario();
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Timezone: Change.Set<string?>("America/New_York")));
		s.Send(new StartTask("7f3a9c", s.Catalogue.ProjectId, null, At("2026-03-02T04:59:00Z")));
		s.Send(new StopTask(At("2026-03-02T04:59:30Z")));
		var previousWeek = new ReportQuery(At("2026-03-01T12:00:00Z"));
		var nextWeek = new ReportQuery(At("2026-03-02T06:00:00Z"));

		ReportMath.Calculate([s.Task], s.Catalogue.State, previousWeek).Single().RawSeconds.ShouldBe(30);
		ReportMath.Calculate([s.Task], s.Catalogue.State, nextWeek).ShouldBeEmpty();

		s.Send(new AmendInterval(1, End: At("2026-03-02T05:00:00Z")));

		s.Task.StoppedAt.ShouldBe(At("2026-03-02T05:00:00Z"));
		ReportMath.Calculate([s.Task], s.Catalogue.State, previousWeek).ShouldBeEmpty();
		ReportMath.Calculate([s.Task], s.Catalogue.State, nextWeek).Single().RawSeconds.ShouldBe(60);
	}

	[Fact]
	public void Configured_week_start_and_dst_use_local_dates()
	{
		var s = new TaskScenario();
		s.Catalogue.Send(new SetDefaults("America/New_York", DayOfWeek.Sunday));
		var query = new ReportQuery(At("2026-03-08T12:00:00Z"));
		var period = ReportPeriod.For(query, s.Catalogue.State.Resolve(s.Catalogue.ProjectId));

		period.From.ShouldBe(new(2026, 3, 8));
		period.Contains(At("2026-03-15T03:59:59Z"), "America/New_York").ShouldBeTrue();
		period.Contains(At("2026-03-15T04:00:00Z"), "America/New_York").ShouldBeFalse();
	}

	[Fact]
	public void Report_sums_task_rounding_excludes_open_and_void_and_never_combines_currencies()
	{
		var s = new TaskScenario();
		var first = Recorded(s, "111111", "2026-03-02T09:01:00Z");
		var second = Recorded(s, "222222", "2026-03-02T09:02:00Z");
		var voided = Recorded(s, "333333", "2026-03-02T09:03:00Z");
		var voidDecision = TaskDecider.Decide(voided, s.Tracker, s.Catalogue.State, new VoidTask());

		voidDecision.Error.ShouldBeNull();

		voided.Evolve(voidDecision.Events.Single());
		s.Start();
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));
		var euro = Recorded(s, "444444", "2026-03-02T09:04:00Z");
		var totals = ReportMath.Calculate([first, second, voided, s.Task, euro], s.Catalogue.State, new(TaskScenario.At));

		totals.Count.ShouldBe(2);

		var dollars = totals.Single(t => t.Currency == "USD");

		dollars.RawSeconds.ShouldBe(60);
		dollars.RoundedSeconds.ShouldBe(120);
		dollars.Earnings.ShouldBe(4m);
		totals.Single(t => t.Currency == "EUR").RoundedSeconds.ShouldBe(60);
	}

	[Fact]
	public void Month_range_and_filters_select_only_matching_tasks()
	{
		var s = new TaskScenario();
		var february = Recorded(s, "111111", "2026-02-28T23:59:59Z");
		var march = Recorded(s, "222222", "2026-03-01T00:00:00Z");
		var month = new ReportQuery(TaskScenario.At, PeriodKind.Month);

		ReportMath.Calculate([february, march], s.Catalogue.State, month).Single().RawSeconds.ShouldBe(30);

		var range = new ReportQuery(TaskScenario.At, PeriodKind.Range, new(2026, 2, 28), new(2026, 3, 1));

		ReportMath.Calculate([february, march], s.Catalogue.State, range).Single().RawSeconds.ShouldBe(30);

		ReportMath
			.Calculate([march],
				s.Catalogue.State,
				month with
				{
					ClientId = Guid.CreateVersion7()
				})
			.ShouldBeEmpty();

		ReportMath
			.Calculate([march],
				s.Catalogue.State,
				month with
				{
					ProjectId = Guid.CreateVersion7()
				})
			.ShouldBeEmpty();

		ReportMath
			.Calculate([march],
				s.Catalogue.State,
				month with
				{
					ClientId = s.Catalogue.ClientId,
					ProjectId = s.Catalogue.ProjectId
				})
			.Count.ShouldBe(1);
	}

	private static DateTimeOffset At(string instant) => DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

	private static TaskView Recorded(TaskScenario s, string id, string stop)
	{
		var task = new TaskView();
		var end = At(stop);

		var result = TaskDecider.Decide(task,
			s.Tracker,
			s.Catalogue.State,
			new RecordTask(id, s.Catalogue.ProjectId, null, end.AddSeconds(-30), end));

		result.Error.ShouldBeNull();

		task.Evolve(result.Events.Single());

		return task;
	}
}
