using System.Collections.Frozen;
using NodaTime;
using NodaTime.TimeZones;

namespace Gigtime;

internal static class TimeZones
{
	private static readonly FrozenDictionary<string, string> ids = DateTimeZoneProviders.Tzdb.Ids
		.ToFrozenDictionary(id => id, StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenDictionary<string, string> windows = TzdbDateTimeZoneSource.Default.WindowsToTzdbIds
		.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	public static DateTimeZone? Find(string? id)
	{
		if (id is null) return null;
		if (ids.TryGetValue(id, out var known) || windows.TryGetValue(id, out known)) return DateTimeZoneProviders.Tzdb[known];

		return null;
	}

	public static DateTimeZone Get(string id) => Find(id) ?? throw new TimeZoneNotFoundException(
		$"Timezone '{id}' is not included in the bundled TZDB. Set the client or defaults timezone to a supported TZDB id.");
}
