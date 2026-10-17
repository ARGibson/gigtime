namespace Gigtime.Domain;

/// <summary>Distinguishes an omitted field from an explicitly cleared nullable override.</summary>
public readonly record struct Change<T>(bool IsSpecified, T Value)
{
	public T Resolve(T current) => IsSpecified ? Value : current;
}

public static class Change
{
	public static Change<T> Set<T>(T value) => new(true, value);
}
