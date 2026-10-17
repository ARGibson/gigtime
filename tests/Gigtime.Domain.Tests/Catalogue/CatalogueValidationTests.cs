using Gigtime.Domain.Catalogue;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Catalogue;

public sealed class CatalogueValidationTests
{
	[Theory]
	[InlineData("defaults", "timezone", "Unknown timezone.")]
	[InlineData("defaults", "rounding", "Unsupported rounding; use nearest-minute.")]
	[InlineData("defaults", "weekday", "Unknown week-start day.")]
	[InlineData("client add", "id-format", "Client id must be a UUIDv7.")]
	[InlineData("client add", "id-duplicate", "Client id already exists.")]
	[InlineData("client add", "key-format", "Invalid client key;")]
	[InlineData("client add", "key-duplicate", "Duplicate client key.")]
	[InlineData("client add", "name", "Client name cannot be blank.")]
	[InlineData("client add", "currency", "Unknown ISO 4217 currency.")]
	[InlineData("client add", "timezone", "Unknown timezone.")]
	[InlineData("client add", "rounding", "Unsupported rounding; use nearest-minute.")]
	[InlineData("client set", "key-format", "Invalid client key;")]
	[InlineData("client set", "key-duplicate", "Duplicate client key.")]
	[InlineData("client set", "name", "Client name cannot be blank.")]
	[InlineData("client set", "currency", "Unknown ISO 4217 currency.")]
	[InlineData("client set", "timezone", "Unknown timezone.")]
	[InlineData("client set", "rounding", "Unsupported rounding; use nearest-minute.")]
	[InlineData("project add", "archived", "The client is archived.")]
	[InlineData("project add", "id-format", "Project id must be a UUIDv7.")]
	[InlineData("project add", "id-duplicate", "Project id already exists.")]
	[InlineData("project add", "key-format", "Invalid project key;")]
	[InlineData("project add", "key-duplicate", "Duplicate project key within this client.")]
	[InlineData("project add", "rate", "Project rate cannot be negative.")]
	[InlineData("project add", "rounding", "Unsupported rounding; use nearest-minute.")]
	[InlineData("project set", "key-format", "Invalid project key;")]
	[InlineData("project set", "key-duplicate", "Duplicate project key within this client.")]
	[InlineData("project set", "rate", "Project rate cannot be negative.")]
	[InlineData("project set", "rounding", "Unsupported rounding; use nearest-minute.")]
	public void Each_catalogue_refusal_names_the_rule_without_events(string command, string rule, string message)
	{
		var s = new CatalogueScenario();
		s.Send(new AddClient(s.ClientId, "acme", "Acme", "USD"));
		s.Send(new AddProject(s.ProjectId, s.ClientId, "site-audit"));
		s.Send(new AddClient(Guid.CreateVersion7(), "globex", "Globex", "USD"));
		s.Send(new AddProject(Guid.CreateVersion7(), s.ClientId, "other"));
		if (rule == "archived") s.Send(new ArchiveClient(s.ClientId));
		var addClient = new AddClient(Guid.CreateVersion7(), "new", "New display", "USD");
		var addProject = new AddProject(Guid.CreateVersion7(), s.ClientId, "new");

		CatalogueCommand input = (command, rule) switch
		{
			("defaults", "timezone") => new SetDefaults(Timezone: "no/such-zone"),
			("defaults", "rounding") => new SetDefaults(Rounding: "hour"),
			("defaults", "weekday") => new SetDefaults(WeekStarts: (DayOfWeek)9),
			("client add", "id-format") => addClient with
			{
				ClientId = Guid.NewGuid()
			},
			("client add", "id-duplicate") => addClient with
			{
				ClientId = s.ClientId
			},
			("client add", "key-format") => addClient with
			{
				Key = "Bad"
			},
			("client add", "key-duplicate") => addClient with
			{
				Key = "acme"
			},
			("client add", "name") => addClient with
			{
				Name = " "
			},
			("client add", "currency") => addClient with
			{
				Currency = "ZZZ"
			},
			("client add", "timezone") => addClient with
			{
				Timezone = "no/such-zone"
			},
			("client add", "rounding") => addClient with
			{
				Rounding = "hour"
			},
			("client set", "key-format") => new ChangeClient(s.ClientId, Key: Change.Set("Bad")),
			("client set", "key-duplicate") => new ChangeClient(s.ClientId, Key: Change.Set("globex")),
			("client set", "name") => new ChangeClient(s.ClientId, Name: Change.Set(" ")),
			("client set", "currency") => new ChangeClient(s.ClientId, Currency: Change.Set("ZZZ")),
			("client set", "timezone") => new ChangeClient(s.ClientId, Timezone: Change.Set<string?>("no/such-zone")),
			("client set", "rounding") => new ChangeClient(s.ClientId, Rounding: Change.Set<string?>("hour")),
			("project add", "archived") => addProject,
			("project add", "id-format") => addProject with
			{
				ProjectId = Guid.NewGuid()
			},
			("project add", "id-duplicate") => addProject with
			{
				ProjectId = s.ProjectId
			},
			("project add", "key-format") => addProject with
			{
				Key = "Bad"
			},
			("project add", "key-duplicate") => addProject with
			{
				Key = "site-audit"
			},
			("project add", "rate") => addProject with
			{
				Rate = -1m
			},
			("project add", "rounding") => addProject with
			{
				Rounding = "hour"
			},
			("project set", "key-format") => new ChangeProject(s.ProjectId, Key: Change.Set("Bad")),
			("project set", "key-duplicate") => new ChangeProject(s.ProjectId, Key: Change.Set("other")),
			("project set", "rate") => new ChangeProject(s.ProjectId, Rate: Change.Set<decimal?>(-1m)),
			("project set", "rounding") => new ChangeProject(s.ProjectId, Rounding: Change.Set<string?>("hour")),
			_ => throw new InvalidOperationException("Unknown test case.")
		};

		var result = CatalogueDecider.Decide(s.State, input);
		result.Error!.Code.ShouldBe("validation");
		result.Error.Message.ShouldStartWith(message);
		result.Error.Command.ShouldBe(input.Command);
		result.Events.ShouldBeEmpty();
	}
}
