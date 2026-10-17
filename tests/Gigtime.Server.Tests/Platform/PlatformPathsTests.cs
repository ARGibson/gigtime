using Gigtime.Platform;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests.Platform;

public sealed class PlatformPathsTests
{
	[Fact]
	public void Linux_paths_resolve_to_absolute_roots()
	{
		var paths = new LinuxPlatformPaths();

		Path.IsPathFullyQualified(paths.DataDirectory).ShouldBeTrue();
		Path.IsPathFullyQualified(paths.RuntimeDirectory).ShouldBeTrue();
		Path.IsPathFullyQualified(paths.ConfigDirectory).ShouldBeTrue();
	}
}
