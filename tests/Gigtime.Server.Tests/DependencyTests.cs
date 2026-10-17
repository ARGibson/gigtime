using Alba;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class DependencyTests
{
	[Fact]
	public void Alba_can_reference_the_server_entry_point()
	{
		typeof(AlbaHost).Assembly.ShouldNotBeNull();
		typeof(Program).Assembly.GetName().Name.ShouldBe("gigtimed");
	}
}
