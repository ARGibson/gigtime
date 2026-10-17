namespace Gigtime.Server.Hosting;

public sealed class EnvelopeProblems : IProblemDetailsWriter
{
	public bool CanWrite(ProblemDetailsContext context) => context.HttpContext.Request.Path.StartsWithSegments("/v1");

	public async ValueTask WriteAsync(ProblemDetailsContext context)
	{
		var code = context.ProblemDetails.Status switch
		{
			404 => "not_found",
			409 => "conflict",
			503 => "unavailable",
			>= 500 => "internal",
			_ => "validation",
		};

		var command = context.HttpContext.Items["command"] as string ?? "request";

		await new EnvelopeResult(command, new(code, code == "internal" ? "The request failed unexpectedly." : "The request is invalid."))
			.ExecuteAsync(context.HttpContext);
	}
}
