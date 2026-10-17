namespace Gigtime.Contracts;

public static class ErrorCodes
{
	public const string Internal = "internal";
	public const string Usage = "usage";
	public const string InvalidTransition = "invalid_transition";
	public const string NotFound = "not_found";
	public const string Config = "config";
	public const string Unavailable = "unavailable";
	public const string Conflict = "conflict";
	public const string Validation = "validation";
}

public static class ExitCodes
{
	public const int Success = 0;
	public const int Internal = 1;
	public const int Usage = 2;
	public const int InvalidTransition = 3;
	public const int NotFound = 4;
	public const int Config = 5;
	public const int Unavailable = 6;
	public const int Conflict = 7;
	public const int Validation = 8;

	public static int FromErrorCode(string code) => code switch
	{
		ErrorCodes.Internal => Internal,
		ErrorCodes.Usage => Usage,
		ErrorCodes.InvalidTransition => InvalidTransition,
		ErrorCodes.NotFound => NotFound,
		ErrorCodes.Config => Config,
		ErrorCodes.Unavailable => Unavailable,
		ErrorCodes.Conflict => Conflict,
		ErrorCodes.Validation => Validation,
		_ => Internal
	};
}
