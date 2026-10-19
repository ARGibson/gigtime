namespace Gigtime.Server.Hosting;

public static class DefaultTimezone
{
	public static string FromHost(string hostId)
	{
		ArgumentNullException.ThrowIfNull(hostId);

		// #35: host-only zoneinfo prefixes and custom local ids must not prevent bootstrap.
		if (hostId.StartsWith("posix/", StringComparison.Ordinal) || hostId.StartsWith("right/", StringComparison.Ordinal))
			hostId = hostId[6..];

		return TimeZones.Find(hostId)?.Id ?? "UTC";
	}
}
