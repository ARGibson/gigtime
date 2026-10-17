namespace Gigtime.Domain.Catalogue;

public sealed record Client(Guid Id, string Key, string Name, string Currency, string? Timezone, string? Rounding, bool Archived = false);
