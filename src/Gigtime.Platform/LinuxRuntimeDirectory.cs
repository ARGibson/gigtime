using System.Runtime.InteropServices;

namespace Gigtime.Platform;

internal static partial class LinuxRuntimeDirectory
{
	private const uint RequiredFields = 0x0b;
	private const int NoFollow = 0x100;
	private const int CurrentDirectory = -100;

	internal static string Resolve(string? configured, string fallback, bool create = true)
	{
		if (configured is not null && Path.IsPathFullyQualified(configured)) return configured;

		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux runtime paths require Linux.");

		if (Statx(CurrentDirectory, fallback, NoFollow, RequiredFields, out var status) != 0)
		{
			if (Marshal.GetLastPInvokeError() != 2) throw Invalid(fallback);

			if (!create) return fallback;

			Directory.CreateDirectory(fallback, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		// #2: never trust or chmod a predictable fallback planted by another user.
		if (Statx(CurrentDirectory, fallback, NoFollow, RequiredFields, out status) != 0
				|| (status.Mask & RequiredFields) != RequiredFields || status.UserId != GetEffectiveUserId()
				|| (status.Mode & 0xf000) != 0x4000 || (status.Mode & 0x0fff) != 0x01c0)
		{
			throw Invalid(fallback);
		}

		return fallback;
	}

	private static IOException Invalid(string path)
	{
		return new($"Runtime fallback '{path}' must be a directory owned by the current user with mode 0700.");
	}

	[LibraryImport("libc", EntryPoint = "geteuid")]
	private static partial uint GetEffectiveUserId();

	[LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
	private static partial int Statx(int directory, string path, int flags, uint mask, out FileStatus status);

	[StructLayout(LayoutKind.Explicit, Size = 256)]
	private struct FileStatus
	{
		[FieldOffset(0)] public uint Mask;
		[FieldOffset(20)] public uint UserId;
		[FieldOffset(28)] public ushort Mode;
	}
}
