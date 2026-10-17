using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class RequestTests
{
	[Theory]
	[InlineData("{")]
	[InlineData("{}")]
	[InlineData("null")]
	public async Task Malformed_or_incomplete_request_bodies_return_validation_envelopes(string body)
	{
		await using var app = await ServiceHost.StartAsync();

		var response = await app.Host.Scenario(s =>
		{
			s.Post.Text(body).ToUrl("/v1/clients").ContentType("application/json");
			s.StatusCodeShouldBe(422);
		});

		(await response.ReadAsJsonAsync<Envelope<AcknowledgementDto>>()).Error!.Code.ShouldBe("validation");
	}

	[Theory]
	[InlineData("unavailable", 503)]
	[InlineData("internal", 500)]
	public async Task Runtime_exceptions_have_the_contract_code_and_status(string code, int status)
	{
		await using var app = await ServiceHost.StartAsync();
		app.Clock.Failure = code == "unavailable" ? new IOException("Store unavailable") : new InvalidOperationException("Unexpected failure");
		var response = await app.GetAsync<AcknowledgementDto>("/v1/status", status);
		response.Ok.ShouldBeFalse();
		response.Error!.Code.ShouldBe(code);
		response.Error.Message.ShouldNotContain("Unexpected failure");
	}

	[Theory]
	[InlineData("/v1/clients?all=bad")]
	[InlineData("/v1/projects?all=bad")]
	public async Task Invalid_boolean_filters_are_validation_envelopes(string url)
	{
		await using var app = await ServiceHost.StartAsync();
		(await app.GetAsync<AcknowledgementDto>(url, 422)).Error!.Code.ShouldBe("validation");
	}
}
