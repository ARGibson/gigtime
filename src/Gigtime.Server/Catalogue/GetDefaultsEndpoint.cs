using JasperFx.Events.Documents;

namespace Gigtime.Server.Catalogue;

public static class GetDefaultsEndpoint
{
	public static async Task<CatalogueView> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		return (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
	}

	[WolverineGet("/v1/defaults")]
	public static Envelope<DefaultsDto> Handle([NotBody] CatalogueView catalogue)
	{
		return Envelopes.Success("defaults show", CatalogueResponses.Defaults(catalogue));
	}
}
