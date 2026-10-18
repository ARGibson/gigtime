using Gigtime.Platform;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests.Platform;

public sealed class GigtimeInstanceTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("a")]
	[InlineData("dev-123")]
	[InlineData("abcdefghijklmnopqrstuvwxyz012345")]
	public void Valid_names_resolve_without_creating_any_files(string? name)
	{
		var instance = GigtimeInstance.Resolve(name);
		instance.Name.ShouldBe(string.IsNullOrEmpty(name) ? null : name);
	}

	[Theory]
	[InlineData("Dev")]
	[InlineData("dév")]
	[InlineData("   ")]
	[InlineData("1dev")]
	[InlineData("-dev")]
	[InlineData("a_b")]
	[InlineData("dev/other")]
	[InlineData("dev ")]
	[InlineData("\n")]
	[InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
	public void Invalid_names_report_the_variable(string name)
	{
		Should.Throw<ArgumentException>(() => GigtimeInstance.Resolve(name)).Message.ShouldContain("GIGTIME_INSTANCE");
	}

	[Fact]
	public void Named_fallback_is_separate_private_and_inspection_does_not_create_it()
	{
		if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux runtime directory permissions.");

		var root = Directory.CreateTempSubdirectory("gig-instance-").FullName;

		try
		{
			var instance = GigtimeInstance.Resolve("dev");
			var paths = new LinuxPlatformPaths(null, null, null, root, root, false, instance);
			paths.RuntimeDirectory.ShouldBe(Path.Combine(root, "gigtime-" + Environment.UserName + "-dev"));
			Directory.GetFileSystemEntries(root).ShouldBeEmpty();
			var created = new LinuxPlatformPaths(null, null, null, root, root, true, instance);
			File.GetUnixFileMode(created.RuntimeDirectory).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			instance.SocketPath(created).ShouldBe(Path.Combine(created.RuntimeDirectory, "gigtime-dev", "gigtimed.sock"));
			Directory.Exists(Path.Combine(root, "gigtime-" + Environment.UserName)).ShouldBeFalse();
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}
