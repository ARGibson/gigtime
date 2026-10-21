using System.CommandLine;

namespace Gigtime.Cli;

internal static class CommandOptions
{
	internal static Command Add(Command parent, string name, string description)
	{
		var command = new Command(name, description);
		parent.Subcommands.Add(command);

		return command;
	}

	internal static void Argument(Command command, string name, bool optional = false)
	{
		var argument = new Argument<string?>(name)
		{
			Arity = optional ? ArgumentArity.ZeroOrOne : ArgumentArity.ExactlyOne,
			Description = name switch
			{
				"key" => "Supply the " + command.Parents.Single().Name + " key.",
				"task" => "Select a task by id.",
				"target" => "Select a task id or supply a client key.",
				"kind" => "Select clients, projects or tasks.",
				"prefix" => "Filter by key or task id prefix.",
				_ => "Supply the " + name + " key.",
			},
		};

		if (name is "task" or "target")
		{
			argument.Validators.Add(result =>
			{
				if (result.Tokens.Any(token => token.Value.StartsWith('-')))
					result.AddError("Task ids cannot start with '-'.");
			});
		}

		command.Arguments.Add(argument);
	}

	internal static void Text(Command command, string name, bool required = false)
	{
		// #3: non-greedy parsing leaves a following flag (including --json) available to the parser.
		var option = new Option<string?>("--" + name)
		{
			Required = required,
			Arity = ArgumentArity.ZeroOrOne,
			Description = name switch
			{
				"timezone" => "Set the IANA timezone.",
				"week-starts" => "Set the first weekday.",
				"rounding" => "Set the rounding policy.",
				"name" => "Set the display name.",
				"key" => "Set the new key.",
				"currency" => "Set the currency code.",
				"rate" => "Set the hourly rate.",
				"state" => "Filter by task state.",
				"ref" => "Set an opaque reference without task content.",
				"client" => "Select the client key.",
				"project" => "Select the project key.",
				"range" => "Select inclusive local dates as yyyy-MM-dd..yyyy-MM-dd.",
				"from" or "to" when command.Name == "export" => "Set an ISO instant or inclusive UTC date bound.",
				"from" or "to" or "start" or "end" => "Set an ISO instant or local time in the client's timezone.",
				"reason" => "Record the reason for voiding the task.",
				"gigtimed" => "Select the absolute daemon executable path.",
				_ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown text option."),
			},
		};

		option.Validators.Add(result =>
		{
			if (result.Tokens.Count == 0) result.AddError($"--{name} requires a value.");
		});

		command.Options.Add(option);
	}

	internal static void Flag(Command command, string name)
	{
		command.Options.Add(new Option<bool>("--" + name)
		{
			Description = name switch
			{
				"all" => "Include archived or voided entries.",
				"submit" => "Submit the task when stopping.",
				"switch" => "Pause the running task and run this task.",
				"week" => "Report this week.",
				"month" => "Report this month.",
				"check" => "Inspect setup and health without changes.",
				_ when name.StartsWith("clear-", StringComparison.Ordinal) => "Clear the " + name[6..] + " override.",
				_ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown flag."),
			},
		});
	}

	internal static void Exclusive(Command command, string value, string clear)
	{
		Text(command, value);
		Flag(command, clear);

		command.Validators.Add(result =>
		{
			if (result.GetValue<string>("--" + value) is not null && result.GetValue<bool>("--" + clear))
				result.AddError($"--{value} and --{clear} cannot be combined.");
		});
	}

	internal static string Segment(string value) => Uri.EscapeDataString(value);

	internal static string Query(params (string Name, string? Value)[] values)
	{
		var pairs = values.Where(v => v.Value is not null).Select(v => v.Name + "=" + Uri.EscapeDataString(v.Value!));
		var query = string.Join("&", pairs);

		return query.Length == 0 ? "" : "?" + query;
	}
}
