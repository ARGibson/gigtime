namespace Gigtime.Platform;

public sealed class GigtimeInstance
{
	public static GigtimeInstance Default { get; } = new(null);
	public string? Name { get; }
	public string FolderName => Name is null ? "gigtime" : "gigtime-" + Name;
	public string ServiceUnit => UnitName + ".service";
	public string SocketUnit => UnitName + ".socket";
	private string UnitName => Name is null ? "gigtimed" : "gigtimed-" + Name;

	private GigtimeInstance(string? name)
	{
		Name = name;
	}

	public static GigtimeInstance FromEnvironment() => Resolve(Environment.GetEnvironmentVariable("GIGTIME_INSTANCE"));

	public static GigtimeInstance Resolve(string? name)
	{
		if (string.IsNullOrEmpty(name)) return Default;

		if (name.Length > 32 || name[0] is < 'a' or > 'z'
				|| name.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
		{
			throw new ArgumentException("GIGTIME_INSTANCE must match ^[a-z][a-z0-9-]{0,31}$, or be empty for the default instance.");
		}

		return new(name);
	}

	public string DataPath(IPlatformPaths paths) => Path.Combine(paths.DataDirectory, FolderName);
	public string DatabasePath(IPlatformPaths paths) => Path.Combine(DataPath(paths), "gigtime.db");
	public string RuntimePath(IPlatformPaths paths) => Path.Combine(paths.RuntimeDirectory, FolderName);
	public string SocketPath(IPlatformPaths paths) => Path.Combine(RuntimePath(paths), "gigtimed.sock");

	public string RuntimeFallback(string temporary)
	{
		return Path.Combine(temporary, "gigtime-" + Environment.UserName + (Name is null ? "" : "-" + Name));
	}
}
