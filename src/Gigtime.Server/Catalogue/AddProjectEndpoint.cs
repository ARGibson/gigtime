namespace Gigtime.Server.Catalogue;

public static class AddProjectEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(string client,
		AddProjectRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CatalogueWrites.LoadAsync(events,
			context,
			s => new AddProject(Guid.CreateVersion7(),
				s.FindClient(client)?.Id ?? Guid.Empty,
				request.Key,
				request.Name,
				CatalogueWrites.Rate(request.Rate),
				request.Rounding),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePost("/v1/clients/{client}/projects")]
	public static (Envelope<ProjectDto>, AppendEvents) Handle(string client, AddProjectRequest request, [NotBody] LoadedCatalogue loaded)
	{
		return (
			Envelopes.Success(loaded.Command.Command,
				CatalogueResponses.Project(CatalogueWrites.After(loaded), ((AddProject)loaded.Command).ProjectId)), CatalogueWrites.Append(loaded));
	}
}
