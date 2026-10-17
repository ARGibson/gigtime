namespace Gigtime.Server.Catalogue;

public static class ArchiveClientEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(string key,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		return CatalogueWrites.LoadAsync(events, context, s => new ArchiveClient(s.FindClient(key)?.Id ?? Guid.Empty), cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePost("/v1/clients/{key}/archive")]
	public static (Envelope<AcknowledgementDto>, AppendEvents) Handle(string key, [NotBody] LoadedCatalogue loaded)
	{
		return (Envelopes.Success(loaded.Command.Command, new AcknowledgementDto(((ArchiveClient)loaded.Command).ClientId.ToString())),
			CatalogueWrites.Append(loaded));
	}
}
