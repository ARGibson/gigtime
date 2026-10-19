using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;
using NodaTime;
using Shouldly;
using Xunit;
using static Gigtime.Domain.Tests.Reporting.ReportScenario;

namespace Gigtime.Domain.Tests.Reporting;

public sealed class TimesheetTests
{
	[Fact]
	public void Monday_and_thursday_intervals_count_on_their_own_days_and_sum_once_per_task_per_day()
	{
		var s = new ReportScenario();
		var task = s.Start("111111", "2026-03-02T09:00:00Z");
		s.Send(task, new PauseTask(At("2026-03-02T09:00:20Z")));
		s.Send(task, new ResumeTask(At("2026-03-02T10:00:00Z")));
		s.Send(task, new PauseTask(At("2026-03-02T10:00:20Z")));
		s.Send(task, new ResumeTask(At("2026-03-05T11:00:00Z")));
		s.Send(task, new StopTask(At("2026-03-05T11:01:00Z")));
		s.Send(task, new SubmitTask(Now));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		result.Days.Select(day => day.Date).ShouldBe([new(2026, 3, 2), new(2026, 3, 5)]);
		result.Days.Select(day => day.RawSeconds).ShouldBe([40, 60]);
		result.Days[0].Tasks.ShouldHaveSingleItem().ShouldBe(new("111111", s.Catalogue.ClientId, s.Catalogue.ProjectId, "ref-111111", 40));
		result.Projects.ShouldHaveSingleItem().RawSeconds.ShouldBe(100);
		result.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(100);
		SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now).Totals.ShouldHaveSingleItem().RoundedSeconds.ShouldBe(120);
	}

	[Fact]
	public void Non_utc_midnight_splits_thirty_minutes_on_each_day()
	{
		var s = new ReportScenario("America/New_York");
		s.Record("111111", "2026-03-03T04:30:00Z", "2026-03-03T05:30:00Z");
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		result.Days.Select(day => day.Date).ShouldBe([new(2026, 3, 2), new(2026, 3, 3)]);
		result.Days.Select(day => day.RawSeconds).ShouldBe([1800, 1800]);
	}

	[Theory]
	[InlineData("2026-03-08T05:00:00Z", "2026-03-09T04:00:00Z", 2026, 3, 8, 23)]
	[InlineData("2026-11-01T04:00:00Z", "2026-11-02T05:00:00Z", 2026, 11, 1, 25)]
	public void Dst_days_use_their_actual_elapsed_length(string start, string end, int year, int month, int day, long hours)
	{
		var s = new ReportScenario("America/New_York");
		s.Record("111111", start, end);
		var date = new DateOnly(year, month, day);
		var query = new ReportQuery(At(end), PeriodKind.Range, date, date.AddDays(1));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, At(end));
		result.Days.ShouldHaveSingleItem().Date.ShouldBe(date);
		result.Days[0].RawSeconds.ShouldBe(hours * 3600);
	}

	[Theory]
	[InlineData("America/Sao_Paulo", "2018-11-04T02:30:00Z", "2018-11-04T03:30:00Z", "2018-11-03", "2018-11-04", 1800)]
	[InlineData("America/Havana", "2026-11-01T03:30:00Z", "2026-11-01T05:30:00Z", "2026-10-31", "2026-11-01", 5400)]
	[InlineData("Pacific/Apia", "2011-12-30T09:30:00Z", "2011-12-30T10:30:00Z", "2011-12-29", "2011-12-31", 1800)]
	public void Invalid_ambiguous_and_skipped_midnights_keep_each_second_on_its_local_date(
		string zone,
		string start,
		string end,
		string first,
		string second,
		long secondDaySeconds)
	{
		var s = new ReportScenario(zone);
		s.Record("111111", start, end);
		var firstDate = DateOnly.Parse(first, System.Globalization.CultureInfo.InvariantCulture);
		var secondDate = DateOnly.Parse(second, System.Globalization.CultureInfo.InvariantCulture);
		var query = new ReportQuery(At(end), PeriodKind.Range, firstDate, secondDate.AddDays(1));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, At(end));
		result.Days.Select(day => day.Date).ShouldBe([firstDate, secondDate]);
		result.Days.Select(day => day.RawSeconds).ShouldBe([1800, secondDaySeconds]);

		var clipped = TimesheetMath.Calculate(s.Tasks,
			s.Catalogue.State,
			query with
			{
				From = secondDate
			},
			At(end));

		clipped.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(secondDaySeconds);
	}

	[Fact]
	public void A_period_containing_only_a_skipped_local_date_has_no_time_or_phantom_day()
	{
		var s = new ReportScenario("Pacific/Apia");
		s.Record("111111", "2011-12-29T09:00:00Z", "2011-12-31T11:00:00Z");
		var query = new ReportQuery(At("2011-12-31T12:00:00Z"), PeriodKind.Range, new(2011, 12, 30), new(2011, 12, 31));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, query.At);
		result.Days.ShouldBeEmpty();
		result.Projects.ShouldBeEmpty();
		result.Clients.ShouldBeEmpty();
	}

	[Theory]
	[InlineData("0001-01-01T00:00:00Z", "0001-01-01T01:00:00Z", 1, 1, 1, 3600)]
	[InlineData("9999-12-30T23:30:00Z", "9999-12-31T00:30:00Z", 9999, 12, 30, 1800)]
	public void Day_splitting_handles_the_supported_instant_range(
		string start,
		string end,
		int year,
		int month,
		int day,
		long seconds)
	{
		var s = new ReportScenario();
		s.Record("111111", start, end);
		var date = new DateOnly(year, month, day);
		var query = new ReportQuery(At(end), PeriodKind.Range, date, date.AddDays(1));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, At(end));
		result.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(seconds);
	}

	[Fact]
	public void Running_intervals_end_at_now_and_every_non_voided_state_counts()
	{
		var s = new ReportScenario();
		s.Start("111111", "2026-03-05T11:59:00Z");
		var paused = s.Start("222222", "2026-03-05T10:00:00Z");
		s.Send(paused, new PauseTask(At("2026-03-05T10:00:20Z")));
		s.Record("333333", "2026-03-05T09:00:00Z", "2026-03-05T09:00:20Z");
		var submitted = s.Record("444444", "2026-03-05T08:00:00Z", "2026-03-05T08:00:20Z");
		s.Send(submitted, new SubmitTask(Now));
		var voided = s.Record("555555", "2026-03-05T07:00:00Z", "2026-03-05T07:00:20Z");
		s.Send(voided, new VoidTask());
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now.AddMilliseconds(900));
		result.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(120);
		result.Days[0].Tasks.Select(task => task.TaskId).ShouldBe(["444444", "333333", "222222", "111111"]);
		result.Days[0].Tasks.Single(task => task.TaskId == "111111").RawSeconds.ShouldBe(60);
	}

	[Fact]
	public void Period_clipping_excludes_outside_seconds_and_zero_length_spans()
	{
		var s = new ReportScenario("America/New_York");
		s.Record("111111", "2026-03-02T04:30:00Z", "2026-03-02T05:30:00Z");
		s.Record("222222", "2026-03-09T03:30:00Z", "2026-03-09T04:30:00Z");
		s.Record("333333", "2026-03-05T09:00:00Z", "2026-03-05T09:00:00Z");
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		result.Days.Select(day => day.Date).ShouldBe([new(2026, 3, 2), new(2026, 3, 8)]);
		result.Days.Select(day => day.RawSeconds).ShouldBe([1800, 1800]);
		result.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
	}

	[Theory]
	[InlineData(6, 8, 2)]
	[InlineData(6, 7, 1)]
	[InlineData(7, 8, 1)]
	public void A_backwards_date_transition_assigns_each_minute_to_its_own_day(int fromDay, int untilDay, int days)
	{
		var s = new ReportScenario("America/St_Johns");
		s.Record("111111", "2010-11-07T02:30:00Z", "2010-11-07T02:32:00Z");
		var query = new ReportQuery(Now, PeriodKind.Range, new(2010, 11, fromDay), new(2010, 11, untilDay));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, Now);
		result.Days.Count.ShouldBe(days);
		result.Days.Select(day => day.Date).ShouldBe(Enumerable.Range(fromDay, days).Select(day => new DateOnly(2010, 11, day)));
		result.Days.ShouldAllBe(day => day.RawSeconds == 60 && day.Tasks.Single().RawSeconds == 60);
		result.Projects.ShouldHaveSingleItem().RawSeconds.ShouldBe(days * 60);
		result.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(days * 60);
	}

	[Fact]
	public void Sitkas_repeated_date_never_moves_the_interval_cursor_backwards()
	{
		var s = new ReportScenario("America/Sitka");
		s.Record("111111", "1867-10-19T00:40:00Z", "1867-10-19T01:40:00Z");
		var query = new ReportQuery(At("1867-10-20T00:00:00Z"), PeriodKind.Range, new(1867, 10, 18), new(1867, 10, 20));
		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, query.At);
		result.Days.Sum(day => day.RawSeconds).ShouldBe(3600);
		result.Projects.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
		result.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
	}

	[Theory]
	[InlineData("America/St_Johns", "2010-11-07T02:30:00Z", "2010-11-07T02:32:00Z", "2010-11-06", "2010-11-08")]
	[InlineData("America/St_Johns", "2010-11-07T02:30:00Z", "2010-11-07T02:32:00Z", "2010-11-06", "2010-11-07")]
	[InlineData("America/St_Johns", "2010-11-07T02:30:00Z", "2010-11-07T02:32:00Z", "2010-11-07", "2010-11-08")]
	[InlineData("Europe/Dublin", "2026-10-25T00:30:00Z", "2026-10-25T02:30:00Z", "2026-10-25", "2026-10-26")]
	[InlineData("Asia/Kathmandu", "1985-12-31T18:00:00Z", "1985-12-31T19:00:00Z", "1985-12-31", "1986-01-02")]
	[InlineData("America/New_York", "2050-03-13T06:30:00Z", "2050-03-13T08:30:00Z", "2050-03-13", "2050-03-14")]
	[InlineData("UTC", "2026-03-01T23:30:00Z", "2026-03-03T00:30:00Z", "2026-03-02", "2026-03-03")]
	[InlineData("America/New_York", "2026-03-08T04:30:00Z", "2026-03-09T04:30:00Z", "2026-03-08", "2026-03-09")]
	[InlineData("America/New_York", "2026-11-01T03:30:00Z", "2026-11-02T05:30:00Z", "2026-11-01", "2026-11-02")]
	[InlineData("America/Havana", "2026-11-01T03:30:00Z", "2026-11-01T05:30:00Z", "2026-10-31", "2026-11-02")]
	[InlineData("America/Sao_Paulo", "2018-11-04T02:30:00Z", "2018-11-04T03:30:00Z", "2018-11-03", "2018-11-05")]
	[InlineData("Pacific/Apia", "2011-12-30T09:30:00Z", "2011-12-30T10:30:00Z", "2011-12-29", "2012-01-01")]
	[InlineData("Pacific/Apia", "2011-12-30T09:30:00Z", "2011-12-30T10:30:00Z", "2011-12-30", "2011-12-31")]
	[InlineData("America/Sitka", "1867-10-19T00:40:00Z", "1867-10-19T01:40:00Z", "1867-10-18", "1867-10-20")]
	[InlineData("America/Sitka", "1867-10-19T00:20:00Z", "1867-10-19T01:40:00Z", "1867-10-18", "1867-10-20")]
	public void Day_slices_match_each_local_dates_seconds_in_the_period(
		string zone,
		string start,
		string end,
		string from,
		string until)
	{
		var s = new ReportScenario(zone);
		s.Record("111111", start, end);

		var query = new ReportQuery(At(end),
			PeriodKind.Range,
			DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
			DateOnly.Parse(until, System.Globalization.CultureInfo.InvariantCulture));

		var period = ReportPeriod.For(query, s.Catalogue.State.Resolve(s.Catalogue.ProjectId));
		var expected = new SortedDictionary<DateOnly, long>();
		var timezone = DateTimeZoneProviders.Tzdb[zone];

		// Check every second's local date independently of transition and midnight enumeration.
		for (var instant = At(start); instant < At(end); instant = instant.AddSeconds(1))
		{
			var date = DateOnly.FromDateTime(Instant.FromDateTimeOffset(instant).InZone(timezone).ToDateTimeUnspecified());
			if (date >= period.From && date < period.UntilExclusive) expected[date] = expected.GetValueOrDefault(date) + 1;
		}

		var result = TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, query, query.At);
		result.Days.Select(day => (day.Date, day.RawSeconds)).ShouldBe(expected.Select(pair => (pair.Key, pair.Value)));
		var total = expected.Values.Sum();
		result.Days.Sum(day => day.RawSeconds).ShouldBe(total);
		result.Days.Sum(day => day.Tasks.Sum(task => task.RawSeconds)).ShouldBe(total);
		result.Projects.Sum(project => project.RawSeconds).ShouldBe(total);
		result.Clients.Sum(client => client.RawSeconds).ShouldBe(total);
		result.Days.ShouldAllBe(day => day.RawSeconds > 0 && day.Tasks.Sum(task => task.RawSeconds) == day.RawSeconds);
	}

	[Fact]
	public void Current_catalogue_keys_order_tasks_and_totals_and_both_filters_apply()
	{
		var s = new ReportScenario();
		var client = Guid.CreateVersion7();
		var project = Guid.CreateVersion7();
		var earlierProject = Guid.CreateVersion7();
		s.Catalogue.Send(new AddClient(client, "zulu", "Zulu", "EUR"));
		s.Catalogue.Send(new AddProject(project, client, "other"));
		s.Catalogue.Send(new AddProject(earlierProject, s.Catalogue.ClientId, "aaa"));
		s.Record("111111", "2026-03-05T07:00:00Z", "2026-03-05T07:01:00Z", project);
		s.Record("222222", "2026-03-05T08:00:00Z", "2026-03-05T08:01:00Z");
		s.Record("333333", "2026-03-05T09:00:00Z", "2026-03-05T09:01:00Z", earlierProject);
		s.Catalogue.Send(new ChangeClient(client, Key: Change.Set("aardvark")));
		var result = TimesheetMath.Calculate(s.Tasks.AsEnumerable().Reverse(), s.Catalogue.State, new(Now), Now);
		result.Days.ShouldHaveSingleItem().Tasks.Select(task => task.TaskId).ShouldBe(["111111", "333333", "222222"]);
		result.Projects.Select(total => total.ProjectId).ShouldBe([project, earlierProject, s.Catalogue.ProjectId]);
		result.Clients.Select(total => total.ClientId).ShouldBe([client, s.Catalogue.ClientId]);

		var filtered = TimesheetMath.Calculate(s.Tasks,
			s.Catalogue.State,
			new(Now, ClientId: s.Catalogue.ClientId, ProjectId: earlierProject),
			Now);

		filtered.Days.ShouldHaveSingleItem().Tasks.ShouldHaveSingleItem().TaskId.ShouldBe("333333");
		TimesheetMath.Calculate(s.Tasks, s.Catalogue.State, new(Now, ClientId: client, ProjectId: earlierProject), Now).Days.ShouldBeEmpty();
	}
}
