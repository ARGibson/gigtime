namespace Gigtime.Domain.Tasks;

public sealed record TaskSubmission(DateTimeOffset SubmittedAt, long RoundedSeconds, string Rounding, decimal? Rate, string Currency);
