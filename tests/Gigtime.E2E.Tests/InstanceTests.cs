using System.Text.Json;
using Shouldly;
using Xunit;

namespace Gigtime.E2E.Tests;

[Collection("published binaries")]
public sealed class InstanceTests(PublishedFixture published)
{
	[Theory]
	[InlineData("", "dev")]
	[InlineData("dev", "other")]
	public async Task Instances_share_XDG_roots_but_isolate_databases_sockets_and_tasks(string first, string second)
	{
		await using var one = new DaySession(published);
		await using var two = new DaySession(published);

		foreach (var key in new[] { "XDG_DATA_HOME", "XDG_RUNTIME_DIR", "XDG_CONFIG_HOME" })
			two.Environment[key] = one.Environment[key];

		one.Environment["GIGTIME_INSTANCE"] = first;
		two.Environment["GIGTIME_INSTANCE"] = second;
		await one.StartAsync();
		await two.StartAsync();
		await one.CatalogueAsync();
		await two.CatalogueAsync();
		var task = await one.SuccessAsync("start", "acme", "work");
		(await two.SuccessAsync("status")).GetProperty("task").ValueKind.ShouldBe(JsonValueKind.Null);
		(await two.SuccessAsync("log")).GetProperty("tasks").GetArrayLength().ShouldBe(0);
		var otherTask = await two.SuccessAsync("start", "acme", "work");
		otherTask.GetProperty("id").GetString().ShouldNotBe(task.GetProperty("id").GetString());
		(await one.SuccessAsync("status")).GetProperty("task").GetProperty("id").GetString().ShouldBe(task.GetProperty("id").GetString());
		var firstSetup = await one.SuccessAsync("setup", "--check");
		var secondSetup = await two.SuccessAsync("setup", "--check");
		firstSetup.GetProperty("socket_path").GetString().ShouldNotBe(secondSetup.GetProperty("socket_path").GetString());
		firstSetup.GetProperty("database_path").GetString().ShouldNotBe(secondSetup.GetProperty("database_path").GetString());

		foreach (var (day, name) in new[] { (one, first), (two, second) })
		{
			var instance = string.IsNullOrEmpty(name) ? null : name;
			(await day.SuccessAsync("status")).GetProperty("instance").GetString().ShouldBe(instance);
			var setup = await day.SuccessAsync("setup", "--check");
			setup.GetProperty("instance").GetString().ShouldBe(instance);
			File.Exists(setup.GetProperty("socket_path").GetString()).ShouldBeTrue();
			File.Exists(setup.GetProperty("database_path").GetString()).ShouldBeTrue();

			foreach (var args in new[] { new[] { "status" }, new[] { "setup", "--check" } })
			{
				var human = await day.HumanAsync(args);
				human.Output.Split('\n').Count(line => line.StartsWith("Instance: ", StringComparison.Ordinal)).ShouldBe(instance is null ? 0 : 1);
				if (instance is not null) human.Output.ShouldContain("Instance: " + instance);
			}
		}

		(await one.SuccessAsync("stop")).GetProperty("id").GetString().ShouldBe(task.GetProperty("id").GetString());
		(await two.SuccessAsync("status")).GetProperty("task").GetProperty("id").GetString().ShouldBe(otherTask.GetProperty("id").GetString());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Named_setup_only_installs_selected_units_and_is_idempotent(bool fallback)
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_INSTANCE"] = "dev";
		day.Environment["GIGTIME_TEST_NO_ACTIVATION"] = fallback ? "1" : "0";
		var setup = await day.SuccessAsync("setup");
		setup.GetProperty("instance").GetString().ShouldBe("dev");
		setup.GetProperty("socket_activation").GetBoolean().ShouldBe(!fallback);
		var service = setup.GetProperty("service_unit").GetString()!;
		Path.GetFileName(service).ShouldBe("gigtimed-dev.service");
		Path.GetFileName(setup.GetProperty("socket_unit").GetString()).ShouldBe("gigtimed-dev.socket");
		var body = await File.ReadAllTextAsync(service, TestContext.Current.CancellationToken);
		body.ShouldContain("Environment=\"GIGTIME_INSTANCE=dev\"");
		body.ShouldContain("WorkingDirectory=" + published.Application);
		var stamp = File.GetLastWriteTimeUtc(service);
		(await day.SuccessAsync("setup")).GetProperty("changed").GetBoolean().ShouldBeFalse();
		(await File.ReadAllTextAsync(service, TestContext.Current.CancellationToken)).ShouldBe(body);
		File.GetLastWriteTimeUtc(service).ShouldBe(stamp);
		var units = Path.GetDirectoryName(service)!;
		File.Exists(Path.Combine(units, "gigtimed.service")).ShouldBeFalse();
		File.Exists(Path.Combine(units, "gigtimed.socket")).ShouldBeFalse();
		var calls = await File.ReadAllLinesAsync(day.Environment["GIGTIME_TEST_SYSTEMCTL_LOG"], TestContext.Current.CancellationToken);

		calls.ShouldAllBe(line => !line.EndsWith(" gigtimed.service", StringComparison.Ordinal)
			&& !line.EndsWith(" gigtimed.socket", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("Dev")]
	[InlineData("../bad")]
	[InlineData("dev ")]
	[InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
	public async Task Invalid_instance_is_config_error_before_either_executable_creates_files(string instance)
	{
		await using var day = new DaySession(published);
		day.Environment["GIGTIME_INSTANCE"] = instance;
		day.Environment["XDG_RUNTIME_DIR"] = "relative";
		var before = Directory.GetFileSystemEntries(day.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

		foreach (var args in new[] { new[] { "status" }, new[] { "setup" }, new[] { "__complete", "clients" } })
		{
			var result = await day.GigAsync([.. args, "--json"]);
			result.ExitCode.ShouldBe(5);
			using var json = JsonDocument.Parse(result.Output);
			json.RootElement.GetProperty("error").GetProperty("code").GetString().ShouldBe("config");
			json.RootElement.GetProperty("error").GetProperty("message").GetString()!.ShouldContain("GIGTIME_INSTANCE");
		}

		var daemon = await CommandProcess.RunAsync(Path.Combine(published.Application, "gigtimed"),
			[],
			day.Root,
			day.Environment,
			TimeSpan.FromSeconds(10));

		daemon.ExitCode.ShouldBe(5);
		daemon.Error.ShouldContain("config:");
		daemon.Error.ShouldContain("GIGTIME_INSTANCE");
		Directory.GetFileSystemEntries(day.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ShouldBe(before);
	}
}
