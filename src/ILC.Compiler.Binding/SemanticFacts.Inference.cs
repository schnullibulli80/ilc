namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static TypeSymbol InferExpressionType(
        ExpressionSyntax? expression,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol>? knownFields,
        IEnumerable<ConstantSymbol>? knownConstants,
        IEnumerable<PropertySymbol>? knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (expression is null)
        {
            return TypeSymbol.Integer;
        }

        return expression switch
        {
            LiteralExpressionSyntax literal => InferLiteralType(literal),
            SetLiteralExpressionSyntax setLiteral => InferSetLiteralType(setLiteral, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            RangeExpressionSyntax range => InferExpressionType(range.Start, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            ParenthesizedExpressionSyntax parenthesized => InferExpressionType(parenthesized.Expression, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MatchAndPatternSyntax andPattern => andPattern.Patterns.Count > 0
                ? InferExpressionType(andPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes)
                : TypeSymbol.Integer,
            MatchNotPatternSyntax notPattern => InferExpressionType(notPattern.Pattern, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MatchOrPatternSyntax orPattern => orPattern.Patterns.Count > 0
                ? InferExpressionType(orPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes)
                : TypeSymbol.Integer,
            MatchRelationalPatternSyntax relational => InferExpressionType(relational.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            ProjectorExpressionSyntax projector => ResolveProjectorType(
                        projector,
                        localTypes,
                        knownTypes ?? [],
                        knownMethods,
                        knownFields,
                        knownConstants,
                        knownProperties,
                        currentMethod)
                ?? new TypeSymbol(
                    GetProjectorTypeName(
                        GetProjectorSignature(
                            projector,
                            localTypes,
                            knownTypes ?? [],
                            knownMethods.ToArray(),
                            (knownFields ?? []).ToArray(),
                            (knownConstants ?? []).ToArray(),
                            (knownProperties ?? []).ToArray(),
                            currentMethod)),
                    true),
            NewExpressionSyntax newExpression => ResolveTypeReferenceInGenericContext(newExpression.TypeName.ToDisplayString(), currentMethod, knownTypes ?? [])
                ?? new TypeSymbol(newExpression.TypeName.ToDisplayString(), true),
            NewArrayExpressionSyntax newArray => new TypeSymbol(
                $"{newArray.ElementTypeName.ToDisplayString()}{GetArrayTypeSuffix(newArray.LengthExpressions)}",
                true),
            ArrayLengthExpressionSyntax => TypeSymbol.Integer,
            ElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            PostfixElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes).Type
                ?? TypeSymbol.Integer,
            NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType) => localType,
            NameExpressionSyntax name => ResolveName(name.Name, localTypes, knownTypes ?? [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            AssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var assignmentType) => assignmentType,
            AssignmentExpressionSyntax => TypeSymbol.Integer,
            CompoundAssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var compoundAssignmentType) => compoundAssignmentType,
            CompoundAssignmentExpressionSyntax => TypeSymbol.Integer,
            UnaryExpressionSyntax unary => unary.OperatorToken.Kind == SyntaxKind.NotKeyword
                ? InferExpressionType(unary.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes) == TypeSymbol.Boolean
                    ? TypeSymbol.Boolean
                    : TypeSymbol.Integer
                : TypeSymbol.Integer,
            BinaryExpressionSyntax binary => binary.OperatorToken.Kind is SyntaxKind.InKeyword or SyntaxKind.NotInKeyword
                ? TypeSymbol.Boolean
                : IsComparisonOperator(binary.OperatorToken.Kind)
                    ? TypeSymbol.Boolean
                    : InferBinaryExpressionType(binary, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            AsExpressionSyntax asExpression => ResolveTypeReferenceInGenericContext(asExpression.TypeName.ToDisplayString(), currentMethod, knownTypes ?? [])
                ?? new TypeSymbol(asExpression.TypeName.ToDisplayString(), true),
            TypeTestExpressionSyntax => TypeSymbol.Boolean,
            CallExpressionSyntax call => call.Target is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Receiver is NameExpressionSyntax receiverName &&
                knownMethods.FirstOrDefault(method =>
                    method.DeclaringTypeName == receiverName.Name.ToDisplayString() &&
                    method.Name == memberAccess.MemberName.Text &&
                    method.Parameters.Count == call.Arguments.Count &&
                    method.IsStatic) is { } staticMethod
                    ? staticMethod.ReturnType
                    : ResolveInvocation(call.Target, call.Arguments.Count, localTypes, knownTypes ?? [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod)?.Method.ReturnType ?? TypeSymbol.Integer,
            QueryExpressionSyntax query => InferQueryExpressionType(query, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes ?? []),
            LambdaExpressionSyntax lambda => lambda.SignatureKeyword.Kind == SyntaxKind.FunctionKeyword && lambda.ReturnType is not null
                ? ResolveTypeReferenceInGenericContext(lambda.ReturnType.ToDisplayString(), currentMethod, knownTypes ?? [])
                    ?? new TypeSymbol(lambda.ReturnType.ToDisplayString(), true)
                : TypeSymbol.Void,
            MatchExpressionSyntax matchExpression => matchExpression.Arms.Count > 0
                ? InferExpressionType(
                    matchExpression.Arms[0].Expression,
                    matchExpression.Arms[0].TypeName is not null &&
                    matchExpression.Arms[0].Identifier is not null &&
                    ResolveTypeReference(matchExpression.Arms[0].TypeName!.ToDisplayString(), knownTypes ?? []) is { } matchArmType
                        ? new Dictionary<string, TypeSymbol>(localTypes, StringComparer.Ordinal)
                        {
                            [matchExpression.Arms[0].Identifier!.Text] = matchArmType
                        }
                        : localTypes,
                    knownMethods,
                    knownFields ?? [],
                    knownConstants ?? [],
                    knownProperties ?? [],
                    currentMethod,
                    knownTypes)
                : TypeSymbol.Integer,
            _ => TypeSymbol.Integer
        };
    }

    private static TypeSymbol InferQueryExpressionType(
        QueryExpressionSyntax query,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (TryTranslateQueryExpression(
                query,
                localTypes,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                out var translated))
        {
            return InferExpressionType(translated, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        }

        return TypeSymbol.Integer;
    }

    private static bool TryGetAssignmentTargetType(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        out TypeSymbol type)
    {
        switch (target)
        {
            case NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType):
                type = localType;
                return true;
            case NameExpressionSyntax name:
                type = ResolvePropertyReference(name.Name, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                    ?? ResolveConstantReference(name.Name, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                    ?? ResolveFieldReference(name.Name, knownFields, currentMethod)?.Type
                    ?? TypeSymbol.Integer;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                type = ResolveMemberAccess(memberAccess, localTypes, [], knownFields, knownConstants, knownProperties, currentMethod).Type
                    ?? TypeSymbol.Integer;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownFields, knownConstants, knownProperties, currentMethod);
                return true;
            case PostfixElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, [], knownFields, knownConstants, knownProperties, currentMethod);
                return true;
            default:
                type = TypeSymbol.Integer;
                return false;
        }
    }

    private static TypeSymbol GetIndexedElementType(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var arrayType = localTypes.TryGetValue(target.ToDisplayString(), out var localType)
            ? localType
            : ResolvePropertyReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Type
                ?? ResolveConstantReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Type
                ?? ResolveFieldReference(target, knownFields, currentMethod)?.Type
                ?? new TypeSymbol("Integer[]", true);

        var indexer = ResolveIndexerReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            return indexer.Type;
        }

        if (IsSliceAccess(indexExpressions))
        {
            return arrayType;
        }

        if (arrayType == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(arrayType))
        {
            return TypeSymbol.Integer;
        }

        var elementTypeName = GetArrayElementTypeName(arrayType.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

    private static TypeSymbol GetIndexedElementType(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target is MemberAccessExpressionSyntax memberAccess &&
            ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property is { IsIndexer: true } directIndexer)
        {
            return directIndexer.Type;
        }

        var targetType = InferExpressionType(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var indexer = ResolveIndexerReference(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            return indexer.Type;
        }

        if (IsSliceAccess(indexExpressions))
        {
            return targetType;
        }

        if (targetType == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(targetType))
        {
            return TypeSymbol.Integer;
        }

        var elementTypeName = GetArrayElementTypeName(targetType.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

}
