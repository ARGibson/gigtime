namespace Gigtime.Domain.Catalogue;

public abstract record CatalogueCommand(string Command);

public sealed record SetDefaults(string? Timezone = null, DayOfWeek? WeekStarts = null, string? Rounding = null)
	: CatalogueCommand("defaults set");

public sealed record AddClient(Guid ClientId, string Key, string Name, string Currency, string? Timezone = null, string? Rounding = null)
	: CatalogueCommand("client add");

public sealed record ChangeClient(
	Guid ClientId,
	Change<string> Key = default,
	Change<string> Name = default,
	Change<string> Currency = default,
	Change<string?> Timezone = default,
	Change<string?> Rounding = default) : CatalogueCommand("client set");

public sealed record ArchiveClient(Guid ClientId) : CatalogueCommand("client archive");

public sealed record AddProject(
	Guid ProjectId,
	Guid ClientId,
	string Key,
	string? Name = null,
	decimal? Rate = null,
	string? Rounding = null) : CatalogueCommand("project add");

public sealed record ChangeProject(
	Guid ProjectId,
	Change<string> Key = default,
	Change<string?> Name = default,
	Change<decimal?> Rate = default,
	Change<string?> Rounding = default) : CatalogueCommand("project set");

public sealed record ArchiveProject(Guid ProjectId) : CatalogueCommand("project archive");
