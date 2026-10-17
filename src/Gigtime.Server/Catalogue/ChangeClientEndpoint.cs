namespace Gigtime.Server.Catalogue;

public static class ChangeClientEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(string key,
		ChangeClientRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CatalogueWrites.LoadAsync(events,
			context,
			s => new ChangeClient(s.FindClient(key)?.Id ?? Guid.Empty,
				request.Key is null ? default : Change.Set(request.Key),
				request.Name is null ? default : Change.Set(request.Name),
				request.Currency is null ? default : Change.Set(request.Currency),
				request.ClearTimezone ? Change.Set<string?>(null) : request.Timezone is null ? default : Change.Set<string?>(request.Timezone),
				request.ClearRounding ? Change.Set<string?>(null) : request.Rounding is null ? default : Change.Set<string?>(request.Rounding)),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePatch("/v1/clients/{key}")]
	public static (Envelope<ClientDto>, AppendEvents) Handle(string key, ChangeClientRequest request, [NotBody] LoadedCatalogue loaded)
	{
		return (
			Envelopes.Success(loaded.Command.Command,
				CatalogueResponses.Client(CatalogueWrites.After(loaded), ((ChangeClient)loaded.Command).ClientId)), CatalogueWrites.Append(loaded));
	}
}
