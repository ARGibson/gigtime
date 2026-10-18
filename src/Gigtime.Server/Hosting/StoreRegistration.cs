using Fisher;
using Fisher.Projections;
using Gigtime.Domain.Tasks;
using Gigtime.Platform;
using JasperFx;
using JasperFx.Events.Projections;
using Microsoft.Data.Sqlite;
using Wolverine.Fisher;

namespace Gigtime.Server.Hosting;

public static class StoreRegistration
{
	public static void AddGigtimeStore(this IServiceCollection services)
	{
		services
			.AddFisher(provider =>
			{
				if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("gigtimed requires Unix sockets.");

				var paths = provider.GetRequiredService<IPlatformPaths>();
				var directory = paths.Instance.DataPath(paths);
				Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				var file = paths.Instance.DatabasePath(paths);

				using (File.Open(file,
								new FileStreamOptions
								{
									Mode = FileMode.OpenOrCreate,
									Access = FileAccess.ReadWrite,
									UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
								}))
				{
					File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				}

				var options = new StoreOptions();

				options.Connection(new SqliteConnectionStringBuilder
				{
					DataSource = file
				}.ToString());

				options.AutoCreateSchemaObjects = AutoCreate.All;
				options.Events.StreamIdentity = StreamIdentity.AsString;
				options.Events.EnableCorrelationId = true;
				options.Projections.Add(new CatalogueProjection(), ProjectionLifecycle.Inline);
				options.Projections.Add(new TrackerProjection(), ProjectionLifecycle.Inline);
				options.Projections.Add(new TaskProjection(), ProjectionLifecycle.Inline);

				options.InitialData.Add(async (store, token) =>
				{
					await using var session = store.LightweightSession();
					session.CorrelationId = "bootstrap";

					if (await session.Events.FetchStreamStateAsync(CatalogueView.StreamId, token) is null)
						session.Events.StartStream<CatalogueView>(CatalogueView.StreamId, new DefaultsChanged(TimeZoneInfo.Local.Id));

					if (await session.Events.FetchStreamStateAsync(OpenTask.StreamId, token) is null)
						session.Events.StartStream<OpenTask>(OpenTask.StreamId, new OpenTaskCleared());

					await session.SaveChangesAsync(token);
				});

				return options;
			})
			.ApplyAllDatabaseChangesOnStartup()
			.SeedInitialDataOnStartup()
			.IntegrateWithWolverine();
	}

	public sealed class CatalogueProjection : SingleStreamProjection<CatalogueView, string>
	{
		public void Apply(DefaultsChanged e, CatalogueView state) => state.Apply(e);
		public void Apply(ClientAdded e, CatalogueView state) => state.Apply(e);
		public void Apply(ClientChanged e, CatalogueView state) => state.Apply(e);
		public void Apply(ClientArchived e, CatalogueView state) => state.Apply(e);
		public void Apply(ProjectAdded e, CatalogueView state) => state.Apply(e);
		public void Apply(ProjectChanged e, CatalogueView state) => state.Apply(e);
		public void Apply(ProjectArchived e, CatalogueView state) => state.Apply(e);
	}

	public sealed class TrackerProjection : SingleStreamProjection<OpenTask, string>
	{
		public void Apply(OpenTaskSet e, OpenTask state) => state.Apply(e);
		public void Apply(OpenTaskCleared e, OpenTask state) => state.Apply(e);
	}

	public sealed class TaskProjection : SingleStreamProjection<TaskView, string>
	{
		public void Apply(TaskStarted e, TaskView state) => state.Apply(e);
		public void Apply(TaskPaused e, TaskView state) => state.Apply(e);
		public void Apply(TaskResumed e, TaskView state) => state.Apply(e);
		public void Apply(TaskStopped e, TaskView state) => state.Apply(e);
		public void Apply(TaskRecorded e, TaskView state) => state.Apply(e);
		public void Apply(IntervalAmended e, TaskView state) => state.Apply(e);
		public void Apply(TaskReassigned e, TaskView state) => state.Apply(e);
		public void Apply(TaskRefChanged e, TaskView state) => state.Apply(e);
		public void Apply(TaskVoided e, TaskView state) => state.Apply(e);
	}
}
