using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Gigtime.Cli.Tests;

public sealed class StreamingFailureTests
{
	private const string EventLine = "{\"sequence\":1,\"type\":\"task_voided\",\"stream\":\"abcdef\",\"version\":3,"
		+ "\"timestamp\":\"2026-03-02T09:00:00Z\",\"correlation_id\":\"test\",\"data\":{}}";

	[Fact]
	public async Task Non_export_timeout_covers_a_body_stalled_after_headers_and_one_byte()
	{
		var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var server = await FakeServer.StartAsync(async context =>
		{
			context.Response.ContentType = "application/json";
			await context.Response.StartAsync(context.RequestAborted);
			await context.Response.WriteAsync("{", context.RequestAborted);
			await context.Response.Body.FlushAsync(context.RequestAborted);
			sent.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
		});

		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(15));
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		var elapsed = Stopwatch.StartNew();

		(await app.RunAsync(["status", "--json"], deadline.Token)).ShouldBe(6);
		elapsed.Stop();
		sent.Task.IsCompleted.ShouldBeTrue();
		elapsed.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(8));
		elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(13));
		var failure = JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!;
		failure.Error!.Code.ShouldBe("unavailable");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Broken_export_connection_keeps_only_complete_lines_on_stdout(bool asJson)
	{
		var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var server = await FakeServer.StartAsync(async context =>
		{
			context.Response.ContentType = "application/x-ndjson";
			await context.Response.WriteAsync(EventLine + "\n", context.RequestAborted);
			await context.Response.Body.FlushAsync(context.RequestAborted);
			await written.Task.WaitAsync(context.RequestAborted);
			context.Abort();
		});

		using var output = new EventLineWriter(written);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(15));
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(asJson ? ["export", "--json"] : ["export"], deadline.Token)).ShouldBe(6);
		output.ToString().ShouldBe(EventLine + Environment.NewLine);
		error.ToString().ShouldContain("unavailable");
		error.ToString().ShouldNotContain("gig setup");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Truncated_last_export_line_is_not_written_and_diagnostic_stays_on_stderr(bool asJson)
	{
		await using var server = await FakeServer.StartAsync(EventLine + "\n{\"sequence\":2,");
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(asJson ? ["export", "--json"] : ["export"], TestContext.Current.CancellationToken)).ShouldBe(1);
		output.ToString().ShouldBe(EventLine + Environment.NewLine);
		JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.ExportEventDto)!.Sequence.ShouldBe(1);
		error.ToString().ShouldContain("internal");
		error.ToString().ShouldNotContain("gig setup");
	}

	[Fact]
	public async Task Export_line_timeout_after_output_is_a_stderr_only_failure()
	{
		var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		await using var server = await FakeServer.StartAsync(async context =>
		{
			context.Response.ContentType = "application/x-ndjson";
			await context.Response.WriteAsync(EventLine + "\n", context.RequestAborted);
			await context.Response.Body.FlushAsync(context.RequestAborted);
			await written.Task.WaitAsync(context.RequestAborted);
			await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
		});

		using var output = new EventLineWriter(written);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(15));
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);
		var elapsed = Stopwatch.StartNew();

		(await app.RunAsync(["export", "--json"], deadline.Token)).ShouldBe(6);
		elapsed.Stop();
		elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(13));
		output.ToString().ShouldBe(EventLine + Environment.NewLine);
		error.ToString().ShouldContain("unavailable");
		error.ToString().ShouldNotContain("gig setup");
	}

	[Fact]
	public async Task Malformed_export_before_output_keeps_the_error_envelope_behavior()
	{
		await using var server = await FakeServer.StartAsync("{");
		using var output = new StringWriter(CultureInfo.InvariantCulture);
		using var error = new StringWriter(CultureInfo.InvariantCulture);
		var paths = server.Paths;
		var app = new CliApplication(output, error, false, false, () => paths);

		(await app.RunAsync(["export", "--json"], TestContext.Current.CancellationToken)).ShouldBe(1);
		JsonSerializer.Deserialize(output.ToString(), GigtimeJsonContext.Default.AcknowledgementResponse)!.Error!.Code.ShouldBe("internal");
		error.ToString().ShouldBeEmpty();
	}
}
