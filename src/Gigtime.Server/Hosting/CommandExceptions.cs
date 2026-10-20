using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Gigtime.Server.Hosting;

public sealed partial class CommandExceptions(ILogger<CommandExceptions> log) : IExceptionHandler
{
	private const int Busy = 5;
	private const int Locked = 6;
	private const int CantOpen = 14;
	private const int IoError = 10;
	private const int Full = 13;

	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
	{
		var code = exception switch
		{
			EventStreamUnexpectedMaxEventIdException or StreamLockedException => "conflict",
			BadHttpRequestException or System.Text.Json.JsonException or TimeZoneNotFoundException => "validation",
			SqliteException { SqliteErrorCode: Busy or Locked or CantOpen or IoError or Full } => "unavailable",
			IOException or TimeoutException => "unavailable",
			_ => "internal",
		};

		var message = exception is TimeZoneNotFoundException
			? exception.Message
			: code switch
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
