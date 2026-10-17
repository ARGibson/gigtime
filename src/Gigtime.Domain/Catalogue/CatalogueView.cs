namespace Gigtime.Domain.Catalogue;

public sealed record Defaults(string Timezone, DayOfWeek WeekStarts = DayOfWeek.Monday, string Rounding = "nearest-minute");
public sealed record Client(Guid Id, string Key, string Name, string Currency, string? Timezone, string? Rounding, bool Archived = false);
public sealed record Project(Guid Id, Guid ClientId, string Key, string? Name, decimal? Rate, string? Rounding, bool Archived = false);
public sealed record ResolvedSettings(string Timezone, DayOfWeek WeekStarts, string Rounding, decimal? Rate, string Currency);

/// <summary>Self-aggregating singleton catalogue; the host registers the store-specific projection (#2).</summary>
public sealed class CatalogueView
{
  public const string StreamId = "catalogue";
  public string Id { get; init; } = StreamId;
  public Defaults Defaults { get; set; } = new(TimeZoneInfo.Local.Id);
  public Dictionary<Guid, Client> Clients { get; init; } = [];
  public Dictionary<Guid, Project> Projects { get; init; } = [];

  public Client? FindClient(Guid id) => Clients.GetValueOrDefault(id);
  public Client? FindClient(string key) => Clients.Values.SingleOrDefault(c => c.Key == key);
  public Project? FindProject(Guid id) => Projects.GetValueOrDefault(id);
  public Project? FindProject(Guid clientId, string key) => Projects.Values.SingleOrDefault(p => p.ClientId == clientId && p.Key == key);

  public ResolvedSettings Resolve(Guid projectId)
  {
	var project = Projects[projectId];
	var client = Clients[project.ClientId];
	return new(client.Timezone ?? Defaults.Timezone, Defaults.WeekStarts,
		project.Rounding ?? client.Rounding ?? Defaults.Rounding, project.Rate, client.Currency);
  }

  public void Apply(DefaultsChanged e) => Defaults = new(e.Timezone ?? Defaults.Timezone, e.WeekStarts ?? Defaults.WeekStarts, e.Rounding ?? Defaults.Rounding);
  public void Apply(ClientAdded e) => Clients.Add(e.ClientId, new(e.ClientId, e.Key, e.Name, e.Currency, e.Timezone, e.Rounding));
  public void Apply(ClientChanged e)
  {
	var c = Clients[e.ClientId];
	Clients[e.ClientId] = c with { Key = e.Key.Resolve(c.Key), Name = e.Name.Resolve(c.Name), Currency = e.Currency.Resolve(c.Currency), Timezone = e.Timezone.Resolve(c.Timezone), Rounding = e.Rounding.Resolve(c.Rounding) };
  }
  public void Apply(ClientArchived e)
  {
	Clients[e.ClientId] = Clients[e.ClientId] with { Archived = true };
	foreach (var p in Projects.Values.Where(p => p.ClientId == e.ClientId).ToArray())
	  Projects[p.Id] = p with { Archived = true };
  }
  public void Apply(ProjectAdded e) => Projects.Add(e.ProjectId, new(e.ProjectId, e.ClientId, e.Key, e.Name, e.Rate, e.Rounding));
  public void Apply(ProjectChanged e)
  {
	var p = Projects[e.ProjectId];
	Projects[e.ProjectId] = p with { Key = e.Key.Resolve(p.Key), Name = e.Name.Resolve(p.Name), Rate = e.Rate.Resolve(p.Rate), Rounding = e.Rounding.Resolve(p.Rounding) };
  }
  public void Apply(ProjectArchived e) => Projects[e.ProjectId] = Projects[e.ProjectId] with { Archived = true };

  public void Evolve(object e)
  {
	switch (e)
	{
	  case DefaultsChanged x: Apply(x); break;
	  case ClientAdded x: Apply(x); break;
	  case ClientChanged x: Apply(x); break;
	  case ClientArchived x: Apply(x); break;
	  case ProjectAdded x: Apply(x); break;
	  case ProjectChanged x: Apply(x); break;
	  case ProjectArchived x: Apply(x); break;
	  default: throw new ArgumentException("Not a catalogue event.", nameof(e));
	}
  }
}
