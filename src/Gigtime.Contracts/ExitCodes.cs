namespace Gigtime.Contracts;

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

	public static int FromErrorCode(string code)
	{
		return code switch
		{
			ErrorCodes.Internal => Internal,
			ErrorCodes.Usage => Usage,
			ErrorCodes.InvalidTransition => InvalidTransition,
			ErrorCodes.NotFound => NotFound,
			ErrorCodes.Config => Config,
			ErrorCodes.Unavailable => Unavailable,
			ErrorCodes.Conflict => Conflict,
			ErrorCodes.Validation => Validation,
			_ => Internal,
		};
	}
}
