using Gigtime.Contracts.Catalogue;

namespace Gigtime.Contracts.Tasks;

public sealed record IntervalDto(int Number, DateTimeOffset Start, DateTimeOffset? End);

public sealed record PolicySnapshotDto(string Rounding, MoneyDto? Rate, string Currency);

public sealed record TaskDto(
	string Id,
	EntityDto Client,
	EntityDto Project,
	string? Ref,
	string State,
	IReadOnlyList<IntervalDto> Intervals,
	long RawSeconds,
	long RoundedSeconds,
	string Display,
	PolicySnapshotDto? Snapshot,
	DateTimeOffset StartedAt,
	DateTimeOffset? StoppedAt,
	string Timezone = "UTC");

public sealed record TasksDto(IReadOnlyList<TaskDto> Tasks);

public sealed record StatusDto(TaskDto? Task, string? Instance = null);
