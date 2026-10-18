using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class UnitSyntaxTests(PublishedFixture published)
{
	[Theory]
	[InlineData("")]
	[InlineData("dev")]
	public async Task Generated_units_pass_systemd_validation_entirely_under_a_temporary_root(string instance)
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_INSTANCE"] = instance;
		day.Environment["XDG_RUNTIME_DIR"] = Path.Combine(day.Root, "runtime space %");
		Directory.CreateDirectory(day.Environment["XDG_RUNTIME_DIR"]);
		var directory = Path.Combine(day.Root, "daemon space %");
		Directory.CreateDirectory(directory);
		var daemon = Path.Combine(directory, "gigtimed");
		File.Copy(Path.Combine(published.Application, "gigtimed"), daemon);
		var installed = await day.SuccessAsync("setup", "--gigtimed", daemon);
		var service = await File.ReadAllTextAsync(installed.GetProperty("service_unit").GetString()!, TestContext.Current.CancellationToken);
		service.ShouldContain("WorkingDirectory=" + directory.Replace("%", "%%", StringComparison.Ordinal) + "\n");
		service.Contains("Environment=\"GIGTIME_INSTANCE=dev\"", StringComparison.Ordinal).ShouldBe(instance == "dev");
		var suffix = instance.Length == 0 ? "" : "-" + instance;
		var serviceName = "gigtimed" + suffix + ".service";
		var socketName = "gigtimed" + suffix + ".socket";
		var root = Path.Combine(day.Root, "verify");
		var units = Path.Combine(root, "etc/systemd/system");
		Directory.CreateDirectory(units);

		foreach (var name in new[] { "basic", "sysinit", "shutdown", "sockets", "default" })
		{
			await File.WriteAllTextAsync(Path.Combine(units, name + ".target"),
				"[Unit]\nDescription=Isolated target\nDefaultDependencies=no\n",
				TestContext.Current.CancellationToken);
		}

		foreach (var (key, name) in new[] { ("service_unit", serviceName), ("socket_unit", socketName) })
			File.Copy(installed.GetProperty(key).GetString()!, Path.Combine(units, name));

		var mirror = Path.Combine(root, daemon.TrimStart(Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(mirror)!);
		File.Copy(daemon, mirror);

		// #5: --root prevents verification from consulting any real system/user unit directory or manager.
		var result = await CommandProcess.RunAsync("systemd-analyze",
			["verify", "--root", root, "--man=no", socketName, serviceName],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(15));

		result.ExitCode.ShouldBe(0, result.Output + result.Error);
		result.Error.ShouldBeEmpty();
	}
}
