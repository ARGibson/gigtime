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
		command.Arguments.Add(new Argument<string?>(name)
		{
			Arity = optional ? ArgumentArity.ZeroOrOne : ArgumentArity.ExactlyOne,
		});
	}

	internal static void Text(Command command, string name, bool required = false)
	{
		// #3: non-greedy parsing leaves a following flag (including --json) available to the parser.
		var option = new Option<string?>("--" + name)
		{
			Required = required,
			Arity = ArgumentArity.ZeroOrOne,
		};

		option.Validators.Add(result =>
		{
			if (result.Tokens.Count == 0) result.AddError($"--{name} requires a value.");
		});

		command.Options.Add(option);
	}

	internal static void Flag(Command command, string name) => command.Options.Add(new Option<bool>("--" + name));

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
