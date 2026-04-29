namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    private static InvocationResolution? ResolveMemberInvocation(
        MemberAccessExpressionSyntax memberAccess,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        bool ignoreAccess = false)
    {
        QualifiedNameSyntax? receiverTypeName = memberAccess.Receiver switch
        {
            NameExpressionSyntax receiverName => receiverName.Name,
            MemberAccessExpressionSyntax nestedReceiver => TryFlattenQualifiedTarget(nestedReceiver),
            _ => null
        };

        if (receiverTypeName is not null &&
            ResolveTypeReference(receiverTypeName.ToDisplayString(), knownTypes) is { } targetType)
        {
            if (TryResolveTypeIntrinsic(targetType, memberAccess.MemberName.Text, argumentCount) is { } typeIntrinsic)
            {
                return new InvocationResolution(typeIntrinsic);
            }

            var staticMethod =
                (targetType as NamedTypeSymbol)?.Methods.FirstOrDefault(candidate =>
                    candidate.DeclaringTypeName == targetType.Name &&
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    candidate.IsStatic)
                ?? knownMethods.FirstOrDefault(candidate =>
                    candidate.DeclaringTypeName == targetType.Name &&
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    candidate.IsStatic);
            if (staticMethod is not null)
            {
                return new InvocationResolution(staticMethod);
            }

            return null;
        }

        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (TryResolveIntrinsic(receiverType, memberAccess.MemberName.Text, argumentCount) is { } intrinsic)
        {
            return new InvocationResolution(intrinsic, receiverType, true);
        }

        var method = GetReceiverTypeHierarchy(receiverType, knownTypes)
            .SelectMany(knownType => knownType.Methods
                .Where(candidate =>
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    (!ignoreAccess || !candidate.IsStatic))
                .Concat(knownMethods.Where(candidate =>
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    (!ignoreAccess || !candidate.IsStatic))))
            .FirstOrDefault(candidate => !ignoreAccess ? !candidate.IsStatic : true);
        if (method is null)
        {
            return null;
        }

        return new InvocationResolution(
            method,
            receiverType,
            method.IsVirtual ||
            method.IsOverride ||
            ResolveTypeReference(receiverType.Name, knownTypes) is NamedTypeSymbol { IsInterface: true });
    }

    private static MethodSymbol? TryResolveIntrinsic(TypeSymbol receiverType, string name, int argumentCount)
    {
        if (receiverType == TypeSymbol.String)
        {
            return TryResolveStringIntrinsic(name, argumentCount);
        }

        if (receiverType == TypeSymbol.Integer && argumentCount == 0 && name == "ToString")
        {
            return new MethodSymbol("ToString", TypeSymbol.String, [], TypeSymbol.Integer.Name, false, null, false, true);
        }

        return null;
    }

    private static MethodSymbol? TryResolveTypeIntrinsic(TypeSymbol targetType, string name, int argumentCount)
    {
        if (targetType == TypeSymbol.Integer && name == "Parse" && argumentCount == 1)
        {
            return new MethodSymbol(
                "Parse",
                TypeSymbol.Integer,
                [new ParameterSymbol("value", TypeSymbol.String)],
                TypeSymbol.Integer.Name,
                true,
                null,
                false,
                true);
        }

        if (targetType == TypeSymbol.Integer && name == "TryParse" && argumentCount == 2)
        {
            return new MethodSymbol(
                "TryParse",
                TypeSymbol.Boolean,
                [new ParameterSymbol("value", TypeSymbol.String), new ParameterSymbol("result", TypeSymbol.Integer, ParameterPassingKind.Out)],
                TypeSymbol.Integer.Name,
                true,
                null,
                false,
                true);
        }

        return null;
    }

    private static MethodSymbol? TryResolveStringIntrinsic(string name, int argumentCount)
    {
        if (argumentCount == 0)
        {
            return name switch
            {
                "ToUpper" => new MethodSymbol("ToUpper", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "ToLower" => new MethodSymbol("ToLower", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "Trim" => new MethodSymbol("Trim", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "TrimStart" => new MethodSymbol("TrimStart", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "TrimEnd" => new MethodSymbol("TrimEnd", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                _ => null
            };
        }

        if (name == "Substring" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Substring",
                TypeSymbol.String,
                [new ParameterSymbol("start", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Replace" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Replace",
                TypeSymbol.String,
                [new ParameterSymbol("oldValue", TypeSymbol.String), new ParameterSymbol("newValue", TypeSymbol.String)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Insert" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Insert",
                TypeSymbol.String,
                [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("value", TypeSymbol.String)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Remove" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Remove",
                TypeSymbol.String,
                [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (argumentCount != 1)
        {
            return null;
        }

        return name switch
        {
            "StartsWith" => new MethodSymbol("StartsWith", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "EndsWith" => new MethodSymbol("EndsWith", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "Contains" => new MethodSymbol("Contains", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "IndexOf" => new MethodSymbol("IndexOf", TypeSymbol.Integer, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "LastIndexOf" => new MethodSymbol("LastIndexOf", TypeSymbol.Integer, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            _ => null
        };
    }

}
