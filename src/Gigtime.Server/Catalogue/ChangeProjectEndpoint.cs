namespace Gigtime.Server.Catalogue;

public static class ChangeProjectEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(string client,
		string key,
		ChangeProjectRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CatalogueWrites.LoadAsync(events,
			context,
			s => new ChangeProject(s.FindProject(s.FindClient(client)?.Id ?? Guid.Empty, key)?.Id ?? Guid.Empty,
				request.Key is null ? default : Change.Set(request.Key),
				request.ClearName ? Change.Set<string?>(null) : request.Name is null ? default : Change.Set<string?>(request.Name),
				request.ClearRate ? Change.Set<decimal?>(null) : request.Rate is null ? default : Change.Set(CatalogueWrites.Rate(request.Rate)),
				request.ClearRounding ? Change.Set<string?>(null) : request.Rounding is null ? default : Change.Set<string?>(request.Rounding)),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePatch("/v1/clients/{client}/projects/{key}")]
	public static (Envelope<ProjectDto>, AppendEvents) Handle(string client,
		string key,
		ChangeProjectRequest request,
		[NotBody] LoadedCatalogue loaded)
	{
		return (
			Envelopes.Success(loaded.Command.Command,
				CatalogueResponses.Project(CatalogueWrites.After(loaded), ((ChangeProject)loaded.Command).ProjectId)),
			CatalogueWrites.Append(loaded));
	}
}
