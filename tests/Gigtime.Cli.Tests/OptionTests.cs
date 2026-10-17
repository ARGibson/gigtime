using System.Globalization;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class OptionTests
{
	[Theory]
	[InlineData("client set example --clear-timezone --clear-rounding", "client set", "clear_timezone", "clear_rounding")]
	[InlineData("project set example work --clear-rate --clear-rounding", "project set", "clear_rate", "clear_rounding")]
	public async Task Clear_flags_are_sent_as_explicit_clears(string command, string response, string first, string second)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response(response));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		using var body = JsonDocument.Parse(server.Requests.ShouldHaveSingleItem().Body);
		body.RootElement.GetProperty(first).GetBoolean().ShouldBeTrue();
		body.RootElement.GetProperty(second).GetBoolean().ShouldBeTrue();
		body.RootElement.GetProperty("rounding").ValueKind.ShouldBe(JsonValueKind.Null);
	}

	[Theory]
	[InlineData("log", "log", "/v1/tasks")]
	[InlineData("project list", "project list", "/v1/projects")]
	[InlineData("client list", "client list", "/v1/clients")]
	public async Task Omitted_filters_preserve_service_defaults(string command, string response, string path)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response(response));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		server.Requests.ShouldHaveSingleItem().Path.ShouldBe(path);
	}

	[Fact]
	public async Task Json_can_precede_the_command_and_queries_are_encoded()
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("log"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, true, false, () => paths);

		(await app.RunAsync(["--json", "log", "--client", "a&b", "--project", "x y"], TestContext.Current.CancellationToken)).ShouldBe(0);
		server.Requests.ShouldHaveSingleItem().Path.ShouldBe("/v1/tasks?client=a%26b&project=x%20y");
		using var body = JsonDocument.Parse(output.ToString());
		body.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
		output.ToString().ShouldNotContain("\u001b");
	}

	[Theory]
	[InlineData("start example work", "start", "ref")]
	[InlineData("project add example work", "project add", "rate")]
	[InlineData("defaults set --timezone UTC", "defaults set", "rounding")]
	public async Task Omitted_options_stay_null_in_requests(string command, string response, string field)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response(response));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(command.Split(' '), TestContext.Current.CancellationToken)).ShouldBe(0);
		using var body = JsonDocument.Parse(server.Requests.ShouldHaveSingleItem().Body);
		body.RootElement.GetProperty(field).ValueKind.ShouldBe(JsonValueKind.Null);
	}
}
