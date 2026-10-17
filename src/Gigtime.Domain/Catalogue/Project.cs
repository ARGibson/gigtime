namespace Gigtime.Domain.Catalogue;

public sealed record Project(Guid Id, Guid ClientId, string Key, string? Name, decimal? Rate, string? Rounding, bool Archived = false);
