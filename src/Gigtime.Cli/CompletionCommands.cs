using System.CommandLine;
using System.Net.Sockets;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Platform;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class CompletionCommands
{
	internal static void Configure(RootCommand root, TextWriter output, Func<IPlatformPaths> paths)
	{
		var completion = Add(root, "completion", "Print shell completion code.");
		var zsh = Add(completion, "zsh", "Print the _gig zsh completion script.");
		zsh.SetAction(_ => ZshCompletion.Write(output));
		var live = Add(root, "__complete", "Live shell completion.");
		live.Hidden = true;
		Argument(live, "kind");
		Argument(live, "prefix", true);
		Text(live, "client");

		live.SetAction(async (r, ct) =>
		{
			await CompleteAsync(r.GetValue<string>("kind")!,
				r.GetValue<string>("prefix") ?? "",
				r.GetValue<string>("--client"),
				output,
				paths,
				ct);

			return 0;
		});
	}

	private static async Task CompleteAsync(string kind,
		string prefix,
		string? clientKey,
		TextWriter output,
		Func<IPlatformPaths> paths,
		CancellationToken cancellationToken)
	{
		if (kind is not ("clients" or "projects" or "tasks")) return;

		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(TimeSpan.FromMilliseconds(100));
			var platformPaths = paths();
			var socketPath = platformPaths.Instance.SocketPath(platformPaths);

			using var transport = new SocketsHttpHandler
			{
				ConnectCallback = async (_, ct) =>
				{
					var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

					try
					{
						await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);

						return new NetworkStream(socket, true);
					}
					catch
					{
						socket.Dispose();

						throw;
					}
				},
			};

			using var http = new HttpClient(transport);

			var path = kind switch
			{
				"clients" => "/v1/clients",
				"projects" => "/v1/projects" + Query(("client", clientKey)),
				_ => "/v1/tasks?last=20"
			};

			var body = await http.GetStringAsync("http://localhost" + path, timeout.Token);

			var values = kind switch
			{
				"clients" => JsonSerializer.Deserialize(body, GigtimeJsonContext.Default.ClientsResponse)?.Data?.Clients.Select(c => c.Key) ?? [],
				"projects" => JsonSerializer.Deserialize(body, GigtimeJsonContext.Default.ProjectsResponse)?.Data?.Projects.Select(p => p.Key)
					?? [],
				_ => JsonSerializer.Deserialize(body, GigtimeJsonContext.Default.TasksResponse)?.Data?.Tasks.Select(t => t.Id) ?? [],
			};

			foreach (var value in values.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal))
			{
				if (!value.Any(char.IsControl))
					output.WriteLine(value);
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
																or JsonException)
		{ }
	}
}
