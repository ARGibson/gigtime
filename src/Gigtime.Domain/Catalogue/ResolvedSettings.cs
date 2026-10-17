namespace Gigtime.Domain.Catalogue;

public sealed record ResolvedSettings(string Timezone, DayOfWeek WeekStarts, string Rounding, decimal? Rate, string Currency);
