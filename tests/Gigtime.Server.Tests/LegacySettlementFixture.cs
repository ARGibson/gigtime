using System.Globalization;
using Fisher;
using Gigtime.Domain;
using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Gigtime.Server.Hosting;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;

namespace Gigtime.Server.Tests;

public static class LegacySettlementFixture
{
	public static async Task SeedAsync(string root, CancellationToken token)
	{
		var data = Path.Combine(root, "data", "gigtime");
		Directory.CreateDirectory(data);

		await using var store = DocumentStore.For(options =>
		{
			options.Connection("Data Source=" + Path.Combine(data, "gigtime.db"));
			options.AutoCreateSchemaObjects = AutoCreate.All;
			options.Events.StreamIdentity = StreamIdentity.AsString;
			options.Events.AddEventType(typeof(MigrationApplied));
			options.Projections.Add(new StoreRegistration.CatalogueProjection(), ProjectionLifecycle.Inline);
			options.Projections.Add(new StoreRegistration.TrackerProjection(), ProjectionLifecycle.Inline);
			options.Projections.Add(new StoreRegistration.TaskProjection(), ProjectionLifecycle.Inline);
		});

		await store.ApplyAllConfiguredChangesToDatabaseAsync(token);
		await using var session = store.LightweightSession();
		var ny = Guid.Parse("10000000-0000-0000-0000-000000000001");
		var eu = Guid.Parse("10000000-0000-0000-0000-000000000002");
		var lon = Guid.Parse("10000000-0000-0000-0000-000000000003");
		var a = Guid.Parse("20000000-0000-0000-0000-000000000001");
		var b = Guid.Parse("20000000-0000-0000-0000-000000000002");
		var c = Guid.Parse("20000000-0000-0000-0000-000000000003");
		var otherA = Guid.Parse("20000000-0000-0000-0000-000000000004");

		session.Events.StartStream<CatalogueView>(CatalogueView.StreamId,
			new DefaultsChanged("UTC"),
			new ClientAdded(ny, "ny", "New York", "USD", "America/New_York"),
			new ClientAdded(eu, "eu", "Berlin", "EUR", "Europe/Berlin"),
			new ClientAdded(lon, "lon", "London", "GBP", "Europe/London"),
			new ProjectAdded(a, ny, "a", Rate: 100m),
			new ProjectAdded(b, ny, "b"),
			new ProjectAdded(c, eu, "c", Rate: 80m),
			new ProjectAdded(otherA, lon, "a", Rate: 50m));

		session.Events.StartStream<OpenTask>(OpenTask.StreamId, new OpenTaskSet("a10008"));
		session.Events.StartStream<TaskView>("a10001", Recorded("a10001", a, "2026-02-28T23:00:01Z", "2026-03-01T00:00:00Z", 100m, "USD"));

		session.Events.StartStream<TaskView>("a10002",
			new TaskStarted("a10002", a, null, At("2026-03-01T03:58:21Z")),
			new TaskStopped(At("2026-03-01T05:00:01Z"), "nearest-minute", 100m, "USD"));

		session.Events.StartStream<TaskView>("a10003", Recorded("a10003", a, "2026-03-31T22:14:24Z", "2026-04-01T00:01:00Z", 100m, "USD"));
		session.Events.StartStream<TaskView>("a10004", Recorded("a10004", b, "2026-03-10T09:00:00Z", "2026-03-10T09:00:29Z", null, "USD"));
		session.Events.StartStream<TaskView>("a10005", Recorded("a10005", c, "2026-02-28T22:00:00Z", "2026-03-01T00:30:00Z", 80m, "EUR"));
		session.Events.StartStream<TaskView>("a10006", Recorded("a10006", otherA, "2026-03-07T09:00:00Z", "2026-03-07T10:30:00Z", 50m, "GBP"));

		session.Events.StartStream<TaskView>("a10007",
			Recorded("a10007", a, "2026-03-02T09:00:00Z", "2026-03-02T19:00:00Z", 100m, "USD"),
			new TaskVoided("duplicate"));

		session.Events.StartStream<TaskView>("a10008", new TaskStarted("a10008", a, null, At("2026-02-01T09:00:00Z")));

		session.Events.Append(CatalogueView.StreamId,
			new ProjectChanged(a, Rate: Change.Set<decimal?>(999m)),
			new ProjectChanged(b, Rate: Change.Set<decimal?>(999m)),
			new ProjectChanged(c, Rate: Change.Set<decimal?>(999m)),
			new ProjectChanged(otherA, Rate: Change.Set<decimal?>(999m)));

		await session.SaveChangesAsync(token);
	}

	private static TaskRecorded Recorded(string id, Guid project, string start, string end, decimal? rate, string currency)
	{
		return new(id, project, null, At(start), At(end), "nearest-minute", rate, currency);
	}

	private static DateTimeOffset At(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
}
