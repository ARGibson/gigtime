using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class UnitSyntaxTests(PublishedFixture published)
{
	[Fact]
	public async Task Generated_units_pass_systemd_validation_entirely_under_a_temporary_root()
	{
		await using var day = new DaySession(published);
		day.Environment["XDG_RUNTIME_DIR"] = Path.Combine(day.Root, "runtime space %");
		Directory.CreateDirectory(day.Environment["XDG_RUNTIME_DIR"]);
		var directory = Path.Combine(day.Root, "daemon space %");
		Directory.CreateDirectory(directory);
		var daemon = Path.Combine(directory, "gigtimed");
		File.Copy(Path.Combine(published.Application, "gigtimed"), daemon);
		var installed = await day.SuccessAsync("setup", "--gigtimed", daemon);
		var root = Path.Combine(day.Root, "verify");
		var units = Path.Combine(root, "etc/systemd/system");
		Directory.CreateDirectory(units);

		foreach (var name in new[] { "basic", "sysinit", "shutdown", "sockets", "default" })
		{
			await File.WriteAllTextAsync(Path.Combine(units, name + ".target"),
				"[Unit]\nDescription=Isolated target\nDefaultDependencies=no\n",
				TestContext.Current.CancellationToken);
		}

		foreach (var (key, name) in new[] { ("service_unit", "gigtimed.service"), ("socket_unit", "gigtimed.socket") })
			File.Copy(installed.GetProperty(key).GetString()!, Path.Combine(units, name));

		var mirror = Path.Combine(root, daemon.TrimStart(Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(mirror)!);
		File.Copy(daemon, mirror);

		// #5: --root prevents verification from consulting any real system/user unit directory or manager.
		var result = await CommandProcess.RunAsync("systemd-analyze",
			["verify", "--root", root, "--man=no", "gigtimed.socket", "gigtimed.service"],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(15));

		result.ExitCode.ShouldBe(0, result.Output + result.Error);
		result.Error.ShouldBeEmpty();
	}
}
