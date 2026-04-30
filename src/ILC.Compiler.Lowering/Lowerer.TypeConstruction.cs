namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private void ValidateConstructedObjectType(
        NewExpressionSyntax newExpression,
        TypeSymbol constructedObjectType,
        MethodSymbol? currentMethod)
    {
        if (currentMethod?.DeclaringTypeName is null)
        {
            return;
        }

        if (SemanticFacts.ResolveTypeReference(currentMethod.DeclaringTypeName, _knownTypes) is not NamedTypeSymbol currentDeclaringType ||
            currentDeclaringType.GenericDefinition?.GenericParameters is not { Count: > 0 } genericParameters ||
            currentDeclaringType.TypeArguments is not { Count: > 0 } currentTypeArguments)
        {
            return;
        }

        if (currentTypeArguments.Any(argument =>
                argument is TypeParameterSymbol parameter &&
                genericParameters.Any(genericParameter => genericParameter.Name == parameter.Name)))
        {
            return;
        }

        if (constructedObjectType is NamedTypeSymbol namedConstructedType &&
            namedConstructedType.GenericDefinition is not null &&
            namedConstructedType.TypeArguments is { Count: > 0 } constructedArguments)
        {
            if (!constructedArguments.Any(argument =>
                    argument is TypeParameterSymbol parameter &&
                    genericParameters.Any(genericParameter => genericParameter.Name == parameter.Name)))
            {
                return;
            }
        }
        else if (constructedObjectType is TypeParameterSymbol typeParameter &&
                 genericParameters.Any(parameter => parameter.Name == typeParameter.Name))
        {
        }
        else if (TryParseConstructedTypeReference(constructedObjectType.Name, out _, out var genericArgumentNames) &&
                 genericArgumentNames.Any(argumentName => genericParameters.Any(parameter => parameter.Name == argumentName)))
        {
        }
        else
        {
            return;
        }

        throw new InvalidOperationException(
            $"Generic new-expression was not specialized: syntax='{newExpression.TypeName.ToDisplayString()}', inferred='{constructedObjectType.Name}', method='{currentMethod.Name}', declaringType='{currentMethod.DeclaringTypeName}'.");
    }

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

    private TypeSymbol? TryCloseTypeReferenceForCurrentMethod(TypeSymbol type, MethodSymbol? currentMethod, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (currentMethod?.DeclaringTypeName is null)
        {
            return null;
        }

        IReadOnlyList<TypeParameterSymbol>? genericParameters = null;
        IReadOnlyList<TypeSymbol>? typeArguments = null;

        if (SemanticFacts.ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is NamedTypeSymbol currentDeclaringType &&
            currentDeclaringType.GenericDefinition?.GenericParameters is { Count: > 0 } resolvedGenericParameters &&
            currentDeclaringType.TypeArguments is { Count: > 0 } resolvedTypeArguments)
        {
            genericParameters = resolvedGenericParameters;
            typeArguments = resolvedTypeArguments;
        }
        else if (TryParseConstructedTypeReference(currentMethod.DeclaringTypeName, out var currentGenericTypeName, out var currentGenericArgumentNames))
        {
            var simpleTypeName = currentGenericTypeName.Contains('.')
                ? currentGenericTypeName[(currentGenericTypeName.LastIndexOf('.') + 1)..]
                : currentGenericTypeName;
            var currentGenericDefinition = knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(candidate =>
                    candidate.Name == simpleTypeName &&
                    candidate.GenericArity == currentGenericArgumentNames.Count &&
                    candidate.GenericDefinition is null)
                .OrderByDescending(GetGenericDefinitionRichnessLocal)
                .FirstOrDefault();
            var parsedTypeArguments = currentGenericArgumentNames
                .Select(argumentName => SemanticFacts.ResolveTypeReference(argumentName, knownTypes))
                .ToArray();

            if (currentGenericDefinition?.GenericParameters is { Count: > 0 } parsedGenericParameters &&
                parsedTypeArguments.All(argument => argument is not null))
            {
                genericParameters = parsedGenericParameters;
                typeArguments = parsedTypeArguments!.Cast<TypeSymbol>().ToArray();
            }
        }

        if ((genericParameters is not { Count: > 0 } || typeArguments is not { Count: > 0 }) &&
            currentMethod.Declaration is not null)
        {
            var openTemplateCandidates = _knownMethods.Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.DeclaringTypeName) &&
                candidate.Name == currentMethod.Name &&
                candidate.Parameters.Count == currentMethod.Parameters.Count &&
                candidate.DeclaringTypeName != currentMethod.DeclaringTypeName &&
                TryParseConstructedTypeReference(candidate.DeclaringTypeName!, out _, out _))
                .ToArray();
            var openTemplateMethod = openTemplateCandidates.FirstOrDefault(candidate =>
                candidate.Declaration == currentMethod.Declaration);
            if (openTemplateMethod?.DeclaringTypeName is not null &&
                TryParseConstructedTypeReference(openTemplateMethod.DeclaringTypeName, out _, out var openMethodArgumentNames) &&
                TryParseConstructedTypeReference(currentMethod.DeclaringTypeName, out _, out var closedMethodArgumentNames) &&
                openMethodArgumentNames.Count == closedMethodArgumentNames.Count)
            {
                genericParameters = openMethodArgumentNames
                    .Select(name => new TypeParameterSymbol(name))
                    .ToArray();
                typeArguments = closedMethodArgumentNames
                    .Select(argumentName => SemanticFacts.ResolveTypeReference(argumentName, knownTypes) ?? new TypeSymbol(argumentName, true))
                    .ToArray();
            }
        }

        if (genericParameters is not { Count: > 0 } || typeArguments is not { Count: > 0 })
        {
            if (currentMethod.DeclaringTypeName.StartsWith("Enumerable<", StringComparison.Ordinal) &&
                (type.Name.StartsWith("WhereEnumerable<", StringComparison.Ordinal) || type.Name.StartsWith("SelectEnumerable<", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "TryCloseTypeReferenceForCurrentMethod could not build substitution for enumerable pipeline type: " +
                    $"currentMethod='{currentMethod.DeclaringTypeName}.{currentMethod.Name}', " +
                    $"targetType='{type.Name}', " +
                    $"resolvedCurrent='{SemanticFacts.ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes)?.Name ?? "<null>"}', " +
                    $"parsedCurrent='{currentMethod.DeclaringTypeName}', " +
                    $"genericParameterCount={(genericParameters?.Count ?? 0)}, " +
                    $"typeArgumentCount={(typeArguments?.Count ?? 0)}, " +
                    $"templateMatches=[{string.Join(" | ", _knownMethods.Where(candidate =>
                        !string.IsNullOrWhiteSpace(candidate.DeclaringTypeName) &&
                        candidate.Name == currentMethod.Name &&
                        candidate.Parameters.Count == currentMethod.Parameters.Count &&
                        candidate.DeclaringTypeName != currentMethod.DeclaringTypeName &&
                        TryParseConstructedTypeReference(candidate.DeclaringTypeName!, out _, out _))
                        .Select(candidate => $"{candidate.DeclaringTypeName}.{candidate.Name} declMatch={ReferenceEquals(candidate.Declaration, currentMethod.Declaration)} params=[{string.Join(", ", candidate.Parameters.Select(parameter => parameter.Type.Name))}] return={candidate.ReturnType.Name}"))}]");
            }
            return null;
        }

        var substitution = genericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, StringComparer.Ordinal);

        TypeSymbol? ResolveWithSubstitution(string displayName)
        {
            if (substitution.TryGetValue(displayName, out var replacement))
            {
                return replacement;
            }

            if (!TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
            {
                return SemanticFacts.ResolveTypeReference(displayName, knownTypes);
            }

            var resolvedArguments = genericArgumentNames
                .Select(ResolveWithSubstitution)
                .ToArray();
            if (resolvedArguments.Any(argument => argument is null))
            {
                return null;
            }

            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(candidate =>
                    candidate.Name == simpleTypeName &&
                    candidate.GenericArity == resolvedArguments.Length &&
                    candidate.GenericDefinition is null)
                .OrderByDescending(GetGenericDefinitionRichnessLocal)
                .FirstOrDefault();
            if (definition is null)
            {
                return SemanticFacts.ResolveTypeReference(displayName, knownTypes);
            }

            return ConstructClosedGenericTypeLocal(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypes);
        }

        var resolved = ResolveWithSubstitution(type.Name);
        if (currentMethod.DeclaringTypeName.StartsWith("Enumerable<", StringComparison.Ordinal) &&
            (type.Name.StartsWith("WhereEnumerable<", StringComparison.Ordinal) || type.Name.StartsWith("SelectEnumerable<", StringComparison.Ordinal)) &&
            ContainsOpenGenericPlaceholder(type) &&
            (resolved is null || resolved.Name == type.Name))
        {
            throw new InvalidOperationException(
                "TryCloseTypeReferenceForCurrentMethod did not specialize enumerable pipeline helper type: " +
                $"currentMethod='{currentMethod.DeclaringTypeName}.{currentMethod.Name}', " +
                $"targetType='{type.Name}', " +
                $"substitution=[{string.Join(", ", substitution.Select(entry => $"{entry.Key}->{entry.Value.Name}"))}], " +
                $"result='{resolved?.Name ?? "<null>"}'");
        }

        return resolved;
    }

    private static NamedTypeSymbol ConstructClosedGenericTypeLocal(
        NamedTypeSymbol definition,
        IReadOnlyList<TypeSymbol> typeArguments,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (definition.GenericParameters is null || definition.GenericParameters.Count != typeArguments.Count)
        {
            return definition;
        }

        var substitution = definition.GenericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, StringComparer.Ordinal);

        TypeSymbol Substitute(TypeSymbol type)
        {
            if (substitution.TryGetValue(type.Name, out var replacement))
            {
                return replacement;
            }

            if (type is NamedTypeSymbol namedType &&
                namedType.GenericDefinition is not null &&
                namedType.TypeArguments is { Count: > 0 })
            {
                var substitutedArguments = namedType.TypeArguments.Select(Substitute).ToArray();
                return ConstructClosedGenericTypeLocal(namedType.GenericDefinition, substitutedArguments, knownTypes);
            }

            if (TryParseConstructedTypeReference(type.Name, out var genericTypeName, out var genericArgumentNames))
            {
                var substitutedArguments = genericArgumentNames
                    .Select(argumentName =>
                    {
                        if (substitution.TryGetValue(argumentName, out var substitutedArgument))
                        {
                            return substitutedArgument;
                        }

                        return SemanticFacts.ResolveTypeReference(argumentName, knownTypes) ?? new TypeSymbol(argumentName, true);
                    })
                    .Select(Substitute)
                    .ToArray();
                var simpleTypeName = genericTypeName.Contains('.')
                    ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                    : genericTypeName;
                var genericDefinition = knownTypes
                    .OfType<NamedTypeSymbol>()
                    .Where(candidate =>
                        candidate.Name == simpleTypeName &&
                        candidate.GenericArity == substitutedArguments.Length &&
                        candidate.GenericDefinition is null)
                    .OrderByDescending(GetGenericDefinitionRichnessLocal)
                    .FirstOrDefault();
                if (genericDefinition is not null)
                {
                    return ConstructClosedGenericTypeLocal(genericDefinition, substitutedArguments, knownTypes);
                }
            }

            return type;
        }

        var closedName = $"{definition.Name}<{string.Join(", ", typeArguments.Select(argument => argument.Name))}>";
        var fields = definition.Fields
            .Select(field => field with
            {
                Type = Substitute(field.Type),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var methods = definition.Methods
            .Select(method => method with
            {
                ReturnType = Substitute(method.ReturnType),
                Parameters = method.Parameters
                    .Select(parameter => parameter with
                    {
                        Type = Substitute(parameter.Type)
                    })
                    .ToArray(),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var properties = definition.Properties
            .Select(property => property with
            {
                Type = Substitute(property.Type),
                IndexParameter = property.IndexParameter is null
                    ? null
                    : property.IndexParameter with
                    {
                        Type = Substitute(property.IndexParameter.Type)
                    },
                GetterMethod = property.GetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => method.Name == property.GetterMethod.Name && method.Parameters.Count == property.GetterMethod.Parameters.Count),
                SetterMethod = property.SetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => method.Name == property.SetterMethod.Name && method.Parameters.Count == property.SetterMethod.Parameters.Count),
                ReadField = property.ReadField is null
                    ? null
                    : fields.FirstOrDefault(field => field.Name == property.ReadField.Name),
                WriteField = property.WriteField is null
                    ? null
                    : fields.FirstOrDefault(field => field.Name == property.WriteField.Name),
                DeclaringTypeName = closedName
            })
            .ToArray();

        return new NamedTypeSymbol(
            closedName,
            definition.IsReferenceType,
            definition.IsRecord,
            definition.IsInterface,
            definition.BaseType is null ? null : Substitute(definition.BaseType),
            definition.InterfaceTypes.Select(Substitute).ToArray(),
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

    private MethodSymbol? TryCloseMethodForCurrentMethod(
        MethodSymbol? method,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (method is null)
        {
            return null;
        }

        var closedDeclaringType = string.IsNullOrWhiteSpace(method.DeclaringTypeName)
            ? null
            : TryCloseTypeReferenceForCurrentMethod(new TypeSymbol(method.DeclaringTypeName, true), currentMethod, knownTypes);
        var closedReturnType = TryCloseTypeReferenceForCurrentMethod(method.ReturnType, currentMethod, knownTypes) ?? method.ReturnType;
        var closedParameters = method.Parameters
            .Select(parameter => parameter with
            {
                Type = TryCloseTypeReferenceForCurrentMethod(parameter.Type, currentMethod, knownTypes) ?? parameter.Type
            })
            .ToArray();

        var declaringTypeName = closedDeclaringType?.Name ?? method.DeclaringTypeName;
        var changed =
            declaringTypeName != method.DeclaringTypeName ||
            closedReturnType.Name != method.ReturnType.Name ||
            closedParameters.Where((parameter, index) => parameter.Type.Name != method.Parameters[index].Type.Name).Any();

        return changed
            ? method with
            {
                DeclaringTypeName = declaringTypeName,
                ReturnType = closedReturnType,
                Parameters = closedParameters
            }
            : method;
    }

    private static (TypeSymbol ConstructedType, MethodSymbol Constructor)? TryResolveConstructedTypeByArgumentTypes(
        string typeDisplayName,
        IReadOnlyList<TypeSymbol> argumentTypes,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var simpleTypeName = typeDisplayName;
        if (TryParseConstructedTypeReference(typeDisplayName, out var genericTypeName, out _))
        {
            simpleTypeName = genericTypeName;
        }

        if (simpleTypeName.Contains('.'))
        {
            simpleTypeName = simpleTypeName[(simpleTypeName.LastIndexOf('.') + 1)..];
        }

        foreach (var candidateType in knownTypes.OfType<NamedTypeSymbol>())
        {
            var candidateSimpleName = candidateType.Name;
            if (TryParseConstructedTypeReference(candidateSimpleName, out var candidateGenericTypeName, out _))
            {
                candidateSimpleName = candidateGenericTypeName;
            }

            if (candidateSimpleName.Contains('.'))
            {
                candidateSimpleName = candidateSimpleName[(candidateSimpleName.LastIndexOf('.') + 1)..];
            }

            if (!string.Equals(candidateSimpleName, simpleTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            var candidateConstructor = candidateType.Methods.FirstOrDefault(method =>
                method.IsConstructor &&
                method.DeclaringTypeName == candidateType.Name &&
                method.Parameters.Count == argumentTypes.Count &&
                method.Parameters.Select(parameter => parameter.Type.Name).SequenceEqual(argumentTypes.Select(type => type.Name), StringComparer.Ordinal));
            if (candidateConstructor is not null)
            {
                return (candidateType, candidateConstructor);
            }
        }

        return null;
    }

    private static bool IsEnumerablePipelineConstruction(MethodSymbol? currentMethod, NewExpressionSyntax newExpression)
    {
        if (currentMethod?.DeclaringTypeName is null)
        {
            return false;
        }

        if (!currentMethod.DeclaringTypeName.StartsWith("Enumerable", StringComparison.Ordinal))
        {
            return false;
        }

        return newExpression.TypeName.ToDisplayString().StartsWith("WhereEnumerable", StringComparison.Ordinal) ||
               newExpression.TypeName.ToDisplayString().StartsWith("SelectEnumerable", StringComparison.Ordinal);
    }

    private static bool ContainsOpenGenericPlaceholder(TypeSymbol type) =>
        type.Name.Contains("<T", StringComparison.Ordinal) ||
        type.Name.EndsWith("<T>", StringComparison.Ordinal) ||
        type.Name.Contains(", T", StringComparison.Ordinal) ||
        type.Name.Contains("<TSource", StringComparison.Ordinal) ||
        type.Name.Contains("<TResult", StringComparison.Ordinal);

    private static TypeSymbol? TryCloseTypeReferenceByDisplayNameForCurrentMethod(
        string typeDisplayName,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (string.IsNullOrWhiteSpace(typeDisplayName) ||
            currentMethod?.DeclaringTypeName is null ||
            !TryParseConstructedTypeReference(currentMethod.DeclaringTypeName, out _, out var currentGenericArgumentNames))
        {
            return null;
        }

        NamedTypeSymbol? currentGenericDefinition = null;
        if (SemanticFacts.ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is NamedTypeSymbol resolvedCurrentType)
        {
            currentGenericDefinition = resolvedCurrentType.GenericDefinition;
        }

        if (currentGenericDefinition?.GenericParameters is not { Count: > 0 } genericParameters ||
            genericParameters.Count != currentGenericArgumentNames.Count)
        {
            return null;
        }

        var substitution = genericParameters
            .Zip(currentGenericArgumentNames, (parameter, argumentName) => (parameter.Name, argumentName))
            .ToDictionary(entry => entry.Name, entry => entry.argumentName, StringComparer.Ordinal);

        string? Substitute(string displayName)
        {
            if (substitution.TryGetValue(displayName, out var replacement))
            {
                return replacement;
            }

            if (!TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
            {
                return displayName;
            }

                var substitutedArguments = genericArgumentNames
                    .Select(Substitute)
                    .ToArray();
            if (substitutedArguments.Any(argument => argument is null))
            {
                return null;
            }

            return $"{genericTypeName}<{string.Join(", ", substitutedArguments!)}>";
        }

        var substitutedDisplayName = Substitute(typeDisplayName);
        return substitutedDisplayName is null || substitutedDisplayName == typeDisplayName
            ? null
            : new TypeSymbol(substitutedDisplayName, true);
    }

    private static int GetGenericDefinitionRichnessLocal(NamedTypeSymbol type) =>
        (type.GenericParameters?.Count ?? 0) * 100 +
        (type.Methods?.Count ?? 0) * 10 +
        (type.Properties?.Count ?? 0) * 10 +
        (type.InterfaceTypes?.Count ?? 0) +
        (type.BaseType is null ? 0 : 1);
}
