using Gigtime.Contracts.Catalogue;

namespace Gigtime.Contracts.Reporting;

public sealed record TimesheetTaskDto(string TaskId, EntityDto Client, EntityDto Project, string? Ref, long RawSeconds, string Display);

public sealed record TimesheetDayDto(string Date, long RawSeconds, string Display, IReadOnlyList<TimesheetTaskDto> Tasks);

public sealed record TimesheetProjectTotalDto(EntityDto Client, EntityDto Project, long RawSeconds, string Display);

public sealed record TimesheetClientTotalDto(EntityDto Client, long RawSeconds, string Display);

public sealed record TimesheetDto(
	IReadOnlyList<TimesheetDayDto> Days,
	IReadOnlyList<TimesheetProjectTotalDto> Projects,
	IReadOnlyList<TimesheetClientTotalDto> Clients);
