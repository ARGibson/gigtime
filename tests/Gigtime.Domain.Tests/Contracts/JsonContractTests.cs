using System.Globalization;
using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Contracts;

public sealed class JsonContractTests
{
  [Fact]
  public void Success_uses_snake_case_integer_seconds_utc_instants_and_decimal_strings()
  {
	var client = new EntityDto(Guid.CreateVersion7(), "acme");
	var project = new EntityDto(Guid.CreateVersion7(), "site-audit");
	var start = DateTimeOffset.Parse("2026-03-02T10:00:00+01:00", CultureInfo.InvariantCulture);
	var task = new TaskDto("7f3a9c", client, project, null, "stopped", [new(1, start, start.AddSeconds(30))],
		30, 60, DurationText.Format(60), new("nearest-minute", MoneyDto.From(123.45m, "USD"), "USD"), start, start.AddSeconds(30));
	var response = Envelopes.Success("stop", task);
	var json = JsonSerializer.Serialize(response, GigtimeJsonContext.Default.TaskResponse);
	using var document = JsonDocument.Parse(json);
	var root = document.RootElement;
	root.GetProperty("ok").GetBoolean().ShouldBeTrue();
	root.GetProperty("command").GetString().ShouldBe("stop");
	root.TryGetProperty("error", out _).ShouldBeFalse();
	var data = root.GetProperty("data");
	data.GetProperty("raw_seconds").GetInt64().ShouldBe(30);
	data.GetProperty("rounded_seconds").GetInt64().ShouldBe(60);
	data.GetProperty("display").GetString().ShouldBe("00:01:00");
	data.GetProperty("started_at").GetString().ShouldBe("2026-03-02T09:00:00Z");
	data.GetProperty("client").GetProperty("key").GetString().ShouldBe("acme");
	data.GetProperty("project").GetProperty("id").GetGuid().ShouldBe(project.Id);
	data.GetProperty("snapshot").GetProperty("rate").GetProperty("amount").GetString().ShouldBe("123.45");
	var read = JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.TaskResponse)!;
	read.Data!.StartedAt.Offset.ShouldBe(TimeSpan.Zero);
	read.Data.Intervals.Single().Start.ShouldBe(start);
	read.Data.Snapshot!.Rate!.Currency.ShouldBe("USD");
  }

  [Fact]
  public void Failure_omits_data_and_keeps_explicit_null_state_and_hint()
  {
	var response = Envelopes.Failure<TaskDto>("pause", new(ErrorCodes.NotFound, "No open task."));
	var json = JsonSerializer.Serialize(response, GigtimeJsonContext.Default.TaskResponse);
	using var document = JsonDocument.Parse(json);
	var root = document.RootElement;
	root.GetProperty("ok").GetBoolean().ShouldBeFalse();
	root.TryGetProperty("data", out _).ShouldBeFalse();
	var error = root.GetProperty("error");
	error.GetProperty("code").GetString().ShouldBe("not_found");
	error.GetProperty("state").ValueKind.ShouldBe(JsonValueKind.Null);
	error.GetProperty("hint").ValueKind.ShouldBe(JsonValueKind.Null);
  }

  [Fact]
  public void Failures_carry_the_state_and_next_command_without_untyped_objects()
  {
	var error = new ErrorDto(ErrorCodes.InvalidTransition, "Task is paused.", new("7f3a9c", "paused", "pause"), "gig resume");
	var response = Envelopes.Failure<StatusDto>("pause", error);
	var json = JsonSerializer.Serialize(response, GigtimeJsonContext.Default.StatusResponse);
	var read = JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.StatusResponse)!;
	read.Error.ShouldBe(error);
  }

  [Theory]
  [InlineData("internal", 1)]
  [InlineData("usage", 2)]
  [InlineData("invalid_transition", 3)]
  [InlineData("not_found", 4)]
  [InlineData("config", 5)]
  [InlineData("unavailable", 6)]
  [InlineData("conflict", 7)]
  [InlineData("validation", 8)]
  public void Error_codes_map_to_the_public_exit_statuses(string code, int exit) => ExitCodes.FromErrorCode(code).ShouldBe(exit);

  [Fact]
  public void All_request_and_response_roots_have_generated_metadata()
  {
	Type[] types = [typeof(SetDefaultsRequest), typeof(AddClientRequest), typeof(ChangeClientRequest), typeof(AddProjectRequest),
			typeof(ChangeProjectRequest), typeof(StartTaskRequest), typeof(RecordTaskRequest), typeof(AmendTaskRequest), typeof(VoidTaskRequest),
			typeof(Envelope<TaskDto>), typeof(Envelope<TasksDto>), typeof(Envelope<StatusDto>), typeof(Envelope<DefaultsDto>),
			typeof(Envelope<ClientDto>), typeof(Envelope<ClientsDto>), typeof(Envelope<ProjectDto>), typeof(Envelope<ProjectsDto>),
			typeof(Envelope<ReportDto>), typeof(Envelope<AcknowledgementDto>), typeof(Envelope<HealthDto>)];
	foreach (var type in types) GigtimeJsonContext.Default.GetTypeInfo(type).ShouldNotBeNull();
  }

  [Fact]
  public void Clearing_flags_and_offset_input_round_trip_through_the_generated_context()
  {
	var request = new ChangeClientRequest(ClearTimezone: true, ClearRounding: true);
	var json = JsonSerializer.Serialize(request, GigtimeJsonContext.Default.ChangeClientRequest);
	json.ShouldContain("\"clear_timezone\":true");
	JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.ChangeClientRequest).ShouldBe(request);
	var backfill = JsonSerializer.Deserialize("""
			{"client":"acme","project":"site-audit","start":"2026-03-02T10:00:00+01:00","end":"2026-03-02T10:01:00+01:00"}
			""", GigtimeJsonContext.Default.RecordTaskRequest)!;
	backfill.Start.Offset.ShouldBe(TimeSpan.Zero);
	backfill.Start.Hour.ShouldBe(9);
  }

  [Theory]
  [InlineData("2026-03-02T09:00:00Z")]
  [InlineData("2026-03-02T10:00:00+01:00")]
  [InlineData("2026-03-02T04:00:00-05:00")]
  public void Wire_instants_with_explicit_offsets_are_normalized_to_utc(string instant)
  {
	var json = "{\"client\":\"acme\",\"project\":\"site-audit\",\"start\":\"" + instant + "\",\"end\":\"" + instant + "\"}";
	var request = JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.RecordTaskRequest)!;
	request.Start.Offset.ShouldBe(TimeSpan.Zero);
	request.Start.ShouldBe(DateTimeOffset.Parse("2026-03-02T09:00:00Z", CultureInfo.InvariantCulture));
  }

  [Theory]
  [InlineData("2026-03-02T09:00:00")]
  [InlineData("2026-03-02T09:00:00.123")]
  [InlineData("2026-03-02")]
  public void Wire_instants_without_an_offset_are_rejected(string instant)
  {
	var json = "{\"client\":\"acme\",\"project\":\"site-audit\",\"start\":\"" + instant + "\",\"end\":\"2026-03-02T10:00:00Z\"}";
	Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.RecordTaskRequest));
  }

  [Theory]
  [InlineData("start")]
  [InlineData("end")]
  public void Nullable_amendment_instants_reject_offset_free_input(string field)
  {
	var json = "{\"interval\":1,\"" + field + "\":\"2026-03-02T09:00:00\"}";
	Should.Throw<JsonException>(() => JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.AmendTaskRequest));
  }

  [Theory]
  [InlineData("2026-03-02T09:00:00Z", "2026-03-02T09:01:00Z")]
  [InlineData("2026-03-02T10:00:00+01:00", "2026-03-02T04:01:00-05:00")]
  public void Nullable_amendment_instants_accept_explicit_offsets(string start, string end)
  {
	var json = "{\"interval\":1,\"start\":\"" + start + "\",\"end\":\"" + end + "\"}";
	var request = JsonSerializer.Deserialize(json, GigtimeJsonContext.Default.AmendTaskRequest)!;
	request.Start.ShouldBe(DateTimeOffset.Parse("2026-03-02T09:00:00Z", CultureInfo.InvariantCulture));
	request.End.ShouldBe(DateTimeOffset.Parse("2026-03-02T09:01:00Z", CultureInfo.InvariantCulture));
	request.Start!.Value.Offset.ShouldBe(TimeSpan.Zero);
	request.End!.Value.Offset.ShouldBe(TimeSpan.Zero);
  }

  [Fact]
  public void Duration_display_keeps_total_hours_instead_of_wrapping_at_a_day()
  {
	DurationText.Format(90061).ShouldBe("25:01:01");
	MoneyDto.From(12.34m, "USD").Amount.ShouldBe("12.34");
  }
}
