// Fails when a tracked C# file has a line that starts with a space. .editorconfig asks for tabs,
// but dotnet format and the build accept space indentation that lands on a tab stop.
// Run from the repo root: dotnet run scripts/check-indentation.cs

// File-based apps default to Native AOT, which restores compiler packages; this script needs none.
#:property PublishAot=false

using System.Diagnostics;

var git = Process.Start(new ProcessStartInfo("git", "ls-files -z -- *.cs")
{
	RedirectStandardOutput = true,
})!;
var listing = await git.StandardOutput.ReadToEndAsync();
await git.WaitForExitAsync();
if (git.ExitCode != 0)
{
	Console.Error.WriteLine("git ls-files failed. Run from the repo root.");
	return 2;
}

// Wolverine writes its generated handlers with spaces.
var files = listing
	.Split('\0', StringSplitOptions.RemoveEmptyEntries)
	.Where(f => !f.Replace('\\', '/').Contains("/Internal/Generated/"));

var failures = 0;
foreach (var file in files)
{
	var lines = await File.ReadAllLinesAsync(file);
	for (var i = 0; i < lines.Length; i++)
	{
		if (!lines[i].StartsWith(' '))
			continue;

		Console.Error.WriteLine($"{file}:{i + 1}: indented with spaces; use tabs.");
		failures++;
	}
}

if (failures == 0)
	Console.WriteLine("ok   every tracked C# file indents with tabs.");
return failures == 0 ? 0 : 1;
