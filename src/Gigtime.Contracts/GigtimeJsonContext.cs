using System.Text.Json.Serialization;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;

namespace Gigtime.Contracts;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	Converters = [typeof(UtcInstantConverter)])]
[JsonSerializable(typeof(Envelope<TaskDto>), TypeInfoPropertyName = "TaskResponse")]
[JsonSerializable(typeof(Envelope<TasksDto>), TypeInfoPropertyName = "TasksResponse")]
[JsonSerializable(typeof(Envelope<StatusDto>), TypeInfoPropertyName = "StatusResponse")]
[JsonSerializable(typeof(Envelope<DefaultsDto>), TypeInfoPropertyName = "DefaultsResponse")]
[JsonSerializable(typeof(Envelope<ClientDto>), TypeInfoPropertyName = "ClientResponse")]
[JsonSerializable(typeof(Envelope<ClientsDto>), TypeInfoPropertyName = "ClientsResponse")]
[JsonSerializable(typeof(Envelope<ProjectDto>), TypeInfoPropertyName = "ProjectResponse")]
[JsonSerializable(typeof(Envelope<ProjectsDto>), TypeInfoPropertyName = "ProjectsResponse")]
[JsonSerializable(typeof(Envelope<ReportDto>), TypeInfoPropertyName = "ReportResponse")]
[JsonSerializable(typeof(Envelope<AcknowledgementDto>), TypeInfoPropertyName = "AcknowledgementResponse")]
[JsonSerializable(typeof(Envelope<HealthDto>), TypeInfoPropertyName = "HealthResponse")]
[JsonSerializable(typeof(ExportEventDto))]
[JsonSerializable(typeof(SetDefaultsRequest))]
[JsonSerializable(typeof(AddClientRequest))]
[JsonSerializable(typeof(ChangeClientRequest))]
[JsonSerializable(typeof(AddProjectRequest))]
[JsonSerializable(typeof(ChangeProjectRequest))]
[JsonSerializable(typeof(StartTaskRequest))]
[JsonSerializable(typeof(RecordTaskInput))]
[JsonSerializable(typeof(AmendTaskInput))]
[JsonSerializable(typeof(VoidTaskRequest))]
public partial class GigtimeJsonContext : JsonSerializerContext;
