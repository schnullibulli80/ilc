namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static PropertySymbol? ResolveIndexerReference(
        QualifiedNameSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target.Parts.Count >= 2)
        {
            var staticDeclaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            var staticIndexer = knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                property.IsStatic &&
                property.DeclaringTypeName == staticDeclaringTypeName &&
                property.Name == target.Parts[^1].Text);
            if (staticIndexer is not null)
            {
                return staticIndexer;
            }
        }

        if (target.Parts.Count == 1 &&
            (locals.TryGetValue(target.Parts[0].Text, out var localTargetType) && IsIndexableType(localTargetType) ||
             currentMethod?.DeclaringTypeName is not null &&
             ResolveFieldReference(target, knownFields, currentMethod) is { Type: var fieldType } && IsIndexableType(fieldType) ||
             ResolvePropertyReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { Type: var propertyType } && IsIndexableType(propertyType) ||
             ResolveConstantReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { Type: var constantType } && IsIndexableType(constantType)))
        {
            return null;
        }

        var valueType = target.Parts.Count == 1
            ? TryResolveValueReferenceType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : null;
        if (valueType is not null)
        {
            var valueHierarchyIndexer = GetReceiverTypeHierarchy(valueType, knownTypes ?? []).ToArray()
                .SelectMany(type => type.Properties.Where(property => property.IsIndexer && !property.IsStatic))
                .FirstOrDefault();
            if (valueHierarchyIndexer is not null)
            {
                return valueHierarchyIndexer;
            }

            var valueIndexer = knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                GetReceiverTypeHierarchy(valueType, knownTypes ?? [])
                    .Any(type => type.Name == property.DeclaringTypeName));
            if (valueIndexer is not null)
            {
                return valueIndexer;
            }
        }

        TypeSymbol? receiverType = null;
        if (target.Parts.Count == 1 && locals.TryGetValue(target.Parts[0].Text, out var localReceiverType))
        {
            receiverType = localReceiverType;
        }
        else if (target.Parts.Count >= 2 && target.Parts[0].Text != "self" && locals.TryGetValue(target.Parts[0].Text, out var qualifiedReceiverType))
        {
            receiverType = qualifiedReceiverType;
        }
        else if (target.Parts.Count == 1 && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            receiverType = new TypeSymbol(currentMethod.DeclaringTypeName, true);
        }
        else if (target.Parts.Count >= 1 && target.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            receiverType = new TypeSymbol(currentMethod.DeclaringTypeName, true);
        }

        if (receiverType is null)
        {
            return null;
        }

        var receiverHierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes ?? []).ToArray();
        var hierarchyIndexer = receiverHierarchy
            .SelectMany(type => type.Properties.Where(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text)))
            .FirstOrDefault();
        if (hierarchyIndexer is not null)
        {
            return hierarchyIndexer;
        }

        return knownProperties.FirstOrDefault(property =>
            property.IsIndexer &&
            !property.IsStatic &&
            receiverHierarchy.Any(type => type.Name == property.DeclaringTypeName) &&
            (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text));
    }

    public static PropertySymbol? ResolveIndexerReference(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target is NameExpressionSyntax nameTarget)
        {
            var resolvedIndexer = ResolveIndexerReference(nameTarget.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            if (resolvedIndexer is not null)
            {
                return resolvedIndexer;
            }
        }

        var targetType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (targetType == TypeSymbol.String || IsArrayType(targetType) || IsSetType(targetType))
        {
            return null;
        }

        return target switch
        {
            NameExpressionSyntax => null,
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property is { IsIndexer: true } property
                ? property
                : GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                    .SelectMany(type => type.Properties.Where(candidate => candidate.IsIndexer && !candidate.IsStatic))
                    .FirstOrDefault()
                    ?? knownProperties.FirstOrDefault(candidate =>
                        candidate.IsIndexer &&
                        !candidate.IsStatic &&
                        GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                            .Any(type => type.Name == candidate.DeclaringTypeName)),
            _ => GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                    .SelectMany(type => type.Properties.Where(property => property.IsIndexer && !property.IsStatic))
                    .FirstOrDefault()
                ?? knownProperties.FirstOrDefault(property =>
                    property.IsIndexer &&
                    !property.IsStatic &&
                    GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                        .Any(type => type.Name == property.DeclaringTypeName))
        };
    }

    public static MethodSymbol? ResolveConstructor(
        QualifiedNameSyntax typeName,
        int argumentCount,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var resolvedType = ResolveTypeReference(typeName.ToDisplayString(), knownTypes);
        if (resolvedType is null)
        {
            return null;
        }

        if (resolvedType is NamedTypeSymbol namedType)
        {
            return namedType.Methods.FirstOrDefault(method =>
                method.IsConstructor &&
                method.DeclaringTypeName == resolvedType.Name &&
                method.Parameters.Count == argumentCount);
        }

        return knownMethods.FirstOrDefault(method =>
            method.IsConstructor &&
            method.DeclaringTypeName == resolvedType.Name &&
            method.Parameters.Count == argumentCount);
    }

    public static InvocationResolution? ResolveInvocation(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            if (TryResolveIntrinsic(valueReceiverType, target.Parts[^1].Text, argumentCount) is { } intrinsic)
            {
                return new InvocationResolution(intrinsic, valueReceiverType, true);
            }

            var instanceMethod = GetTypeHierarchy(valueReceiverType, knownTypes)
                .SelectMany(knownType => knownMethods.Where(method =>
                    method.DeclaringTypeName == knownType.Name &&
                    method.Name == target.Parts[^1].Text &&
                    SupportsArgumentCount(method, argumentCount) &&
                    !method.IsStatic))
                .FirstOrDefault();
            if (instanceMethod is not null)
            {
                return new InvocationResolution(
                    instanceMethod,
                    valueReceiverType,
                    instanceMethod.IsVirtual ||
                    instanceMethod.IsOverride ||
                    ResolveTypeReference(valueReceiverType.Name, knownTypes) is NamedTypeSymbol { IsInterface: true });
            }
        }

        if (target.Parts.Count >= 2)
        {
            var declaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            if (ResolveTypeReference(declaringTypeName, knownTypes) is { } targetType)
            {
                if (TryResolveTypeIntrinsic(targetType, target.Parts[^1].Text, argumentCount) is { } typeIntrinsic)
                {
                    return new InvocationResolution(typeIntrinsic);
                }

                if (targetType is NamedTypeSymbol namedTargetType)
                {
                    var staticTargetMethod = namedTargetType.Methods.FirstOrDefault(method =>
                        method.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(method, argumentCount) &&
                        method.IsStatic);
                    if (staticTargetMethod is not null)
                    {
                        return new InvocationResolution(staticTargetMethod);
                    }
                }
            }
        }

        var staticMethod = ResolveMethod(target.ToDisplayString(), argumentCount, knownMethods, currentMethod, knownTypes);
        return staticMethod is null ? null : new InvocationResolution(staticMethod);
    }

    public static MethodSymbol? ResolveMethod(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var candidates = ResolveMethodCandidates(name, argumentCount, knownMethods);
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            if (ResolveTypeReference(qualifiedTarget.DeclaringTypeName, knownTypes) is NamedTypeSymbol targetType)
            {
                var targetTypeMethod = targetType.Methods.FirstOrDefault(method =>
                    method.Name == qualifiedTarget.MethodName &&
                    SupportsArgumentCount(method, argumentCount) &&
                    method.IsStatic);
                if (targetTypeMethod is not null)
                {
                    return targetTypeMethod;
                }
            }

            return candidates.FirstOrDefault(method => method.IsStatic);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName && (method.IsStatic || !currentMethod.IsStatic));
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }

            if (ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is NamedTypeSymbol currentDeclaringType)
            {
                var currentTypeMethod = currentDeclaringType.Methods.FirstOrDefault(method =>
                    method.Name == qualifiedTarget.MethodName &&
                    SupportsArgumentCount(method, argumentCount) &&
                    (method.IsStatic || !currentMethod.IsStatic));
                if (currentTypeMethod is not null)
                {
                    return currentTypeMethod;
                }
            }
        }

        return candidates.FirstOrDefault(method => method.IsStatic) ?? candidates.FirstOrDefault();
    }

    internal static MethodSymbol? ResolveMethodIgnoringAccess(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var candidates = ResolveMethodCandidates(name, argumentCount, knownMethods);
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            if (ResolveTypeReference(qualifiedTarget.DeclaringTypeName, knownTypes) is NamedTypeSymbol targetType)
            {
                var targetTypeMethod = targetType.Methods.FirstOrDefault(method =>
                    method.Name == qualifiedTarget.MethodName &&
                    SupportsArgumentCount(method, argumentCount));
                if (targetTypeMethod is not null)
                {
                    return targetTypeMethod;
                }
            }

            return candidates.FirstOrDefault();
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName);
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }

            if (ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is NamedTypeSymbol currentDeclaringType)
            {
                var currentTypeMethod = currentDeclaringType.Methods.FirstOrDefault(method =>
                    method.Name == qualifiedTarget.MethodName &&
                    SupportsArgumentCount(method, argumentCount));
                if (currentTypeMethod is not null)
                {
                    return currentTypeMethod;
                }
            }
        }

        return candidates.FirstOrDefault();
    }

    internal static InvocationResolution? ResolveInvocationIgnoringAccess(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var method = GetReceiverTypeHierarchy(valueReceiverType, knownTypes)
                .SelectMany(knownType => knownType.Methods
                    .Where(candidate =>
                        candidate.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(candidate, argumentCount))
                    .Concat(knownMethods.Where(candidate =>
                        candidate.DeclaringTypeName == knownType.Name &&
                        candidate.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(candidate, argumentCount))))
                .FirstOrDefault();
            if (method is not null)
            {
                return new InvocationResolution(method, valueReceiverType, !method.IsStatic);
            }
        }

        if (target.Parts.Count >= 2)
        {
            var declaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            if (ResolveTypeReference(declaringTypeName, knownTypes) is { } targetType)
            {
                if (TryResolveTypeIntrinsic(targetType, target.Parts[^1].Text, argumentCount) is { } typeIntrinsic)
                {
                    return new InvocationResolution(typeIntrinsic);
                }

                if (targetType is NamedTypeSymbol namedTargetType)
                {
                    var staticTargetMethod = namedTargetType.Methods.FirstOrDefault(method =>
                        method.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(method, argumentCount));
                    if (staticTargetMethod is not null)
                    {
                        return new InvocationResolution(staticTargetMethod);
                    }
                }
            }
        }

        var staticMethod = ResolveMethodIgnoringAccess(target.ToDisplayString(), argumentCount, knownMethods, currentMethod, knownTypes);
        return staticMethod is null ? null : new InvocationResolution(staticMethod);
    }

    private static MethodSymbol[] ResolveMethodCandidates(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        return knownMethods
            .Where(method =>
                method.Name == qualifiedTarget.MethodName &&
                SupportsArgumentCount(method, argumentCount) &&
                (qualifiedTarget.DeclaringTypeName is null || method.DeclaringTypeName == qualifiedTarget.DeclaringTypeName))
            .ToArray();
    }

    public static bool SupportsArgumentCount(MethodSymbol method, int argumentCount)
    {
        if (method.Parameters.Count > 0 &&
            method.Parameters[^1].PassingKind == ParameterPassingKind.Params)
        {
            return argumentCount >= method.Parameters.Count - 1;
        }

        return method.Parameters.Count == argumentCount;
    }

    public static NameResolution ResolveName(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var displayName = name.ToDisplayString();
        if (name.Parts.Count == 1 && locals.TryGetValue(displayName, out var localType))
        {
            return new NameResolution(NameResolutionKind.LocalOrGlobal, displayName, localType);
        }

        var receiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (receiverType is not null)
        {
            var hierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes);
            var instanceField = hierarchy
                .SelectMany(receiver => receiver.Fields
                    .Where(field =>
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)
                    .Concat(knownFields.Where(field =>
                        field.DeclaringTypeName == receiver.Name &&
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceField.Type, null, instanceField);
            }

            var instanceProperty = hierarchy
                .SelectMany(receiver => receiver.Properties
                    .Where(property =>
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)
                    .Concat(knownProperties.Where(property =>
                        property.DeclaringTypeName == receiver.Name &&
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)))
                .FirstOrDefault();
            if (instanceProperty is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceProperty.Type, null, instanceProperty.ReadField);
            }

            var instanceConstant = hierarchy
                .SelectMany(receiver => knownConstants.Where(constant =>
                    constant.IsStatic &&
                    constant.DeclaringTypeName == receiver.Name &&
                    constant.Name == name.Parts[^1].Text))
                .FirstOrDefault();
            if (instanceConstant is not null)
            {
                return new NameResolution(NameResolutionKind.Constant, displayName, instanceConstant.Type, null, null, instanceConstant);
            }

            var instanceMethodGroup = hierarchy
                .SelectMany(receiver => receiver.Methods
                    .Where(method =>
                        method.Name == name.Parts[^1].Text &&
                        !method.IsStatic)
                    .Concat(knownMethods.Where(method =>
                        method.DeclaringTypeName == receiver.Name &&
                        method.Name == name.Parts[^1].Text &&
                        !method.IsStatic)))
                .FirstOrDefault();
            if (instanceMethodGroup is not null)
            {
                return new NameResolution(NameResolutionKind.MethodGroup, displayName, instanceMethodGroup.ReturnType, instanceMethodGroup);
            }
        }

        var propertyCandidate = ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (propertyCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Field, displayName, propertyCandidate.Type, null, propertyCandidate.ReadField);
        }

        var constantCandidate = ResolveConstantReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (constantCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Constant, displayName, constantCandidate.Type, null, null, constantCandidate);
        }

        var fieldCandidate = ResolveFieldReference(name, knownFields, currentMethod);
        if (fieldCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Field, displayName, fieldCandidate.Type, null, fieldCandidate);
        }

        var typeCandidate = ResolveTypeReference(displayName, knownTypes);
        if (typeCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Type, displayName, typeCandidate);
        }

        var methodCandidate = ResolveMethodGroup(displayName, knownMethods, currentMethod);
        if (methodCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.MethodGroup, displayName, methodCandidate.ReturnType, methodCandidate);
        }

        return new NameResolution(NameResolutionKind.Unknown, displayName);
    }

}
