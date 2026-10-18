using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class PublishedPayloadTests(PublishedFixture published)
{
	[Fact]
	public void Published_server_contains_only_linux_x64_natives()
	{
		File.Exists(Path.Combine(published.Application, "libe_sqlite3.so")).ShouldBeTrue();
		var runtimes = Path.Combine(published.Application, "runtimes");

		if (Directory.Exists(runtimes))
			Directory.GetDirectories(runtimes).Select(Path.GetFileName).ShouldAllBe(rid => rid == "linux-x64");
	}
}
