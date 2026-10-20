using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Gigtime.Server.Hosting;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class DefaultTimezoneTests
{
	[Theory]
	[InlineData("posix/Europe/London", "Europe/London")]
	[InlineData("right/Europe/London", "Europe/London")]
	[InlineData("Local", "UTC")]
	[InlineData("GMT Standard Time", "Europe/London")]
	[InlineData("Europe/London", "Europe/London")]
	[InlineData("europe/london", "Europe/London")]
	[InlineData("US/Eastern", "US/Eastern")]
	public void Host_ids_resolve_to_supported_defaults_or_UTC(string hostId, string expected)
	{
		DefaultTimezone.FromHost(hostId).ShouldBe(expected);
	}

	[Theory]
	[InlineData("posix/Europe/London", "Europe/London")]
	[InlineData("right/Europe/London", "Europe/London")]
	[InlineData("Local", "UTC")]
	public async Task A_fresh_store_starts_and_serves_with_a_host_id_outside_TZDB(string hostId, string expected)
	{
		var hostTimezone = TimeZoneInfo.CreateCustomTimeZone(hostId, TimeSpan.Zero, hostId, hostId);
		await using var app = await ServiceHost.StartAsync(hostTimezone: hostTimezone);
		(await app.GetAsync<DefaultsDto>("/v1/defaults")).Data!.Timezone.ShouldBe(expected);
		(await app.GetAsync<TimesheetDto>("/v1/timesheet")).Data!.Days.ShouldBeEmpty();

		await app.CreateProjectAsync();

		await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("example", "project", "2026-03-02T08:00:00Z", "2026-03-02T09:00:00Z"));

		(await app.GetAsync<TimesheetDto>("/v1/timesheet")).Data!.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);

		await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(Timezone: "America/New_York"));
		await app.RestartAsync();
		(await app.GetAsync<DefaultsDto>("/v1/defaults")).Data!.Timezone.ShouldBe("America/New_York");
		(await app.GetAsync<TimesheetDto>("/v1/timesheet")).Data!.Days.ShouldHaveSingleItem().RawSeconds.ShouldBe(3600);
	}
}
