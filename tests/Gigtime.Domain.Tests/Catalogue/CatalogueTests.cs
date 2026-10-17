using Gigtime.Domain.Catalogue;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Catalogue;

public sealed class CatalogueTests
{
	[Theory]
	[InlineData("acme", true)]
	[InlineData("0", true)]
	[InlineData("site-audit", true)]
	[InlineData("", false)]
	[InlineData("Acme", false)]
	[InlineData("-acme", false)]
	[InlineData("acme_1", false)]
	[InlineData("acme\n", false)]
	[InlineData("abcdefghijklmnopqrstuvwxyz123456", true)]
	[InlineData("abcdefghijklmnopqrstuvwxyz1234567", false)]
	public void Client_keys_follow_the_public_grammar(string key, bool valid)
	{
		var result = CatalogueDecider.Decide(new(), new AddClient(Guid.CreateVersion7(), key, "Acme", "USD"));

		result.IsSuccess.ShouldBe(valid);
	}

	[Fact]
	public void Client_and_project_keys_stay_unique_even_when_archived()
	{
		var s = new CatalogueScenario();
		s.Send(new AddClient(s.ClientId, "acme", "Acme", "USD"));
		s.Send(new AddProject(s.ProjectId, s.ClientId, "site-audit"));

		s.Refuse(new AddClient(Guid.CreateVersion7(), "acme", "Globex", "EUR"));
		s.Refuse(new AddProject(Guid.CreateVersion7(), s.ClientId, "site-audit"));

		var other = Guid.CreateVersion7();
		s.Send(new AddClient(other, "globex", "Globex", "EUR"));
		s.Send(new AddProject(Guid.CreateVersion7(), other, "site-audit"));

		s.Refuse(new ChangeClient(other, Key: Change.Set<string>("acme")));

		var another = Guid.CreateVersion7();
		s.Send(new AddProject(another, s.ClientId, "other"));

		s.Refuse(new ChangeProject(another, Key: Change.Set<string>("site-audit")));

		s.Send(new ArchiveClient(s.ClientId));

		s.State.FindProject(s.ProjectId)!.Archived.ShouldBeTrue();
		s.State.FindProject(another)!.Archived.ShouldBeTrue();
		s.State.FindProject(other, "site-audit")!.Archived.ShouldBeFalse();
		s.Refuse(new AddClient(Guid.CreateVersion7(), "acme", "Acme", "USD"));
		s.Refuse(new AddProject(Guid.CreateVersion7(), s.ClientId, "new"));
	}

	[Fact]
	public void Clearing_overrides_is_distinct_from_omitting_them()
	{
		var s = new CatalogueScenario();
		s.Send(new SetDefaults("UTC", DayOfWeek.Sunday));
		s.Send(new AddClient(s.ClientId, "acme", "Acme", "USD", "America/New_York", "nearest-minute"));
		s.Send(new AddProject(s.ProjectId, s.ClientId, "site-audit", "Audit", 120m, "nearest-minute"));
		s.Send(new ChangeClient(s.ClientId, Name: Change.Set<string>("Acme display")));

		s.State.Resolve(s.ProjectId).Timezone.ShouldBe("America/New_York");

		s.Send(new ChangeClient(s.ClientId, Timezone: Change.Set<string?>(null), Rounding: Change.Set<string?>(null)));

		s.Send(new ChangeProject(s.ProjectId,
			Name: Change.Set<string?>(null),
			Rate: Change.Set<decimal?>(null),
			Rounding: Change.Set<string?>(null)));

		s.State.FindProject(s.ProjectId)!.Name.ShouldBeNull();
		s.State.Resolve(s.ProjectId).ShouldBe(new("UTC", DayOfWeek.Sunday, "nearest-minute", null, "USD"));
	}

	[Theory]
	[InlineData("ZZZ", null, null)]
	[InlineData("usd", null, null)]
	[InlineData("USD", "no/such-zone", null)]
	[InlineData("USD", null, "ceiling")]
	public void Invalid_settings_produce_validation_without_events(string currency, string? zone, string? rounding)
	{
		var d = CatalogueDecider.Decide(new(), new AddClient(Guid.CreateVersion7(), "acme", "Acme", currency, zone, rounding));

		d.Error!.Code.ShouldBe("validation");
		d.Events.ShouldBeEmpty();
	}

	[Fact]
	public void Missing_objects_and_invalid_defaults_are_refused()
	{
		var s = new CatalogueScenario();

		s.Refuse(new ChangeClient(s.ClientId), "not_found");
		s.Refuse(new ChangeProject(s.ProjectId), "not_found");
		s.Refuse(new ArchiveClient(s.ClientId), "not_found");
		s.Refuse(new ArchiveProject(s.ProjectId), "not_found");
		s.Refuse(new AddProject(s.ProjectId, s.ClientId, "site-audit"), "not_found");
		s.Refuse(new SetDefaults(Rounding: "hour"));
		s.Refuse(new SetDefaults(Timezone: "invalid"));
		s.Refuse(new SetDefaults(WeekStarts: (DayOfWeek)9));
		s.Refuse(new AddClient(Guid.NewGuid(), "acme", "Acme", "USD"));
		s.Refuse(new AddClient(s.ClientId, "acme", " ", "USD"));

		s.Send(new AddClient(s.ClientId, "acme", "Acme", "USD"));

		s.Refuse(new AddClient(s.ClientId, "globex", "Globex", "USD"));
		s.Refuse(new ChangeClient(s.ClientId, Currency: Change.Set<string>("ZZZ")));
		s.Refuse(new ChangeClient(s.ClientId, Name: Change.Set<string>("")));
		s.Refuse(new AddProject(s.ProjectId, s.ClientId, "site-audit", Rate: -1));

		s.Send(new AddProject(s.ProjectId, s.ClientId, "site-audit"));

		s.Refuse(new ChangeProject(s.ProjectId, Rate: Change.Set<decimal?>(-1)));

		s.Send(new ArchiveProject(s.ProjectId));

		s.State.FindProject(s.ProjectId)!.Archived.ShouldBeTrue();
	}

	[Fact]
	public void Evolve_is_deterministic_for_catalogue_events()
	{
		var s = new CatalogueScenario();
		s.Send(new SetDefaults("UTC"));
		s.Send(new AddClient(s.ClientId, "acme", "Acme", "USD"));
		s.Send(new AddProject(s.ProjectId, s.ClientId, "site-audit"));
		s.Send(new ChangeProject(s.ProjectId, Rate: Change.Set<decimal?>(60m)));
		s.Send(new ArchiveClient(s.ClientId));
		var replay = new CatalogueView();
		foreach (var e in s.Events) replay.Evolve(e);

		replay.Defaults.ShouldBe(s.State.Defaults);
		replay.Clients.ShouldBe(s.State.Clients);
		replay.Projects.ShouldBe(s.State.Projects);
	}
}
