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
	if (!CatalogueRules.ValidTimezone(c.Timezone) || !CatalogueRules.ValidRounding(c.Rounding)
		|| (c.WeekStarts.HasValue && !Enum.IsDefined(c.WeekStarts.Value)))
	  return Invalid(c, "Use a known timezone, weekday and nearest-minute rounding.");
	return Decision.Accept(new DefaultsChanged(c.Timezone, c.WeekStarts, c.Rounding));
  }

  private static Decision Add(CatalogueView state, AddClient c)
  {
	if (!CatalogueRules.ValidId(c.ClientId) || state.FindClient(c.ClientId) is not null
		|| !CatalogueRules.ValidKey(c.Key) || state.FindClient(c.Key) is not null
		|| string.IsNullOrWhiteSpace(c.Name) || !CatalogueRules.ValidCurrency(c.Currency)
		|| !CatalogueRules.ValidTimezone(c.Timezone) || !CatalogueRules.ValidRounding(c.Rounding))
	  return Invalid(c, "Supply a new UUIDv7, unique key, display name, ISO 4217 currency and supported settings.");
	return Decision.Accept(new ClientAdded(c.ClientId, c.Key, c.Name, c.Currency, c.Timezone, c.Rounding));
  }

  private static Decision Change(CatalogueView state, ChangeClient c)
  {
	var old = state.FindClient(c.ClientId);
	if (old is null) return Missing(c);
	var key = c.Key.Resolve(old.Key);
	if (!CatalogueRules.ValidKey(key) || state.Clients.Values.Any(x => x.Id != old.Id && x.Key == key)
		|| string.IsNullOrWhiteSpace(c.Name.Resolve(old.Name)) || !CatalogueRules.ValidCurrency(c.Currency.Resolve(old.Currency))
		|| !CatalogueRules.ValidTimezone(c.Timezone.Resolve(old.Timezone)) || !CatalogueRules.ValidRounding(c.Rounding.Resolve(old.Rounding)))
	  return Invalid(c, "Changed client settings must retain a unique key and valid name, currency, timezone and rounding.");
	return Decision.Accept(new ClientChanged(c.ClientId, c.Key, c.Name, c.Currency, c.Timezone, c.Rounding));
  }

  private static Decision Add(CatalogueView state, AddProject c)
  {
	var client = state.FindClient(c.ClientId);
	if (client is null) return Missing(c);
	if (client.Archived) return Invalid(c, "The client is archived.");
	if (!CatalogueRules.ValidId(c.ProjectId) || state.FindProject(c.ProjectId) is not null
		|| !CatalogueRules.ValidKey(c.Key) || state.FindProject(c.ClientId, c.Key) is not null
		|| c.Rate < 0 || !CatalogueRules.ValidRounding(c.Rounding))
	  return Invalid(c, "Supply a new UUIDv7, a key unique within this client, a nonnegative rate and supported rounding.");
	return Decision.Accept(new ProjectAdded(c.ProjectId, c.ClientId, c.Key, c.Name, c.Rate, c.Rounding));
  }

  private static Decision Change(CatalogueView state, ChangeProject c)
  {
	var old = state.FindProject(c.ProjectId);
	if (old is null) return Missing(c);
	var key = c.Key.Resolve(old.Key);
	if (!CatalogueRules.ValidKey(key) || state.Projects.Values.Any(x => x.Id != old.Id && x.ClientId == old.ClientId && x.Key == key)
		|| c.Rate.Resolve(old.Rate) < 0 || !CatalogueRules.ValidRounding(c.Rounding.Resolve(old.Rounding)))
	  return Invalid(c, "Changed project settings must retain a unique key, nonnegative rate and supported rounding.");
	return Decision.Accept(new ProjectChanged(c.ProjectId, c.Key, c.Name, c.Rate, c.Rounding));
  }

  private static Decision Invalid(CatalogueCommand c, string message) => Decision.Refuse("validation", message, "catalogue", c.Command);
  private static Decision Missing(CatalogueCommand c) => Decision.Refuse("not_found", "The client or project does not exist.", "catalogue", c.Command, "gig client list");
}
