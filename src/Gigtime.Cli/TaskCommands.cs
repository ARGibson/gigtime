using System.CommandLine;
using System.Globalization;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class TaskCommands
{
	internal static void Configure(RootCommand root, CliExchange exchange)
	{
		var start = Add(root, "start", "Start a task and its first interval.");
		Argument(start, "client");
		Argument(start, "project");
		Text(start, "ref");
		Text(start, "rate");

		start.SetAction((r, ct) => exchange.WriteAsync(r,
			"start",
			HttpMethod.Post,
			"/v1/tasks/start",
			new StartTaskRequest(r.GetValue<string>("client")!,
				r.GetValue<string>("project")!,
				r.GetValue<string>("--ref"),
				r.GetValue<string>("--rate")),
			GigtimeJsonContext.Default.StartTaskRequest,
			GigtimeJsonContext.Default.TaskResponse,
			ct));

		foreach (var name in new[] { "pause", "resume", "stop" })
		{
			var command = Add(root, name, char.ToUpperInvariant(name[0]) + name[1..] + " the open task.");
			if (name == "stop") Flag(command, "submit");

			command.SetAction((r, ct) => exchange.SendAsync(r,
				name,
				HttpMethod.Post,
				"/v1/tasks/" + name + (name == "stop" && r.GetValue<bool>("--submit") ? "?submit=true" : ""),
				null,
				GigtimeJsonContext.Default.TaskResponse,
				ct));
		}

		foreach (var name in new[] { "submit", "unsubmit" })
		{
			var command = Add(root,
				name,
				name == "submit"
					? "Submit a stopped task and fix its figure."
					: "Return a submitted task to stopped for corrections.");

			Argument(command, "task");

			command.SetAction((r, ct) => exchange.SendAsync(r,
				name,
				HttpMethod.Post,
				"/v1/tasks/" + Segment(r.GetValue<string>("task")!) + "/" + name,
				null,
				GigtimeJsonContext.Default.TaskResponse,
				ct));
		}

		var status = Add(root, "status", "Show the open task and running totals, or none.");
		status.SetAction((r, ct) => exchange.GetAsync(r, "status", "/v1/status", GigtimeJsonContext.Default.StatusResponse, ct));
		var log = Add(root, "log", "Show recent tasks, newest first (default 10).");

		var last = new Option<int?>("--last")
		{
			Arity = ArgumentArity.ExactlyOne,
			Description = "Limit the number of recent tasks."
		};

		log.Options.Add(last);

		log.Validators.Add(result =>
		{
			if (result.GetResult(last) is { Tokens.Count: > 0 } option
					&& int.TryParse(option.Tokens[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count <= 0)
			{
				result.AddError("--last must be a positive integer.");
			}
		});

		Text(log, "client");
		Text(log, "project");
		Flag(log, "all");

		log.SetAction((r, ct) => exchange.GetAsync(r,
			"log",
			"/v1/tasks" + Query(("last", r.GetValue(last)?.ToString(CultureInfo.InvariantCulture)),
				("client", r.GetValue<string>("--client")),
				("project", r.GetValue<string>("--project")),
				("all", r.GetValue<bool>("--all") ? "true" : null)),
			GigtimeJsonContext.Default.TasksResponse,
			ct));

		var show = Add(root, "show", "Show a task with numbered intervals.");
		Argument(show, "task");

		show.SetAction((r, ct) => exchange.GetAsync(r,
			"show",
			"/v1/tasks/" + Segment(r.GetValue<string>("task")!),
			GigtimeJsonContext.Default.TaskResponse,
			ct));
	}
}
