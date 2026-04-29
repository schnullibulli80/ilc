namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    private static FieldSymbol? ResolveFieldReference(
        QualifiedNameSyntax name,
        IEnumerable<FieldSymbol> knownFields,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var fields = knownFields.ToArray();
        var locals = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        if (currentMethod?.DeclaringTypeName is not null)
        {
            foreach (var parameter in currentMethod.Parameters)
            {
                locals[parameter.Name] = parameter.Type;
            }

            if (!currentMethod.IsStatic)
            {
                locals["self"] = new TypeSymbol(currentMethod.DeclaringTypeName, true);
            }
        }

        var valueReceiverType = TryResolveValueReceiverType(name, locals, fields, [], [], currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceField = GetTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => fields.Where(field =>
                    !field.IsStatic &&
                    field.DeclaringTypeName == knownType.Name &&
                    field.Name == name.Parts[^1].Text))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return instanceField;
            }
        }

        var displayName = name.ToDisplayString();
        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            return fields.FirstOrDefault(field =>
                !field.IsStatic &&
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == name.Parts[1].Text);
        }

        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var fieldName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return fields.FirstOrDefault(field => field.IsStatic && field.DeclaringTypeName == declaringTypeName && field.Name == fieldName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeField = fields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName &&
                (field.IsStatic || !currentMethod.IsStatic));
            if (sameTypeField is not null)
            {
                return sameTypeField;
            }
        }

        return null;
    }

    internal static FieldSymbol? ResolveFieldIgnoringAccess(
        QualifiedNameSyntax name,
        IEnumerable<FieldSymbol> knownFields,
        MethodSymbol? currentMethod)
    {
        var displayName = name.ToDisplayString();
        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null)
        {
            return knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == name.Parts[1].Text);
        }

        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var fieldName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return knownFields.FirstOrDefault(field => field.DeclaringTypeName == declaringTypeName && field.Name == fieldName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            return knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName);
        }

        return null;
    }

    public static PropertySymbol? ResolvePropertyReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var properties = knownProperties.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, properties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            return GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Properties
                    .Where(property =>
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)
                    .Concat(properties.Where(property =>
                        property.DeclaringTypeName == knownType.Name &&
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)))
                .FirstOrDefault();
        }

        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            return properties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == name.Parts[^1].Text &&
                !property.IsStatic);
        }

        var displayName = name.ToDisplayString();
        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var propertyName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return properties.FirstOrDefault(property =>
                property.IsStatic &&
                property.DeclaringTypeName == declaringTypeName &&
                property.Name == propertyName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            return properties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == displayName &&
                (property.IsStatic || !currentMethod.IsStatic));
        }

        return null;
    }

    public static ConstantSymbol? ResolveConstantReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var constants = knownConstants.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceConstant = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => constants.Where(constant =>
                    constant.DeclaringTypeName == knownType.Name &&
                    constant.Name == name.Parts[^1].Text &&
                    constant.IsStatic))
                .FirstOrDefault();
            if (instanceConstant is not null)
            {
                return instanceConstant;
            }
        }

        var displayName = name.ToDisplayString();
        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var constantName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return constants.FirstOrDefault(constant =>
                constant.IsStatic &&
                constant.DeclaringTypeName == declaringTypeName &&
                constant.Name == constantName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeConstant = constants.FirstOrDefault(constant =>
                constant.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                constant.Name == displayName &&
                constant.IsStatic);
            if (sameTypeConstant is not null)
            {
                return sameTypeConstant;
            }
        }

        return constants.FirstOrDefault(constant =>
            constant.DeclaringTypeName is null &&
            constant.Name == displayName);
    }

    public static bool IsConstantExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null) =>
        expression is LiteralExpressionSyntax ||
        expression is NameExpressionSyntax name && ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is not null ||
        expression is MemberAccessExpressionSyntax member && ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Constant is not null;

    public static bool TryGetInt32LiteralValue(SyntaxToken token, out int value)
    {
        switch (token.Value)
        {
            case int parsedInt:
                value = parsedInt;
                return true;
            case NumericLiteralValue numericLiteral when numericLiteral.Int32Value is int literalInt:
                value = literalInt;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public static object? GetLiteralValue(SyntaxToken token) =>
        token.Kind switch
        {
            SyntaxKind.TrueKeyword => 1,
            SyntaxKind.FalseKeyword => 0,
            SyntaxKind.NumberToken when token.Value is NumericLiteralValue numericLiteral => numericLiteral.Int32Value,
            _ => token.Value
        };

    public static object? GetConstantValue(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null) =>
        expression switch
        {
            LiteralExpressionSyntax literal => GetLiteralValue(literal.LiteralToken) ?? 0,
            NameExpressionSyntax name => ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Value,
            MemberAccessExpressionSyntax member => ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Constant?.Value,
            ParenthesizedExpressionSyntax parenthesized => GetConstantValue(parenthesized.Expression, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.NotKeyword => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is int operand
                ? InferExpressionType(unary.Operand, locals, [], knownFields, knownConstants, knownProperties, currentMethod) == TypeSymbol.Boolean
                    ? operand == 0 ? 1 : 0
                    : ~operand
                : null,
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.MinusToken => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod) is int negatedOperand
                ? -negatedOperand
                : null,
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.PlusToken => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod) is int positiveOperand
                ? positiveOperand
                : null,
            _ => null
        };

    public static TypeSymbol? TryResolveValueReceiverType(
        QualifiedNameSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var qualifier = GetQualifier(memberAccess);
        if (qualifier is null)
        {
            return currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic
                ? new TypeSymbol(currentMethod.DeclaringTypeName, true)
                : null;
        }

        return TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
    }

    public static TypeSymbol? TryResolveValueReferenceType(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var displayName = name.ToDisplayString();
        if (locals.TryGetValue(displayName, out var localType))
        {
            return localType;
        }

        if (name.Parts.Count == 1 &&
            currentMethod?.DeclaringTypeName is not null &&
            (displayName == "self" || !locals.ContainsKey(displayName)))
        {
            var sameTypeProperty = knownProperties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == displayName &&
                (property.IsStatic || !currentMethod.IsStatic));
            if (sameTypeProperty is not null)
            {
                return sameTypeProperty.Type;
            }

            var sameTypeField = knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName &&
                (field.IsStatic || !currentMethod.IsStatic));
            if (sameTypeField is not null)
            {
                return sameTypeField.Type;
            }

            var sameTypeConstant = knownConstants.FirstOrDefault(constant =>
                constant.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                constant.Name == displayName &&
                constant.IsStatic);
            if (sameTypeConstant is not null)
            {
                return sameTypeConstant.Type;
            }

            if (displayName == "self" && !currentMethod.IsStatic)
            {
                return new TypeSymbol(currentMethod.DeclaringTypeName, true);
            }
        }

        if (name.Parts.Count < 2)
        {
            return null;
        }

        var qualifier = GetQualifier(name);
        if (qualifier is null)
        {
            return null;
        }

        var valueReceiverType = TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceProperty = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Properties
                    .Where(property =>
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)
                    .Concat(knownProperties.Where(property =>
                        property.DeclaringTypeName == knownType.Name &&
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)))
                .FirstOrDefault();
            if (instanceProperty is not null)
            {
                return instanceProperty.Type;
            }

            var instanceField = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Fields
                    .Where(field =>
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)
                    .Concat(knownFields.Where(field =>
                        field.DeclaringTypeName == knownType.Name &&
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return instanceField.Type;
            }
        }

        var qualifierTypeName = qualifier.ToDisplayString();
        var declaringTypeName = qualifierTypeName.Contains('.')
            ? qualifierTypeName[(qualifierTypeName.LastIndexOf('.') + 1)..]
            : qualifierTypeName;

        var staticProperty = knownProperties.FirstOrDefault(property =>
            property.IsStatic &&
            property.DeclaringTypeName == declaringTypeName &&
            property.Name == name.Parts[^1].Text);
        if (staticProperty is not null)
        {
            return staticProperty.Type;
        }

        var staticField = knownFields.FirstOrDefault(field =>
            field.IsStatic &&
            field.DeclaringTypeName == declaringTypeName &&
            field.Name == name.Parts[^1].Text);
        return staticField?.Type;
    }

    private static QualifiedNameSyntax? GetQualifier(QualifiedNameSyntax name) =>
        name.Parts.Count > 1
            ? new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray())
            : null;

    private static MethodSymbol? ResolveMethodGroup(
        string name,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod)
    {
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        var candidates = knownMethods
            .Where(method =>
                method.Name == qualifiedTarget.MethodName &&
                (qualifiedTarget.DeclaringTypeName is null || method.DeclaringTypeName == qualifiedTarget.DeclaringTypeName))
            .ToArray();

        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            return candidates.FirstOrDefault();
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName);
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }
        }

        return candidates.FirstOrDefault();
    }
}
