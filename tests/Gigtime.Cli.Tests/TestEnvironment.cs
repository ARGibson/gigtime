using System.Runtime.CompilerServices;

namespace Gigtime.Cli.Tests;

internal static class TestEnvironment
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		// #30: in-process hosts and CLI calls always begin with the default test instance.
		Environment.SetEnvironmentVariable("GIGTIME_INSTANCE", null);
	}
}
