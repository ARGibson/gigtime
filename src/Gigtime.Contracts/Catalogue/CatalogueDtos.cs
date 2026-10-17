namespace Gigtime.Contracts.Catalogue;

public sealed record DefaultsDto(string Timezone, string WeekStarts, string Rounding);

public sealed record ClientDto(Guid Id, string Key, string Name, string Currency, string Timezone, string Rounding, bool Archived);

public sealed record ProjectDto(Guid Id, string Key, EntityDto Client, string? Name, MoneyDto? Rate, string Rounding, bool Archived);

public sealed record ClientsDto(IReadOnlyList<ClientDto> Clients);

public sealed record ProjectsDto(IReadOnlyList<ProjectDto> Projects);
