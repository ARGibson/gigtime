using System.Globalization;

namespace Gigtime.Server.Catalogue;

public static class CatalogueWrites
{
	public static async Task<LoadedCatalogue> LoadAsync(
		IEventStoreOperations events,
		HttpContext context,
		Func<CatalogueView, CatalogueCommand> command,
		CancellationToken cancellationToken)
	{
		var stream = await events.FetchForWriting<CatalogueView>(CatalogueView.StreamId, cancellationToken);
		var state = stream.Aggregate ?? new CatalogueView();
		var intent = command(state);
		context.Items["command"] = intent.Command;

		return new(state, stream.StartingVersion ?? 0, intent, CatalogueDecider.Decide(state, intent), context.TraceIdentifier);
	}

	public static CatalogueView After(LoadedCatalogue loaded)
	{
		var state = new CatalogueView
		{
			Defaults = loaded.State.Defaults,
			Clients = new(loaded.State.Clients),
			Projects = new(loaded.State.Projects),
		};

		foreach (var e in loaded.Decision.Events) state.Evolve(e);

		return state;
	}

	public static AppendEvents Append(LoadedCatalogue loaded)
	{
		return Storage.AppendEvents(CatalogueView.StreamId,
			loaded.Version,
			CorrelatedEvents.Wrap(loaded.Decision.Events, loaded.CorrelationId));
	}

	public static decimal? Rate(string? value)
	{
		return value is null ? null :
			decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) ? rate : -1m;
	}
}
