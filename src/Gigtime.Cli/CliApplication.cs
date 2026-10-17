using System.CommandLine;
using Gigtime.Contracts;
using Gigtime.Platform;

namespace Gigtime.Cli;

public sealed class CliApplication(TextWriter output, TextWriter error, bool isTerminal, bool noColor, Func<IPlatformPaths> paths)
{
	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
	{
		var root = new RootCommand("gig — time tracking over the gigtimed Unix socket");

		var json = new Option<bool>("--json")
		{
			Description = "Print one JSON envelope.",
			Recursive = true
		};

		root.Options.Add(json);
		var exchange = new CliExchange(output, error, isTerminal, noColor, paths, json);
		CatalogueCommands.Configure(root, exchange);
		TaskCommands.Configure(root, exchange);

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
