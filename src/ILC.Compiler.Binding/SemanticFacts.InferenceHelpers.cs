namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    private static TypeSymbol InferLiteralType(LiteralExpressionSyntax literal) =>
        literal.LiteralToken.Kind switch
        {
            SyntaxKind.TrueKeyword => TypeSymbol.Boolean,
            SyntaxKind.FalseKeyword => TypeSymbol.Boolean,
            SyntaxKind.StringToken => TypeSymbol.String,
            SyntaxKind.NilKeyword => TypeSymbol.Nil,
            _ => TypeSymbol.Integer
        };

    private static TypeSymbol InferSetLiteralType(
        SetLiteralExpressionSyntax setLiteral,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes)
    {
        if (setLiteral.Elements.Count == 0)
        {
            return new TypeSymbol("set", false);
        }

        var elementType = InferExpressionType(setLiteral.Elements[0], localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        return IsEnumType(elementType)
            ? CreateSetType(elementType)
            : TypeSymbol.Unknown;
    }

    private static TypeSymbol InferBinaryExpressionType(
        BinaryExpressionSyntax binary,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes)
    {
        var leftType = InferExpressionType(binary.Left, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var rightType = InferExpressionType(binary.Right, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (binary.OperatorToken.Kind == SyntaxKind.PlusToken &&
            leftType == TypeSymbol.String &&
            rightType == TypeSymbol.String)
        {
            return TypeSymbol.String;
        }

        if (binary.OperatorToken.Kind == SyntaxKind.NullCoalescingToken)
        {
            if (leftType.IsReferenceType && leftType != TypeSymbol.Nil)
            {
                return leftType;
            }

            if (rightType.IsReferenceType && rightType != TypeSymbol.Nil)
            {
                return rightType;
            }

            return leftType;
        }

        if (binary.OperatorToken.Kind is SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword)
        {
            return IsBuiltInIntegerType(leftType) ? leftType : TypeSymbol.Unknown;
        }

        if (binary.OperatorToken.Kind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword &&
            leftType == TypeSymbol.Boolean &&
            rightType == TypeSymbol.Boolean)
        {
            return TypeSymbol.Boolean;
        }

        if (IsSetType(leftType) && IsSetType(rightType))
        {
            return leftType;
        }

        if (leftType == TypeSymbol.Unknown)
        {
            return rightType == TypeSymbol.Unknown ? TypeSymbol.Unknown : rightType;
        }

        if (rightType == TypeSymbol.Unknown)
        {
            return leftType;
        }

        return TypeSymbol.Integer;
    }

    private static bool IsComparisonOperator(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken;

    private static (string? DeclaringTypeName, string MethodName) ParseQualifiedMethodTarget(string name)
    {
        var lastSeparator = name.LastIndexOf('.');
        if (lastSeparator < 0)
        {
            return (null, name);
        }

        var methodName = name[(lastSeparator + 1)..];
        var qualifier = name[..lastSeparator];
        var typeNameSeparator = qualifier.LastIndexOf('.');
        var declaringTypeName = typeNameSeparator >= 0
            ? qualifier[(typeNameSeparator + 1)..]
            : qualifier;

        return (declaringTypeName, methodName);
    }

}
