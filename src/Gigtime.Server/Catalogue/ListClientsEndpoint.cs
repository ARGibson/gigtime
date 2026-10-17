using JasperFx.Events.Documents;

namespace Gigtime.Server.Catalogue;

public static class ListClientsEndpoint
{
	public static async Task<CatalogueView> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		return (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
	}

	[WolverineGet("/v1/clients")]
	public static Envelope<ClientsDto> Handle([NotBody] CatalogueView catalogue, bool all = false)
	{
		var clients = catalogue
			.Clients.Values.Where(c => all || !c.Archived)
			.OrderBy(c => c.Key)
			.Select(c => CatalogueResponses.Client(catalogue, c.Id))
			.ToArray();

		return Envelopes.Success("client list", new ClientsDto(clients));
	}
}
