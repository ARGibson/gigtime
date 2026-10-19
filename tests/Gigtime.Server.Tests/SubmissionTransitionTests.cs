using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class SubmissionTransitionTests
{
	public static IEnumerable<object[]> Transitions()
	{
		foreach (var state in new[] { "running", "paused", "stopped", "submitted", "voided" })
		{
			foreach (var command in new[] { "submit", "unsubmit" })
				yield return [state, command];
		}
	}

	[Theory]
	[MemberData(nameof(Transitions))]
	public async Task Endpoint_transitions_and_refusal_hints_match_the_task_state_and_refusals_append_nothing(string state, string command)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		if (state == "paused") await app.PostAsync<TaskDto>("/v1/tasks/pause");
		if (state is "stopped" or "submitted" or "voided") await app.PostAsync<TaskDto>("/v1/tasks/stop");
		if (state == "submitted") await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit");
		if (state == "voided") await app.PostAsync<TaskDto>($"/v1/tasks/{id}/void", new VoidTaskRequest());
		var before = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		var beforeText = await before.ReadAsTextAsync();
		var valid = state == "stopped" && command == "submit" || state == "submitted" && command == "unsubmit";
		var response = await app.PostAsync<TaskDto>($"/v1/tasks/{id}/{command}", status: valid ? 200 : 409);

		if (valid)
		{
			response.Data!.State.ShouldBe(command == "submit" ? "submitted" : "stopped");

			return;
		}

		response.Error!.Code.ShouldBe("invalid_transition");
		response.Error.State!.State.ShouldBe(state);
		if (state == "submitted") response.Error.Hint.ShouldBe("gig unsubmit " + id);
		if (command == "submit" && state is "running" or "paused") response.Error.Hint.ShouldBe("gig stop --submit");
		var after = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		(await after.ReadAsTextAsync()).ShouldBe(beforeText);
	}

	[Theory]
	[InlineData("start")]
	[InlineData("add")]
	[InlineData("amend")]
	public async Task Malformed_own_rates_are_validation_refusals_without_new_events(string command)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		await app.PostAsync<TaskDto>("/v1/tasks/stop");
		var before = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		var beforeText = await before.ReadAsTextAsync();

		var response = command switch
		{
			"start" => await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Rate: "bad"), 422),
			"add" => await app.PostAsync<TaskDto>("/v1/tasks/add",
				new RecordTaskInput("example",
					"project",
					"2026-03-02T09:00:00Z",
					"2026-03-02T09:30:00Z",
					Rate: "bad"),
				422),
			_ => await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(Rate: "bad"), 422),
		};

		response.Error!.Code.ShouldBe("validation");
		var after = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		(await after.ReadAsTextAsync()).ShouldBe(beforeText);
	}
}
