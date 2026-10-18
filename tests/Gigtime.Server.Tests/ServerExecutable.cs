using System.Reflection;

namespace Gigtime.Server.Tests;

internal static class ServerExecutable
{
	internal static string Path => typeof(ServerExecutable)
		.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
		.Single(a => a.Key == "GigtimeServerPath")
		.Value!;
}
