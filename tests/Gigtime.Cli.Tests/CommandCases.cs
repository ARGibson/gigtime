using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;

namespace Gigtime.Cli.Tests;

internal static class CommandCases
{
	internal static readonly string[] Names =
	[
		"defaults show", "defaults set", "client add", "client set", "client list", "client archive",
		"project add", "project set", "project list", "project archive", "start", "pause", "resume", "stop", "status", "log", "show", "amend",
		"add", "void", "report",
	];

	internal static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-03-02T09:00:00Z", CultureInfo.InvariantCulture);
	internal static readonly Guid ClientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
	internal static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

	internal static readonly TaskDto Task = new("01JTASK",
		new(ClientId, "example"),
		new(ProjectId, "work"),
		"ticket-7",
		"stopped",
		[new(1, Start, Start.AddSeconds(3661))],
		3661,
		4500,
		"01:15:00",
		new("up:15", new("25.00", "GBP"), "GBP"),
		Start,
		Start.AddSeconds(3661));

	internal static string[] Args(string name)
	{
		return name switch
		{
			"amend" =>
			[
				"amend", "01JTASK", "--interval", "1", "--start", "2026-03-02T09:00Z", "--end", "10:00", "--client", "example", "--project", "work",
				"--ref", "corrected"
			],
			"add" => ["add", "example", "work", "--from", "2026-03-02T09:00", "--to", "10:00", "--ref", "backfill"],
			"void" => ["void", "01JTASK", "--reason", "mistake"],
			"report" => ["report", "--range", "2026-03-01..2026-03-31", "--client", "example", "--project", "work"],

			"defaults show" => ["defaults", "show"],
			"defaults set" => ["defaults", "set", "--timezone", "Europe/London", "--week-starts", "Monday", "--rounding", "up:15"],
			"client add" =>
				["client", "add", "example", "--name", "Example [blue]", "--currency", "GBP", "--timezone", "Europe/London", "--rounding", "up:15"],
			"client set" =>
			[
				"client", "set", "old", "--key", "example", "--name", "Example [blue]", "--currency", "GBP", "--timezone", "Europe/London",
				"--rounding", "up:15"
			],
			"client list" => ["client", "list", "--all"],
			"client archive" => ["client", "archive", "example"],
			"project add" => ["project", "add", "example", "work", "--name", "Work", "--rate", "25.00", "--rounding", "up:15"],
			"project set" => ["project", "set", "example", "old", "--key", "work", "--name", "Work", "--rate", "25.00", "--rounding", "up:15"],
			"project list" => ["project", "list", "example", "--all"],
			"project archive" => ["project", "archive", "example", "work"],
			"start" => ["start", "example", "work", "--ref", "ticket-7"],
			"log" => ["log", "--last", "3", "--client", "example", "--project", "work", "--all"],
			"show" => ["show", "01JTASK"],
			_ => [name],
		};
	}

	internal static (string Method, string Path) Route(string name)
	{
		return name switch
		{
			"amend" => ("POST", "/v1/tasks/01JTASK/amend"),
			"add" => ("POST", "/v1/tasks/add"),
			"void" => ("POST", "/v1/tasks/01JTASK/void"),
			"report" => ("GET", "/v1/report?range=2026-03-01..2026-03-31&client=example&project=work"),

			"defaults show" => ("GET", "/v1/defaults"),
			"defaults set" => ("POST", "/v1/defaults"),
			"client add" => ("POST", "/v1/clients"),
			"client set" => ("PATCH", "/v1/clients/old"),
			"client list" => ("GET", "/v1/clients?all=true"),
			"client archive" => ("POST", "/v1/clients/example/archive"),
			"project add" => ("POST", "/v1/clients/example/projects"),
			"project set" => ("PATCH", "/v1/clients/example/projects/old"),
			"project list" => ("GET", "/v1/projects?client=example&all=true"),
			"project archive" => ("POST", "/v1/clients/example/projects/work/archive"),
			"start" => ("POST", "/v1/tasks/start"),
			"status" => ("GET", "/v1/status"),
			"log" => ("GET", "/v1/tasks?last=3&client=example&project=work&all=true"),
			"show" => ("GET", "/v1/tasks/01JTASK"),
			_ => ("POST", "/v1/tasks/" + name),
		};
	}

