using System.Globalization;

namespace Gigtime.Cli.Tests;

internal sealed class EventLineWriter(TaskCompletionSource written) : StringWriter(CultureInfo.InvariantCulture)
{
	public override void WriteLine(string? value)
	{
		base.WriteLine(value);
		written.TrySetResult();
	}
}
