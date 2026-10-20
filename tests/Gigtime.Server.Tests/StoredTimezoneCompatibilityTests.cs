using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain;
using Gigtime.Domain.Catalogue;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class StoredTimezoneCompatibilityTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Unsupported_stored_zone_is_a_validation_refusal_and_catalogue_commands_can_repair_it(bool clientOverride)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(Timezone: "UTC"));
		await app.CreateProjectAsync();

		var task = (await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("example", "project", "2026-03-02T08:00:00Z", "2026-03-02T09:00:00Z"))).Data!;

		await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/submit");
		const string unsupported = "posix/America/New_York";
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();

		// Old host tzdata could accept an id absent from the bundled provider. Current validation
		// deliberately refuses such new input, so reproduce this persisted state at the event boundary.
		await using (var session = factory.LightweightSession())
		{
			object changed = clientOverride
				? new ClientChanged(task.Client.Id, Timezone: Change.Set<string?>(unsupported))
				: new DefaultsChanged(unsupported);

			session.Events.Append(CatalogueView.StreamId, changed);
			await session.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await app.RestartAsync();
		factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();

		await using (var session = factory.QuerySession())
		{
			var catalogue = (await session.LoadAsync<CatalogueView>(CatalogueView.StreamId, TestContext.Current.CancellationToken))!;
			catalogue.Resolve(task.Project.Id).Timezone.ShouldBe(unsupported);
		}

		var report = await app.GetAsync<ReportDto>("/v1/report", 422);
		report.Ok.ShouldBeFalse();
		report.Error!.Code.ShouldBe("validation");
		report.Error.Message.ShouldContain(unsupported);
		var timesheet = await app.GetAsync<TimesheetDto>("/v1/timesheet", 422);
		timesheet.Ok.ShouldBeFalse();
		timesheet.Error!.Code.ShouldBe("validation");
		timesheet.Error.Message.ShouldContain(unsupported);

		if (clientOverride)
			await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Timezone: "UTC"));
		else
			await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(Timezone: "UTC"));

		var repaired = (await app.GetAsync<ReportDto>("/v1/report")).Data!;
		repaired.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
		repaired.Totals[0].Amount!.Rounded.ShouldBe("12.50");
		(await app.GetAsync<TimesheetDto>("/v1/timesheet")).Data!.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
		(await app.GetAsync<TaskDto>("/v1/tasks/" + task.Id)).Data!.State.ShouldBe("submitted");
		await app.RestartAsync();
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
	}
}
