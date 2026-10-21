using System.CommandLine;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Gigtime.Contracts;
using Gigtime.Platform;

namespace Gigtime.Cli;

internal sealed class CliExchange(
	TextWriter output,
	TextWriter error,
	bool isTerminal,
	bool noColor,
	Func<IPlatformPaths> paths,
	Option<bool> json,
	TimeProvider clock,
	TextReader input,
	bool canPrompt)
{
	internal Task<int> GetAsync<T>(ParseResult parsed,
		string command,
		string path,
		JsonTypeInfo<Envelope<T>> responseType,
		CancellationToken cancellationToken) where T : class
	{
		return SendAsync(parsed, command, HttpMethod.Get, path, null, responseType, cancellationToken);
	}

	internal Task<int> WriteAsync<TRequest, TResponse>(ParseResult parsed,
		string command,
		HttpMethod method,
		string path,
		TRequest request,
		JsonTypeInfo<TRequest> requestType,
		JsonTypeInfo<Envelope<TResponse>> responseType,
		CancellationToken cancellationToken) where TResponse : class
	{
		return SendAsync(parsed, command, method, path, JsonContent.Create(request, requestType), responseType, cancellationToken);
	}

	internal async Task<int> SendAsync<T>(ParseResult parsed,
		string command,
		HttpMethod method,
		string path,
		HttpContent? content,
		JsonTypeInfo<Envelope<T>> responseType,
		CancellationToken cancellationToken,
		bool stream = false,
		bool promptAllowed = true) where T : class
	{
		var asJson = parsed.GetValue(json);
		var requestBody = content is null ? null : await content.ReadAsStringAsync(cancellationToken);

		using var request = new HttpRequestMessage(method, "http://localhost" + path)
		{
			Content = content
		};

		string socketPath;

		try
		{
			var p = paths();
			socketPath = p.Instance.SocketPath(p);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return Fail(command,
				ErrorCodes.Unavailable,
				"Cannot resolve the gigtimed runtime directory: " + exception.Message,
				asJson,
				"Choose a private XDG_RUNTIME_DIR, or correct the reported fallback directory's ownership and mode (0700).");
		}

		var exportStarted = false;

		try
		{
			using var transport = new SocketsHttpHandler
			{
				ConnectCallback = async (_, token) =>
				{
					var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

					try
					{
						await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token);

						return new NetworkStream(socket, true);
					}
					catch
					{
						socket.Dispose();

						throw;
					}
				},
			};

			using var client = new HttpClient(transport)
			{
				Timeout = TimeSpan.FromSeconds(10)
			};

			var completion = stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;
			using var response = await client.SendAsync(request, completion, cancellationToken);

			if (stream && response.IsSuccessStatusCode)
			{
				await using var bytes = await response.Content.ReadAsStreamAsync(cancellationToken);
				using var reader = new StreamReader(bytes);

				while (true)
				{
					using var lineTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
					lineTimeout.CancelAfter(TimeSpan.FromSeconds(10));
					var line = await reader.ReadLineAsync(lineTimeout.Token);

					if (line is null) break;

					var item = JsonSerializer.Deserialize(line, GigtimeJsonContext.Default.ExportEventDto);

					if (item is null || string.IsNullOrWhiteSpace(item.Type))
						return Fail(command, ErrorCodes.Internal, "The service returned invalid export data.", asJson && !exportStarted);

					output.WriteLine(line);
					exportStarted = true;
				}

				return ExitCodes.Success;
			}

			using var bodyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			bodyTimeout.CancelAfter(TimeSpan.FromSeconds(10));
			var body = await response.Content.ReadAsStringAsync(bodyTimeout.Token);
			var envelope = JsonSerializer.Deserialize(body, responseType);

			if (envelope is null || string.IsNullOrWhiteSpace(envelope.Command)
					|| (envelope.Ok ? !response.IsSuccessStatusCode || envelope.Data is null : envelope.Error is null))
			{
				return Fail(command, ErrorCodes.Internal, "The service returned an invalid envelope.", asJson);
			}

			if (!envelope.Ok && !asJson && canPrompt && promptAllowed
					&& envelope.Error is { Code: ErrorCodes.InvalidTransition } refusal)
			{
				var retryPath = await CliPrompt.RetryPathAsync(command, path, refusal, input, error, cancellationToken);

				if (retryPath is not null)
				{
					HttpContent? retryContent = null;

					if (command == "start" && requestBody is not null)
					{
						var start = JsonSerializer.Deserialize(requestBody, GigtimeJsonContext.Default.StartTaskRequest)!;

						retryContent = JsonContent.Create(start with
						{
							Switch = true,
							SwitchFrom = refusal.State!.TaskId,
						},
							GigtimeJsonContext.Default.StartTaskRequest);
					}

					return await SendAsync(parsed,
						command,
						method,
						retryPath,
						retryContent,
						responseType,
						cancellationToken,
						promptAllowed: false);
				}
			}

			if (asJson)
			{
				output.Write(body);
				if (!body.EndsWith('\n')) output.WriteLine();
			}
			else if (envelope.Ok)
			{
				new HumanOutput(output, isTerminal, noColor, clock.GetUtcNow()).Write(envelope.Data!, envelope.Command);

				foreach (var warning in envelope.Warnings ?? []) error.WriteLine(warning.Code + ": " + warning.Message);
			}
			else
			{
				WriteError(envelope.Error!);
			}

			return envelope.Ok ? ExitCodes.Success : ExitCodes.FromErrorCode(envelope.Error!.Code);
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or UnauthorizedAccessException
																				or OperationCanceledException)
		{
			return Fail(command,
				ErrorCodes.Unavailable,
				"Cannot reach gigtimed at " + socketPath + ": " + exception.Message,
				asJson && !exportStarted,
				exportStarted ? null : "Run gig setup.");
		}
		catch (TimeZoneNotFoundException exception)
		{
			return Fail(command, ErrorCodes.Validation, exception.Message, asJson && !exportStarted);
		}
		catch (JsonException)
		{
			return Fail(command, ErrorCodes.Internal, "The service returned invalid JSON.", asJson && !exportStarted);
		}
	}

	internal Task<int> ExportAsync(ParseResult parsed, string path, CancellationToken cancellationToken)
	{
		return SendAsync(parsed,
			"export",
			HttpMethod.Get,
			path,
			null,
			GigtimeJsonContext.Default.AcknowledgementResponse,
			cancellationToken,
			true);
	}

	internal int Fail(string command, string code, string message, bool asJson, string? hint = null)
	{
		var failure = Envelopes.Failure<AcknowledgementDto>(command, new(code, message, Hint: hint));

		if (asJson) output.WriteLine(JsonSerializer.Serialize(failure, GigtimeJsonContext.Default.AcknowledgementResponse));
		else WriteError(failure.Error!);

		return ExitCodes.FromErrorCode(code);
	}

	private void WriteError(ErrorDto failure)
	{
		error.WriteLine($"{failure.Code}: {failure.Message}");
		if (failure.Hint is not null) error.WriteLine(failure.Hint);
	}
}
