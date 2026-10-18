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
	public void Readme_json_table_matches_public_response_members_in_both_directions()
	{
		(string Name, Type Type)[] shapes =
		[
			("Envelope", typeof(Envelope<TaskDto>)), ("Error", typeof(ErrorDto)), ("Error state", typeof(ErrorStateDto)),
			("Defaults", typeof(DefaultsDto)), ("Client", typeof(ClientDto)), ("Project", typeof(ProjectDto)),
			("Client list", typeof(ClientsDto)), ("Project list", typeof(ProjectsDto)), ("Entity", typeof(EntityDto)),
			("Money", typeof(MoneyDto)), ("Task", typeof(TaskDto)), ("Interval", typeof(IntervalDto)),
			("Policy snapshot", typeof(PolicySnapshotDto)), ("Log", typeof(TasksDto)), ("Status", typeof(StatusDto)),
			("Report", typeof(ReportDto)), ("Project report total", typeof(ReportTotalDto)),
			("Client/currency report total", typeof(ClientReportTotalDto)), ("Earnings", typeof(EarningsDto)),
			("Acknowledgement", typeof(AcknowledgementDto)), ("Health", typeof(HealthDto)), ("Setup/check", typeof(SetupDto)),
			("Export event", typeof(ExportEventDto))
		];

		var table = Readme().Split("| Data shape | JSON keys |")[1].Split("\n\n")[0];

		var rows = table
			.Split('\n', StringSplitOptions.RemoveEmptyEntries)
			.Where(line => !line.StartsWith("|---", StringComparison.Ordinal))
			.Select(line => line.Split('|'))
			.ToDictionary(parts => parts[1].Split('(')[0].Trim(), parts => parts[2]);

		rows.Keys.Order(StringComparer.Ordinal).ShouldBe(shapes.Select(shape => shape.Name).Order(StringComparer.Ordinal));

		foreach (var (name, type) in shapes)
		{
			var actual = JsonKeys().Matches(rows[name]).Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal);

			var expected = GigtimeJsonContext.Default.GetTypeInfo(type)!
				.Properties.Select(property => property.Name)
				.Order(StringComparer.Ordinal);

			actual.ShouldBe(expected, name);
		}
	}

	[Fact]
	public void Status_and_setup_json_include_explicit_instance_even_for_default()
	{
		var status = System.Text.Json.JsonSerializer.Serialize(Envelopes.Success("status", new StatusDto(null)),
			GigtimeJsonContext.Default.StatusResponse);

		status.ShouldContain("\"instance\":null");
		GigtimeJsonContext.Default.GetTypeInfo(typeof(SetupDto))!.Properties.Select(p => p.Name).ShouldContain("instance");
	}

	[Fact]
	public void Readme_server_install_uses_the_fixture_framework_dependent_publish_for_linux_x64()
	{
		var command = Readme().Split('\n').Single(line => line.StartsWith("dotnet publish src/Gigtime.Server ", StringComparison.Ordinal));
		command.ShouldBe("dotnet publish src/Gigtime.Server -c Release -r linux-x64 --self-contained false --no-restore -o artifacts/install");
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

	[GeneratedRegex("`([^`]+)`")]
	private static partial Regex JsonKeys();

	[GeneratedRegex(@"^\| `(gig [^`]+)` \|", RegexOptions.Multiline)]
	private static partial Regex CommandRows();

	[GeneratedRegex(@"^\| ([0-8]) \| ([a-z_]+) \|", RegexOptions.Multiline)]
	private static partial Regex ExitRows();
}
