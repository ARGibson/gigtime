using System.Text.Json;
using Gigtime.Server.Export;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Hosting;

public static class ExportHttp
{
	public static void MapEventExport(this WebApplication app)
	{
		// #4: JSON Lines needs an HTTP streaming adapter; the feature returns a typed, paged event sequence.
		app.MapGet("/v1/export",
			async (HttpContext context, IDocumentSessionFactory factory, TimeProvider clock,
				string? from, string? to) =>
			{
				context.Items["command"] = "export";
				var now = clock.GetUtcNow();

				await using var session = factory.QuerySession();
				// #4: the shared session read tier lacks paged queries; bind the store API only at the hosting boundary.
				var events = ((Fisher.IQuerySession)session).Events;
				var loaded = ExportEndpoint.Load(events.QueryEventsAsync, from, to, now, context.RequestAborted);

				if (ExportEndpoint.Validate(from, to, now) is { } refusal)
				{
					await refusal.ExecuteAsync(context);

					return;
				}

				var export = ExportEndpoint.Handle(loaded);
				context.Response.ContentType = "application/x-ndjson";

				await foreach (var item in export.Events.WithCancellation(context.RequestAborted))
				{
					await JsonSerializer.SerializeAsync(context.Response.Body,
						item,
						GigtimeJsonContext.Default.ExportEventDto,
						context.RequestAborted);

					await context.Response.WriteAsync("\n", context.RequestAborted);
					await context.Response.Body.FlushAsync(context.RequestAborted);
				}
			});
	}
}
