using Alba;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gigtime.Server.Tests;

public sealed class ServiceHost : IAsyncDisposable
{
	public string DirectoryPath { get; private init; } = Path.Combine(Path.GetTempPath(), $"gigtime-test-{Guid.NewGuid():N}");
	public IAlbaHost Host { get; private set; } = null!;
	public TestClock Clock { get; } = new();

	public static async Task<ServiceHost> StartAsync(string? directory = null, TimeZoneInfo? hostTimezone = null)
	{
		var fixture = directory is null
			? new ServiceHost()
			: new ServiceHost
			{
				DirectoryPath = directory
			};

		if (hostTimezone is not null) fixture.Clock.HostTimezone = hostTimezone;
		await fixture.StartHostAsync();

		return fixture;
	}

	public async Task RestartAsync()
	{
		await Host.DisposeAsync();
		await StartHostAsync();
	}

	private async Task StartHostAsync()
	{
		Host = await AlbaHost.For<Program>(builder => builder.ConfigureServices(services =>
		{
			services.RemoveAll<IPlatformPaths>();
			services.AddSingleton<IPlatformPaths>(new TestPaths(DirectoryPath));
			services.RemoveAll<TimeProvider>();
			services.AddSingleton<TimeProvider>(Clock);
		}));
	}

	public async Task<Envelope<T>> GetAsync<T>(string url, int status = 200) where T : class
	{
		var response = await Host.Scenario(s =>
		{
			s.Get.Url(url);
			s.StatusCodeShouldBe(status);
		});

		return (await response.ReadAsJsonAsync<Envelope<T>>());
	}

	public async Task<Envelope<T>> PostAsync<T>(string url, object? body = null, int status = 200, string? correlationId = null)
		where T : class
	{
		var response = await Host.Scenario(s =>
		{
			if (body is null) s.Post.Url(url);
			else s.Post.Json(body).ToUrl(url);

			if (correlationId is not null) s.WithRequestHeader("X-Correlation-Id", correlationId);
			s.StatusCodeShouldBe(status);
		});

		return (await response.ReadAsJsonAsync<Envelope<T>>());
	}

	public async Task<Envelope<T>> PatchAsync<T>(string url, object body, int status = 200) where T : class
	{
		var response = await Host.Scenario(s =>
		{
			s.Patch.Json(body).ToUrl(url);
			s.StatusCodeShouldBe(status);
		});

		return (await response.ReadAsJsonAsync<Envelope<T>>());
	}

	public async Task CreateProjectAsync()
	{
		await PostAsync<ClientDto>("/v1/clients", new AddClientRequest("example", "Example", "GBP"));
		await PostAsync<ProjectDto>("/v1/clients/example/projects", new AddProjectRequest("project", Rate: "12.50"));
	}

	public async ValueTask DisposeAsync()
	{
		await Host.DisposeAsync();
		Directory.Delete(DirectoryPath, true);
	}
}

public sealed record TestPaths(string Directory) : IPlatformPaths
{
	public string DataDirectory => Path.Combine(Directory, "data");
	public string RuntimeDirectory => Path.Combine(Directory, "runtime");
	public string ConfigDirectory => Path.Combine(Directory, "config");
}
