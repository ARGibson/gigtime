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
		var types = assembly.GetTypes().Where(IsFeatureType).ToArray();

		const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance
			| BindingFlags.DeclaredOnly;

		var methods = types
			.SelectMany(t => t.GetMethods(members))
			.Cast<MethodBase>()
			.Concat(types.SelectMany(t => t.GetConstructors(members)))
			.ToArray();

		methods.ShouldNotBeEmpty();

		foreach (var type in types)
		{
			AssertNoFisher(type.BaseType);
			AssertAttributes(type.GetCustomAttributesData());
			foreach (var contract in type.GetInterfaces()) AssertNoFisher(contract);

			foreach (var property in type.GetProperties(
								BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
			{
				AssertNoFisher(property.PropertyType);
				AssertAttributes(property.GetCustomAttributesData());
				foreach (var parameter in property.GetIndexParameters()) AssertAttributes(parameter.GetCustomAttributesData());
			}

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
				AssertNoFisher(field.FieldType);
		}

		foreach (var method in methods)
		{
			if (method is MethodInfo info)
			{
				AssertNoFisher(info.ReturnType);
				AssertAttributes(info.ReturnParameter.GetCustomAttributesData());
			}

			AssertAttributes(method.GetCustomAttributesData());

			foreach (var parameter in method.GetParameters())
			{
				AssertNoFisher(parameter.ParameterType);
				AssertAttributes(parameter.GetCustomAttributesData());
			}

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
						method is MethodInfo generic ? generic.GetGenericArguments() : Type.EmptyTypes);

					AssertNoFisher(member as Type ?? member?.DeclaringType);

					if (member is MethodInfo called)
					{
						foreach (var argument in called.GetGenericArguments())
							AssertNoFisher(argument);
					}
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

	private static bool IsFeatureType(Type type)
	{
		var name = type.Namespace;

		return name is not null && name.StartsWith("Gigtime.Server.", StringComparison.Ordinal)
			&& name != "Gigtime.Server.Hosting" && !name.StartsWith("Gigtime.Server.Hosting.", StringComparison.Ordinal)
			&& !name.StartsWith("Gigtime.Server.Generated", StringComparison.Ordinal)
			&& !name.StartsWith("Gigtime.Server.Internal.Generated", StringComparison.Ordinal);
	}

	private static void AssertAttributes(IList<CustomAttributeData> attributes)
	{
		foreach (var attribute in attributes)
		{
			AssertNoFisher(attribute.AttributeType);
			foreach (var argument in attribute.ConstructorArguments) AssertAttributeArgument(argument);
			foreach (var argument in attribute.NamedArguments) AssertAttributeArgument(argument.TypedValue);
		}
	}

	private static void AssertAttributeArgument(CustomAttributeTypedArgument argument)
	{
		AssertNoFisher(argument.ArgumentType);
		if (argument.Value is Type type) AssertNoFisher(type);

		if (argument.Value is IEnumerable<CustomAttributeTypedArgument> values)
		{
			foreach (var value in values)
				AssertAttributeArgument(value);
		}
	}

	private static void AssertNoFisher(Type? type)
	{
		if (type is null) return;

		var name = type.Assembly.GetName().Name!;

		(name is "Wolverine.Fisher" or "Weasel.Sqlite" || name.StartsWith("Fisher", StringComparison.Ordinal))
			.ShouldBeFalse($"Handler references {type} from forbidden assembly {name}");

		if (type.HasElementType) AssertNoFisher(type.GetElementType());
		foreach (var argument in type.GenericTypeArguments) AssertNoFisher(argument);
	}
}
