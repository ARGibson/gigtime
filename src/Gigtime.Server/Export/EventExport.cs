namespace Gigtime.Server.Export;

public sealed record EventExport(IAsyncEnumerable<ExportEventDto> Events);
