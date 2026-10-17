using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class CommandTests(PublishedFixture published)
{
	[Fact]
	public async Task Catalogue_commands_change_resolved_settings_and_refuse_invalid_unknown_duplicate_and_archived_resources()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		await day.RefusalAsync(8, "validation", "client", "add", "acme", "--name", "Acme", "--currency", "GBP");
		await day.RefusalAsync(8, "validation", "project", "add", "acme", "work");
		await day.RefusalAsync(8, "validation", "defaults", "set", "--timezone", "not-a-zone");
		await day.RefusalAsync(8, "validation", "client", "set", "acme", "--currency", "bad");
		await day.RefusalAsync(4, "not_found", "client", "set", "missing", "--name", "Missing");
		await day.RefusalAsync(4, "not_found", "project", "set", "acme", "missing", "--name", "Missing");
		await day.SuccessAsync("client", "set", "acme", "--name", "Acme Limited", "--timezone", "UTC");
		(await day.SuccessAsync("client", "list")).GetProperty("clients")[0].GetProperty("name").GetString().ShouldBe("Acme Limited");
		await day.SuccessAsync("project", "set", "acme", "work", "--name", "Build", "--rate", "40");

		(await day.SuccessAsync("project", "list", "acme")).GetProperty("projects")[0]
			.GetProperty("rate")
			.GetProperty("amount")
			.GetString()
			.ShouldBe("40");

		await day.RefusalAsync(8, "validation", "project", "set", "acme", "work", "--rate", "-2");
		await day.SuccessAsync("project", "archive", "acme", "work");
		(await day.SuccessAsync("project", "list", "acme")).GetProperty("projects").GetArrayLength().ShouldBe(0);
		(await day.SuccessAsync("project", "list", "acme", "--all")).GetProperty("projects").GetArrayLength().ShouldBe(1);
		await day.RefusalAsync(8, "validation", "start", "acme", "work");
		await day.SuccessAsync("client", "archive", "acme");
		(await day.SuccessAsync("client", "list")).GetProperty("clients").GetArrayLength().ShouldBe(0);
		(await day.SuccessAsync("client", "list", "--all")).GetProperty("clients").GetArrayLength().ShouldBe(1);
	}

	[Fact]
	public async Task Lifecycle_and_corrections_refuse_invalid_transitions_and_leave_the_open_task_unchanged()
	{
		await using var day = new DaySession(published);
		await day.StartAsync();
		await day.CatalogueAsync();
		foreach (var command in new[] { "pause", "resume", "stop" }) await day.RefusalAsync(4, "not_found", command);
		await day.RefusalAsync(4, "not_found", "start", "missing", "work");
		await day.RefusalAsync(4, "not_found", "show", "ffffff");
		await day.RefusalAsync(2, "usage", "log", "--last", "0");
		await day.RefusalAsync(2, "usage", "amend", "ffffff");
		var open = (await day.SuccessAsync("start", "acme", "work")).GetProperty("id").GetString()!;
		await day.RefusalAsync(3, "invalid_transition", "start", "acme", "work");
		await day.RefusalAsync(3, "invalid_transition", "resume");
		await day.RefusalAsync(3, "invalid_transition", "void", open);
		await day.SuccessAsync("pause");
		await day.RefusalAsync(3, "invalid_transition", "pause");
		var added = await day.SuccessAsync("add", "acme", "work", "--from", "2026-03-02T09:00:00Z", "--to", "2026-03-02T10:00:00Z");
		var id = added.GetProperty("id").GetString()!;
		added.GetProperty("raw_seconds").GetInt64().ShouldBe(3600);
		(await day.SuccessAsync("status")).GetProperty("task").GetProperty("id").GetString().ShouldBe(open);
		await day.RefusalAsync(8, "validation", "add", "acme", "work", "--from", "2026-03-02T11:00:00Z", "--to", "2026-03-02T10:00:00Z");
		await day.RefusalAsync(8, "validation", "amend", id, "--interval", "1", "--end", "2026-03-02T08:00:00Z");
		(await day.SuccessAsync("show", id)).GetProperty("raw_seconds").GetInt64().ShouldBe(3600);
		await day.SuccessAsync("amend", id, "--ref", "opaque");
		(await day.SuccessAsync("show", id)).GetProperty("ref").GetString().ShouldBe("opaque");
		await day.SuccessAsync("amend", id, "--clear-ref");
		await day.SuccessAsync("stop");
		await day.RefusalAsync(4, "not_found", "stop");
		await day.SuccessAsync("void", id);
		await day.RefusalAsync(3, "invalid_transition", "void", id);
		await day.RefusalAsync(3, "invalid_transition", "amend", id, "--ref", "opaque");
		await day.RefusalAsync(4, "not_found", "report", "--client", "missing");
		await day.RefusalAsync(2, "usage", "report", "--week", "--month");
		await day.RefusalAsync(8, "validation", "export", "--from", "2030-01-02", "--to", "2030-01-01");
		await day.RefusalAsync(2, "usage", "export", "--from", "bad");
		(await day.GigAsync("export", "--from", "2099-01-01")).Output.ShouldBeEmpty();
	}

	[Fact]
	public async Task Unavailable_service_maps_exit_codes_and_completion_stays_silent()
	{
		await using var day = new DaySession(published);
		await day.RefusalAsync(6, "unavailable", "status");
		var result = await day.GigAsync("__complete", "clients");
		result.ExitCode.ShouldBe(0);
		result.Output.ShouldBeEmpty();
		result.Error.ShouldBeEmpty();
		await day.RefusalAsync(2, "usage", "not-a-command");
	}
}
