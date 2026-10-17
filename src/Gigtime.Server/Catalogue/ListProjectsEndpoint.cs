using JasperFx.Events.Documents;

namespace Gigtime.Server.Catalogue;

public static class ListProjectsEndpoint
{
	public static async Task<CatalogueView> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		return (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
	}

	public static EnvelopeResult? Validate([NotBody] CatalogueView catalogue, string? client = null)
	{
		return client is not null && catalogue.FindClient(client) is null
			? new("project list", new("not_found", "The client does not exist."))
			: null;
	}

	[WolverineGet("/v1/projects")]
	public static Envelope<ProjectsDto> Handle([NotBody] CatalogueView catalogue, string? client = null, bool all = false)
	{
		var clientId = client is null ? (Guid?)null : catalogue.FindClient(client)!.Id;

		var projects = catalogue
			.Projects.Values
			.Where(p => (all || !p.Archived) && (clientId is null || p.ClientId == clientId))
			.OrderBy(p => catalogue.Clients[p.ClientId].Key)
			.ThenBy(p => p.Key)
			.Select(p => CatalogueResponses.Project(catalogue, p.Id))
			.ToArray();

		return Envelopes.Success("project list", new ProjectsDto(projects));
	}
}
