using JasperFx.Events;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Scaffold;

public sealed class PackageTests
{
  [Fact]
  public void Shared_event_assembly_exposes_store_parameterized_projection_base()
  {
	var assembly = typeof(IEvent).Assembly;
	var projection = assembly.GetType("JasperFx.Events.Aggregation.JasperFxSingleStreamProjectionBase`4");
	projection.ShouldNotBeNull();
	projection.GetGenericArguments().Length.ShouldBe(4);
  }
}
