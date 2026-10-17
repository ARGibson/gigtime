using System.Globalization;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Tasks;
using Spectre.Console;

namespace Gigtime.Cli;

internal sealed class HumanOutput
{
	private readonly IAnsiConsole console;

	internal HumanOutput(TextWriter output, bool isTerminal, bool noColor)
	{
		var color = isTerminal && !noColor;

		console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Out = new AnsiConsoleOutput(output),
			Ansi = color ? AnsiSupport.Yes : AnsiSupport.No,
			ColorSystem = color ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
			Interactive = InteractionSupport.No,
		});

		console.Profile.Width = 180;
	}

	internal void Write(object data, string command)
	{
		switch (data)
		{
			case DefaultsDto defaults:
				Table(["Timezone", "Week starts", "Rounding"], [[defaults.Timezone, defaults.WeekStarts, defaults.Rounding]]);

				break;
			case ClientDto client:
				Clients([client]);

				break;
			case ClientsDto clients:
				Clients(clients.Clients);

				break;
			case ProjectDto project:
				Projects([project]);

				break;
			case ProjectsDto projects:
				Projects(projects.Projects);

				break;
			case TaskDto task:
				Task(task);
				if (command == "stop") console.WriteLine("Submit the rounded figure.");

				break;
			case StatusDto { Task: { } task }:
				Task(task);

				break;
			case StatusDto:
				console.WriteLine("none");

				break;
			case TasksDto tasks:
				Table(["Id", "Client", "Project", "Ref", "State", "Raw", "Rounded"],
					tasks.Tasks.Select(t => new[]
					{
						t.Id, t.Client.Key, t.Project.Key, t.Ref ?? "", t.State, DurationText.Format(t.RawSeconds),
						DurationText.Format(t.RoundedSeconds),
					}));

				break;
			case AcknowledgementDto acknowledgement:
				console.WriteLine("Archived " + acknowledgement.Id);

				break;
		}
	}

	private void Clients(IEnumerable<ClientDto> clients)
	{
		Table(["Key", "Name", "Currency", "Timezone", "Rounding", "Archived"],
			clients.Select(c => new[] { c.Key, c.Name, c.Currency, c.Timezone, c.Rounding, c.Archived ? "yes" : "no", }));
	}

	private void Projects(IEnumerable<ProjectDto> projects)
	{
		Table(["Client", "Key", "Name", "Rate", "Rounding", "Archived"],
			projects.Select(p => new[]
			{
				p.Client.Key, p.Key, p.Name ?? "", p.Rate is null ? "none" : p.Rate.Amount + " " + p.Rate.Currency, p.Rounding,
				p.Archived ? "yes" : "no",
			}));
	}

	private void Task(TaskDto task)
	{
		Table(["Id", "Client", "Project", "Ref", "State", "Raw", "Rounded"],
		[
			new[]
			{
				task.Id, task.Client.Key, task.Project.Key, task.Ref ?? "", task.State, DurationText.Format(task.RawSeconds),
				DurationText.Format(task.RoundedSeconds),
			}
		]);

		var stopped = task.StoppedAt is null ? "open" : Instant(task.StoppedAt);
		console.WriteLine("Started: " + Instant(task.StartedAt) + "  Stopped: " + stopped);

		Table(["Interval", "Start (UTC)", "End (UTC)"],
			task.Intervals.Select(i => new[] { i.Number.ToString(CultureInfo.InvariantCulture), Instant(i.Start), Instant(i.End), }));
	}

	private static string Instant(DateTimeOffset? instant) => instant?.ToString("O", CultureInfo.InvariantCulture) ?? "running";

	private void Table(string[] headings, IEnumerable<string[]> rows)
	{
		var table = new Table().Border(TableBorder.Simple);

		foreach (var heading in headings)
			table.AddColumn(new TableColumn(new Text(heading, new Style(Color.Blue, decoration: Decoration.Bold))));

		foreach (var row in rows) table.AddRow(row.Select(cell => new Text(cell)).ToArray());

		console.Write(table);
	}
}
