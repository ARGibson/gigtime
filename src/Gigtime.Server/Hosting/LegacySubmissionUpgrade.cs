using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Hosting;

public sealed class LegacySubmissionUpgrade(IDocumentSessionFactory sessions) : IHostedService
{
	public const string StreamId = "migrations";
	public const string Name = "submit-legacy-stopped";

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await using var query = sessions.QuerySession();
		var markers = await query.Events.FetchStreamAsync(StreamId, token: cancellationToken);

		if (markers.Any(e => e.Data is MigrationApplied { Name: Name })) return;

		var tasks = await query.Query<TaskView>().ToListAsync(cancellationToken);

		foreach (var document in tasks)
		{
			await using var session = sessions.LightweightSession();
			var stream = await session.Events.FetchForWriting<TaskView>(document.Id, cancellationToken);
			var task = new TaskView();
			foreach (var e in await session.Events.FetchStreamAsync(document.Id, token: cancellationToken)) task.Evolve(e.Data);

			if (LegacySubmissionMigration.Decide(task) is not { } submitted) continue;

			stream.AppendMany(CorrelatedEvents.Wrap([submitted], Name));
			await session.SaveChangesAsync(cancellationToken);
		}

		await using var marker = sessions.LightweightSession();
		marker.Events.Append(StreamId, CorrelatedEvents.Wrap([new MigrationApplied(Name)], Name));
		await marker.SaveChangesAsync(cancellationToken);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
