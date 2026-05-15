namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static TypeSymbol? ResolveTypeReference(string displayName, IEnumerable<TypeSymbol> knownTypes)
    {
        if (displayName.StartsWith("set of ", NameComparison))
        {
            var elementType = ResolveTypeReference(displayName["set of ".Length..], knownTypes);
            return elementType is not null ? CreateSetType(elementType) : null;
        }

        if (TryResolveExactTypeReference(displayName, knownTypes) is { } exactType)
        {
            return exactType;
        }

        if (TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
        {
            var resolvedArguments = genericArgumentNames
                .Select(argumentName => ResolveTypeReference(argumentName, knownTypes))
                .ToArray();
            if (resolvedArguments.Any(argument => argument is null))
            {
                return null;
            }

            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = FindTypesByName(knownTypes, simpleTypeName)
                .OfType<NamedTypeSymbol>()
                .Where(type => NameEquals(type.Name, simpleTypeName) && type.GenericArity == resolvedArguments.Length)
                .OrderByDescending(GetGenericDefinitionRichness)
                .FirstOrDefault();
            if (definition is null)
            {
                return null;
            }

            return ConstructClosedGenericType(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypes);
        }

        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return TryResolveBuiltInType(typeName)
            ?? FindTypesByName(knownTypes, typeName).FirstOrDefault();
    }

    public static TypeSymbol? ResolveTypeReferenceInGenericContext(
        string displayName,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var resolvedType = ResolveTypeReference(displayName, knownTypes);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        if (currentMethod?.DeclaringTypeName is null)
        {
            return null;
        }

        if (ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is not NamedTypeSymbol currentDeclaringType ||
            currentDeclaringType.GenericDefinition?.GenericParameters is not { Count: > 0 } genericParameters ||
            currentDeclaringType.TypeArguments is not { Count: > 0 } typeArguments)
        {
            return null;
        }

        var substitution = genericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, NameComparer);

        return ResolveTypeReferenceWithSubstitution(displayName, substitution, knownTypes);
    }

    public static TypeSymbol? TryResolveBuiltInType(string displayName)
    {
        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return TypeSymbol.BuiltInTypes.FirstOrDefault(type => NameEquals(type.Name, typeName));
    }

    public static bool IsCompatibleReferenceType(TypeSymbol sourceType, TypeSymbol targetType, IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (sourceType == TypeSymbol.Nil)
        {
            return targetType.IsReferenceType;
        }

        if (!targetType.IsReferenceType || !sourceType.IsReferenceType)
        {
            return false;
        }

        if (NameEquals(targetType.Name, TypeSymbol.Object.Name) || NameEquals(sourceType.Name, targetType.Name))
        {
            return true;
        }

        var resolvedTypes = knownTypes ?? [];
        var resolvedSourceType = ResolveNamedType(sourceType, resolvedTypes);
        var resolvedTargetType = ResolveNamedType(targetType, resolvedTypes);
        if (resolvedSourceType is null || resolvedTargetType is null)
        {
            return false;
        }

        if (resolvedTargetType.IsInterface)
        {
            if (resolvedSourceType.IsInterface)
            {
                return GetInterfaceHierarchy(resolvedSourceType, resolvedTypes).Any(candidate => NameEquals(candidate.Name, resolvedTargetType.Name));
            }

            foreach (var candidateType in GetTypeHierarchy(resolvedSourceType, resolvedTypes))
            {
                if (NameEquals(candidateType.Name, resolvedTargetType.Name))
                {
                    return true;
                }

                foreach (var implementedInterface in candidateType.InterfaceTypes)
                {
                    if (GetInterfaceHierarchy(implementedInterface, resolvedTypes).Any(candidate => NameEquals(candidate.Name, resolvedTargetType.Name)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return GetTypeHierarchy(resolvedSourceType, resolvedTypes).Any(candidate => NameEquals(candidate.Name, resolvedTargetType.Name));
    }

    public static bool IsOpenGenericDefinition(TypeSymbol type) =>
        type is NamedTypeSymbol namedType &&
        namedType.GenericArity > 0 &&
        namedType.GenericDefinition is null &&
        (namedType.TypeArguments is null || namedType.TypeArguments.Count == 0);

    private static bool TryParseConstructedTypeReference(string displayName, out string genericTypeName, out IReadOnlyList<string> genericArgumentNames)
    {
        genericTypeName = string.Empty;
        genericArgumentNames = [];
        var lessThanIndex = displayName.IndexOf('<');
        if (lessThanIndex <= 0 || !displayName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        genericTypeName = displayName[..lessThanIndex].Trim();
        genericArgumentNames = SplitGenericArgumentNames(displayName[(lessThanIndex + 1)..^1]);
        return genericArgumentNames.Count > 0;
    }

    private static TypeSymbol? ResolveTypeReferenceWithSubstitution(
        string displayName,
        IReadOnlyDictionary<string, TypeSymbol> substitution,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (substitution.TryGetValue(displayName, out var exactReplacement))
        {
            return exactReplacement;
        }

        if (displayName.StartsWith("set of ", NameComparison))
        {
            var elementType = ResolveTypeReferenceWithSubstitution(displayName["set of ".Length..], substitution, knownTypes);
            return elementType is not null ? CreateSetType(elementType) : null;
        }

        if (displayName.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = ResolveTypeReferenceWithSubstitution(displayName[..^2], substitution, knownTypes);
            return elementType is not null ? new TypeSymbol($"{elementType.Name}[]", true) : null;
        }

        if (TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
        {
            var resolvedArguments = genericArgumentNames
                .Select(argumentName => ResolveTypeReferenceWithSubstitution(argumentName, substitution, knownTypes))
                .ToArray();
            if (resolvedArguments.Any(argument => argument is null))
            {
                return null;
            }

            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = FindTypesByName(knownTypes, simpleTypeName)
                .OfType<NamedTypeSymbol>()
                .FirstOrDefault(type => NameEquals(type.Name, simpleTypeName) && type.GenericArity == resolvedArguments.Length);
            if (definition is null)
            {
                return null;
            }

            return ConstructClosedGenericType(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypes);
        }

        return ResolveTypeReference(displayName, knownTypes);
    }

    private static IEnumerable<TypeSymbol> FindTypesByName(
        IEnumerable<TypeSymbol> knownTypes,
        string typeName) =>
        knownTypes is IIndexedSymbolList<TypeSymbol> indexedTypes &&
        indexedTypes.ByName.TryGetValue(typeName, out var types)
            ? types
            : knownTypes.Where(type => NameEquals(type.Name, typeName));

    private static TypeSymbol? TryResolveExactTypeReference(
        string displayName,
        IEnumerable<TypeSymbol> knownTypes)
    {
        var lookupName = GetTypeLookupName(displayName);
        return string.IsNullOrWhiteSpace(lookupName)
            ? null
            : FindTypesByName(knownTypes, lookupName).FirstOrDefault();
    }

    private static string GetTypeLookupName(string displayName)
    {
        if (TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
        {
            var simpleGenericTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            return $"{simpleGenericTypeName}<{string.Join(", ", genericArgumentNames)}>";
        }

        return displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;
    }

    private static IReadOnlyList<string> SplitGenericArgumentNames(string value)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        var finalArgument = value[start..].Trim();
        if (!string.IsNullOrEmpty(finalArgument))
        {
            arguments.Add(finalArgument);
        }

        return arguments;
    }

    private static NamedTypeSymbol ConstructClosedGenericType(
        NamedTypeSymbol definition,
        IReadOnlyList<TypeSymbol> typeArguments,
        IEnumerable<TypeSymbol> knownTypes)
    {
        definition = knownTypes
            .OfType<NamedTypeSymbol>()
            .Where(candidate => NameEquals(candidate.Name, definition.Name) && candidate.GenericArity == definition.GenericArity)
            .OrderByDescending(GetGenericDefinitionRichness)
            .FirstOrDefault() ?? definition;

        if (definition.GenericParameters is null || definition.GenericParameters.Count != typeArguments.Count)
        {
            return definition;
        }

        var substitution = definition.GenericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, NameComparer);

        var closedName = $"{definition.Name}<{string.Join(", ", typeArguments.Select(argument => argument.Name))}>";
        var fields = definition.Fields
            .Select(field => field with
            {
                Type = SubstituteGenericType(field.Type, substitution, knownTypes),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var methods = definition.Methods
            .Select(method => method with
            {
                ReturnType = SubstituteGenericType(method.ReturnType, substitution, knownTypes),
                Parameters = method.Parameters
                    .Select(parameter => parameter with
                    {
                        Type = SubstituteGenericType(parameter.Type, substitution, knownTypes)
                    })
                    .ToArray(),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var properties = definition.Properties
            .Select(property => property with
            {
                Type = SubstituteGenericType(property.Type, substitution, knownTypes),
                IndexParameter = property.IndexParameter is null
                    ? null
                    : property.IndexParameter with
                    {
                        Type = SubstituteGenericType(property.IndexParameter.Type, substitution, knownTypes)
                    },
                GetterMethod = property.GetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => NameEquals(method.Name, property.GetterMethod.Name) && method.Parameters.Count == property.GetterMethod.Parameters.Count),
                SetterMethod = property.SetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => NameEquals(method.Name, property.SetterMethod.Name) && method.Parameters.Count == property.SetterMethod.Parameters.Count),
                ReadField = property.ReadField is null
                    ? null
                    : fields.FirstOrDefault(field => NameEquals(field.Name, property.ReadField.Name)),
                WriteField = property.WriteField is null
                    ? null
                    : fields.FirstOrDefault(field => NameEquals(field.Name, property.WriteField.Name)),
                DeclaringTypeName = closedName
            })
            .ToArray();

        return new NamedTypeSymbol(
            closedName,
            definition.IsReferenceType,
            definition.IsRecord,
            definition.IsInterface,
            definition.BaseType is null ? null : SubstituteGenericType(definition.BaseType, substitution, knownTypes),
            definition.InterfaceTypes.Select(type => SubstituteGenericType(type, substitution, knownTypes)).ToArray(),
            methods,
            fields,
            definition.Constants,
            properties,
            definition.GenericArity,
            null,
            definition,
            typeArguments,
            definition.IsDelegate);
    }

    private static TypeSymbol SubstituteGenericType(
        TypeSymbol type,
        IReadOnlyDictionary<string, TypeSymbol> substitution,
        IEnumerable<TypeSymbol> knownTypes)
    {
        if (substitution.TryGetValue(type.Name, out var replacement))
        {
            return replacement;
        }

        if (type.Name.StartsWith("set of ", StringComparison.Ordinal))
        {
            var elementType = SubstituteGenericType(
                ResolveTypeReference(type.Name["set of ".Length..], knownTypes) ?? new TypeSymbol(type.Name["set of ".Length..], true),
                substitution,
                knownTypes);
            return CreateSetType(elementType);
        }

        if (type.Name.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementTypeName = type.Name[..^2];
            var elementType = SubstituteGenericType(
                ResolveTypeReference(elementTypeName, knownTypes) ?? new TypeSymbol(elementTypeName, true),
                substitution,
                knownTypes);
            return new TypeSymbol($"{elementType.Name}[]", true);
        }

        if (type is NamedTypeSymbol namedType && namedType.GenericDefinition is not null && namedType.TypeArguments is not null)
        {
            var substitutedArguments = namedType.TypeArguments
                .Select(argument => SubstituteGenericType(argument, substitution, knownTypes))
                .ToArray();
            return ConstructClosedGenericType(namedType.GenericDefinition, substitutedArguments, knownTypes);
        }

        if (TryParseConstructedTypeReference(type.Name, out var genericTypeName, out var genericArgumentNames))
        {
            var substitutedArguments = genericArgumentNames
                .Select(argumentName => SubstituteGenericType(
                    ResolveTypeReference(argumentName, knownTypes) ?? new TypeSymbol(argumentName, true),
                    substitution,
                    knownTypes))
                .ToArray();
            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(candidate => NameEquals(candidate.Name, simpleTypeName) && candidate.GenericArity == substitutedArguments.Length)
                .OrderByDescending(GetGenericDefinitionRichness)
                .FirstOrDefault();
            if (definition is not null)
            {
                return ConstructClosedGenericType(definition, substitutedArguments, knownTypes);
            }
        }

        return type;
    }

    private static int GetGenericDefinitionRichness(NamedTypeSymbol type) =>
        (type.GenericParameters?.Count ?? 0) * 100 +
        type.Methods.Count * 10 +
        type.Properties.Count * 10 +
        type.InterfaceTypes.Count +
        (type.BaseType is null ? 0 : 1);

}
