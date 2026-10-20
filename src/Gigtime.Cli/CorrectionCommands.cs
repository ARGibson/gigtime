using System.CommandLine;
using System.Globalization;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class CorrectionCommands
{
	internal static void Configure(RootCommand root, CliExchange exchange)
	{
		var add = Add(root, "add", "Backfill a new task or add an interval to an existing task.");
		Argument(add, "target");
		Argument(add, "project", true);
		Time(add, "from", true);
		Time(add, "to", true);
		Text(add, "ref");
		Text(add, "rate");

		add.SetAction((r, ct) =>
		{
			var target = r.GetValue<string>("target")!;
			var project = r.GetValue<string>("project");

			if (project is null)
			{
				if (r.GetValue<string>("--ref") is not null || r.GetValue<string>("--rate") is not null)
				{
					return Task.FromResult(exchange.Fail("add",
						ErrorCodes.Validation,
						"Adding an interval to a task takes no --ref or --rate.",
						r.GetValue<bool>("--json")));
				}

				return exchange.WriteAsync(r,
					"add",
					HttpMethod.Post,
					"/v1/tasks/" + Segment(target) + "/add",
					new AddIntervalInput(r.GetValue<string>("--from")!, r.GetValue<string>("--to")!),
					GigtimeJsonContext.Default.AddIntervalInput,
					GigtimeJsonContext.Default.TaskResponse,
					ct);
			}

			return exchange.WriteAsync(r,
				"add",
				HttpMethod.Post,
				"/v1/tasks/add",
				new RecordTaskInput(target,
					project,
					r.GetValue<string>("--from")!,
					r.GetValue<string>("--to")!,
					r.GetValue<string>("--ref"),
					r.GetValue<string>("--rate")),
				GigtimeJsonContext.Default.RecordTaskInput,
				GigtimeJsonContext.Default.TaskResponse,
				ct);
		});

		var amend = Add(root, "amend", "Correct an unsubmitted task.");
		Argument(amend, "task");

		var interval = new Option<int?>("--interval")
		{
			Arity = ArgumentArity.ExactlyOne,
			Description = "Select the numbered interval to correct."
		};

		amend.Options.Add(interval);
		Time(amend, "start");
		Time(amend, "end");
		Text(amend, "client");
		Text(amend, "project");
		Exclusive(amend, "ref", "clear-ref");
		Exclusive(amend, "rate", "clear-rate");

		amend.Validators.Add(result =>
		{
			var hasTime = result.GetValue<string>("--start") is not null || result.GetValue<string>("--end") is not null;
			var hasInterval = result.GetResult(interval) is not null;
			if (hasTime != hasInterval) result.AddError("Supply --interval and at least one of --start/--end together.");

			if (result.GetResult(interval) is { Tokens.Count: > 0 } option
					&& int.TryParse(option.Tokens[0].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n <= 0)
			{
				result.AddError("--interval must be positive.");
			}

			if ((result.GetValue<string>("--client") is null) != (result.GetValue<string>("--project") is null))
				result.AddError("Supply both --client and --project.");

			if (!hasTime && result.GetValue<string>("--client") is null && result.GetValue<string>("--ref") is null
					&& !result.GetValue<bool>("--clear-ref") && result.GetValue<string>("--rate") is null
					&& !result.GetValue<bool>("--clear-rate"))
			{
				result.AddError("Supply a correction.");
			}
		});

		amend.SetAction((r, ct) => exchange.WriteAsync(r,
			"amend",
			HttpMethod.Post,
			"/v1/tasks/" + Segment(r.GetValue<string>("task")!) + "/amend",
			new AmendTaskInput(r.GetValue(interval),
				r.GetValue<string>("--start"),
				r.GetValue<string>("--end"),
				r.GetValue<string>("--client"),
				r.GetValue<string>("--project"),
				r.GetValue<string>("--ref"),
				r.GetValue<bool>("--clear-ref"),
				r.GetValue<string>("--rate"),
				r.GetValue<bool>("--clear-rate")),
			GigtimeJsonContext.Default.AmendTaskInput,
			GigtimeJsonContext.Default.TaskResponse,
			ct));

		var @void = Add(root, "void", "Exclude a stopped task from reports, retaining its history.");
		Argument(@void, "task");
		Text(@void, "reason");

		@void.SetAction((r, ct) => exchange.WriteAsync(r,
			"void",
			HttpMethod.Post,
			"/v1/tasks/" + Segment(r.GetValue<string>("task")!) + "/void",
			new VoidTaskRequest(r.GetValue<string>("--reason")),
			GigtimeJsonContext.Default.VoidTaskRequest,
			GigtimeJsonContext.Default.TaskResponse,
			ct));
	}

	private static void Time(Command command, string name, bool required = false)
	{
		Text(command, name, required);

		command.Validators.Add(result =>
		{
			if (result.GetValue<string>("--" + name) is { } value && !InputTime.IsValid(value))
				result.AddError($"--{name} expects ISO 8601 or HH:mm in the client's timezone.");
		});
	}
}
