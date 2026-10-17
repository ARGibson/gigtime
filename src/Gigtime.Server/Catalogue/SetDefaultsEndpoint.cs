namespace Gigtime.Server.Catalogue;

public static class SetDefaultsEndpoint
{
	public static Task<LoadedCatalogue> LoadAsync(SetDefaultsRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CatalogueWrites.LoadAsync(events,
			context,
			_ => new SetDefaults(request.Timezone,
				request.WeekStarts is null ? null : Enum.TryParse<DayOfWeek>(request.WeekStarts, true, out var day) ? day : (DayOfWeek)99,
				request.Rounding),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCatalogue loaded) => EnvelopeResult.From(loaded.Decision);

	[WolverinePost("/v1/defaults")]
	public static (Envelope<DefaultsDto>, AppendEvents) Handle(SetDefaultsRequest request, [NotBody] LoadedCatalogue loaded)
	{
		return (Envelopes.Success(loaded.Command.Command, CatalogueResponses.Defaults(CatalogueWrites.After(loaded))),
			CatalogueWrites.Append(loaded));
	}
}
