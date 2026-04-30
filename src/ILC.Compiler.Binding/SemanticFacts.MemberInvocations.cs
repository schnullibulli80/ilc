namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System;

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
        bool ignoreAccess = false,
        Func<string, IDisposable>? profiler = null)
    {
        QualifiedNameSyntax? receiverTypeName;
        using (profiler?.Invoke("ResolveReceiverTypeName"))
        {
            receiverTypeName = memberAccess.Receiver switch
            {
                NameExpressionSyntax receiverName => receiverName.Name,
                MemberAccessExpressionSyntax nestedReceiver => TryFlattenQualifiedTarget(nestedReceiver),
                _ => null
            };
        }

        TypeSymbol? targetType = null;
        using (profiler?.Invoke("ResolveStaticReceiverType"))
        {
            if (receiverTypeName is not null &&
                !IsLocalQualifiedReceiver(receiverTypeName, locals))
            {
                targetType = ResolveTypeReference(receiverTypeName.ToDisplayString(), knownTypes);
            }
        }

        if (targetType is not null)
        {
            using (profiler?.Invoke("ResolveTypeIntrinsic"))
            {
                if (TryResolveTypeIntrinsic(targetType, memberAccess.MemberName.Text, argumentCount) is { } typeIntrinsic)
                {
                    return new InvocationResolution(typeIntrinsic);
                }
            }

            using (profiler?.Invoke("ResolveStaticMethod"))
            {
                var staticMethod =
                    (targetType as NamedTypeSymbol)?.Methods.FirstOrDefault(candidate =>
                        candidate.DeclaringTypeName == targetType.Name &&
                        candidate.Name == memberAccess.MemberName.Text &&
                        SupportsArgumentCount(candidate, argumentCount) &&
                        candidate.IsStatic)
                    ?? FindMethodsByDeclaringType(knownMethods, targetType.Name)
                        .FirstOrDefault(candidate =>
                            candidate.Name == memberAccess.MemberName.Text &&
                            SupportsArgumentCount(candidate, argumentCount) &&
                            candidate.IsStatic);
                if (staticMethod is not null)
                {
                    return new InvocationResolution(staticMethod);
                }
            }

            return null;
        }

        TypeSymbol receiverType;
        using (profiler?.Invoke("InferReceiverType"))
        {
            receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        }

        using (profiler?.Invoke("ResolveInstanceIntrinsic"))
        {
            if (TryResolveIntrinsic(receiverType, memberAccess.MemberName.Text, argumentCount) is { } intrinsic)
            {
                return new InvocationResolution(intrinsic, receiverType, true);
            }
        }

        MethodSymbol? method;
        using (profiler?.Invoke("ResolveInstanceMethod"))
        {
            method = GetReceiverTypeHierarchy(receiverType, knownTypes)
                .SelectMany(knownType => knownType.Methods
                    .Where(candidate =>
                        candidate.Name == memberAccess.MemberName.Text &&
                        SupportsArgumentCount(candidate, argumentCount) &&
                        (!ignoreAccess || !candidate.IsStatic))
                    .Concat(FindMethodsByDeclaringType(knownMethods, knownType.Name)
                        .Where(candidate =>
                            candidate.Name == memberAccess.MemberName.Text &&
                            SupportsArgumentCount(candidate, argumentCount) &&
                            (!ignoreAccess || !candidate.IsStatic))))
                .FirstOrDefault(candidate => !ignoreAccess ? !candidate.IsStatic : true);
        }

        if (method is null)
        {
            return null;
        }

        bool isVirtual;
        using (profiler?.Invoke("ComputeDispatchKind"))
        {
            isVirtual = method.IsVirtual ||
                method.IsOverride ||
                ResolveTypeReference(receiverType.Name, knownTypes) is NamedTypeSymbol { IsInterface: true };
        }

        return new InvocationResolution(
            method,
            receiverType,
            isVirtual);
    }

    private sealed record IntrinsicMethodSignature(
        TypeSymbol DeclaringType,
        string Name,
        TypeSymbol ReturnType,
        bool IsStatic,
        IReadOnlyList<ParameterSymbol> Parameters);

    private static readonly IReadOnlyList<IntrinsicMethodSignature> InstanceIntrinsicSignatures =
    [
        new(TypeSymbol.Integer, "ToString", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "ToUpper", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "ToLower", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "Trim", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "TrimStart", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "TrimEnd", TypeSymbol.String, false, []),
        new(TypeSymbol.String, "Substring", TypeSymbol.String, false, [new ParameterSymbol("start", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)]),
        new(TypeSymbol.String, "Replace", TypeSymbol.String, false, [new ParameterSymbol("oldValue", TypeSymbol.String), new ParameterSymbol("newValue", TypeSymbol.String)]),
        new(TypeSymbol.String, "Insert", TypeSymbol.String, false, [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.String, "Remove", TypeSymbol.String, false, [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)]),
        new(TypeSymbol.String, "StartsWith", TypeSymbol.Boolean, false, [new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.String, "EndsWith", TypeSymbol.Boolean, false, [new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.String, "Contains", TypeSymbol.Boolean, false, [new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.String, "IndexOf", TypeSymbol.Integer, false, [new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.String, "LastIndexOf", TypeSymbol.Integer, false, [new ParameterSymbol("value", TypeSymbol.String)])
    ];

    private static readonly IReadOnlyList<IntrinsicMethodSignature> TypeIntrinsicSignatures =
    [
        new(TypeSymbol.Integer, "Parse", TypeSymbol.Integer, true, [new ParameterSymbol("value", TypeSymbol.String)]),
        new(TypeSymbol.Integer, "TryParse", TypeSymbol.Boolean, true, [new ParameterSymbol("value", TypeSymbol.String), new ParameterSymbol("result", TypeSymbol.Integer, ParameterPassingKind.Out)])
    ];

    private static MethodSymbol? TryResolveIntrinsic(TypeSymbol receiverType, string name, int argumentCount) =>
        InstanceIntrinsicSignatures
            .Where(signature => IntrinsicSignatureMatches(signature, receiverType, name, argumentCount))
            .Select(CreateIntrinsicMethod)
            .FirstOrDefault();

    private static MethodSymbol? TryResolveTypeIntrinsic(TypeSymbol targetType, string name, int argumentCount) =>
        TypeIntrinsicSignatures
            .Where(signature => IntrinsicSignatureMatches(signature, targetType, name, argumentCount))
            .Select(CreateIntrinsicMethod)
            .FirstOrDefault();

    private static bool IntrinsicSignatureMatches(IntrinsicMethodSignature signature, TypeSymbol declaringType, string name, int argumentCount) =>
        signature.DeclaringType == declaringType &&
        signature.Name == name &&
        signature.Parameters.Count == argumentCount;

    private static MethodSymbol CreateIntrinsicMethod(IntrinsicMethodSignature signature) =>
        new(
            signature.Name,
            signature.ReturnType,
            signature.Parameters,
            signature.DeclaringType.Name,
            signature.IsStatic,
            null,
            false,
            true);

    private static bool IsLocalQualifiedReceiver(
        QualifiedNameSyntax receiverTypeName,
        IReadOnlyDictionary<string, TypeSymbol> locals) =>
        receiverTypeName.Parts.Count > 0 &&
        locals.ContainsKey(receiverTypeName.Parts[0].Text);

}
