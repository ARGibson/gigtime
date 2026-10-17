// Fails when a tracked C# file indents with spaces: a line that starts with a space, or tabs
// followed by spaces before code. .editorconfig asks for tabs, but dotnet format and the build
// accept space indentation that lands on a tab stop.
// Run anywhere in the repo: dotnet run scripts/check-indentation.cs

// File-based apps default to Native AOT, which restores compiler packages; this script needs none.
#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

string root;
string listing;
try
{
	root = (await GitAsync(Environment.CurrentDirectory, "rev-parse --show-toplevel")).Trim();
	listing = await GitAsync(root, "ls-files -z -- *.cs");
}
catch (Exception e) when (e is Win32Exception or InvalidOperationException)
{
	Console.Error.WriteLine($"git failed: {e.Message}");
	return 2;
}

// Wolverine writes its generated handlers with spaces.
var files = listing
	.Split('\0', StringSplitOptions.RemoveEmptyEntries)
	.Where(f => !f.Replace('\\', '/').Contains("/Internal/Generated/"));

var spaceIndent = new Regex(@"^(?: |\t+ +\S)");
var failures = 0;
foreach (var file in files)
{
	var lines = await File.ReadAllLinesAsync(Path.Combine(root, file));
	for (var i = 0; i < lines.Length; i++)
	{
		if (!spaceIndent.IsMatch(lines[i])) continue;

		Console.Error.WriteLine($"{file}:{i + 1}: indented with spaces; use tabs.");
		failures++;
	}
}

if (failures == 0) Console.WriteLine("ok   every tracked C# file indents with tabs.");

return failures == 0 ? 0 : 1;

static async Task<string> GitAsync(string directory, string arguments)
{
	var git = Process.Start(new ProcessStartInfo("git", arguments)
	{
		WorkingDirectory = directory,
		RedirectStandardOutput = true,
	})!;
	var output = await git.StandardOutput.ReadToEndAsync();
	await git.WaitForExitAsync();
	if (git.ExitCode != 0) throw new InvalidOperationException($"git {arguments} exited {git.ExitCode}");

	return output;
}
