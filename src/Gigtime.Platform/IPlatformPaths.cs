namespace Gigtime.Platform;

public interface IPlatformPaths
{
	string DataDirectory { get; }
	string RuntimeDirectory { get; }
	string ConfigDirectory { get; }
}

public sealed class LinuxPlatformPaths : IPlatformPaths
{
	public string DataDirectory { get; } = Root("XDG_DATA_HOME", Path.Combine(Home, ".local", "share"));
	public string RuntimeDirectory { get; } = Root("XDG_RUNTIME_DIR", Path.Combine(Path.GetTempPath(), $"gigtime-{Environment.UserName}"));
	public string ConfigDirectory { get; } = Root("XDG_CONFIG_HOME", Path.Combine(Home, ".config"));
	private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	private static string Root(string variable, string fallback)
	{
		var value = Environment.GetEnvironmentVariable(variable);

		return value is not null && Path.IsPathFullyQualified(value) ? value : fallback;
	}
}
