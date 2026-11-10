using System.Reflection;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class HerdrSocketTests(PublishedFixture published, ITestOutputHelper output)
{
	private static readonly string script = Path.GetFullPath(Path.Combine(typeof(HerdrSocketTests)
		.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
		.Single(a => a.Key == "RepositoryRoot").Value!, "integrations/herdr/gig-tab-status"));

	[Fact]
	public async Task Unix_protocol_failures_deadlines_formatting_and_isolation_are_exercised()
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_TEST_CLI"] = Path.Combine(published.Application, "gig");
		var cases = Path.Combine(Path.GetDirectoryName(script)!, "../../tests/Gigtime.E2E.Tests/herdr_socket_cases.py");
		var result = await CommandProcess.RunAsync("python3", ["-I", cases, script],
			day.Root, day.Environment, TimeSpan.FromSeconds(60));
		output.WriteLine(result.Output + result.Error);
		result.ExitCode.ShouldBe(0, result.Output + result.Error);
		result.Error.ShouldContain("Ran 17 tests");
	}

	[Theory]
	[InlineData("", "absolute")]
	[InlineData("dev", "absolute")]
	[InlineData("", "")]
	[InlineData("dev", "relative")]
	[InlineData("", "unset")]
	public async Task Reader_paths_equal_the_real_platform_resolver(string instance, string runtime)
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_INSTANCE"] = instance;
		day.Environment["USER"] = "spoofed";
		day.Environment["LOGNAME"] = "spoofed";
		if (runtime == "unset") day.Environment.Remove("XDG_RUNTIME_DIR");
		else if (runtime != "absolute") day.Environment["XDG_RUNTIME_DIR"] = runtime;
		// #33: probe the read-only platform resolver before any daemon can create its fallback.
		var setup = await day.GigAsync("setup", "--check", "--json");
		using var json = JsonDocument.Parse(setup.Output);
		var expected = json.RootElement.GetProperty("data").GetProperty("socket_path").GetString();
		var resolved = await CommandProcess.RunAsync("python3",
			["-I", "-c", "import runpy,sys; print(runpy.run_path(sys.argv[1])['socket_path']()[0])", script],
			day.Root, day.Environment, TimeSpan.FromSeconds(5));
		resolved.ExitCode.ShouldBe(0, resolved.Error);
		resolved.Output.TrimEnd().ShouldBe(expected);
		Directory.Exists(Path.GetDirectoryName(expected)).ShouldBeFalse();
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.SuccessAsync("start", "acme", "work");
		var line = await CommandProcess.RunAsync(script, [], day.Root, day.Environment, TimeSpan.FromSeconds(4));
		line.ExitCode.ShouldBe(0, line.Error);
		line.Output.ShouldMatch(@"\Aacme/work \d{2,}:\d{2}:\d{2}\n\z");
	}
}
