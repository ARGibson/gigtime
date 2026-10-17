using System.Data.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Gigtime.Server.Hosting;

public sealed partial class CommandExceptions(ILogger<CommandExceptions> log) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		var code = exception switch
		{
			EventStreamUnexpectedMaxEventIdException or StreamLockedException => "conflict",
			BadHttpRequestException or System.Text.Json.JsonException => "validation",
			DbException or IOException or TimeoutException => "unavailable",
			_ => "internal",
		};

		var message = code switch
		{
			"conflict" => "State changed while this command was running. Retry the command.",
			"validation" => "The request body or parameters are invalid.",
			"unavailable" => "The store is temporarily unavailable.",
			_ => "The command failed unexpectedly.",
		};

		var command = httpContext.Items["command"] as string ?? "request";
		if (code is "internal" or "unavailable") Failed(log, exception, command, code);
		await new EnvelopeResult(command, new ErrorDto(code, message)).ExecuteAsync(httpContext);

		return true;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Command {Command} failed with {Code}")]
	private static partial void Failed(ILogger log, Exception exception, string command, string code);
}
