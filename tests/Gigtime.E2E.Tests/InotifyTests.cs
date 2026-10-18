using System.Globalization;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class InotifyTests(PublishedFixture published, ITestOutputHelper output)
{
	[Theory]
	[InlineData(0)]
	[InlineData(48)]
	public async Task Published_daemon_watch_count_stays_small_independent_of_working_directory_size(int parents)
	{
		await using var day = new DaySession(published);
		var workingDirectory = Path.Combine(day.Root, "working");
		Directory.CreateDirectory(workingDirectory);

		for (var parent = 0; parent < parents; parent++)
		{
			for (var child = 0; child < 50; child++)
			{
				Directory.CreateDirectory(Path.Combine(workingDirectory,
					parent.ToString(CultureInfo.InvariantCulture),
					child.ToString(CultureInfo.InvariantCulture)));
			}
		}

		var folders = Directory.GetDirectories(workingDirectory, "*", SearchOption.AllDirectories).Length;
		await day.StartAsync(workingDirectory);

		var watches = Directory
			.GetFiles("/proc/" + day.ServerProcessId.ToString(CultureInfo.InvariantCulture) + "/fdinfo")
			.Sum(CountWatches);

		output.WriteLine($"Working directory subfolders: {folders}; daemon inotify watches: {watches}.");
		// #28: configuration does not reload; allow a small fixed margin for runtime changes.
		watches.ShouldBeLessThanOrEqualTo(16);
		await day.CatalogueAsync();
		var task = await day.SuccessAsync("start", "acme", "work");
		(await day.SuccessAsync("stop")).GetProperty("id").GetString().ShouldBe(task.GetProperty("id").GetString());
	}

	private static int CountWatches(string path)
	{
		try
		{
			return File.ReadLines(path).Count(line => line.StartsWith("inotify ", StringComparison.Ordinal));
		}
		catch (FileNotFoundException)
		{
			// #28: sockets and their fdinfo entries can close while /proc is being sampled.
			return 0;
		}
	}
}
