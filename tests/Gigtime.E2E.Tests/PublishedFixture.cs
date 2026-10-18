using System.Reflection;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

public sealed class PublishedFixture : IAsyncLifetime
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "gig-published-" + Guid.NewGuid().ToString("N"));
	public string Application => Path.Combine(Root, "application");

	public async ValueTask InitializeAsync()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Native end-to-end tests require Linux and clang.");

		Directory.CreateDirectory(Root);

		var repository = Path.GetFullPath(typeof(PublishedFixture)
			.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == "RepositoryRoot")
			.Value!);

		var environment = new Dictionary<string, string>
		{
			["ArtifactsPath"] = Path.Combine(Root, "build"),
			["UseArtifactsOutput"] = "true",
			["XDG_DATA_HOME"] = Path.Combine(Root, "build-data"),
			["XDG_RUNTIME_DIR"] = Path.Combine(Root, "build-runtime"),
			["XDG_CONFIG_HOME"] = Path.Combine(Root, "build-config"),
		};

		foreach (var value in environment.Values.Where(Path.IsPathFullyQualified)) Directory.CreateDirectory(value);

		foreach (var project in new[] { "Gigtime.Cli", "Gigtime.Server" })
		{
			var cli = project == "Gigtime.Cli";

			string[] arguments =
			[
				"publish", "src/" + project, "-c", "Release", "-p:RestoreLockedMode=true",
				"-p:PublishAot=" + (cli ? "true" : "false"), "-warnaserror", "-p:ILLinkTreatWarningsAsErrors=true",
				"-p:IlcTreatWarningsAsErrors=true", "-o", cli ? Path.Combine(Root, "cli") : Application
			];

			var result = await CommandProcess.RunAsync("dotnet", arguments, repository, environment, TimeSpan.FromMinutes(5));
			result.ExitCode.ShouldBe(0, project + " publish failed:\n" + result.Output + result.Error);
		}

		File.Copy(Path.Combine(Root, "cli", "gig"), Path.Combine(Application, "gig"));
		File.Exists(Path.Combine(Application, "gigtimed")).ShouldBeTrue();
	}

	public ValueTask DisposeAsync()
	{
		if (Directory.Exists(Root)) Directory.Delete(Root, true);

		return ValueTask.CompletedTask;
	}
}

[CollectionDefinition("published binaries")]
public sealed class PublishedBinaries : ICollectionFixture<PublishedFixture>;
