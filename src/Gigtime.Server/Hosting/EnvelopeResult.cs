namespace Gigtime.Server.Hosting;

public sealed record EnvelopeResult(string Command, ErrorDto Error) : IResult
{
	public async Task ExecuteAsync(HttpContext httpContext)
	{
		httpContext.Response.StatusCode = Error.Code switch
		{
			"validation" => 422,
			"not_found" => 404,
			"invalid_transition" or "conflict" => 409,
			"unavailable" => 503,
			_ => 500,
		};

		await httpContext.Response.WriteAsJsonAsync(Envelopes.Failure<AcknowledgementDto>(Command, Error));
	}

	public static EnvelopeResult? From(Decision decision, string? taskId = null)
	{
		return decision.Error is { } error
			? new(error.Command,
				new(error.Code,
					error.Message,
					new(error.TaskId ?? (string.IsNullOrEmpty(taskId) ? null : taskId), error.State, error.Command, error.Tasks),
					error.Hint))
			: null;
	}
}
