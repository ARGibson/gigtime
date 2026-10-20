using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;

namespace Gigtime.Contracts.Tasks;

public sealed record IntervalDto(int Number, DateTimeOffset Start, DateTimeOffset? End);

public sealed record SubmissionDto(
	DateTimeOffset SubmittedAt,
	long RoundedSeconds,
	string Rounding,
	MoneyDto? Rate,
	string Currency,
	EarningsDto? Amount);

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
	SubmissionDto? Submission,
	DateTimeOffset StartedAt,
	DateTimeOffset? StoppedAt,
	string Timezone = "UTC",
	MoneyDto? Rate = null);

public sealed record TasksDto(IReadOnlyList<TaskDto> Tasks);

public sealed record StatusDto(TaskDto? Running, IReadOnlyList<TaskDto> Paused, string? Instance = null);
