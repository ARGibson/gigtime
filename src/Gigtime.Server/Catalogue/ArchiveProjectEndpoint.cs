namespace Gigtime.Server.Catalogue;

public static class ArchiveProjectEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(string client,
		string key,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		return CatalogueWrites.LoadAsync(events,
			context,
			s => new ArchiveProject(s.FindProject(s.FindClient(client)?.Id ?? Guid.Empty, key)?.Id ?? Guid.Empty),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePost("/v1/clients/{client}/projects/{key}/archive")]
	public static (Envelope<AcknowledgementDto>, AppendEvents) Handle(string client, string key, [NotBody] LoadedCatalogue loaded)
	{
		return (Envelopes.Success(loaded.Command.Command, new AcknowledgementDto(((ArchiveProject)loaded.Command).ProjectId.ToString())),
			CatalogueWrites.Append(loaded));
	}
}
