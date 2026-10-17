using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class CommandTests
{
	public static IEnumerable<object[]> Commands =>
		CommandCases.Names.SelectMany(name => new[] { new object[] { name, false }, [name, true] });

	public static IEnumerable<object[]> Refusals => CommandCases.Names.SelectMany(name =>
		new[]
		{
			("internal", 1), ("usage", 2), ("invalid_transition", 3), ("not_found", 4), ("config", 5), ("unavailable", 6), ("conflict", 7),
			("validation", 8), ("future_code", 1),
		}.SelectMany(item => new[] { new object[] { name, item.Item1, item.Item2, false }, [name, item.Item1, item.Item2, true] }));

	[Theory]
	[MemberData(nameof(Commands))]
	public async Task Commands_send_the_contract_and_render_the_response(string name, bool asJson)
	{
		var response = CommandCases.Response(name);
		await using var server = await FakeServer.StartAsync(response);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		var args = CommandCases.Args(name);
		if (asJson) args = [.. args, "--json"];

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(0);
		error.ToString().ShouldBeEmpty();
		var request = server.Requests.ShouldHaveSingleItem();
		(request.Method, request.Path).ShouldBe(CommandCases.Route(name));
		var expectedBody = CommandCases.Body(name);

		if (expectedBody.Length == 0) request.Body.ShouldBeEmpty();
		else JsonNode.DeepEquals(JsonNode.Parse(expectedBody), JsonNode.Parse(request.Body)).ShouldBeTrue();

		if (asJson)
		{
			JsonNode.DeepEquals(JsonNode.Parse(response), JsonNode.Parse(output.ToString())).ShouldBeTrue();
			output.ToString().TrimEnd().ShouldNotContain("\n");
		}
		else
		{
			output.ToString().ShouldContain(CommandCases.Marker(name));
			output.ToString().ShouldNotContain("\u001b");

			if (name is "show" or "status" or "stop")
			{
				output.ToString().ShouldContain("01:01:01");
				output.ToString().ShouldContain("Interval");
				output.ToString().ShouldContain("2026-03-02T09:00:00");
			}
		}
	}

	[Theory]
	[MemberData(nameof(Refusals))]
	public async Task Every_command_maps_service_refusals(string name, string code, int expected, bool asJson)
	{
		var response = JsonSerializer.Serialize(Envelopes.Failure<AcknowledgementDto>(name,
				new(code, "Canned refusal", new("01JTASK", "running", "stop"), "Canned hint")),
			GigtimeJsonContext.Default.AcknowledgementResponse);

		await using var server = await FakeServer.StartAsync(response, 409);
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		var args = CommandCases.Args(name);
		if (asJson) args = [.. args, "--json"];

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(expected);
		server.Requests.Count.ShouldBe(1);

		if (asJson)
		{
			JsonNode.DeepEquals(JsonNode.Parse(response), JsonNode.Parse(output.ToString())).ShouldBeTrue();
		}
		else
		{
			output.ToString().ShouldBeEmpty();
			error.ToString().ShouldContain(code);
			error.ToString().ShouldContain("Canned hint");
		}
	}
}