	internal static string Body(string name)
	{
		return name switch
		{
			"amend" => JsonSerializer.Serialize(new AmendTaskInput(1, "2026-03-02T09:00Z", "10:00", "example", "work", "corrected"),
				GigtimeJsonContext.Default.AmendTaskInput),
			"add" => JsonSerializer.Serialize(new RecordTaskInput("example", "work", "2026-03-02T09:00", "10:00", "backfill"),
				GigtimeJsonContext.Default.RecordTaskInput),
			"void" => JsonSerializer.Serialize(new VoidTaskRequest("mistake"), GigtimeJsonContext.Default.VoidTaskRequest),

			"defaults set" => JsonSerializer.Serialize(new SetDefaultsRequest("Europe/London", "Monday", "up:15"),
				GigtimeJsonContext.Default.SetDefaultsRequest),
			"client add" => JsonSerializer.Serialize(new AddClientRequest("example", "Example [blue]", "GBP", "Europe/London", "up:15"),
				GigtimeJsonContext.Default.AddClientRequest),
			"client set" => JsonSerializer.Serialize(
				new ChangeClientRequest("example", "Example [blue]", "GBP", "Europe/London", Rounding: "up:15"),
				GigtimeJsonContext.Default.ChangeClientRequest),
			"project add" => JsonSerializer.Serialize(new AddProjectRequest("work", "Work", "25.00", "up:15"),
				GigtimeJsonContext.Default.AddProjectRequest),
			"project set" => JsonSerializer.Serialize(new ChangeProjectRequest(Key: "work", Name: "Work", Rate: "25.00", Rounding: "up:15"),
				GigtimeJsonContext.Default.ChangeProjectRequest),
			"start" => JsonSerializer.Serialize(new StartTaskRequest("example", "work", "ticket-7"), GigtimeJsonContext.Default.StartTaskRequest),
			_ => "",
		};
	}

	internal static string Response(string name)
	{
		var client = new ClientDto(ClientId, "example", "Example [blue]", "GBP", "Europe/London", "up:15", false);
		var project = new ProjectDto(ProjectId, "work", new(ClientId, "example"), "Work", new("25.00", "GBP"), "up:15", false);

		return name switch
		{
			"report" => JsonSerializer.Serialize(Envelopes.Success(name,
					new ReportDto(
						[new(new(ClientId, "example"), new(ProjectId, "work"), "GBP", 3661, 4500, "01:15:00", new("GBP", "12.50", "12.50"))],
						[new(new(ClientId, "example"), "GBP", 3661, 4500, "01:15:00", new("GBP", "12.50", "12.50"))])),
				GigtimeJsonContext.Default.ReportResponse),

			"defaults show" or "defaults set" => JsonSerializer.Serialize(
				Envelopes.Success(name, new DefaultsDto("Europe/London", "Monday", "up:15")),
				GigtimeJsonContext.Default.DefaultsResponse),
			"client add" or "client set" => JsonSerializer.Serialize(Envelopes.Success(name, client), GigtimeJsonContext.Default.ClientResponse),
			"client list" => JsonSerializer.Serialize(Envelopes.Success(name, new ClientsDto([client])),
				GigtimeJsonContext.Default.ClientsResponse),
			"project add" or "project set" => JsonSerializer.Serialize(Envelopes.Success(name, project),
				GigtimeJsonContext.Default.ProjectResponse),
			"project list" => JsonSerializer.Serialize(Envelopes.Success(name, new ProjectsDto([project])),
				GigtimeJsonContext.Default.ProjectsResponse),
			"client archive" or "project archive" => JsonSerializer.Serialize(Envelopes.Success(name, new AcknowledgementDto("archived-id")),
				GigtimeJsonContext.Default.AcknowledgementResponse),
			"log" => JsonSerializer.Serialize(Envelopes.Success(name, new TasksDto([Task])), GigtimeJsonContext.Default.TasksResponse),
			"status" => JsonSerializer.Serialize(Envelopes.Success(name, new StatusDto(Task)), GigtimeJsonContext.Default.StatusResponse),
			_ => JsonSerializer.Serialize(Envelopes.Success(name, Task), GigtimeJsonContext.Default.TaskResponse),
		};
	}

	internal static string Marker(string name)
	{
		return name switch
		{
			"report" => "12.50",
			"defaults show" or "defaults set" => "Europe/London",
			"client add" or "client set" or "client list" => "Example [blue]",
			"project add" or "project set" or "project list" => "25.00 GBP",
			"client archive" or "project archive" => "archived-id",
			_ => "01:15:00",
		};
	}
}
