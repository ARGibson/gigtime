using System.Reflection;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class DependencyTests
{
	[Fact]
	public void Cli_has_no_store_or_service_reference()
	{
		var references = Assembly.Load("gig").GetReferencedAssemblies().Select(a => a.Name ?? "");

		references.ShouldNotContain(name => name.StartsWith("Fisher", StringComparison.Ordinal)
			|| name.StartsWith("Wolverine", StringComparison.Ordinal)
			|| name.StartsWith("JasperFx", StringComparison.Ordinal)
			|| name == "Gigtime.Server");
	}
}
