namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static bool IsArrayType(TypeSymbol type)
    {
        var openBracketIndex = type.Name.LastIndexOf('[');
        var rankSpecifier = openBracketIndex > 0 && type.Name.EndsWith("]", StringComparison.Ordinal)
            ? type.Name[(openBracketIndex + 1)..^1]
            : null;
        return openBracketIndex > 0 &&
            type.Name.EndsWith("]", StringComparison.Ordinal) &&
            rankSpecifier is not null &&
            (rankSpecifier.Length == 0 || rankSpecifier.All(character => char.IsDigit(character) || character == ','));
    }

    public static bool IsIndexableType(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
    }

    public static bool IsSliceAccess(IReadOnlyList<ExpressionSyntax> indexExpressions) =>
        indexExpressions.Count == 1 && indexExpressions[0] is RangeExpressionSyntax;

    private static bool IsEnumerableInterface(NamedTypeSymbol type) =>
        type.IsInterface &&
        (type.Name == "IEnumerable" || type.Name.StartsWith("IEnumerable<", StringComparison.Ordinal));

    public static bool IsSetType(TypeSymbol type) =>
        type.Name.StartsWith("set of ", StringComparison.Ordinal);

    public static TypeSymbol? GetSetElementType(TypeSymbol type)
    {
        if (!IsSetType(type))
        {
            return null;
        }

        var elementTypeName = type.Name["set of ".Length..];
        return ResolveTypeReference(elementTypeName, []) ?? new TypeSymbol(elementTypeName, false);
    }

    public static TypeSymbol CreateSetType(TypeSymbol elementType) =>
        new($"set of {elementType.Name}", false);

    public static bool HasLengthProperty(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
    }

    public static bool IsEnumType(TypeSymbol type)
    {
        return !type.IsReferenceType &&
            type != TypeSymbol.Void &&
            type != TypeSymbol.Boolean &&
            type != TypeSymbol.Char &&
            !IsBuiltInIntegerType(type);
    }

    public static bool IsBuiltInType(TypeSymbol type) =>
        TypeSymbol.BuiltInTypes.Any(candidate => candidate == type);

    public static bool IsBuiltInIntegerType(TypeSymbol type) =>
        type == TypeSymbol.Integer ||
        type == TypeSymbol.UInt128 ||
        type == TypeSymbol.UInt256 ||
        type == TypeSymbol.UInt512 ||
        type == TypeSymbol.UInt1024 ||
        type == TypeSymbol.UInt2048;

    public static int? GetIntegerBitWidth(TypeSymbol type) =>
        type.Name switch
        {
            "Integer" => 32,
            "UInt128" => 128,
            "UInt256" => 256,
            "UInt512" => 512,
            "UInt1024" => 1024,
            "UInt2048" => 2048,
            _ => null
        };

    public static TypeSymbol? GetElementType(TypeSymbol type)
    {
        if (type == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(type))
        {
            return null;
        }

        var elementTypeName = GetArrayElementTypeName(type.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

    public static EnumerablePatternResolution? ResolveEnumerablePattern(TypeSymbol collectionType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        NamedTypeSymbol? TryBuildPattern(NamedTypeSymbol enumerableType)
        {
            return IsEnumerableInterface(enumerableType) ? enumerableType : null;
        }

        EnumerablePatternResolution? CreatePattern(NamedTypeSymbol enumerableType)
        {
            var getEnumeratorMethod = enumerableType.Methods.FirstOrDefault(method =>
                method.Name == "GetEnumerator" &&
                method.Parameters.Count == 0 &&
                !method.IsConstructor);
            if (getEnumeratorMethod is null)
            {
                return null;
            }

            var enumeratorType = ResolveNamedType(getEnumeratorMethod.ReturnType, knownTypes) ?? getEnumeratorMethod.ReturnType;
            var resolvedEnumeratorType = ResolveNamedType(enumeratorType, knownTypes);
            if (resolvedEnumeratorType is null)
            {
                return null;
            }

            var moveNextMethod = GetReceiverTypeHierarchy(resolvedEnumeratorType, knownTypes)
                .SelectMany(type => type.Methods)
                .FirstOrDefault(method =>
                    method.Name == "MoveNext" &&
                    method.Parameters.Count == 0 &&
                    method.ReturnType == TypeSymbol.Boolean);
            var currentProperty = GetReceiverTypeHierarchy(resolvedEnumeratorType, knownTypes)
                .SelectMany(type => type.Properties)
                .FirstOrDefault(property =>
                    property.Name == "Current" &&
                    property.GetterMethod is not null &&
                    property.IndexParameter is null);
            if (moveNextMethod is null || currentProperty?.GetterMethod is null)
            {
                return null;
            }

            return new EnumerablePatternResolution(
                currentProperty.Type,
                enumeratorType,
                getEnumeratorMethod,
                moveNextMethod,
                currentProperty.GetterMethod);
        }

        var resolvedCollectionType = ResolveNamedType(collectionType, knownTypes);
        if (resolvedCollectionType is null)
        {
            return null;
        }

        foreach (var candidate in GetReceiverTypeHierarchy(resolvedCollectionType, knownTypes))
        {
            if (TryBuildPattern(candidate) is { } directEnumerable &&
                CreatePattern(directEnumerable) is { } directPattern)
            {
                return directPattern;
            }

            foreach (var interfaceType in candidate.InterfaceTypes)
            {
                foreach (var inheritedInterface in GetInterfaceHierarchy(interfaceType, knownTypes))
                {
                    if (TryBuildPattern(inheritedInterface) is { } enumerableInterface &&
                        CreatePattern(enumerableInterface) is { } pattern)
                    {
                        return pattern;
                    }
                }
            }
        }

        return null;
    }

    public static int GetArrayRank(TypeSymbol type)
    {
        if (!IsArrayType(type))
        {
            return 0;
        }

        var dimensions = GetArrayDimensions(type);
        return dimensions.Count == 0 ? 1 : dimensions.Count;
    }

    public static IReadOnlyList<int?> GetArrayDimensions(TypeSymbol type)
    {
        if (!IsArrayType(type))
        {
            return [];
        }

        var openBracketIndex = type.Name.LastIndexOf('[');
        var content = type.Name[(openBracketIndex + 1)..^1];
        if (content.Length == 0)
        {
            return [null];
        }

        return content.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? (int?)value : null)
            .ToArray();
    }

    private static string GetArrayElementTypeName(string arrayTypeName)
    {
        var openBracketIndex = arrayTypeName.LastIndexOf('[');
        return openBracketIndex > 0 ? arrayTypeName[..openBracketIndex] : arrayTypeName;
    }

    private static string GetArrayTypeSuffix(IReadOnlyList<ExpressionSyntax> lengthExpressions)
    {
        var literalTexts = lengthExpressions.Select(GetArrayLengthLiteralText).ToArray();
        if (literalTexts.All(text => text is not null))
        {
            return $"[{string.Join(",", literalTexts!)}]";
        }

        return lengthExpressions.Count == 1
            ? "[]"
            : $"[{new string(',', lengthExpressions.Count - 1)}]";
    }

    private static string? GetArrayLengthLiteralText(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && literal.LiteralToken.Kind == SyntaxKind.NumberToken
            ? literal.LiteralToken.Text
            : null;
    }

}
