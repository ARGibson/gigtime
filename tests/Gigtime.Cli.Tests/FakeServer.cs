using System.Collections.Concurrent;
using Gigtime.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gigtime.Cli.Tests;

internal sealed class FakeServer(WebApplication application, string directory, IPlatformPaths paths) : IAsyncDisposable
{
	internal IPlatformPaths Paths { get; } = paths;
	internal ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

	internal static async Task<FakeServer> StartAsync(string body, int status = 200)
	{
		var directory = Path.Combine(Path.GetTempPath(), "gig-cli-" + Guid.NewGuid().ToString("N"));
		var paths = new FakePaths(Path.Combine(directory, "data"), directory, Path.Combine(directory, "config"));
		Directory.CreateDirectory(Path.Combine(directory, "gigtime"));

		var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
		{
			ContentRootPath = directory,
			EnvironmentName = "Testing",
			Args = [],
		});

		builder.Logging.ClearProviders();
		builder.WebHost.UseKestrel(options => options.ListenUnixSocket(Path.Combine(directory, "gigtime", "gigtimed.sock")));
		var application = builder.Build();
		var server = new FakeServer(application, directory, paths);

		application.Run(async context =>
		{
			using var reader = new StreamReader(context.Request.Body);

			server.Requests.Enqueue(new(context.Request.Method,
				context.Request.Path + context.Request.QueryString,
				await reader.ReadToEndAsync(context.RequestAborted)));

			context.Response.StatusCode = status;
			context.Response.ContentType = "application/json";
			await context.Response.WriteAsync(body, context.RequestAborted);
		});

		await application.StartAsync();

		return server;
	}

	public async ValueTask DisposeAsync()
	{
		await application.DisposeAsync();
		Directory.Delete(directory, true);
	}
}
