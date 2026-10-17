namespace Gigtime.Server.Tasks;

public sealed record LoadedCorrection(LoadedTaskCommand Loaded, EnvelopeResult? Refusal);
