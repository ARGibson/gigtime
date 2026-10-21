using System.CommandLine;
using Gigtime.Contracts;
using Gigtime.Platform;

namespace Gigtime.Cli;

public sealed class CliApplication(
	TextWriter output,
	TextWriter error,
	bool isTerminal,
	bool noColor,
	Func<IPlatformPaths> paths,
	Func<IPlatformPaths, IServiceInstaller> installers,
	Func<string> executable,
	TimeProvider clock,
	TextReader input,
	bool inputIsTerminal)
{
	public CliApplication(TextWriter output,
		TextWriter error,
		bool isTerminal,
		bool noColor,
		Func<IPlatformPaths> paths,
		Func<IPlatformPaths, IServiceInstaller> installers,
		Func<string> executable,
		TimeProvider clock)
		: this(output, error, isTerminal, noColor, paths, installers, executable, clock, Console.In, !Console.IsInputRedirected) { }

	public CliApplication(TextWriter output,
		TextWriter error,
		bool isTerminal,
		bool noColor,
		Func<IPlatformPaths> paths,
		Func<IPlatformPaths, IServiceInstaller> installers,
		Func<string> executable)
		: this(output, error, isTerminal, noColor, paths, installers, executable, TimeProvider.System) { }

	public CliApplication(TextWriter output, TextWriter error, bool isTerminal, bool noColor, Func<IPlatformPaths> paths)
		: this(output,
			error,
			isTerminal,
			noColor,
			paths,
			p => new LinuxServiceInstaller(p),
			() => File.ResolveLinkTarget("/proc/self/exe", true)!.FullName)
	{ }

	public RootCommand CreateCommand()
	{
		var root = new RootCommand("Track time over the gigtimed Unix socket.");

		var json = new Option<bool>("--json")
		{
			Description = "Print one JSON envelope.",
			Recursive = true
		};

		root.Options.Add(json);
		var exchange = new CliExchange(output, error, isTerminal, noColor, paths, json, clock, input, isTerminal && inputIsTerminal);
		CatalogueCommands.Configure(root, exchange);
		TaskCommands.Configure(root, exchange);
		CorrectionCommands.Configure(root, exchange);
		ReportCommands.Configure(root, exchange);

		SetupCommands.Configure(root, exchange, output, paths, installers, executable, json);
		CompletionCommands.Configure(root, output, paths);

		foreach (var option in root.Options)
		{
			if (option.Name == "--help") option.Description = "Show help and usage information.";
			if (option.Name == "--version") option.Description = "Show version information.";
		}

		return root;
	}

	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
	{
		try
		{
			GigtimeInstance.FromEnvironment();
		}
		catch (ArgumentException exception)
		{
			var failure = new CliExchange(output,
				error,
				isTerminal,
				noColor,
				paths,
				new Option<bool>("--json"),
				clock,
				input,
				isTerminal && inputIsTerminal);

			return failure.Fail("gig", ErrorCodes.Config, exception.Message, args.Contains("--json", StringComparer.Ordinal));
		}

		var root = CreateCommand();
		var json = (Option<bool>)root.Options.Single(o => o.Name == "--json");
		var exchange = new CliExchange(output, error, isTerminal, noColor, paths, json, clock, input, isTerminal && inputIsTerminal);

		var parsed = root.Parse(args,
			new ParserConfiguration
			{
				ResponseFileTokenReplacer = null
			});

		if (parsed.Errors.Count > 0)
		{
			return exchange.Fail("gig",
				ErrorCodes.Usage,
				string.Join(Environment.NewLine, parsed.Errors.Select(e => e.Message)),
				args.Contains("--json", StringComparer.Ordinal));
		}

		if (parsed.Action is null)
			return exchange.Fail("gig", ErrorCodes.Usage, "Choose a command. Run gig --help for usage.", parsed.GetValue(json));

		return await parsed.InvokeAsync(new InvocationConfiguration
		{
			Output = output,
			Error = error,
			EnableDefaultExceptionHandler = false,
		},
			cancellationToken);
	}
}
