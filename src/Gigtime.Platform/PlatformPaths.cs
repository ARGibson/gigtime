namespace Gigtime.Platform;

public interface IPlatformPaths
{
	string DataDirectory { get; }
	string RuntimeDirectory { get; }
	string ConfigDirectory { get; }
}

public sealed class LinuxPlatformPaths : IPlatformPaths
{
	public string DataDirectory { get; }
	public string RuntimeDirectory { get; }
	public string ConfigDirectory { get; }

	public LinuxPlatformPaths() : this(Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
		Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
		Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		Path.GetTempPath())
	{ }

	internal LinuxPlatformPaths(string? data, string? runtime, string? config, string home, string temporary)
	{
		DataDirectory = Root(data, Path.Combine(home, ".local", "share"));
		RuntimeDirectory = LinuxRuntimeDirectory.Resolve(runtime, Path.Combine(temporary, $"gigtime-{Environment.UserName}"));
		ConfigDirectory = Root(config, Path.Combine(home, ".config"));
	}

	private static string Root(string? value, string fallback)
	{
		return value is not null && Path.IsPathFullyQualified(value) ? value : fallback;
	}
}
