using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class HostTests
{
	[Fact]
	public async Task Host_configuration_file_providers_do_not_reload_on_change()
	{
		await using var app = await ServiceHost.StartAsync();
		var configuration = (IConfigurationRoot)app.Host.Services.GetRequiredService<IConfiguration>();
		var files = configuration.Providers.OfType<JsonConfigurationProvider>().ToArray();
		files.ShouldNotBeEmpty();
		files.ShouldAllBe(provider => !provider.Source.ReloadOnChange);
	}

	[Fact]
	public async Task Static_host_serves_health_and_persisted_defaults()
	{
		await using var app = await ServiceHost.StartAsync();
		var health = await app.Host.Scenario(s => s.Get.Url("/v1/health"));
		(await health.ReadAsJsonAsync<Envelope<HealthDto>>()).Ok.ShouldBeTrue();
		var defaults = await app.Host.Scenario(s => s.Get.Url("/v1/defaults"));
		(await defaults.ReadAsJsonAsync<Envelope<DefaultsDto>>()).Data!.Timezone.ShouldBe(TimeZoneInfo.Local.Id);
	}

	[Fact]
	public async Task Catalogue_writes_are_visible_immediately_and_refusals_are_envelopes()
	{
		await using var app = await ServiceHost.StartAsync();
		var added = await app.Host.Scenario(s => s.Post.Json(new AddClientRequest("example", "Example", "GBP")).ToUrl("/v1/clients"));
		(await added.ReadAsJsonAsync<Envelope<ClientDto>>()).Data!.Key.ShouldBe("example");
		var listed = await app.Host.Scenario(s => s.Get.Url("/v1/clients"));
		(await listed.ReadAsJsonAsync<Envelope<ClientsDto>>()).Data!.Clients.ShouldHaveSingleItem();

		var invalid = await app.Host.Scenario(s =>
		{
			s.Post.Json(new AddClientRequest("example", "Example", "GBP")).ToUrl("/v1/clients");
			s.StatusCodeShouldBe(422);
		});

		(await invalid.ReadAsJsonAsync<Envelope<ClientDto>>()).Error!.Code.ShouldBe("validation");
	}
}
