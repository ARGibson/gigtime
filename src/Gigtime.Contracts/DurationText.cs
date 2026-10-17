using System.Globalization;

namespace Gigtime.Contracts;

public static class DurationText
{
  public static string Format(long seconds)
  {
	ArgumentOutOfRangeException.ThrowIfNegative(seconds);
	return string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}");
  }
}
