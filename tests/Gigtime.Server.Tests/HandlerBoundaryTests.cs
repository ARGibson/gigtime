using System.Reflection;
using System.Reflection.Emit;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class HandlerBoundaryTests
{
	private static readonly Dictionary<short, OpCode> operations = typeof(OpCodes)
		.GetFields(BindingFlags.Public | BindingFlags.Static)
		.Select(f => (OpCode)f.GetValue(null)!)
		.ToDictionary(o => o.Value);

	[Fact]
	public void Handlers_reference_no_Fisher_types_in_signatures_locals_or_calls()
	{
		var assembly = typeof(Program).Assembly;
		var types = assembly.GetTypes().Where(t => t.Namespace is "Gigtime.Server.Catalogue" or "Gigtime.Server.Tasks").ToArray();

		var methods = types
			.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance
				| BindingFlags.DeclaredOnly))
			.ToArray();

		methods.ShouldNotBeEmpty();

		foreach (var type in types)
		{
			AssertNoFisher(type.BaseType);

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
				AssertNoFisher(field.FieldType);
		}

		foreach (var method in methods)
		{
			AssertNoFisher(method.ReturnType);
			foreach (var parameter in method.GetParameters()) AssertNoFisher(parameter.ParameterType);

			if (method.GetMethodBody() is not { } body) continue;

			foreach (var local in body.LocalVariables) AssertNoFisher(local.LocalType);
			var bytes = body.GetILAsByteArray()!;

			for (var offset = 0; offset < bytes.Length;)
			{
				var value = bytes[offset++] == 0xfe ? (short)(0xfe00 | bytes[offset++]) : bytes[offset - 1];
				var operation = operations[value];

				if (operation.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
				{
					var member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset),
						method.DeclaringType?.GenericTypeArguments,
						method.GetGenericArguments());

					AssertNoFisher(member as Type ?? member?.DeclaringType);
				}

				offset += operation.OperandType switch
				{
					OperandType.InlineNone => 0,
					OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
					OperandType.InlineVar => 2,
					OperandType.InlineI8 or OperandType.InlineR => 8,
					OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(bytes, offset)),
					_ => 4,
				};
			}
		}
	}

	private static void AssertNoFisher(Type? type)
	{
		if (type is null) return;

		(type.Namespace?.StartsWith("Fisher", StringComparison.Ordinal) == true).ShouldBeFalse($"Handler references {type}");
		if (type.HasElementType) AssertNoFisher(type.GetElementType());
		foreach (var argument in type.GenericTypeArguments) AssertNoFisher(argument);
	}
}
