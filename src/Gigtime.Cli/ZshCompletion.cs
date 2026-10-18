namespace Gigtime.Cli;

internal static class ZshCompletion
{
	internal static void Write(TextWriter output)
	{
		using var resource = typeof(ZshCompletion).Assembly.GetManifestResourceStream("gig._gig")!;
		using var reader = new StreamReader(resource);
		output.Write(reader.ReadToEnd());
	}
}
