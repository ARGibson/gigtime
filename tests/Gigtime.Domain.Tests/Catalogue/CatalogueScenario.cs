using Gigtime.Domain.Catalogue;
using Shouldly;

namespace Gigtime.Domain.Tests.Catalogue;

internal sealed class CatalogueScenario
{
	public CatalogueView State { get; } = new();
	public Guid ClientId { get; } = Guid.CreateVersion7();
	public Guid ProjectId { get; } = Guid.CreateVersion7();
	public List<object> Events { get; } = [];

	public void Send(CatalogueCommand c)
	{
		var decision = CatalogueDecider.Decide(State, c);
		decision.Error.ShouldBeNull();

		foreach (var e in decision.Events)
		{
			State.Evolve(e);
			Events.Add(e);
		}
	}

	public void Refuse(CatalogueCommand c, string code = "validation")
	{
		var decision = CatalogueDecider.Decide(State, c);
		decision.Error!.Code.ShouldBe(code);
		decision.Events.ShouldBeEmpty();
	}
}
