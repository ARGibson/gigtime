namespace Gigtime.Domain.Catalogue;

/// <summary>The catalogue stream serializes key uniqueness, including archived entries.</summary>
public static class CatalogueDecider
{
	public static Decision Decide(CatalogueView state, CatalogueCommand command)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(command);

		return command switch
		{
			SetDefaults c => Defaults(state, c),
			AddClient c => Add(state, c),
			ChangeClient c => Change(state, c),
			ArchiveClient c => state.FindClient(c.ClientId) is null ? Missing(c) : Decision.Accept(new ClientArchived(c.ClientId)),
			AddProject c => Add(state, c),
			ChangeProject c => Change(state, c),
			ArchiveProject c => state.FindProject(c.ProjectId) is null ? Missing(c) : Decision.Accept(new ProjectArchived(c.ProjectId)),
			_ => Invalid(command, "Unknown catalogue command.")
		};
	}

	private static Decision Defaults(CatalogueView state, SetDefaults c)
	{
		if (!CatalogueRules.ValidTimezone(c.Timezone)) return Invalid(c, "Unknown timezone.");
		if (!CatalogueRules.ValidRounding(c.Rounding)) return Invalid(c, "Unsupported rounding; use nearest-minute.");
		if (c.WeekStarts.HasValue && !Enum.IsDefined(c.WeekStarts.Value)) return Invalid(c, "Unknown week-start day.");

		return Decision.Accept(new DefaultsChanged(c.Timezone, c.WeekStarts, c.Rounding));
	}

	private static Decision Add(CatalogueView state, AddClient c)
	{
		if (!CatalogueRules.ValidId(c.ClientId)) return Invalid(c, "Client id must be a UUIDv7.");
		if (state.FindClient(c.ClientId) is not null) return Invalid(c, "Client id already exists.");

		if (!CatalogueRules.ValidKey(c.Key))
			return Invalid(c, "Invalid client key; use 1–32 lowercase letters, digits or hyphens, starting with a letter or digit.");

		if (state.FindClient(c.Key) is not null) return Invalid(c, "Duplicate client key.");
		if (string.IsNullOrWhiteSpace(c.Name)) return Invalid(c, "Client name cannot be blank.");
		if (!CatalogueRules.ValidCurrency(c.Currency)) return Invalid(c, "Unknown ISO 4217 currency.");
		if (!CatalogueRules.ValidTimezone(c.Timezone)) return Invalid(c, "Unknown timezone.");
		if (!CatalogueRules.ValidRounding(c.Rounding)) return Invalid(c, "Unsupported rounding; use nearest-minute.");

		return Decision.Accept(new ClientAdded(c.ClientId, c.Key, c.Name, c.Currency, c.Timezone, c.Rounding));
	}

	private static Decision Change(CatalogueView state, ChangeClient c)
	{
		var old = state.FindClient(c.ClientId);

		if (old is null) return Missing(c);

		var key = c.Key.Resolve(old.Key);

		if (!CatalogueRules.ValidKey(key))
			return Invalid(c, "Invalid client key; use 1–32 lowercase letters, digits or hyphens, starting with a letter or digit.");

		if (state.Clients.Values.Any(x => x.Id != old.Id && x.Key == key)) return Invalid(c, "Duplicate client key.");
		if (string.IsNullOrWhiteSpace(c.Name.Resolve(old.Name))) return Invalid(c, "Client name cannot be blank.");
		if (!CatalogueRules.ValidCurrency(c.Currency.Resolve(old.Currency))) return Invalid(c, "Unknown ISO 4217 currency.");
		if (!CatalogueRules.ValidTimezone(c.Timezone.Resolve(old.Timezone))) return Invalid(c, "Unknown timezone.");
		if (!CatalogueRules.ValidRounding(c.Rounding.Resolve(old.Rounding))) return Invalid(c, "Unsupported rounding; use nearest-minute.");

		return Decision.Accept(new ClientChanged(c.ClientId, c.Key, c.Name, c.Currency, c.Timezone, c.Rounding));
	}

	private static Decision Add(CatalogueView state, AddProject c)
	{
		var client = state.FindClient(c.ClientId);

		if (client is null) return Missing(c);
		if (client.Archived) return Invalid(c, "The client is archived.");
		if (!CatalogueRules.ValidId(c.ProjectId)) return Invalid(c, "Project id must be a UUIDv7.");
		if (state.FindProject(c.ProjectId) is not null) return Invalid(c, "Project id already exists.");

		if (!CatalogueRules.ValidKey(c.Key))
			return Invalid(c, "Invalid project key; use 1–32 lowercase letters, digits or hyphens, starting with a letter or digit.");

		if (state.FindProject(c.ClientId, c.Key) is not null) return Invalid(c, "Duplicate project key within this client.");
		if (c.Rate < 0) return Invalid(c, "Project rate cannot be negative.");
		if (!CatalogueRules.ValidRounding(c.Rounding)) return Invalid(c, "Unsupported rounding; use nearest-minute.");

		return Decision.Accept(new ProjectAdded(c.ProjectId, c.ClientId, c.Key, c.Name, c.Rate, c.Rounding));
	}

	private static Decision Change(CatalogueView state, ChangeProject c)
	{
		var old = state.FindProject(c.ProjectId);

		if (old is null) return Missing(c);

		var key = c.Key.Resolve(old.Key);

		if (!CatalogueRules.ValidKey(key))
			return Invalid(c, "Invalid project key; use 1–32 lowercase letters, digits or hyphens, starting with a letter or digit.");

		if (state.Projects.Values.Any(x => x.Id != old.Id && x.ClientId == old.ClientId && x.Key == key))
			return Invalid(c, "Duplicate project key within this client.");

		if (c.Rate.Resolve(old.Rate) < 0) return Invalid(c, "Project rate cannot be negative.");
		if (!CatalogueRules.ValidRounding(c.Rounding.Resolve(old.Rounding))) return Invalid(c, "Unsupported rounding; use nearest-minute.");

		return Decision.Accept(new ProjectChanged(c.ProjectId, c.Key, c.Name, c.Rate, c.Rounding));
	}

	private static Decision Invalid(CatalogueCommand c, string message) => Decision.Refuse("validation", message, "catalogue", c.Command);

	private static Decision Missing(CatalogueCommand c) => Decision.Refuse("not_found",
		"The client or project does not exist.",
		"catalogue",
		c.Command,
		"gig client list");
}
