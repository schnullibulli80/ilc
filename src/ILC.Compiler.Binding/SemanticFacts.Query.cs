namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public static partial class SemanticFacts
{
    public static bool TryTranslateQueryExpression(
        QueryExpressionSyntax query,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        out ExpressionSyntax translated)
    {
        translated = query;

        var sourceType = InferExpressionType(query.SourceExpression, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var enumerablePattern = ResolveEnumerablePattern(sourceType, knownTypes);
        if (enumerablePattern is null)
        {
            return false;
        }

        var rangeVariableType = enumerablePattern.ElementType;
        var queryLocals = new Dictionary<string, TypeSymbol>(localTypes, NameComparer)
        {
            [query.Identifier.Text] = rangeVariableType
        };
        var joinSourceExpression = query.JoinSourceExpression;
        var joinLeftExpression = query.JoinLeftExpression;
        var joinRightExpression = query.JoinRightExpression;
        var joinIntoIdentifier = query.JoinIntoIdentifier;
        TypeSymbol? joinRangeVariableType = null;
        TypeSymbol? joinKeyType = null;
        TypeSymbol? groupedJoinRangeVariableType = null;
        if (joinSourceExpression is not null && query.JoinIdentifier is not null)
        {
            joinRangeVariableType = ResolveEnumerablePattern(
                InferExpressionType(joinSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                knownTypes)?.ElementType;
            if (joinRangeVariableType is null)
            {
                return false;
            }

            queryLocals[query.JoinIdentifier.Text] = joinRangeVariableType;
            if (joinIntoIdentifier is not null)
            {
                groupedJoinRangeVariableType =
                    ResolveTypeReference($"IEnumerable<{joinRangeVariableType.Name}>", knownTypes)
                    ?? new TypeSymbol($"IEnumerable<{joinRangeVariableType.Name}>", true);
                queryLocals[joinIntoIdentifier.Text] = groupedJoinRangeVariableType;
            }
            if (joinLeftExpression is null || joinRightExpression is null)
            {
                return false;
            }

            joinKeyType = InferExpressionType(joinLeftExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            var rightJoinKeyType = InferExpressionType(joinRightExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            if (joinKeyType.Name != rightJoinKeyType.Name ||
                (joinKeyType != TypeSymbol.Integer && joinKeyType != TypeSymbol.String))
            {
                return false;
            }
        }

        var secondSourceExpression = query.SecondSourceExpression;
        TypeSymbol? secondRangeVariableType = null;
        if (secondSourceExpression is not null && query.SecondIdentifier is not null)
        {
            secondRangeVariableType = ResolveEnumerablePattern(
                InferExpressionType(secondSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                knownTypes)?.ElementType;
            if (secondRangeVariableType is null)
            {
                return false;
            }

            queryLocals[query.SecondIdentifier.Text] = secondRangeVariableType;
        }

        var letExpression = query.LetExpression;
        if (letExpression is not null && query.LetIdentifier is not null)
        {
            queryLocals[query.LetIdentifier.Text] = InferExpressionType(letExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        }

        var predicateExpression = query.PredicateExpression;
        var orderByExpression = query.OrderByExpression;
        var thenByExpression = query.ThenByExpression;
        var selectExpression = query.SelectExpression;
        var groupExpression = query.GroupExpression;
        var groupByExpression = query.GroupByExpression;
        var continuationPredicateExpression = query.ContinuationPredicateExpression;
        var continuationOrderByExpression = query.ContinuationOrderByExpression;
        var continuationThenByExpression = query.ContinuationThenByExpression;
        var continuationSelectExpression = query.ContinuationSelectExpression;
        var continuationLetExpression = query.ContinuationLetExpression;
        var takeExpression = query.TakeExpression;
        var skipExpression = query.SkipExpression;
        if (letExpression is not null && query.LetIdentifier is not null)
        {
            predicateExpression = predicateExpression is null ? null : RewriteQueryLetReference(predicateExpression, query.LetIdentifier.Text, letExpression);
            orderByExpression = orderByExpression is null ? null : RewriteQueryLetReference(orderByExpression, query.LetIdentifier.Text, letExpression);
            thenByExpression = thenByExpression is null ? null : RewriteQueryLetReference(thenByExpression, query.LetIdentifier.Text, letExpression);
            groupExpression = groupExpression is null ? null : RewriteQueryLetReference(groupExpression, query.LetIdentifier.Text, letExpression);
            groupByExpression = groupByExpression is null ? null : RewriteQueryLetReference(groupByExpression, query.LetIdentifier.Text, letExpression);
            selectExpression = RewriteQueryLetReference(selectExpression, query.LetIdentifier.Text, letExpression);
            continuationPredicateExpression = continuationPredicateExpression is null ? null : RewriteQueryLetReference(continuationPredicateExpression, query.LetIdentifier.Text, letExpression);
            continuationOrderByExpression = continuationOrderByExpression is null ? null : RewriteQueryLetReference(continuationOrderByExpression, query.LetIdentifier.Text, letExpression);
            continuationThenByExpression = continuationThenByExpression is null ? null : RewriteQueryLetReference(continuationThenByExpression, query.LetIdentifier.Text, letExpression);
            continuationLetExpression = continuationLetExpression is null ? null : RewriteQueryLetReference(continuationLetExpression, query.LetIdentifier.Text, letExpression);
            continuationSelectExpression = continuationSelectExpression is null ? null : RewriteQueryLetReference(continuationSelectExpression, query.LetIdentifier.Text, letExpression);
            takeExpression = takeExpression is null ? null : RewriteQueryLetReference(takeExpression, query.LetIdentifier.Text, letExpression);
            skipExpression = skipExpression is null ? null : RewriteQueryLetReference(skipExpression, query.LetIdentifier.Text, letExpression);
        }

        var projectedType = groupExpression is not null
            ? InferExpressionType(groupExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : InferExpressionType(selectExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var groupKeyType = groupByExpression is not null
            ? InferExpressionType(groupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : null;
        var groupedResultType = groupExpression is not null && groupKeyType is not null
            ? ResolveTypeReference($"Grouping<{groupKeyType.Name}, {projectedType.Name}>", knownTypes)
                ?? new TypeSymbol($"Grouping<{groupKeyType.Name}, {projectedType.Name}>", true)
            : null;
        var continuationRangeType = groupedResultType ?? projectedType;
        var continuationLocals = query.IntoIdentifier is not null
            ? new Dictionary<string, TypeSymbol>(localTypes, NameComparer)
                {
                    [query.IntoIdentifier.Text] = continuationRangeType
                }
            : null;

        if (continuationLocals is not null && continuationLetExpression is not null && query.ContinuationLetIdentifier is not null)
        {
            continuationLocals[query.ContinuationLetIdentifier.Text] =
                InferExpressionType(
                    continuationLetExpression,
                    continuationLocals,
                    knownMethods,
                    knownFields,
                    knownConstants,
                    knownProperties,
                    currentMethod,
                    knownTypes);
        }

        var finalProjectedType = continuationSelectExpression is not null && continuationLocals is not null
            ? InferExpressionType(
                continuationSelectExpression,
                continuationLocals,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                knownTypes)
            : groupedResultType ?? projectedType;

        var currentSource = query.SourceExpression;
        var currentRangeVariableName = query.Identifier.Text;
        if (joinSourceExpression is not null &&
            joinLeftExpression is not null &&
            joinRightExpression is not null &&
            joinRangeVariableType is not null &&
            joinKeyType is not null &&
            query.JoinIdentifier is not null &&
            joinIntoIdentifier is not null &&
            groupedJoinRangeVariableType is not null)
        {
            currentSource = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {joinRangeVariableType.Name}, {groupedJoinRangeVariableType.Name}>",
                "GroupJoin",
                [
                    currentSource,
                    joinSourceExpression,
                    CreateLambda(
                        "function",
                        [CreateParameter(query.Identifier.Text, rangeVariableType.Name)],
                        joinKeyType.Name,
                        joinLeftExpression),
                    CreateLambda(
                        "function",
                        [CreateParameter(query.JoinIdentifier.Text, joinRangeVariableType.Name)],
                        joinKeyType.Name,
                        joinRightExpression),
                    CreateLambda(
                        "function",
                        [
                            CreateParameter(query.Identifier.Text, rangeVariableType.Name),
                            CreateParameter(joinIntoIdentifier.Text, groupedJoinRangeVariableType.Name)
                        ],
                        groupedJoinRangeVariableType.Name,
                        CreateNameExpression(joinIntoIdentifier.Text))
                ]);
            rangeVariableType = groupedJoinRangeVariableType;
            currentRangeVariableName = joinIntoIdentifier.Text;
        }
        else if (joinSourceExpression is not null &&
            joinLeftExpression is not null &&
            joinRightExpression is not null &&
            joinRangeVariableType is not null &&
            joinKeyType is not null &&
            query.JoinIdentifier is not null)
        {
            currentSource = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {joinRangeVariableType.Name}, {joinRangeVariableType.Name}>",
                "Join",
                [
                    currentSource,
                    joinSourceExpression,
                    CreateLambda(
                        "function",
                        [CreateParameter(query.Identifier.Text, rangeVariableType.Name)],
                        joinKeyType.Name,
                        joinLeftExpression),
                    CreateLambda(
                        "function",
                        [CreateParameter(query.JoinIdentifier.Text, joinRangeVariableType.Name)],
                        joinKeyType.Name,
                        joinRightExpression),
                    CreateLambda(
                        "function",
                        [
                            CreateParameter(query.Identifier.Text, rangeVariableType.Name),
                            CreateParameter(query.JoinIdentifier.Text, joinRangeVariableType.Name)
                        ],
                        joinRangeVariableType.Name,
                        CreateNameExpression(query.JoinIdentifier.Text))
                ]);
            rangeVariableType = joinRangeVariableType;
            currentRangeVariableName = query.JoinIdentifier.Text;
        }
        else if (secondSourceExpression is not null &&
            secondRangeVariableType is not null &&
            query.SecondIdentifier is not null)
        {
            currentSource = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {secondRangeVariableType.Name}, {secondRangeVariableType.Name}>",
                "SelectMany",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(query.Identifier.Text, rangeVariableType.Name)],
                        $"IEnumerable<{secondRangeVariableType.Name}>",
                        secondSourceExpression),
                    CreateLambda(
                        "function",
                        [
                            CreateParameter(query.Identifier.Text, rangeVariableType.Name),
                            CreateParameter(query.SecondIdentifier.Text, secondRangeVariableType.Name)
                        ],
                        secondRangeVariableType.Name,
                        CreateNameExpression(query.SecondIdentifier.Text))
                ]);
            rangeVariableType = secondRangeVariableType;
            currentRangeVariableName = query.SecondIdentifier.Text;
        }

        if (predicateExpression is not null)
        {
            currentSource = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}>",
                "Where",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        "Boolean",
                        predicateExpression)
                ]);
        }

        if (orderByExpression is not null)
        {
            if (thenByExpression is not null)
            {
                currentSource = CreateEnumerableCall(
                    $"Enumerable<{rangeVariableType.Name}>",
                    query.ThenByDescendingKeyword is null ? "OrderBy" : "OrderByDescending",
                    [
                        currentSource,
                        CreateLambda(
                            "function",
                            [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                            "Integer",
                            thenByExpression)
                    ]);
            }

            currentSource = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}>",
                query.DescendingKeyword is null ? "OrderBy" : "OrderByDescending",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        "Integer",
                        orderByExpression)
                ]);
        }

        if (groupExpression is not null && groupByExpression is not null && groupKeyType is not null)
        {
            translated = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {groupKeyType.Name}, {projectedType.Name}>",
                "GroupBy",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        groupKeyType.Name,
                        groupByExpression),
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        projectedType.Name,
                        groupExpression)
                ]);
        }
        else
        {
            translated = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {projectedType.Name}>",
                "Select",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        projectedType.Name,
                        selectExpression)
                ]);
        }

        if (query.IntoIdentifier is not null && continuationSelectExpression is not null)
        {
            currentSource = translated;
            rangeVariableType = continuationRangeType;
            currentRangeVariableName = query.IntoIdentifier.Text;

            if (continuationLetExpression is not null && query.ContinuationLetIdentifier is not null)
            {
                continuationPredicateExpression = continuationPredicateExpression is null
                    ? null
                    : RewriteQueryLetReference(continuationPredicateExpression, query.ContinuationLetIdentifier.Text, continuationLetExpression);
                continuationOrderByExpression = continuationOrderByExpression is null
                    ? null
                    : RewriteQueryLetReference(continuationOrderByExpression, query.ContinuationLetIdentifier.Text, continuationLetExpression);
                continuationThenByExpression = continuationThenByExpression is null
                    ? null
                    : RewriteQueryLetReference(continuationThenByExpression, query.ContinuationLetIdentifier.Text, continuationLetExpression);
                continuationSelectExpression =
                    RewriteQueryLetReference(continuationSelectExpression, query.ContinuationLetIdentifier.Text, continuationLetExpression);
            }

            if (continuationPredicateExpression is not null)
            {
                currentSource = CreateEnumerableCall(
                    $"Enumerable<{rangeVariableType.Name}>",
                    "Where",
                    [
                        currentSource,
                        CreateLambda(
                            "function",
                            [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                            "Boolean",
                            continuationPredicateExpression)
                    ]);
            }

            if (continuationOrderByExpression is not null)
            {
                if (continuationThenByExpression is not null)
                {
                    currentSource = CreateEnumerableCall(
                        $"Enumerable<{rangeVariableType.Name}>",
                        query.ContinuationThenByDescendingKeyword is null ? "OrderBy" : "OrderByDescending",
                        [
                            currentSource,
                            CreateLambda(
                                "function",
                                [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                                "Integer",
                                continuationThenByExpression)
                        ]);
                }

                currentSource = CreateEnumerableCall(
                    $"Enumerable<{rangeVariableType.Name}>",
                    query.ContinuationDescendingKeyword is null ? "OrderBy" : "OrderByDescending",
                    [
                        currentSource,
                        CreateLambda(
                            "function",
                            [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                            "Integer",
                            continuationOrderByExpression)
                    ]);
            }

            translated = CreateEnumerableCall(
                $"Enumerable<{rangeVariableType.Name}, {finalProjectedType.Name}>",
                "Select",
                [
                    currentSource,
                    CreateLambda(
                        "function",
                        [CreateParameter(currentRangeVariableName, rangeVariableType.Name)],
                        finalProjectedType.Name,
                        continuationSelectExpression)
                ]);
        }

        if (takeExpression is not null)
        {
            translated = CreateEnumerableCall(
                $"Enumerable<{finalProjectedType.Name}>",
                "Take",
                [
                    translated,
                    takeExpression
                ]);
        }

        if (skipExpression is not null)
        {
            translated = CreateEnumerableCall(
                $"Enumerable<{finalProjectedType.Name}>",
                "Skip",
                [
                    translated,
                    skipExpression
                ]);
        }

        return true;

        static CallExpressionSyntax CreateEnumerableCall(
            string receiverTypeName,
            string methodName,
            IReadOnlyList<ExpressionSyntax> arguments)
        {
            return new CallExpressionSyntax(
                new MemberAccessExpressionSyntax(
                    CreateNameExpression(receiverTypeName),
                    CreateToken(SyntaxKind.DotToken, "."),
                    CreateToken(SyntaxKind.IdentifierToken, methodName)),
                CreateToken(SyntaxKind.OpenParenToken, "("),
                arguments.Select(argument => new ArgumentSyntax(argument)).ToArray(),
                CreateToken(SyntaxKind.CloseParenToken, ")"));
        }

        static LambdaExpressionSyntax CreateLambda(
            string signatureKeyword,
            IReadOnlyList<ParameterSyntax> parameters,
            string returnTypeName,
            ExpressionSyntax body)
        {
            return new LambdaExpressionSyntax(
                CreateToken(signatureKeyword == "function" ? SyntaxKind.FunctionKeyword : SyntaxKind.ProcedureKeyword, signatureKeyword),
                CreateToken(SyntaxKind.OpenParenToken, "("),
                parameters,
                CreateToken(SyntaxKind.CloseParenToken, ")"),
                signatureKeyword == "function" ? CreateToken(SyntaxKind.ColonToken, ":") : null,
                signatureKeyword == "function" ? CreateQualifiedName(returnTypeName) : null,
                CreateToken(SyntaxKind.ArrowToken, "=>"),
                body);
        }

        static ParameterSyntax CreateParameter(string name, string typeName) =>
            new(
                null,
                CreateToken(SyntaxKind.IdentifierToken, name),
                CreateToken(SyntaxKind.ColonToken, ":"),
                CreateQualifiedName(typeName));

        static NameExpressionSyntax CreateNameExpression(string displayName) =>
            new(CreateQualifiedName(displayName));

        static QualifiedNameSyntax CreateQualifiedName(string displayName) =>
            new(ParseQualifiedNameTokens(displayName));

        static IReadOnlyList<SyntaxToken> ParseQualifiedNameTokens(string displayName)
        {
            var parts = new List<SyntaxToken>();
            foreach (var part in displayName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                parts.Add(CreateToken(SyntaxKind.IdentifierToken, part));
            }

            return parts;
        }

        static SyntaxToken CreateToken(SyntaxKind kind, string text) =>
            new(kind, text, null, new TextSpan(0, 0));
    }

    private static ExpressionSyntax RewriteQueryLetReference(
        ExpressionSyntax expression,
        string localName,
        ExpressionSyntax replacement) =>
        expression switch
        {
            NameExpressionSyntax name when name.Name.Parts.Count == 1 && name.Name.Parts[0].Text == localName => replacement,
            ParenthesizedExpressionSyntax parenthesized => parenthesized with
            {
                Expression = RewriteQueryLetReference(parenthesized.Expression, localName, replacement)
            },
            AssignmentExpressionSyntax assignment => assignment with
            {
                Expression = RewriteQueryLetReference(assignment.Expression, localName, replacement)
            },
            CompoundAssignmentExpressionSyntax assignment => assignment with
            {
                Expression = RewriteQueryLetReference(assignment.Expression, localName, replacement)
            },
            UnaryExpressionSyntax unary => unary with
            {
                Operand = RewriteQueryLetReference(unary.Operand, localName, replacement)
            },
            BinaryExpressionSyntax binary => binary with
            {
                Left = RewriteQueryLetReference(binary.Left, localName, replacement),
                Right = RewriteQueryLetReference(binary.Right, localName, replacement)
            },
            CallExpressionSyntax call => call with
            {
                Target = RewriteQueryLetReference(call.Target, localName, replacement),
                Arguments = call.Arguments.Select(argument => argument with
                {
                    Expression = RewriteQueryLetReference(argument.Expression, localName, replacement)
                }).ToArray()
            },
            MemberAccessExpressionSyntax memberAccess => memberAccess with
            {
                Receiver = RewriteQueryLetReference(memberAccess.Receiver, localName, replacement)
            },
            PostfixElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                Target = RewriteQueryLetReference(elementAccess.Target, localName, replacement),
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteQueryLetReference(index, localName, replacement)).ToArray()
            },
            ElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteQueryLetReference(index, localName, replacement)).ToArray()
            },
            AsExpressionSyntax asExpression => asExpression with
            {
                Expression = RewriteQueryLetReference(asExpression.Expression, localName, replacement)
            },
            TypeTestExpressionSyntax typeTest => typeTest with
            {
                Expression = RewriteQueryLetReference(typeTest.Expression, localName, replacement)
            },
            NewExpressionSyntax newExpression => newExpression with
            {
                Arguments = newExpression.Arguments.Select(argument => argument with
                {
                    Expression = RewriteQueryLetReference(argument.Expression, localName, replacement)
                }).ToArray()
            },
            ProjectorExpressionSyntax projector => projector with
            {
                Members = projector.Members.Select(member => member with
                {
                    Expression = RewriteQueryLetReference(member.Expression, localName, replacement)
                }).ToArray()
            },
            NewArrayExpressionSyntax newArray => newArray with
            {
                LengthExpressions = newArray.LengthExpressions.Select(length => RewriteQueryLetReference(length, localName, replacement)).ToArray()
            },
            _ => expression
        };

}
