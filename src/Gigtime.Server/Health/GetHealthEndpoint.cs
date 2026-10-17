namespace Gigtime.Server.Health;

public static class GetHealthEndpoint
{
	[WolverineGet("/v1/health")]
	public static Envelope<HealthDto> Handle() => Envelopes.Success("health", new HealthDto("ok"));
}
