namespace Gigtime.Server.Catalogue;

public static class CatalogueResponses
{
	public static DefaultsDto Defaults(CatalogueView catalogue)
	{
		var d = catalogue.Defaults;

		return new(d.Timezone, d.WeekStarts.ToString().ToLowerInvariant(), d.Rounding);
	}

	public static ClientDto Client(CatalogueView catalogue, Guid id)
	{
		var c = catalogue.Clients[id];

		return new(c.Id,
			c.Key,
			c.Name,
			c.Currency,
			c.Timezone ?? catalogue.Defaults.Timezone,
			c.Rounding ?? catalogue.Defaults.Rounding,
			c.Archived);
	}

	public static ProjectDto Project(CatalogueView catalogue, Guid id)
	{
		var p = catalogue.Projects[id];
		var c = catalogue.Clients[p.ClientId];

		return new(p.Id,
			p.Key,
			new(c.Id, c.Key),
			p.Name,
			p.Rate is { } rate ? MoneyDto.From(rate, c.Currency) : null,
			p.Rounding ?? c.Rounding ?? catalogue.Defaults.Rounding,
			p.Archived);
	}
}
