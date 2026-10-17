using System.Reflection;
using System.Runtime.CompilerServices;
using JasperFx;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using Wolverine.Http.CodeGen;

namespace Gigtime.Server.Hosting;

public sealed class ClockParameters : IParameterStrategy
{
	private readonly ConditionalWeakTable<HttpChain, Variable> clocks = new();

	public bool TryMatch(HttpChain chain, IServiceContainer container, ParameterInfo parameter, out Variable? variable)
	{
		variable = null;

		if (parameter.ParameterType != typeof(DateTimeOffset) || parameter.Name != "now") return false;

		variable = clocks.GetValue(chain, _ => new MethodCall(typeof(TimeProvider), nameof(TimeProvider.GetUtcNow)).ReturnVariable!);

		return true;
	}
}
