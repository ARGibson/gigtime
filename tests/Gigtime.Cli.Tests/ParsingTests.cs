using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using Gigtime.Contracts;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class ParsingTests
{
	public static IEnumerable<object[]> InvalidArguments => new string[][]
	{
		[], ["unknown"], ["defaults"], ["client", "add", "example"], ["project", "add", "example"], ["start", "example"], ["pause", "extra"],
		["resume", "--nope"], ["stop", "--ref", "x"], ["status", "--all"], ["show"], ["log", "--last", "bad"], ["log", "--last", "0"],
		["log", "--last", "-1"], ["log", "--last", "2147483648"], ["log", "--all", "bad"], ["defaults", "set", "--timezone"],
		["client", "set", "a", "--timezone", "UTC", "--clear-timezone"], ["client", "set", "a", "--rounding", "none", "--clear-rounding"],
		["project", "set", "a", "b", "--rate", "2", "--clear-rate"], ["project", "set", "a", "b", "--rounding", "none", "--clear-rounding"],
	}.SelectMany(args => new[] { new object[] { args, false }, [args, true] });

	[Theory]
	[MemberData(nameof(InvalidArguments))]
	public async Task Argument_errors_never_resolve_paths_or_contact_the_service(string[] args, bool asJson)
	{
		await using var server = await FakeServer.StartAsync(CommandCases.Response("status"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var resolved = false;

		var app = new CliApplication(output,
			error,
			false,
			false,
			() =>
			{
				resolved = true;

				return paths;
			});

		if (asJson) args = [.. args, "--json"];

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(2);
		resolved.ShouldBeFalse();
		server.Requests.ShouldBeEmpty();

		if (asJson)
		{
			var envelope = JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!;
			envelope.Ok.ShouldBeFalse();
			envelope.Error!.Code.ShouldBe("usage");
		}
		else
		{
			output.ToString().ShouldBeEmpty();
			error.ToString().ShouldContain("usage");
		}
	}

	[Theory]
	[InlineData("--help")]
	[InlineData("client", "--help")]
	[InlineData("project", "set", "--help")]
	public async Task Help_does_not_resolve_paths(params string[] args)
	{
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var app = new CliApplication(output, error, false, false, () => throw new InvalidOperationException("Help resolved paths."));

		(await app.RunAsync(args, TestContext.Current.CancellationToken)).ShouldBe(0);
		output.ToString().ShouldContain("Usage:");
		error.ToString().ShouldBeEmpty();
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public async Task Missing_and_refused_sockets_are_unavailable_with_setup_hint(bool refused, bool asJson)
	{
		var directory = Path.Combine(Path.GetTempPath(), "gig-cli-missing-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(directory, "gigtime"));
		using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

		try
		{
			if (refused) socket.Bind(new UnixDomainSocketEndPoint(Path.Combine(directory, "gigtime", "gigtimed.sock")));
			using var output = new StringWriter(CultureInfo.InvariantCulture);
			using var error = new StringWriter(CultureInfo.InvariantCulture);
			var app = new CliApplication(output, error, false, false, () => new FakePaths(directory, directory, directory));

			(await app.RunAsync(asJson ? ["status", "--json"] : ["status"], TestContext.Current.CancellationToken)).ShouldBe(6);

			if (asJson)
			{
				var envelope = JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!;
				envelope.Command.ShouldBe("status");
				envelope.Error!.Code.ShouldBe("unavailable");
				envelope.Error.Message.ShouldContain(Path.Combine(directory, "gigtime", "gigtimed.sock"));
				envelope.Error.Hint!.ShouldContain("gig setup");
			}
			else
			{
				output.ToString().ShouldBeEmpty();
				error.ToString().ShouldContain("unavailable");
				error.ToString().ShouldContain(Path.Combine(directory, "gigtime", "gigtimed.sock"));
				error.ToString().ShouldContain("gig setup");
			}
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Runtime_directory_refusal_has_directory_repair_guidance_without_setup_hint(bool asJson)
	{
		var directory = Path.Combine(Path.GetTempPath(), "gig-cli-invalid-" + Guid.NewGuid().ToString("N"));
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);

		var app = new CliApplication(output,
			error,
			false,
			false,
			() => throw new IOException($"Runtime fallback '{directory}' must be owned by the current user with mode 0700."));

		(await app.RunAsync(asJson ? ["status", "--json"] : ["status"], TestContext.Current.CancellationToken)).ShouldBe(6);

		if (asJson)
		{
			var envelope = JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!;
			envelope.Error!.Code.ShouldBe("unavailable");
			envelope.Error.Message.ShouldContain(directory);
			envelope.Error.Hint!.ShouldContain("ownership");
			envelope.Error.Hint!.ShouldContain("0700");
			output.ToString().ShouldNotContain("gig setup");
		}
		else
		{
			output.ToString().ShouldBeEmpty();
			error.ToString().ShouldContain(directory);
			error.ToString().ShouldContain("ownership");
			error.ToString().ShouldContain("0700");
			error.ToString().ShouldNotContain("gig setup");
		}
	}
}
