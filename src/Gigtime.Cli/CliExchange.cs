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
	Option<bool> json)
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
		CancellationToken cancellationToken) where T : class
	{
		var asJson = parsed.GetValue(json);

		using var request = new HttpRequestMessage(method, "http://localhost" + path)
		{
			Content = content
		};

		string socketPath;

		try
		{
			socketPath = Path.Combine(paths().RuntimeDirectory, "gigtime", "gigtimed.sock");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return Fail(command,
				ErrorCodes.Unavailable,
				"Cannot resolve the gigtimed runtime directory: " + exception.Message,
				asJson,
				"Choose a private XDG_RUNTIME_DIR, or correct the reported fallback directory's ownership and mode (0700).");
		}

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

			using var response = await client.SendAsync(request, cancellationToken);
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			var envelope = JsonSerializer.Deserialize(body, responseType);

			if (envelope is null || string.IsNullOrWhiteSpace(envelope.Command)
					|| (envelope.Ok ? !response.IsSuccessStatusCode || envelope.Data is null : envelope.Error is null))
			{
				return Fail(command, ErrorCodes.Internal, "The service returned an invalid envelope.", asJson);
			}

			if (asJson) output.Write(body);
			else if (envelope.Ok) new HumanOutput(output, isTerminal, noColor).Write(envelope.Data!, envelope.Command);
			else WriteError(envelope.Error!);

			return envelope.Ok ? ExitCodes.Success : ExitCodes.FromErrorCode(envelope.Error!.Code);
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException or SocketException or UnauthorizedAccessException
																				or OperationCanceledException)
		{
			return Fail(command,
				ErrorCodes.Unavailable,
				"Cannot reach gigtimed at " + socketPath + ": " + exception.Message,
				asJson,
				"Run gig setup.");
		}
		catch (JsonException)
		{
			return Fail(command, ErrorCodes.Internal, "The service returned invalid JSON.", asJson);
		}
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
