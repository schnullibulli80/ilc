namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static InvocationResolution? ResolveInvocation(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var directResolution = target switch
        {
            NameExpressionSyntax name => ResolveInvocation(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            _ => null
        };
        if (directResolution is not null)
        {
            return directResolution;
        }

        return TryResolveDelegateInvocation(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    public static InvocationResolution? ResolveInvocationIgnoringAccess(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var directResolution = target switch
        {
            NameExpressionSyntax name => ResolveInvocationIgnoringAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, ignoreAccess: true),
            _ => null
        };
        if (directResolution is not null)
        {
            return directResolution;
        }

        return TryResolveDelegateInvocation(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    private static InvocationResolution? TryResolveDelegateInvocation(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var targetType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (ResolveNamedType(targetType, knownTypes) is not NamedTypeSymbol { IsDelegate: true } delegateType)
        {
            return null;
        }

        var invokeMethod = delegateType.Methods.FirstOrDefault(method =>
            method.Name == "Invoke" &&
            !method.IsStatic &&
            SupportsArgumentCount(method, argumentCount));
        return invokeMethod is null ? null : new InvocationResolution(invokeMethod, delegateType, true);
    }

    public static MemberResolution ResolveMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var displayName = $"{GetExpressionDisplayName(memberAccess.Receiver)}.{memberAccess.MemberName.Text}";
        if (memberAccess.MemberName.Text == "Length" && HasLengthProperty(receiverType))
        {
            return new MemberResolution(displayName, TypeSymbol.Integer);
        }

        var typeHierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes ?? []);
        var property = typeHierarchy
            .SelectMany(knownType => knownType.Properties
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownProperties.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (property is not null)
        {
            return new MemberResolution(displayName, property.Type, property.ReadField, property, null);
        }

        var field = typeHierarchy
            .SelectMany(knownType => knownType.Fields
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownFields.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (field is not null)
        {
            return new MemberResolution(displayName, field.Type, field);
        }

        var constant = typeHierarchy
            .SelectMany(knownType => knownConstants.Where(candidate =>
                candidate.IsStatic &&
                candidate.DeclaringTypeName == knownType.Name &&
                candidate.Name == memberAccess.MemberName.Text))
            .FirstOrDefault();
        if (constant is not null)
        {
            return new MemberResolution(displayName, constant.Type, null, null, null, constant);
        }

        var method = typeHierarchy
            .SelectMany(knownType => knownType.Methods
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownMethods.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (method is not null)
        {
            return new MemberResolution(displayName, method.ReturnType, Method: method);
        }

        return new MemberResolution(displayName);
    }

    public static string GetExpressionDisplayName(ExpressionSyntax expression) =>
        expression switch
        {
            NameExpressionSyntax name => name.Name.ToDisplayString(),
            ParenthesizedExpressionSyntax parenthesized => $"({GetExpressionDisplayName(parenthesized.Expression)})",
            RangeExpressionSyntax range => $"{GetExpressionDisplayName(range.Start)}..{GetExpressionDisplayName(range.End)}",
            ElementAccessExpressionSyntax element => $"{element.Target.ToDisplayString()}[...]",
            PostfixElementAccessExpressionSyntax element => $"{GetExpressionDisplayName(element.Target)}[...]",
            ArrayLengthExpressionSyntax length => $"{length.Target.ToDisplayString()}.Length",
            MemberAccessExpressionSyntax member => $"{GetExpressionDisplayName(member.Receiver)}.{member.MemberName.Text}",
            AsExpressionSyntax asExpression => $"{GetExpressionDisplayName(asExpression.Expression)} as {asExpression.TypeName.ToDisplayString()}",
            TypeTestExpressionSyntax typeTest => $"{GetExpressionDisplayName(typeTest.Expression)} is {typeTest.TypeName.ToDisplayString()}",
            CallExpressionSyntax call => $"{GetExpressionDisplayName(call.Target)}(...)",
            QueryExpressionSyntax => "query",
            MatchExpressionSyntax => "match",
            MatchNotPatternSyntax notPattern => $"not {GetExpressionDisplayName(notPattern.Pattern)}",
            MatchOrPatternSyntax orPattern => string.Join(" or ", orPattern.Patterns.Select(GetExpressionDisplayName)),
            MatchAndPatternSyntax andPattern => string.Join(" and ", andPattern.Patterns.Select(GetExpressionDisplayName)),
            MatchRelationalPatternSyntax relational => $"{relational.OperatorToken.Text}{GetExpressionDisplayName(relational.Operand)}",
            _ => expression.Kind.ToString()
        };

}
