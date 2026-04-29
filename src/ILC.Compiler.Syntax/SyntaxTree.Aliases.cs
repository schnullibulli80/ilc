namespace ILC.Compiler.Syntax;

public sealed partial class SyntaxTree
{
    private static CompilationUnitSyntax NormalizeUsesAliases(CompilationUnitSyntax root)
    {
        var aliases = root.Uses?.Imports
            .Where(importSyntax => importSyntax.AliasIdentifier is not null)
            .ToDictionary(
                importSyntax => importSyntax.AliasIdentifier!.Text,
                importSyntax => importSyntax.NamespaceName,
                StringComparer.Ordinal);
        if (aliases is null || aliases.Count == 0)
        {
            return root;
        }

        return root with
        {
            Members = root.Members.Select(member => NormalizeMember(member, aliases)).ToArray()
        };
    }

    private static MemberSyntax NormalizeMember(MemberSyntax member, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        member switch
        {
            TopLevelVariableDeclarationSyntax variableDeclaration => variableDeclaration with
            {
                Declarators = variableDeclaration.Declarators.Select(declarator => declarator with
                {
                    TypeName = NormalizeQualifiedName(declarator.TypeName, aliases),
                    Initializer = NormalizeExpression(declarator.Initializer, aliases)
                }).ToArray()
            },
            TopLevelConstantDeclarationSyntax constantDeclaration => constantDeclaration with
            {
                Declarators = constantDeclaration.Declarators.Select(declarator => declarator with
                {
                    TypeName = NormalizeQualifiedName(declarator.TypeName, aliases),
                    Initializer = NormalizeExpression(declarator.Initializer, aliases)!
                }).ToArray()
            },
            TopLevelExpressionStatementSyntax expressionStatement => expressionStatement with
            {
                Expression = NormalizeExpression(expressionStatement.Expression, aliases)!
            },
            EnumDeclarationSyntax enumDeclaration => enumDeclaration,
            DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration with
            {
                ReturnType = NormalizeQualifiedName(delegateDeclaration.ReturnType, aliases),
                Parameters = delegateDeclaration.Parameters.Select(parameter => parameter with
                {
                    TypeName = NormalizeQualifiedName(parameter.TypeName, aliases)!
                }).ToArray()
            },
            ClassDeclarationSyntax classDeclaration => classDeclaration with
            {
                BaseType = NormalizeQualifiedName(classDeclaration.BaseType, aliases),
                InterfaceTypes = classDeclaration.InterfaceTypes.Select(typeName => NormalizeQualifiedName(typeName, aliases)!).ToArray(),
                Members = classDeclaration.Members.Select(memberSyntax => NormalizeTypeMember(memberSyntax, aliases)).ToArray()
            },
            InterfaceDeclarationSyntax interfaceDeclaration => interfaceDeclaration with
            {
                BaseInterfaces = interfaceDeclaration.BaseInterfaces.Select(typeName => NormalizeQualifiedName(typeName, aliases)!).ToArray(),
                Members = interfaceDeclaration.Members.Select(memberSyntax => NormalizeTypeMember(memberSyntax, aliases)).ToArray()
            },
            _ => member
        };

    private static TypeMemberSyntax NormalizeTypeMember(TypeMemberSyntax member, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        member switch
        {
            FieldDeclarationSyntax fieldDeclaration => fieldDeclaration with
            {
                Declarators = fieldDeclaration.Declarators.Select(declarator => declarator with
                {
                    TypeName = NormalizeQualifiedName(declarator.TypeName, aliases),
                    Initializer = NormalizeExpression(declarator.Initializer, aliases)
                }).ToArray()
            },
            ConstantDeclarationSyntax constantDeclaration => constantDeclaration with
            {
                Declarators = constantDeclaration.Declarators.Select(declarator => declarator with
                {
                    TypeName = NormalizeQualifiedName(declarator.TypeName, aliases),
                    Initializer = NormalizeExpression(declarator.Initializer, aliases)!
                }).ToArray()
            },
            PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration with
            {
                TypeName = NormalizeQualifiedName(propertyDeclaration.TypeName, aliases)!,
                IndexParameter = NormalizeParameter(propertyDeclaration.IndexParameter, aliases),
                GetterBody = NormalizeBlock(propertyDeclaration.GetterBody, aliases),
                SetterBody = NormalizeBlock(propertyDeclaration.SetterBody, aliases)
            },
            MethodDeclarationSyntax methodDeclaration => methodDeclaration with
            {
                Attributes = methodDeclaration.Attributes.Select(attribute => NormalizeAttribute(attribute, aliases)).ToArray(),
                ReturnType = NormalizeQualifiedName(methodDeclaration.ReturnType, aliases),
                Parameters = methodDeclaration.Parameters.Select(parameter => NormalizeParameter(parameter, aliases)!).ToArray(),
                ExpressionBody = NormalizeExpression(methodDeclaration.ExpressionBody, aliases),
                Body = NormalizeBlock(methodDeclaration.Body, aliases)
            },
            _ => member
        };

    private static AttributeSyntax NormalizeAttribute(AttributeSyntax attribute, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        attribute with
        {
            Name = NormalizeQualifiedName(attribute.Name, aliases)!,
            Arguments = attribute.Arguments.Select(argument => argument with
            {
                Expression = NormalizeExpression(argument.Expression, aliases)!
            }).ToArray()
        };

    private static ParameterSyntax? NormalizeParameter(ParameterSyntax? parameter, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        parameter is null
            ? null
            : parameter with { TypeName = NormalizeQualifiedName(parameter.TypeName, aliases)! };

    private static BlockStatementSyntax? NormalizeBlock(BlockStatementSyntax? block, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        block is null
            ? null
            : block with { Statements = block.Statements.Select(statement => NormalizeStatement(statement, aliases)).ToArray() };

    private static StatementSyntax NormalizeStatement(StatementSyntax statement, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        statement switch
        {
            BlockStatementSyntax block => NormalizeBlock(block, aliases)!,
            IfStatementSyntax ifStatement => ifStatement with
            {
                Condition = NormalizeExpression(ifStatement.Condition, aliases)!,
                ThenStatement = NormalizeStatement(ifStatement.ThenStatement, aliases),
                ElseStatement = ifStatement.ElseStatement is null ? null : NormalizeStatement(ifStatement.ElseStatement, aliases)
            },
            WhileStatementSyntax whileStatement => whileStatement with
            {
                Condition = NormalizeExpression(whileStatement.Condition, aliases)!,
                Body = NormalizeStatement(whileStatement.Body, aliases)
            },
            RepeatStatementSyntax repeatStatement => repeatStatement with
            {
                Statements = repeatStatement.Statements.Select(item => NormalizeStatement(item, aliases)).ToArray(),
                Condition = NormalizeExpression(repeatStatement.Condition, aliases)!
            },
            ForStatementSyntax forStatement => forStatement with
            {
                LowerBound = NormalizeExpression(forStatement.LowerBound, aliases)!,
                UpperBound = NormalizeExpression(forStatement.UpperBound, aliases)!,
                StepExpression = NormalizeExpression(forStatement.StepExpression, aliases),
                Body = NormalizeStatement(forStatement.Body, aliases)
            },
            ForeachStatementSyntax foreachStatement => foreachStatement with
            {
                Collection = NormalizeExpression(foreachStatement.Collection, aliases)!,
                Body = NormalizeStatement(foreachStatement.Body, aliases)
            },
            WithStatementSyntax withStatement => withStatement with
            {
                Receiver = NormalizeExpression(withStatement.Receiver, aliases)!,
                Body = NormalizeStatement(withStatement.Body, aliases)
            },
            CaseStatementSyntax caseStatement => caseStatement with
            {
                Expression = NormalizeExpression(caseStatement.Expression, aliases)!,
                Clauses = caseStatement.Clauses.Select(clause => clause with
                {
                    Labels = clause.Labels.Select(label => NormalizeExpression(label, aliases)!).ToArray(),
                    Guard = NormalizeExpression(clause.Guard, aliases),
                    Body = NormalizeStatement(clause.Body, aliases)
                }).ToArray(),
                ElseStatements = caseStatement.ElseStatements.Select(item => NormalizeStatement(item, aliases)).ToArray()
            },
            MatchStatementSyntax matchStatement => matchStatement with
            {
                Expression = NormalizeExpression(matchStatement.Expression, aliases)!,
                Arms = matchStatement.Arms.Select(arm => arm with
                {
                    Labels = arm.Labels.Select(label => NormalizeExpression(label, aliases)!).ToArray(),
                    TypeName = NormalizeQualifiedName(arm.TypeName, aliases),
                    Guard = NormalizeExpression(arm.Guard, aliases),
                    Body = NormalizeStatement(arm.Body, aliases)
                }).ToArray(),
                ElseStatements = matchStatement.ElseStatements.Select(item => NormalizeStatement(item, aliases)).ToArray()
            },
            ReturnStatementSyntax returnStatement => returnStatement with
            {
                Expression = NormalizeExpression(returnStatement.Expression, aliases)
            },
            IncStatementSyntax incStatement => incStatement with
            {
                Target = NormalizeExpression(incStatement.Target, aliases)!
            },
            DecStatementSyntax decStatement => decStatement with
            {
                Target = NormalizeExpression(decStatement.Target, aliases)!
            },
            IncludeStatementSyntax includeStatement => includeStatement with
            {
                Target = NormalizeExpression(includeStatement.Target, aliases)!,
                Value = NormalizeExpression(includeStatement.Value, aliases)!
            },
            ExcludeStatementSyntax excludeStatement => excludeStatement with
            {
                Target = NormalizeExpression(excludeStatement.Target, aliases)!,
                Value = NormalizeExpression(excludeStatement.Value, aliases)!
            },
            RaiseStatementSyntax raiseStatement => raiseStatement with
            {
                Expression = NormalizeExpression(raiseStatement.Expression, aliases)!
            },
            TryStatementSyntax tryStatement => tryStatement with
            {
                TryStatements = tryStatement.TryStatements.Select(item => NormalizeStatement(item, aliases)).ToArray(),
                ExceptionClauses = tryStatement.ExceptionClauses.Select(clause => clause with
                {
                    TypeName = NormalizeQualifiedName(clause.TypeName, aliases)!,
                    Body = NormalizeStatement(clause.Body, aliases)
                }).ToArray(),
                ExceptStatements = tryStatement.ExceptStatements.Select(item => NormalizeStatement(item, aliases)).ToArray(),
                FinallyStatements = tryStatement.FinallyStatements.Select(item => NormalizeStatement(item, aliases)).ToArray()
            },
            LocalVariableDeclarationStatementSyntax localVariable => localVariable with
            {
                Declarators = localVariable.Declarators.Select(declarator => declarator with
                {
                    TypeName = NormalizeQualifiedName(declarator.TypeName, aliases),
                    Initializer = NormalizeExpression(declarator.Initializer, aliases)
                }).ToArray()
            },
            ExpressionStatementSyntax expressionStatement => expressionStatement with
            {
                Expression = NormalizeExpression(expressionStatement.Expression, aliases)!
            },
            _ => statement
        };

    private static ExpressionSyntax? NormalizeExpression(ExpressionSyntax? expression, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        expression switch
        {
            null => null,
            LiteralExpressionSyntax literal => literal,
            SetLiteralExpressionSyntax setLiteral => setLiteral with
            {
                Elements = setLiteral.Elements.Select(item => NormalizeExpression(item, aliases)!).ToArray()
            },
            ProjectorExpressionSyntax projector => projector with
            {
                Members = projector.Members.Select(member => member with
                {
                    Expression = NormalizeExpression(member.Expression, aliases)!
                }).ToArray()
            },
            NewExpressionSyntax newExpression => newExpression with
            {
                TypeName = NormalizeQualifiedName(newExpression.TypeName, aliases)!,
                Arguments = newExpression.Arguments.Select(argument => NormalizeArgument(argument, aliases)).ToArray()
            },
            NewArrayExpressionSyntax newArray => newArray with
            {
                ElementTypeName = NormalizeQualifiedName(newArray.ElementTypeName, aliases)!,
                LengthExpressions = newArray.LengthExpressions.Select(item => NormalizeExpression(item, aliases)!).ToArray()
            },
            NameExpressionSyntax name => name with
            {
                Name = NormalizeQualifiedName(name.Name, aliases)!
            },
            ArrayLengthExpressionSyntax arrayLength => arrayLength with
            {
                Target = NormalizeQualifiedName(arrayLength.Target, aliases)!
            },
            ElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                Target = NormalizeQualifiedName(elementAccess.Target, aliases)!,
                IndexExpressions = elementAccess.IndexExpressions.Select(item => NormalizeExpression(item, aliases)!).ToArray()
            },
            PostfixElementAccessExpressionSyntax postfixElementAccess => postfixElementAccess with
            {
                Target = NormalizeExpression(postfixElementAccess.Target, aliases)!,
                IndexExpressions = postfixElementAccess.IndexExpressions.Select(item => NormalizeExpression(item, aliases)!).ToArray()
            },
            MemberAccessExpressionSyntax memberAccess => NormalizeMemberAccess(memberAccess, aliases),
            AssignmentExpressionSyntax assignment => assignment with
            {
                Target = NormalizeExpression(assignment.Target, aliases)!,
                Expression = NormalizeExpression(assignment.Expression, aliases)!
            },
            CompoundAssignmentExpressionSyntax assignment => assignment with
            {
                Target = NormalizeExpression(assignment.Target, aliases)!,
                Expression = NormalizeExpression(assignment.Expression, aliases)!
            },
            BinaryExpressionSyntax binary => binary with
            {
                Left = NormalizeExpression(binary.Left, aliases)!,
                Right = NormalizeExpression(binary.Right, aliases)!
            },
            MatchNotPatternSyntax notPattern => notPattern with
            {
                Pattern = NormalizeExpression(notPattern.Pattern, aliases)!
            },
            MatchOrPatternSyntax orPattern => orPattern with
            {
                Patterns = orPattern.Patterns.Select(item => NormalizeExpression(item, aliases)!).ToArray()
            },
            MatchAndPatternSyntax andPattern => andPattern with
            {
                Patterns = andPattern.Patterns.Select(item => (MatchRelationalPatternSyntax)NormalizeExpression(item, aliases)!).ToArray()
            },
            MatchRelationalPatternSyntax relational => relational with
            {
                Operand = NormalizeExpression(relational.Operand, aliases)!
            },
            ParenthesizedExpressionSyntax parenthesized => parenthesized with
            {
                Expression = NormalizeExpression(parenthesized.Expression, aliases)!
            },
            RangeExpressionSyntax range => range with
            {
                Start = NormalizeExpression(range.Start, aliases)!,
                End = NormalizeExpression(range.End, aliases)!
            },
            AsExpressionSyntax asExpression => asExpression with
            {
                Expression = NormalizeExpression(asExpression.Expression, aliases)!,
                TypeName = NormalizeQualifiedName(asExpression.TypeName, aliases)!,
            },
            TypeTestExpressionSyntax typeTest => typeTest with
            {
                Expression = NormalizeExpression(typeTest.Expression, aliases)!,
                TypeName = NormalizeQualifiedName(typeTest.TypeName, aliases)!
            },
            CallExpressionSyntax call => call with
            {
                Target = NormalizeExpression(call.Target, aliases)!,
                Arguments = call.Arguments.Select(argument => NormalizeArgument(argument, aliases)).ToArray()
            },
            QueryExpressionSyntax query => query with
            {
                SourceExpression = NormalizeExpression(query.SourceExpression, aliases)!,
                JoinSourceExpression = query.JoinSourceExpression is null ? null : NormalizeExpression(query.JoinSourceExpression, aliases)!,
                JoinLeftExpression = query.JoinLeftExpression is null ? null : NormalizeExpression(query.JoinLeftExpression, aliases)!,
                JoinRightExpression = query.JoinRightExpression is null ? null : NormalizeExpression(query.JoinRightExpression, aliases)!,
                JoinIntoKeyword = query.JoinIntoKeyword,
                JoinIntoIdentifier = query.JoinIntoIdentifier,
                SecondSourceExpression = query.SecondSourceExpression is null ? null : NormalizeExpression(query.SecondSourceExpression, aliases)!,
                LetExpression = query.LetExpression is null ? null : NormalizeExpression(query.LetExpression, aliases)!,
                PredicateExpression = query.PredicateExpression is null ? null : NormalizeExpression(query.PredicateExpression, aliases)!,
                OrderByExpression = query.OrderByExpression is null ? null : NormalizeExpression(query.OrderByExpression, aliases)!,
                ThenByExpression = query.ThenByExpression is null ? null : NormalizeExpression(query.ThenByExpression, aliases)!,
                GroupExpression = query.GroupExpression is null ? null : NormalizeExpression(query.GroupExpression, aliases)!,
                GroupByExpression = query.GroupByExpression is null ? null : NormalizeExpression(query.GroupByExpression, aliases)!,
                SelectExpression = NormalizeExpression(query.SelectExpression, aliases)!,
                ContinuationLetExpression = query.ContinuationLetExpression is null ? null : NormalizeExpression(query.ContinuationLetExpression, aliases)!,
                ContinuationPredicateExpression = query.ContinuationPredicateExpression is null ? null : NormalizeExpression(query.ContinuationPredicateExpression, aliases)!,
                ContinuationOrderByExpression = query.ContinuationOrderByExpression is null ? null : NormalizeExpression(query.ContinuationOrderByExpression, aliases)!,
                ContinuationThenByExpression = query.ContinuationThenByExpression is null ? null : NormalizeExpression(query.ContinuationThenByExpression, aliases)!,
                ContinuationSelectExpression = query.ContinuationSelectExpression is null ? null : NormalizeExpression(query.ContinuationSelectExpression, aliases)!,
                TakeExpression = query.TakeExpression is null ? null : NormalizeExpression(query.TakeExpression, aliases)!,
                SkipExpression = query.SkipExpression is null ? null : NormalizeExpression(query.SkipExpression, aliases)!
            },
            LambdaExpressionSyntax lambda => lambda with
            {
                Parameters = lambda.Parameters.Select(parameter => parameter with
                {
                    TypeName = NormalizeQualifiedName(parameter.TypeName, aliases)!
                }).ToArray(),
                ReturnType = NormalizeQualifiedName(lambda.ReturnType, aliases),
                Body = NormalizeExpression(lambda.Body, aliases)!
            },
            UnaryExpressionSyntax unary => unary with
            {
                Operand = NormalizeExpression(unary.Operand, aliases)!
            },
            MatchExpressionSyntax matchExpression => matchExpression with
            {
                Expression = NormalizeExpression(matchExpression.Expression, aliases)!,
                Arms = matchExpression.Arms.Select(arm => arm with
                {
                    Labels = arm.Labels.Select(label => NormalizeExpression(label, aliases)!).ToArray(),
                    TypeName = NormalizeQualifiedName(arm.TypeName, aliases),
                    Guard = NormalizeExpression(arm.Guard, aliases),
                    Expression = NormalizeExpression(arm.Expression, aliases)!
                }).ToArray()
            },
            _ => expression
        };

    private static ArgumentSyntax NormalizeArgument(ArgumentSyntax argument, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases) =>
        argument with { Expression = NormalizeExpression(argument.Expression, aliases)! };

    private static ExpressionSyntax NormalizeMemberAccess(MemberAccessExpressionSyntax memberAccess, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases)
    {
        var normalizedReceiver = NormalizeExpression(memberAccess.Receiver, aliases)!;
        if (normalizedReceiver is NameExpressionSyntax { Name.Parts.Count: 1 } receiverName &&
            aliases.ContainsKey(receiverName.Name.Parts[0].Text))
        {
            return new NameExpressionSyntax(new QualifiedNameSyntax([memberAccess.MemberName]));
        }

        return memberAccess with { Receiver = normalizedReceiver };
    }

    private static QualifiedNameSyntax? NormalizeQualifiedName(QualifiedNameSyntax? name, IReadOnlyDictionary<string, QualifiedNameSyntax> aliases)
    {
        if (name is null || name.Parts.Count < 2 || !aliases.ContainsKey(name.Parts[0].Text))
        {
            return name;
        }

        return new QualifiedNameSyntax(name.Parts.Skip(1).ToArray());
    }
}
