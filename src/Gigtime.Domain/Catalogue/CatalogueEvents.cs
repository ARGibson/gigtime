namespace Gigtime.Domain.Catalogue;

public sealed record DefaultsChanged(string? Timezone = null, DayOfWeek? WeekStarts = null, string? Rounding = null);
public sealed record ClientAdded(Guid ClientId, string Key, string Name, string Currency, string? Timezone = null, string? Rounding = null);
public sealed record ClientChanged(Guid ClientId, Change<string> Key = default, Change<string> Name = default,
	Change<string> Currency = default, Change<string?> Timezone = default, Change<string?> Rounding = default);
public sealed record ClientArchived(Guid ClientId);
public sealed record ProjectAdded(Guid ProjectId, Guid ClientId, string Key, string? Name = null, decimal? Rate = null, string? Rounding = null);
public sealed record ProjectChanged(Guid ProjectId, Change<string> Key = default, Change<string?> Name = default,
	Change<decimal?> Rate = default, Change<string?> Rounding = default);
public sealed record ProjectArchived(Guid ProjectId);
