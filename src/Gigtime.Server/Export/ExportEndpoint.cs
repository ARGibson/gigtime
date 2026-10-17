using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Gigtime.Server.Export;

public static class ExportEndpoint
{
	private static readonly JsonSerializerOptions json = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	public static EnvelopeResult? Validate(string? from, string? to, DateTimeOffset now)
	{
		return ExportWindow.TryResolve(from, to, now, out _, out _)
			? null
			: new("export", new("validation", "Supply ordered ISO times or UTC dates for from/to."));
	}

	public static EventExport Load(Func<EventQuery, CancellationToken, Task<PagedEvents>> read,
		string? from,
		string? to,
		DateTimeOffset now,
		CancellationToken token)
	{
		ExportWindow.TryResolve(from, to, now, out var start, out var end);

		return new(LoadAsync(read, start, end, token));
	}

	public static EventExport Handle(EventExport loaded) => loaded;

	private static async IAsyncEnumerable<ExportEventDto> LoadAsync(Func<EventQuery, CancellationToken, Task<PagedEvents>> read,
		DateTimeOffset? start,
		DateTimeOffset? end,
		[EnumeratorCancellation] CancellationToken token)
	{
		var query = new EventQuery
		{
			PageSize = 100,
			TimestampFrom = start,
			TimestampTo = end
		};

		var remaining = long.MaxValue;

		while (remaining > 0)
		{
			var page = await read(query, token);
			if (remaining == long.MaxValue) remaining = page.TotalCount;

			if (page.Events.Count == 0) yield break;

			foreach (var item in page.Events)
			{
				if (remaining-- <= 0) yield break;

				yield return new(item.Sequence,
					item.EventTypeName,
					item.StreamKey ?? item.StreamId.ToString(),
					item.Version,
					item.Timestamp,
					item.CorrelationId,
					JsonSerializer.SerializeToElement(item.Data,
						item.Data.GetType(),
						json));

				query.SequenceFloor = item.Sequence + 1;
			}
		}
	}
}
