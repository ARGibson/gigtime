namespace Gigtime.Server.Catalogue;

public static class AddClientEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(AddClientRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CatalogueWrites.LoadAsync(events,
			context,
			_ => new AddClient(Guid.CreateVersion7(), request.Key, request.Name, request.Currency, request.Timezone, request.Rounding),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePost("/v1/clients")]
	public static (Envelope<ClientDto>, AppendEvents) Handle(AddClientRequest request, [NotBody] LoadedCatalogue loaded)
	{
		return (
			Envelopes.Success(loaded.Command.Command,
				CatalogueResponses.Client(CatalogueWrites.After(loaded), ((AddClient)loaded.Command).ClientId)), CatalogueWrites.Append(loaded));
	}
}
