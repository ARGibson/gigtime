using System.Security.Cryptography;

namespace Gigtime.Domain.Tasks;

public static class TaskIds
{
  public static bool IsValid(string? id) => id is { Length: 6 } && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  /// <summary>The host checks existing streams and still uses optimistic concurrency when appending.</summary>
  public static string Create(Func<string, bool> exists) => Create(exists, () => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3)));

  public static string Create(Func<string, bool> exists, Func<string> nextCandidate)
  {
	ArgumentNullException.ThrowIfNull(exists);
	ArgumentNullException.ThrowIfNull(nextCandidate);
	for (var attempt = 0; attempt < 1024; attempt++)
	{
	  var candidate = nextCandidate();
	  if (!IsValid(candidate)) throw new ArgumentException("Candidates must be six lowercase hex characters.", nameof(nextCandidate));
	  if (!exists(candidate)) return candidate;
	}
	throw new InvalidOperationException("No unused task id found; retry id allocation.");
  }
}
