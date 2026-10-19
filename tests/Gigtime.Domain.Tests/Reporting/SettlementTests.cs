using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;
using static Gigtime.Domain.Tests.Reporting.ReportScenario;

namespace Gigtime.Domain.Tests.Reporting;

public sealed class SettlementTests
{
	[Fact]
	public void Submission_instant_selects_the_clients_week_instead_of_the_stop_time()
	{
		var s = new ReportScenario("America/New_York");
		var sunday = s.Record("111111", "2026-02-01T09:00:00Z", "2026-02-01T09:02:00Z");
		var monday = s.Record("222222", "2026-02-01T10:00:00Z", "2026-02-01T10:01:00Z");
		s.Send(sunday, new SubmitTask(At("2026-03-02T04:59:59Z")));
		s.Send(monday, new SubmitTask(At("2026-03-02T05:00:00Z")));
		var current = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		current.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
		current.Clients.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
		current.Unsubmitted.ShouldBeEmpty();
		var previous = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(At("2026-03-01T12:00:00Z")), Now);
		previous.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(120);
	}

	[Fact]
	public void Unsubmit_removes_frozen_totals_and_resubmit_moves_the_task_to_its_new_period()
	{
		var s = new ReportScenario();
		var task = s.Record("111111", "2026-02-01T09:00:00Z", "2026-02-01T10:00:00Z");
		s.Send(task, new SubmitTask(At("2026-03-01T12:00:00Z")));
		var previous = new ReportQuery(At("2026-03-01T12:00:00Z"));
		SettlementMath.Calculate(s.Tasks, s.Catalogue.State, previous, Now).Totals.ShouldHaveSingleItem().Earnings.ShouldBe(120m);
		s.Send(task, new UnsubmitTask(Now));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(180m)));
		var unsubmitted = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, previous, Now);
		unsubmitted.Totals.ShouldBeEmpty();
		unsubmitted.Clients.ShouldBeEmpty();
		unsubmitted.Unsubmitted.ShouldHaveSingleItem().Figures.Earnings.ShouldBe(180m);
		s.Send(task, new SubmitTask(Now));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(240m)));
		SettlementMath.Calculate(s.Tasks, s.Catalogue.State, previous, Now).Totals.ShouldBeEmpty();
		var resubmitted = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		resubmitted.Totals.ShouldHaveSingleItem().Earnings.ShouldBe(180m);
		resubmitted.Unsubmitted.ShouldBeEmpty();
	}

	[Fact]
	public void Unsubmitted_includes_all_editable_states_from_any_period_and_running_time_to_now()
	{
		var s = new ReportScenario();
		var stopped = s.Record("111111", "2025-01-01T09:00:00Z", "2025-01-01T10:00:00Z");
		var paused = s.Start("222222", "2025-01-02T09:00:00Z");
		s.Send(paused, new PauseTask(At("2025-01-02T09:00:20Z")));
		s.Start("333333", "2026-03-05T11:59:30Z", rate: 60m);
		var voided = s.Record("444444", "2026-03-05T09:00:00Z", "2026-03-05T10:00:00Z");
		s.Send(voided, new VoidTask());
		var result = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now.AddMilliseconds(900));
		result.Totals.ShouldBeEmpty();
		result.Unsubmitted.Select(task => task.TaskId).ShouldBe([stopped.Id, paused.Id, "333333"]);
		result.Unsubmitted.Select(task => task.State).ShouldBe([TaskState.Stopped, TaskState.Paused, TaskState.Running]);
		var running = result.Unsubmitted[^1];
		running.Figures.RawSeconds.ShouldBe(30);
		running.Figures.RoundedSeconds.ShouldBe(60);
		running.Figures.Earnings.ShouldBe(1m);
	}

	[Fact]
	public void Settlement_keeps_currencies_separate_and_any_unrated_task_makes_its_group_unrated()
	{
		var s = new ReportScenario();
		var one = s.Record("111111", "2026-03-02T09:00:00Z", "2026-03-02T09:00:30Z");
		s.Send(one, new SubmitTask(Now));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(null)));
		var unrated = s.Record("222222", "2026-03-02T10:00:00Z", "2026-03-02T10:00:30Z");
		s.Send(unrated, new SubmitTask(Now));
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(180m)));
		var euro = s.Record("333333", "2026-03-02T11:00:00Z", "2026-03-02T11:00:30Z");
		s.Send(euro, new SubmitTask(Now));
		var result = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		result.Totals.Select(total => total.Currency).ShouldBe(["EUR", "USD"]);
		result.Totals.Single(total => total.Currency == "USD").ShouldBe(new(s.Catalogue.ClientId, s.Catalogue.ProjectId, "USD", 60, 120, null));
		result.Totals.Single(total => total.Currency == "EUR").Earnings.ShouldBe(3m);
		result.Clients.Single(total => total.Currency == "USD").Earnings.ShouldBeNull();
		result.Clients.Single(total => total.Currency == "EUR").Earnings.ShouldBe(3m);
	}

	[Fact]
	public void Group_amounts_sum_unrounded_and_round_only_the_final_currency_total()
	{
		var s = new ReportScenario();

		foreach (var id in new[] { "111111", "222222" })
		{
			var task = s.Record(id, "2026-03-02T09:00:00Z", "2026-03-02T09:00:30Z", rate: 1m);
			s.Send(task, new SubmitTask(Now));
		}

		var result = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		var amount = result.Totals.ShouldHaveSingleItem().Earnings!.Value;
		amount.ShouldBe(2 * (1m / 60m));
		CurrencyRounding.Round(amount, "USD").ShouldBe(0.03m);
		result.Clients.ShouldHaveSingleItem().Earnings.ShouldBe(amount);
	}

	[Fact]
	public void Stale_currency_and_unrated_provisional_amounts_are_null_but_zero_rate_is_known()
	{
		var s = new ReportScenario();
		s.Record("111111", "2026-03-02T09:00:00Z", "2026-03-02T10:00:00Z", rate: 60m);
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(null)));
		s.Record("222222", "2026-03-02T10:00:00Z", "2026-03-02T11:00:00Z");
		s.Record("333333", "2026-03-02T11:00:00Z", "2026-03-02T12:00:00Z", rate: 0m);
		var result = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now), Now);
		result.Unsubmitted.Select(task => task.Figures.Earnings).ShouldBe([null, null, 0m]);
		result.Unsubmitted.ShouldAllBe(task => task.Figures.Currency == "EUR");
	}

	[Fact]
	public void Current_keys_order_submitted_and_unsubmitted_sections_and_filters_cover_both()
	{
		var s = new ReportScenario();
		var client = Guid.CreateVersion7();
		var project = Guid.CreateVersion7();
		s.Catalogue.Send(new AddClient(client, "zulu", "Zulu", "EUR"));
		s.Catalogue.Send(new AddProject(project, client, "other", Rate: 90m));
		var other = s.Record("111111", "2026-03-02T09:00:00Z", "2026-03-02T10:00:00Z", project);
		s.Send(other, new SubmitTask(Now));
		var original = s.Record("222222", "2026-03-02T10:00:00Z", "2026-03-02T11:00:00Z");
		s.Send(original, new SubmitTask(Now));
		s.Record("333333", "2026-03-02T08:00:00Z", "2026-03-02T09:00:00Z", project);
		s.Record("444444", "2026-03-02T07:00:00Z", "2026-03-02T08:00:00Z");
		s.Catalogue.Send(new ChangeClient(client, Key: Change.Set("aardvark")));
		var result = SettlementMath.Calculate(s.Tasks.AsEnumerable().Reverse(), s.Catalogue.State, new(Now), Now);
		result.Totals.Select(total => total.ClientId).ShouldBe([client, s.Catalogue.ClientId]);
		result.Clients.Select(total => total.ClientId).ShouldBe([client, s.Catalogue.ClientId]);
		result.Unsubmitted.Select(task => task.TaskId).ShouldBe(["333333", "444444"]);
		var selected = SettlementMath.Calculate(s.Tasks, s.Catalogue.State, new(Now, ClientId: client, ProjectId: project), Now);
		selected.Totals.ShouldHaveSingleItem().ProjectId.ShouldBe(project);
		selected.Unsubmitted.ShouldHaveSingleItem().TaskId.ShouldBe("333333");

		var empty = SettlementMath.Calculate(s.Tasks,
			s.Catalogue.State,
			new(Now, ClientId: s.Catalogue.ClientId, ProjectId: project),
			Now);

		empty.Totals.ShouldBeEmpty();
		empty.Unsubmitted.ShouldBeEmpty();
	}

	[Fact]
	public void Month_and_explicit_date_ranges_select_by_submission_time_and_exclude_the_end_boundary()
	{
		var s = new ReportScenario();
		var february = s.Record("111111", "2026-01-01T09:00:00Z", "2026-01-01T09:01:00Z");
		var march = s.Record("222222", "2026-01-01T09:00:00Z", "2026-01-01T09:02:00Z");
		s.Send(february, new SubmitTask(At("2026-02-28T23:59:59Z")));
		s.Send(march, new SubmitTask(At("2026-03-01T00:00:00Z")));

		SettlementMath
			.Calculate(s.Tasks, s.Catalogue.State, new(Now, PeriodKind.Month), Now)
			.Totals.ShouldHaveSingleItem()
			.RawSeconds.ShouldBe(120);

		SettlementMath
			.Calculate(s.Tasks,
				s.Catalogue.State,
				new(Now, PeriodKind.Range, new(2026, 2, 28), new(2026, 3, 1)),
				Now)
			.Totals.ShouldHaveSingleItem()
			.RawSeconds.ShouldBe(60);
	}
}
