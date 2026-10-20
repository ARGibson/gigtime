using System.Text.Json;
using Fisher;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Gigtime.Server.Hosting;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Documents;
using JasperFx.Events.Projections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class LegacySubmissionUpgradeTests
{
	private static readonly DateTimeOffset at = DateTimeOffset.Parse("2026-03-02T09:00:00Z",
		System.Globalization.CultureInfo.InvariantCulture);

	[Theory]
	[InlineData("2026-02-01..2026-02-28", null, null)]
	[InlineData("2026-03-01..2026-03-31", null, null)]
	[InlineData("2026-03-01..2026-03-31", "ny", null)]
	[InlineData("2026-03-01..2026-03-31", null, "a")]
	[InlineData("2026-03-01..2026-03-31", "ny", "a")]
	public async Task Legacy_settlement_matches_captured_main_figures(string range, string? clientFilter, string? projectFilter)
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-legacy-report-" + Guid.NewGuid().ToString("N"));
		await LegacySettlementFixture.SeedAsync(root, TestContext.Current.CancellationToken);
		await using var app = await ServiceHost.StartAsync(root);
		var url = "/v1/report?range=" + range;
		if (clientFilter is not null) url += "&client=" + clientFilter;
		if (projectFilter is not null) url += "&project=" + projectFilter;
		var report = (await app.GetAsync<ReportDto>(url)).Data!;
		// #35: literal figures captured by running this exact fixture through main 9a5ab60's report.
		const string nyAmount = "281.66666666666666666666666666";
		var february = range.StartsWith("2026-02", StringComparison.Ordinal);

		(string Client, string Project, string Currency, long Raw, long Rounded, string? Unrounded, string? Amount)[] expected = february
			? [("ny", "a", "USD", 3599, 3600, "100", "100.00")]
			:
			[
				("ny", "a", "USD", 10096, 10140, nyAmount, "281.67"),
				("ny", "b", "USD", 29, 0, null, null),
				("eu", "c", "EUR", 9000, 9000, "200", "200.00"),
				("lon", "a", "GBP", 5400, 5400, "75", "75.00"),
			];

		var included = expected.Where(x => (clientFilter is null || x.Client == clientFilter)
			&& (projectFilter is null || x.Project == projectFilter));

		report
			.Totals.Select(x => (x.Client.Key, x.Project.Key, x.Currency, x.RawSeconds, x.RoundedSeconds,
				x.Amount?.Unrounded, x.Amount?.Rounded))
			.Order()
			.ShouldBe(included.Order());

		(string Client, string Currency, long Raw, long Rounded, string? Unrounded, string? Amount)[] expectedClients = february
			? [("ny", "USD", 3599, 3600, "100", "100.00")]
			: projectFilter is not null
				? [("ny", "USD", 10096, 10140, nyAmount, "281.67"), ("lon", "GBP", 5400, 5400, "75", "75.00")]
				:
				[
					("ny", "USD", 10125, 10140, null, null), ("eu", "EUR", 9000, 9000, "200", "200.00"),
					("lon", "GBP", 5400, 5400, "75", "75.00"),
				];

		report
			.Clients.Select(x => (x.Client.Key, x.Currency, x.RawSeconds, x.RoundedSeconds, x.Amount?.Unrounded, x.Amount?.Rounded))
			.Order()
			.ShouldBe(expectedClients.Where(x => clientFilter is null || x.Client == clientFilter).Order());

		report.Unsubmitted.ShouldHaveSingleItem().TaskId.ShouldBe("a10008");
	}

	[Fact]
	public async Task Old_stored_snapshot_document_loads_without_a_projection_rebuild()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-legacy-document-" + Guid.NewGuid().ToString("N"));
		await SeedLegacyAsync(root);

		try
		{
			// #34: use the pre-submission writer's JSON shape, independent of today's serializer.
			const string document = """
															{"id":"aaaaaa","projectId":"11111111-1111-1111-1111-111111111111","state":3,
															"timeline":[{"number":1,"start":"2026-03-02T09:00:00+00:00","end":"2026-03-02T09:00:40+00:00","seconds":40}],
															"snapshot":{"rounding":"nearest-minute","rate":120,"currency":"GBP"},
															"startedAt":"2026-03-02T09:00:00+00:00","stoppedAt":"2026-03-02T09:00:40+00:00",
															"rawSeconds":40,"roundedSeconds":60}
															""";

			await using (var connection = new SqliteConnection("Data Source=" + Path.Combine(root, "data", "gigtime", "gigtime.db")))
			{
				await connection.OpenAsync(TestContext.Current.CancellationToken);
				await using var command = connection.CreateCommand();
				command.CommandText = "UPDATE fi_doc_taskview SET data = $document WHERE id = 'aaaaaa'";
				command.Parameters.AddWithValue("$document", document);
				(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
			}

			await using var store = OpenStore(root);
			await using var query = store.QuerySession();
			var task = (await query.LoadAsync<TaskView>("aaaaaa", TestContext.Current.CancellationToken))!;
			task.Id.ShouldBe("aaaaaa");
			task.ProjectId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
			task.RawSeconds.ShouldBe(40);
			LegacySubmissionMigration.Decide(task).ShouldBe(new(at.AddSeconds(40), 60, "nearest-minute", 120m, "GBP"));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task Legacy_stopped_and_backfilled_tasks_migrate_at_their_stop_with_original_figures_and_restart_is_a_noop()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-legacy-" + Guid.NewGuid().ToString("N"));
		await SeedLegacyAsync(root);
		await using var app = await ServiceHost.StartAsync(root);

		foreach (var id in new[] { "aaaaaa", "bbbbbb" })
		{
			var task = (await app.GetAsync<TaskDto>($"/v1/tasks/{id}")).Data!;
			task.State.ShouldBe("submitted");
			task.Submission!.SubmittedAt.ShouldBe(task.StoppedAt!.Value);
			task.Submission.SubmittedAt.ShouldBe(at.AddSeconds(40));
			task.Submission.RoundedSeconds.ShouldBe(60);
			task.Submission.Rate!.Amount.ShouldBe("120");
			task.Submission.Currency.ShouldBe("GBP");
			task.Submission.Amount!.Rounded.ShouldBe("2.00");
		}

		(await app.GetAsync<TaskDto>("/v1/tasks/cccccc")).Data!.State.ShouldBe("voided");
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Running!.Id.ShouldBe("dddddd");
		(await app.GetAsync<TaskDto>("/v1/tasks/dddddd")).Data!.Submission.ShouldBeNull();
		var before = await ExportAsync(app);
		var exported = before.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<JsonElement>(line));
		exported.Count(e => e.GetProperty("type").GetString() == "task_submitted").ShouldBe(2);
		before.ShouldContain("migration_applied");
		before.ShouldContain("submit-legacy-stopped");
		await app.RestartAsync();
		(await ExportAsync(app)).ShouldBe(before);
		await app.PostAsync<TaskDto>("/v1/tasks/aaaaaa/unsubmit");
		var afterUnsubmit = await ExportAsync(app);
		await app.RestartAsync();
		(await ExportAsync(app)).ShouldBe(afterUnsubmit);
		(await app.GetAsync<TaskDto>("/v1/tasks/aaaaaa")).Data!.State.ShouldBe("stopped");
	}

	[Fact]
	public async Task Empty_store_records_one_marker_and_restart_does_not_repeat_it()
	{
		await using var app = await ServiceHost.StartAsync();
		var before = await ExportAsync(app);

		before
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Count(line => line.Contains("migration_applied", StringComparison.Ordinal))
			.ShouldBe(1);

		await app.RestartAsync();
		(await ExportAsync(app)).ShouldBe(before);
	}

	[Fact]
	public async Task Partial_migration_retry_skips_the_already_submitted_task_and_appends_only_missing_submission_and_marker()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-legacy-partial-" + Guid.NewGuid().ToString("N"));
		await SeedLegacyAsync(root, partial: true);
		await using var app = await ServiceHost.StartAsync(root);
		var text = await ExportAsync(app);
		var events = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<JsonElement>(line));
		events.Count(e => e.GetProperty("type").GetString() == "task_submitted").ShouldBe(2);
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var query = factory.QuerySession();
		var first = await query.Events.FetchStreamAsync("aaaaaa", token: TestContext.Current.CancellationToken);
		first.Count(e => e.Data is TaskSubmitted).ShouldBe(1);
	}

	[Fact]
	public async Task Migration_failure_prevents_host_startup_and_does_not_record_completion()
	{
		var root = Path.Combine(Path.GetTempPath(), "gig-legacy-fail-" + Guid.NewGuid().ToString("N"));
		await SeedLegacyAsync(root, corrupt: true);

		try
		{
			await Should.ThrowAsync<ArgumentException>(async () => await ServiceHost.StartAsync(root));
			await using var store = OpenStore(root);
			await using var query = store.QuerySession();

			(await query.Events.FetchStreamAsync(LegacySubmissionUpgrade.StreamId, token: TestContext.Current.CancellationToken))
				.ShouldBeEmpty();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private static async Task SeedLegacyAsync(string root, bool partial = false, bool corrupt = false)
	{
		await using var store = OpenStore(root);
		await store.ApplyAllConfiguredChangesToDatabaseAsync(TestContext.Current.CancellationToken);
		await using var session = store.LightweightSession();
		var client = Guid.CreateVersion7();
		var project = Guid.CreateVersion7();

		session.Events.StartStream<CatalogueView>(CatalogueView.StreamId,
			new DefaultsChanged("UTC"),
			new ClientAdded(client, "legacy", "Legacy", "USD"),
			new ProjectAdded(project, client, "work", null, 999m));

		session.Events.StartStream<OpenTask>(OpenTask.StreamId, new OpenTaskSet("dddddd"));
		var stopped = new TaskStopped(at.AddSeconds(40), corrupt ? "unsupported" : "nearest-minute", 120m, "GBP");
		session.Events.StartStream<TaskView>("aaaaaa", new TaskStarted("aaaaaa", project, null, at), stopped);

		session.Events.StartStream<TaskView>("bbbbbb",
			new TaskRecorded("bbbbbb",
				project,
				null,
				at,
				at.AddSeconds(40),
				"nearest-minute",
				120m,
				"GBP"));

		session.Events.StartStream<TaskView>("cccccc",
			new TaskRecorded("cccccc",
				project,
				null,
				at,
				at.AddSeconds(40),
				"nearest-minute",
				120m,
				"GBP"),
			new TaskVoided("duplicate"));

		session.Events.StartStream<TaskView>("dddddd", new TaskStarted("dddddd", project, null, at));
		await session.SaveChangesAsync(TestContext.Current.CancellationToken);

		if (!partial) return;

		session.Events.Append("aaaaaa", new TaskSubmitted(at.AddSeconds(40), 60, "nearest-minute", 120m, "GBP"));
		await session.SaveChangesAsync(TestContext.Current.CancellationToken);
	}

	private static DocumentStore OpenStore(string root)
	{
		var data = Path.Combine(root, "data", "gigtime");
		Directory.CreateDirectory(data);

		return DocumentStore.For(options =>
		{
			options.Connection("Data Source=" + Path.Combine(data, "gigtime.db"));
			options.AutoCreateSchemaObjects = AutoCreate.All;
			options.Events.StreamIdentity = StreamIdentity.AsString;
			options.Events.EnableCorrelationId = true;
			options.Events.AddEventType(typeof(MigrationApplied));
			options.Projections.Add(new StoreRegistration.CatalogueProjection(), ProjectionLifecycle.Inline);
			options.Projections.Add(new StoreRegistration.TrackerProjection(), ProjectionLifecycle.Inline);
			options.Projections.Add(new StoreRegistration.TaskProjection(), ProjectionLifecycle.Inline);
		});
	}

	private static async Task<string> ExportAsync(ServiceHost app)
	{
		var result = await app.Host.Scenario(s => s.Get.Url("/v1/export"));

		return await result.ReadAsTextAsync();
	}
}
