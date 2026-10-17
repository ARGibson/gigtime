using System.Globalization;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class ReportTests
{
	[Theory]
	[InlineData("GBP", "0.3", "0.01", "0.02")]
	[InlineData("JPY", "30", "1", "2")]
	[InlineData("BHD", "0.03", "0.001", "0.002")]
	public async Task Earnings_round_once_per_project_and_per_client_currency(string currency,
		string rate,
		string expected,
		string clientExpected)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("one", "One", currency, "UTC"));

		foreach (var key in new[] { "a", "b" })
		{
			await app.PostAsync<ProjectDto>("/v1/clients/one/projects", new AddProjectRequest(key, Rate: rate));

			for (var i = 0; i < 2; i++)
				await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("one", key, "2026-03-02T08:00:00Z", "2026-03-02T08:01:00Z"));
		}

		var report = (await app.GetAsync<ReportDto>("/v1/report")).Data!;
		report.Totals.Count.ShouldBe(2);

		foreach (var total in report.Totals)
		{
			total.RawSeconds.ShouldBe(120);
			total.RoundedSeconds.ShouldBe(120);
			total.EstimatedEarnings!.Rounded.ShouldBe(expected);

			decimal
				.Parse(total.EstimatedEarnings.Unrounded, CultureInfo.InvariantCulture)
				.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
		}

		report.Clients.ShouldHaveSingleItem().RoundedSeconds.ShouldBe(240);
		report.Clients[0].EstimatedEarnings!.Rounded.ShouldBe(clientExpected);

		decimal
			.Parse(report.Clients[0].EstimatedEarnings!.Unrounded, CultureInfo.InvariantCulture)
			.ShouldBe(2 * decimal.Parse(expected, CultureInfo.InvariantCulture));
	}

	[Fact]
	public async Task Client_rounding_uses_unrounded_project_totals_and_currencies_never_mix()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("one", "One", "GBP", "UTC"));

		foreach (var key in new[] { "a", "b" })
		{
			await app.PostAsync<ProjectDto>("/v1/clients/one/projects", new AddProjectRequest(key, Rate: "0.3"));
			await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("one", key, "2026-03-02T08:00:00Z", "2026-03-02T08:01:00Z"));
		}

		await app.PatchAsync<ClientDto>("/v1/clients/one", new ChangeClientRequest(Currency: "USD"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("one", "a", "2026-03-02T08:00:00Z", "2026-03-02T08:01:00Z"));
		var report = (await app.GetAsync<ReportDto>("/v1/report")).Data!;
		report.Clients.Select(t => t.Currency).Order().ShouldBe(["GBP", "USD"]);
		report.Clients.Single(t => t.Currency == "GBP").EstimatedEarnings!.Rounded.ShouldBe("0.01");
		report.Totals.Where(t => t.Currency == "GBP").Select(t => t.EstimatedEarnings!.Rounded).ShouldBe(["0.01", "0.01"]);
		(await app.GetAsync<ReportDto>("/v1/report?client=one&project=b")).Data!.Totals.ShouldHaveSingleItem().Project.Key.ShouldBe("b");
		(await app.GetAsync<ReportDto>("/v1/report?project=b")).Data!.Totals.Count.ShouldBe(1);
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("two", "Two", "EUR", "UTC"));
		await app.PostAsync<ProjectDto>("/v1/clients/two/projects", new AddProjectRequest("b"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("two", "b", "08:00", "08:01"));
		(await app.GetAsync<ReportDto>("/v1/report?client=one&project=b")).Data!.Totals.ShouldHaveSingleItem().Currency.ShouldBe("GBP");
		(await app.GetAsync<ReportDto>("/v1/report?project=b")).Data!.Totals.Count.ShouldBe(2);

		(await app.GetAsync<ReportDto>("/v1/report?client=missing", 404)).Error!.Code.ShouldBe("not_found");
		(await app.GetAsync<ReportDto>("/v1/report?project=missing", 404)).Error!.Code.ShouldBe("not_found");
		(await app.GetAsync<ReportDto>("/v1/report?week=true&month=true", 422)).Error!.Code.ShouldBe("validation");
		(await app.GetAsync<ReportDto>("/v1/report?range=bad", 422)).Error!.Code.ShouldBe("validation");
	}

	[Fact]
	public async Task Report_membership_uses_stop_local_date_across_week_month_and_DST_boundaries()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("ny", "New York", "USD", "America/New_York"));
		await app.PostAsync<ProjectDto>("/v1/clients/ny/projects", new AddProjectRequest("work"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "work", "2026-03-02T04:58:00Z", "2026-03-02T04:59:00Z"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "work", "2026-03-02T04:59:00Z", "2026-03-02T05:01:00Z"));
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(120);
		await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(WeekStarts: "Sunday"));
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(180);

		app.Clock.Now = DateTimeOffset.Parse("2026-03-01T00:30:00Z", CultureInfo.InvariantCulture);
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "work", "2026-03-01T00:00:00Z", "2026-03-01T00:01:00Z"));
		(await app.GetAsync<ReportDto>("/v1/report?month=true")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "work", "2026-03-09T03:58:00Z", "2026-03-09T03:59:00Z"));
		await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "work", "2026-03-09T03:59:00Z", "2026-03-09T04:00:00Z"));
		(await app.GetAsync<ReportDto>("/v1/report?range=2026-03-08..2026-03-08")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(60);
	}
}
