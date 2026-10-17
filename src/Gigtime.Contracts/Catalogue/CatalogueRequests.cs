namespace Gigtime.Contracts.Catalogue;

public sealed record SetDefaultsRequest(string? Timezone = null, string? WeekStarts = null, string? Rounding = null);
public sealed record AddClientRequest(string Key, string Name, string Currency, string? Timezone = null, string? Rounding = null);
public sealed record ChangeClientRequest(string? Key = null, string? Name = null, string? Currency = null,
	string? Timezone = null, bool ClearTimezone = false, string? Rounding = null, bool ClearRounding = false);
public sealed record AddProjectRequest(string Key, string? Name = null, string? Rate = null, string? Rounding = null);
public sealed record ChangeProjectRequest(string? Key = null, string? Name = null, bool ClearName = false,
	string? Rate = null, bool ClearRate = false, string? Rounding = null, bool ClearRounding = false);
