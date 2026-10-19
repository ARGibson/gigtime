using System.Globalization;
using Gigtime.Contracts.Reporting;
using Gigtime.Domain.Reporting;

namespace Gigtime.Server.Reporting;

public static class TimesheetResponses
{
	public static TimesheetDto From(TimesheetResult result, CatalogueView catalogue)
	{
		var days = result
			.Days.Select(d => new TimesheetDayDto(d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				d.RawSeconds,
				DurationText.Format(d.RawSeconds),
				d
					.Tasks.Select(t => new TimesheetTaskDto(t.TaskId,
						new(t.ClientId, catalogue.Clients[t.ClientId].Key),
						new(t.ProjectId, catalogue.Projects[t.ProjectId].Key),
						t.Ref,
						t.RawSeconds,
						DurationText.Format(t.RawSeconds)))
					.ToArray()))
			.ToArray();

		var projects = result
			.Projects.Select(t => new TimesheetProjectTotalDto(
				new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				new(t.ProjectId, catalogue.Projects[t.ProjectId].Key),
				t.RawSeconds,
				DurationText.Format(t.RawSeconds)))
			.ToArray();

		var clients = result
			.Clients.Select(t => new TimesheetClientTotalDto(
				new(t.ClientId, catalogue.Clients[t.ClientId].Key),
				t.RawSeconds,
				DurationText.Format(t.RawSeconds)))
			.ToArray();

		return new(days, projects, clients);
	}
}
