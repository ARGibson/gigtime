using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public sealed record CorrectionIntent(IReadOnlyList<TaskCommand> Commands, ErrorDto? Error = null);
