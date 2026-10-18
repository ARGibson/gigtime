using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class TaskResponses
{
	public static TaskDto From(TaskView task, CatalogueView catalogue, DateTimeOffset now)
	{
		var p = catalogue.Projects[task.ProjectId];
		var c = catalogue.Clients[p.ClientId];
		var raw = task.RawSecondsAt(now);
		var rounded = task.Snapshot is null ? raw : task.RoundedSeconds;

		var snapshot = task.Snapshot is { } policy
			? new PolicySnapshotDto(policy.Rounding, policy.Rate is { } rate ? MoneyDto.From(rate, policy.Currency) : null, policy.Currency)
			: null;

		return new(task.Id,
			new(c.Id, c.Key),
			new(p.Id, p.Key),
			task.Ref,
			task.State.ToString().ToLowerInvariant(),
			task.Intervals.Select(i => new IntervalDto(i.Number, i.Start, i.End)).ToArray(),
			raw,
			rounded,
			DurationText.Format(rounded),
			snapshot,
			task.StartedAt!.Value,
			task.StoppedAt,
			c.Timezone ?? catalogue.Defaults.Timezone);
	}
}
