using System.CommandLine;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed partial class HelpTests
{
	[Fact]
	public async Task Every_command_option_and_argument_has_capitalized_help_without_repeated_words()
	{
		using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
		var app = new CliApplication(output, TextWriter.Null, false, true, () => throw new InvalidOperationException());
		var count = 0;

		foreach (var (command, args) in Commands(app.CreateCommand(), []))
		{
			Description(command.Description, string.Join(' ', args));
			foreach (var option in command.Options) Description(option.Description, string.Join(' ', args) + " " + option.Name);

			foreach (var argument in command.Arguments)
			{
				Description(argument.Description, string.Join(' ', args) + " <" + argument.Name + ">");
				if (argument.Name == "key") argument.Description.ShouldBe("Supply the " + args[0] + " key.");
			}

			if (command.Hidden) continue;

			output.GetStringBuilder().Clear();
			(await app.RunAsync([.. args, "--help"], TestContext.Current.CancellationToken)).ShouldBe(0);
			output.ToString().ShouldContain(command.Description!);
			count++;
		}

		count.ShouldBeGreaterThan(20);
	}

	private static void Description(string? description, string name)
	{
		description.ShouldNotBeNullOrWhiteSpace(name);
		char.IsUpper(description[0]).ShouldBeTrue(name + ": " + description);
		description.EndsWith('.').ShouldBeTrue(name);
		RepeatedWord().IsMatch(description).ShouldBeFalse(name + ": " + description);
	}

	[GeneratedRegex(@"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex RepeatedWord();

	private static IEnumerable<(Command Command, string[] Args)> Commands(Command command, string[] args)
	{
		yield return (command, args);

		foreach (var child in command.Subcommands)
		{
			foreach (var entry in Commands(child, [.. args, child.Name]))
				yield return entry;
		}
	}
}
