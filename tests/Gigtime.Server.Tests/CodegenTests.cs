using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class CodegenTests
{
	[Fact]
	public async Task Codegen_write_does_not_create_a_runtime_folder()
	{
		var directory = Path.Combine(Path.GetTempPath(), "gig-codegen-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		var start = new ProcessStartInfo("dotnet")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			WorkingDirectory = directory
		};

		start.ArgumentList.Add(ServerExecutable.Path);
		start.ArgumentList.Add("codegen");
		start.ArgumentList.Add("write");

		start.Environment.Remove("GIGTIME_INSTANCE");

		foreach (var (key, leaf) in new[] { ("XDG_DATA_HOME", "data"), ("XDG_RUNTIME_DIR", "runtime"), ("XDG_CONFIG_HOME", "config") })
			start.Environment[key] = Path.Combine(directory, leaf);

		using var process = Process.Start(start)!;
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);

		try
		{
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			process.ExitCode.ShouldBe(0, await output + await error);
			Directory.Exists(Path.Combine(directory, "runtime")).ShouldBeFalse();
		}
		finally
		{
			if (!process.HasExited) process.Kill(true);
			await process.WaitForExitAsync(TestContext.Current.CancellationToken);
			Directory.Delete(directory, true);
		}
	}
}
