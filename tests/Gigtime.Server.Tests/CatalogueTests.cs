using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class CatalogueTests
{
	[Fact]
	public async Task Defaults_and_catalogue_changes_clear_overrides_and_keep_archived_keys()
	{
		await using var app = await ServiceHost.StartAsync();
		var defaults = await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest("Europe/London", "sunday"));
		defaults.Data!.WeekStarts.ShouldBe("sunday");
		(await app.GetAsync<DefaultsDto>("/v1/defaults")).Data.ShouldBe(defaults.Data);
		(await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(WeekStarts: "bad"), 422)).Error!.Code.ShouldBe("validation");

		(await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(Timezone: "bad/timezone"), 422)).Error!.Code
			.ShouldBe("validation");

		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("example", "Example", "GBP", "UTC"));
		var renamed = await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Key: "renamed", ClearTimezone: true));
		renamed.Data!.Timezone.ShouldBe("Europe/London");
		(await app.GetAsync<ClientsDto>("/v1/clients")).Data!.Clients.ShouldHaveSingleItem().Key.ShouldBe("renamed");
		await app.PostAsync<ProjectDto>("/v1/clients/renamed/projects", new AddProjectRequest("project", "Project", "12.50"));

		var project = await app.PatchAsync<ProjectDto>("/v1/clients/renamed/projects/project",
			new ChangeProjectRequest(Key: "new-key", ClearName: true, ClearRate: true));

		project.Data!.Name.ShouldBeNull();
		project.Data.Rate.ShouldBeNull();
		(await app.GetAsync<ProjectsDto>("/v1/projects?client=renamed")).Data!.Projects.ShouldHaveSingleItem().Key.ShouldBe("new-key");
		await app.PostAsync<AcknowledgementDto>("/v1/clients/renamed/projects/new-key/archive");
		(await app.GetAsync<ProjectsDto>("/v1/projects")).Data!.Projects.ShouldBeEmpty();
		(await app.GetAsync<ProjectsDto>("/v1/projects?all=true")).Data!.Projects.ShouldHaveSingleItem().Archived.ShouldBeTrue();
		await app.PostAsync<AcknowledgementDto>("/v1/clients/renamed/archive");
		(await app.GetAsync<ClientsDto>("/v1/clients")).Data!.Clients.ShouldBeEmpty();
		(await app.GetAsync<ClientsDto>("/v1/clients?all=true")).Data!.Clients.ShouldHaveSingleItem().Archived.ShouldBeTrue();

		(await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("renamed", "Duplicate", "GBP"), 422)).Error!.Code
			.ShouldBe("validation");
	}

	[Fact]
	public async Task Catalogue_writes_refuse_missing_resources_and_invalid_values()
	{
		await using var app = await ServiceHost.StartAsync();

		(await app.PatchAsync<ClientDto>("/v1/clients/missing", new ChangeClientRequest(Name: "Changed"), 404)).Error!.Code
			.ShouldBe("not_found");

		(await app.PostAsync<AcknowledgementDto>("/v1/clients/missing/archive", status: 404)).Error!.Code.ShouldBe("not_found");

		(await app.PostAsync<ProjectDto>("/v1/clients/missing/projects", new AddProjectRequest("project"), 404)).Error!.Code
			.ShouldBe("not_found");

		(await app.PatchAsync<ProjectDto>("/v1/clients/missing/projects/missing", new ChangeProjectRequest(Name: "Changed"), 404)).Error!.Code
			.ShouldBe("not_found");

		(await app.PostAsync<AcknowledgementDto>("/v1/clients/missing/projects/missing/archive", status: 404)).Error!.Code
			.ShouldBe("not_found");

		(await app.GetAsync<ProjectsDto>("/v1/projects?client=missing", 404)).Error!.Code.ShouldBe("not_found");
		(await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("BAD", "Example", "GBP"), 422)).Error!.Code.ShouldBe("validation");
		await app.CreateProjectAsync();

		(await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Currency: "invalid"), 422)).Error!.Code
			.ShouldBe("validation");

		(await app.PostAsync<ProjectDto>("/v1/clients/example/projects", new AddProjectRequest("other", Rate: "not-a-number"), 422)).Error!.Code
			.ShouldBe("validation");

		(await app.PatchAsync<ProjectDto>("/v1/clients/example/projects/project", new ChangeProjectRequest(Rounding: "invalid"), 422)).Error!
			.Code.ShouldBe("validation");
	}
}
