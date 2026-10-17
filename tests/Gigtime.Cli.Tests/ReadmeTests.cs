using System.CommandLine;
using System.Reflection;
using System.Text.RegularExpressions;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed partial class ReadmeTests
{
	[Fact]
	public void Readme_command_table_matches_every_visible_leaf_in_the_real_parser()
	{
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => throw new InvalidOperationException());
		var commands = Leaves(app.CreateCommand(), "gig").Order(StringComparer.Ordinal);
		var documented = CommandRows().Matches(Readme()).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);
		documented.ShouldBe(commands);
	}

	[Fact]
	public void Readme_exit_table_matches_every_public_exit_constant_exactly()
	{
		var expected = typeof(ExitCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(f => f.IsLiteral)
			.ToDictionary(f => (int)f.GetRawConstantValue()!, f => SnakeCase(f.Name));

		var actual = ExitRows()
			.Matches(Readme())
			.ToDictionary(m => int.Parse(m.Groups[1].Value,
					System.Globalization.CultureInfo.InvariantCulture),
				m => m.Groups[2].Value);

		actual.OrderBy(p => p.Key).ShouldBe(expected.OrderBy(p => p.Key));
	}

	[Fact]
	public void Readme_names_every_public_response_json_member_from_source_generated_metadata()
	{
		Type[] types =
		[
			typeof(Envelope<TaskDto>), typeof(ErrorDto), typeof(ErrorStateDto), typeof(DefaultsDto), typeof(ClientDto),
			typeof(ProjectDto), typeof(ClientsDto), typeof(ProjectsDto), typeof(EntityDto), typeof(MoneyDto), typeof(TaskDto),
			typeof(IntervalDto), typeof(PolicySnapshotDto), typeof(TasksDto), typeof(StatusDto), typeof(ReportDto), typeof(ReportTotalDto),
			typeof(ClientReportTotalDto), typeof(EarningsDto), typeof(AcknowledgementDto), typeof(HealthDto), typeof(SetupDto),
			typeof(ExportEventDto)
		];

		var readme = Readme();

		foreach (var type in types)
		{
			foreach (var property in GigtimeJsonContext.Default.GetTypeInfo(type)!.Properties)
				readme.ShouldContain("`" + property.Name + "`");
		}
	}

	[Fact]
	public async Task Zsh_script_names_every_parser_flag()
	{
		var app = new CliApplication(TextWriter.Null, TextWriter.Null, false, false, () => throw new InvalidOperationException());
		using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
		var printing = new CliApplication(output, TextWriter.Null, false, false, () => throw new InvalidOperationException());
		(await printing.RunAsync(["completion", "zsh"], TestContext.Current.CancellationToken)).ShouldBe(0);
		var script = output.ToString();
		foreach (var option in Options(app.CreateCommand())) script.ShouldContain(option.Name);
	}

	private static IEnumerable<string> Leaves(Command command, string path)
	{
		foreach (var child in command.Subcommands.Where(c => !c.Hidden))
		{
			var next = path + " " + child.Name;

			if (child.Subcommands.Count == 0)
			{
				yield return next;
			}
			else
			{
				foreach (var leaf in Leaves(child, next))
					yield return leaf;
			}
		}
	}

	private static IEnumerable<Option> Options(Command command) => command.Options.Concat(
		command.Subcommands.Where(c => !c.Hidden).SelectMany(Options));

	private static string Readme()
	{
		var root = Path.GetFullPath(typeof(ReadmeTests)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == "RepositoryRoot")
			.Value!);

		return File.ReadAllText(Path.Combine(root, "README.md"));
	}

	private static string SnakeCase(string value) =>
		Regex.Replace(value, "([a-z])([A-Z])", "$1_$2", RegexOptions.CultureInvariant).ToLowerInvariant();

	[GeneratedRegex(@"^\| `(gig [^`]+)` \|", RegexOptions.Multiline)]
	private static partial Regex CommandRows();

	[GeneratedRegex(@"^\| ([0-8]) \| ([a-z_]+) \|", RegexOptions.Multiline)]
	private static partial Regex ExitRows();
}
