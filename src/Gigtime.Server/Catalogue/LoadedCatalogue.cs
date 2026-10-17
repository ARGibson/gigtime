namespace Gigtime.Server.Catalogue;

public sealed record LoadedCatalogue(CatalogueView State, long Version, CatalogueCommand Command, Decision Decision, string CorrelationId);
