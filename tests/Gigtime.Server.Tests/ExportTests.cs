using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class ExportTests
{
	[Fact]
	public async Task Voided_task_is_excluded_from_report_but_export_retains_every_event_in_order_across_pages()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = "";

		for (var i = 0; i < 105; i++)
			id = (await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("example", "project", "08:00", "08:01"))).Data!.Id;

		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/void", new VoidTaskRequest("mistake"));
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(104 * 60);
		(await app.GetAsync<TasksDto>("/v1/tasks?all=true&last=200")).Data!.Tasks.Single(t => t.Id == id).State.ShouldBe("voided");
		(await app.GetAsync<TasksDto>("/v1/tasks?last=200")).Data!.Tasks.ShouldNotContain(t => t.Id == id);
		var response = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		var text = await response.ReadAsTextAsync();
		var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		lines.Length.ShouldBeGreaterThan(100);
		var exported = lines.Select(line => JsonSerializer.Deserialize(line, GigtimeJsonContext.Default.ExportEventDto)!).ToArray();
		exported.Select(e => e.Sequence).ShouldBe(exported.Select(e => e.Sequence).Order());
		exported.Select(e => e.Sequence).Distinct().Count().ShouldBe(exported.Length);
		exported.Any(e => e.Stream == id && e.Data.TryGetProperty("reason", out var reason) && reason.GetString() == "mistake").ShouldBeTrue();

		foreach (var item in exported)
		{
			item.Type.ShouldNotBeNullOrWhiteSpace();
			item.Stream.ShouldNotBeNullOrWhiteSpace();
			item.Version.ShouldBeGreaterThan(0);
			item.CorrelationId.ShouldNotBeNullOrWhiteSpace();
			item.Data.ValueKind.ShouldBe(JsonValueKind.Object);
		}

		var stamp = Uri.EscapeDataString(exported[^1].Timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
		var filtered = await app.Host.Scenario(s => s.Get.Url("/v1/export?from=" + stamp + "&to=" + stamp));
		(await filtered.ReadAsTextAsync()).ShouldContain("mistake");

		var future = Uri.EscapeDataString(exported[^1]
			.Timestamp.AddSeconds(1)
			.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

		var empty = await app.Host.Scenario(s => s.Get.Url("/v1/export?from=" + future));
		(await empty.ReadAsTextAsync()).ShouldBeEmpty();
		(await app.GetAsync<AcknowledgementDto>("/v1/export?from=2030-01-02&to=2030-01-01", 422)).Error!.Code.ShouldBe("validation");

		(await app.GetAsync<AcknowledgementDto>("/v1/export?from=bad", 422)).Error!.Code.ShouldBe("validation");
	}
}
