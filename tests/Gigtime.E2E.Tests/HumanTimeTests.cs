using System.Globalization;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed partial class HumanTimeTests(PublishedFixture published)
{
	[Fact]
	public async Task Native_human_commands_render_client_times_and_a_cross_midnight_task_while_JSON_stays_UTC()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.SuccessAsync("client", "set", "acme", "--timezone", "America/New_York");
		var start = await day.HumanAsync("start", "acme", "work");
		var task = (await day.SuccessAsync("status")).GetProperty("task");
		var id = task.GetProperty("id").GetString()!;
		task.GetProperty("timezone").GetString().ShouldBe("America/New_York");

		var localStart = TimeZoneInfo
			.ConvertTime(task.GetProperty("started_at").GetDateTimeOffset(),
				TimeZoneInfo.FindSystemTimeZoneById("America/New_York"))
			.ToString("HH:mm", CultureInfo.InvariantCulture);

		foreach (var result in new[]
						{
							start, await day.HumanAsync("status"), await day.HumanAsync("show", id), await day.HumanAsync("pause"),
							await day.HumanAsync("stop"), await day.HumanAsync("log")
						})
		{
			result.Output.ShouldContain(localStart);
			result.Output.Split("Times in America/New_York").Length.ShouldBe(2);
			RoundTripInstant().IsMatch(result.Output).ShouldBeFalse();
		}

		MinuteTaskLine().IsMatch(start.Output).ShouldBeTrue(start.Output);
		start.Output.ShouldContain("Stopped: open");
		start.Output.ShouldContain("running");
		var historical = await day.SuccessAsync("add", "acme", "work", "--from", "2026-03-03T04:30:00Z", "--to", "2026-03-03T05:30:00Z");
		historical.GetProperty("started_at").GetString().ShouldBe("2026-03-03T04:30:00Z");
		historical.GetProperty("stopped_at").GetString().ShouldBe("2026-03-03T05:30:00Z");
		historical.GetProperty("intervals")[0].GetProperty("start").GetString().ShouldBe("2026-03-03T04:30:00Z");

		foreach (var result in new[] { await day.HumanAsync("show", historical.GetProperty("id").GetString()!), await day.HumanAsync("log") })
		{
			result.Output.ShouldContain("23:30");
			result.Output.ShouldContain("00:30");
			result.Output.ShouldContain("01:00:00");
			result.Output.Split("Times in America/New_York").Length.ShouldBe(2);
			RoundTripInstant().IsMatch(result.Output).ShouldBeFalse();
		}
	}

	[GeneratedRegex(@"Started: (?:\d{4}-\d{2}-\d{2} )?\d{2}:\d{2}  Stopped: (?:open|(?:\d{4}-\d{2}-\d{2} )?\d{2}:\d{2})\r?\n")]
	private static partial Regex MinuteTaskLine();

	[GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}")]
	private static partial Regex RoundTripInstant();
}
