using Gigtime.Cli;
using Gigtime.Platform;

var application = new CliApplication(Console.Out,
	Console.Error,
	!Console.IsOutputRedirected,
	Environment.GetEnvironmentVariable("NO_COLOR") is not null,
	() => new LinuxPlatformPaths());

return await application.RunAsync(args, CancellationToken.None);
