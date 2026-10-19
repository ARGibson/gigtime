using System.Globalization;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Spectre.Console;

namespace Gigtime.Cli;

internal sealed class HumanOutput
{
	private readonly IAnsiConsole console;
	private readonly DateTimeOffset now;

	internal HumanOutput(TextWriter output, bool isTerminal, bool noColor, DateTimeOffset utcNow)
	{
		now = utcNow;
		var color = isTerminal && !noColor;

		console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Out = new AnsiConsoleOutput(output),
			Ansi = color ? AnsiSupport.Yes : AnsiSupport.No,
			ColorSystem = color ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
			Interactive = InteractionSupport.No,
			// #3: CI enrichers must not override gig’s terminal and NO_COLOR decision.
			Enrichment = new ProfileEnrichment
			{
				UseDefaultEnrichers = color
			},
		});

		console.Profile.Width = 180;
	}

	internal void Write(object data, string command)
	{
		if (data is StatusDto { Instance: { } instance }) console.WriteLine("Instance: " + instance);

		switch (data)
		{
			case TimesheetDto timesheet:
				Timesheet(timesheet);

				break;
			case ReportDto report:
				Report(report);

				break;
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
				if (command == "stop" && task.Submission is null) console.WriteLine("Submit the rounded figure.");

				break;
			case StatusDto { Task: { } task }:
				Task(task);

				break;
			case StatusDto:
				console.WriteLine("none");

				break;
			case TasksDto tasks:
				Log(tasks.Tasks);

				break;
			case AcknowledgementDto acknowledgement:
				console.WriteLine("Archived " + acknowledgement.Id);

				break;
		}
	}

	private void Report(ReportDto report)
	{
		Table(["Client", "Project", "Currency", "Raw", "Rounded", "Amount"],
			report.Totals.Select(t => new[]
			{
				t.Client.Key, t.Project.Key, t.Currency, DurationText.Format(t.RawSeconds), DurationText.Format(t.RoundedSeconds),
				t.Amount?.Rounded ?? "not rated",
			}));

		Table(["Client total", "Currency", "Raw", "Rounded", "Amount"],
			report.Clients.Select(t => new[]
			{
				t.Client.Key, t.Currency, DurationText.Format(t.RawSeconds), DurationText.Format(t.RoundedSeconds),
				t.Amount?.Rounded ?? "not rated",
			}));

		console.WriteLine("Unsubmitted (provisional)");

		Table(["Id", "Client", "Project", "Ref", "State", "Raw", "Rounded", "Provisional amount"],
			report.Unsubmitted.Select(t => new[]
			{
				t.TaskId, t.Client.Key, t.Project.Key, t.Ref ?? "", t.State, DurationText.Format(t.RawSeconds),
				DurationText.Format(t.RoundedSeconds), t.Amount is { } amount ? amount.Rounded + " " + amount.Currency : "not rated",
			}));
	}

	private void Timesheet(TimesheetDto timesheet)
	{
		foreach (var day in timesheet.Days)
		{
			console.WriteLine(day.Date + "  " + DurationText.Format(day.RawSeconds));

			Table(["Id", "Client", "Project", "Ref", "Time"],
				day.Tasks.Select(t => new[] { t.TaskId, t.Client.Key, t.Project.Key, t.Ref ?? "", DurationText.Format(t.RawSeconds), }));
		}

		Table(["Client", "Project", "Period time"],
			timesheet.Projects.Select(t => new[] { t.Client.Key, t.Project.Key, DurationText.Format(t.RawSeconds), }));

		Table(["Client total", "Period time"], timesheet.Clients.Select(t => new[] { t.Client.Key, DurationText.Format(t.RawSeconds), }));
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
		var zone = TimeZoneInfo.FindSystemTimeZoneById(task.Timezone);
		console.WriteLine("Times in " + task.Timezone);

		Table(["Id", "Client", "Project", "Ref", "State", "Raw", "Rounded"],
		[
			new[]
			{
				task.Id, task.Client.Key, task.Project.Key, task.Ref ?? "", task.State, DurationText.Format(task.RawSeconds),
				DurationText.Format(task.RoundedSeconds),
			}
		]);

		console.WriteLine("Own rate: " + (task.Rate is { } rate ? rate.Amount + " " + rate.Currency : "project rate"));

		if (task.Submission is { } submission)
		{
			console.WriteLine("Submitted: " + Instant(submission.SubmittedAt, zone));
			console.WriteLine("Submission rounding: " + submission.Rounding);

			console.WriteLine("Submission rate: " + (submission.Rate is { } fixedRate
				? fixedRate.Amount + " " + fixedRate.Currency
				: "none"));

			console.WriteLine("Submission amount: " + (submission.Amount is { } amount
				? amount.Rounded + " " + amount.Currency
				: "not rated"));
		}

		var stopped = task.StoppedAt is null ? "open" : Instant(task.StoppedAt, zone);
		console.WriteLine("Started: " + Instant(task.StartedAt, zone) + "  Stopped: " + stopped);

		Table(["Interval", "Start", "End"],
			task.Intervals.Select(i => new[] { i.Number.ToString(CultureInfo.InvariantCulture), Instant(i.Start, zone), Instant(i.End, zone), }));
	}

	private void Log(IReadOnlyList<TaskDto> tasks)
	{
		string[] headings = ["Id", "Client", "Project", "Ref", "State", "Started", "Stopped", "Raw", "Rounded"];
		var rows = new List<string[]>();
		string? timezone = null;

		foreach (var task in tasks)
		{
			if (task.Timezone != timezone)
			{
				if (rows.Count > 0) Table(headings, rows);
				rows.Clear();
				timezone = task.Timezone;
				console.WriteLine("Times in " + timezone);
			}

			var zone = TimeZoneInfo.FindSystemTimeZoneById(task.Timezone);

			rows.Add([
				task.Id, task.Client.Key, task.Project.Key, task.Ref ?? "", task.State,
				Instant(task.StartedAt, zone), task.StoppedAt is null ? "open" : Instant(task.StoppedAt, zone),
				DurationText.Format(task.RawSeconds), DurationText.Format(task.RoundedSeconds)
			]);
		}

		Table(headings, rows);
	}

	private string Instant(DateTimeOffset? instant, TimeZoneInfo zone)
	{
		if (instant is null) return "running";

		var local = TimeZoneInfo.ConvertTime(instant.Value, zone);
		var today = TimeZoneInfo.ConvertTime(now, zone).Date;

		return local.ToString(local.Date == today ? "HH:mm" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	}

	private void Table(string[] headings, IEnumerable<string[]> rows)
	{
		var table = new Table().Border(TableBorder.Simple);

		foreach (var heading in headings)
			table.AddColumn(new TableColumn(new Text(heading, new Style(Color.Blue, decoration: Decoration.Bold))));

		foreach (var row in rows) table.AddRow(row.Select(cell => new Text(cell)).ToArray());

		console.Write(table);
	}
}
