namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private StatementSyntax RewriteWithStatement(
        StatementSyntax statement,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        statement switch
        {
            BlockStatementSyntax block => block with
            {
                Statements = block.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            ExpressionStatementSyntax expressionStatement => expressionStatement with
            {
                Expression = RewriteWithExpression(expressionStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            IncStatementSyntax incStatement => incStatement with
            {
                Target = RewriteWithExpression(incStatement.Target, receiver, registerByName, localTypes, currentMethod)
            },
            DecStatementSyntax decStatement => decStatement with
            {
                Target = RewriteWithExpression(decStatement.Target, receiver, registerByName, localTypes, currentMethod)
            },
            IncludeStatementSyntax includeStatement => includeStatement with
            {
                Target = RewriteWithExpression(includeStatement.Target, receiver, registerByName, localTypes, currentMethod),
                Value = RewriteWithExpression(includeStatement.Value, receiver, registerByName, localTypes, currentMethod)
            },
            ExcludeStatementSyntax excludeStatement => excludeStatement with
            {
                Target = RewriteWithExpression(excludeStatement.Target, receiver, registerByName, localTypes, currentMethod),
                Value = RewriteWithExpression(excludeStatement.Value, receiver, registerByName, localTypes, currentMethod)
            },
            ReturnStatementSyntax returnStatement when returnStatement.Expression is not null => returnStatement with
            {
                Expression = RewriteWithExpression(returnStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            RaiseStatementSyntax raiseStatement when raiseStatement.Expression is not null => raiseStatement with
            {
                Expression = RewriteWithExpression(raiseStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            IfStatementSyntax ifStatement => ifStatement with
            {
                Condition = RewriteWithExpression(ifStatement.Condition, receiver, registerByName, localTypes, currentMethod),
                ThenStatement = RewriteWithStatement(ifStatement.ThenStatement, receiver, registerByName, localTypes, currentMethod),
                ElseStatement = ifStatement.ElseStatement is null
                    ? null
                    : RewriteWithStatement(ifStatement.ElseStatement, receiver, registerByName, localTypes, currentMethod)
            },
            WhileStatementSyntax whileStatement => whileStatement with
            {
                Condition = RewriteWithExpression(whileStatement.Condition, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(whileStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            RepeatStatementSyntax repeatStatement => repeatStatement with
            {
                Statements = repeatStatement.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                Condition = RewriteWithExpression(repeatStatement.Condition, receiver, registerByName, localTypes, currentMethod)
            },
            ForStatementSyntax forStatement => forStatement with
            {
                LowerBound = RewriteWithExpression(forStatement.LowerBound, receiver, registerByName, localTypes, currentMethod),
                UpperBound = RewriteWithExpression(forStatement.UpperBound, receiver, registerByName, localTypes, currentMethod),
                StepExpression = forStatement.StepExpression is null
                    ? null
                    : RewriteWithExpression(forStatement.StepExpression, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(forStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            ForeachStatementSyntax foreachStatement => foreachStatement with
            {
                Collection = RewriteWithExpression(foreachStatement.Collection, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(foreachStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            CaseStatementSyntax caseStatement => caseStatement with
            {
                Expression = RewriteWithExpression(caseStatement.Expression, receiver, registerByName, localTypes, currentMethod),
                Clauses = caseStatement.Clauses
                    .Select(clause => clause with
                    {
                        Labels = clause.Labels
                            .Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod))
                            .ToArray(),
                        Guard = clause.Guard is null
                            ? null
                            : RewriteWithExpression(clause.Guard, receiver, registerByName, localTypes, currentMethod),
                        Body = RewriteWithStatement(clause.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = caseStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchStatementSyntax matchStatement => matchStatement with
            {
                Expression = RewriteWithExpression(matchStatement.Expression, receiver, registerByName, localTypes, currentMethod),
                Arms = matchStatement.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels
                            .Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod))
                            .ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, registerByName, localTypes, currentMethod),
                        Body = RewriteWithStatement(arm.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = matchStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            LocalVariableDeclarationStatementSyntax localVariable => localVariable with
            {
                Declarators = localVariable.Declarators
                    .Select(declarator => declarator.Initializer is null
                        ? declarator
                        : declarator with
                        {
                            Initializer = RewriteWithExpression(declarator.Initializer, receiver, registerByName, localTypes, currentMethod)
                        })
                    .ToArray()
            },
            TryStatementSyntax tryStatement => tryStatement with
            {
                TryStatements = tryStatement.TryStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                ExceptionClauses = tryStatement.ExceptionClauses
                    .Select(clause => clause with
                    {
                        Body = RewriteWithStatement(clause.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ExceptStatements = tryStatement.ExceptStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                FinallyStatements = tryStatement.FinallyStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            WithStatementSyntax nestedWith => nestedWith with
            {
                Receiver = RewriteWithExpression(nestedWith.Receiver, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(nestedWith.Body, receiver, registerByName, localTypes, currentMethod)
            },
            _ => statement
        };

    private ExpressionSyntax RewriteWithExpression(
        ExpressionSyntax expression,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (expression is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            ShouldQualifyWithName(name.Name, registerByName, localTypes, currentMethod))
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return expression switch
        {
            AssignmentExpressionSyntax assignment => assignment with
            {
                Target = RewriteWithAssignmentTarget(assignment.Target, receiver, registerByName, localTypes, currentMethod),
                Expression = RewriteWithExpression(assignment.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            UnaryExpressionSyntax unary => unary with
            {
                Operand = RewriteWithExpression(unary.Operand, receiver, registerByName, localTypes, currentMethod)
            },
            MatchNotPatternSyntax notPattern => notPattern with
            {
                Pattern = RewriteWithExpression(notPattern.Pattern, receiver, registerByName, localTypes, currentMethod)
            },
            MatchOrPatternSyntax orPattern => orPattern with
            {
                Patterns = orPattern.Patterns
                    .Select(pattern => RewriteWithExpression(pattern, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchAndPatternSyntax andPattern => andPattern with
            {
                Patterns = andPattern.Patterns
                    .Select(pattern => (MatchRelationalPatternSyntax)RewriteWithExpression(pattern, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchRelationalPatternSyntax relational => relational with
            {
                Operand = RewriteWithExpression(relational.Operand, receiver, registerByName, localTypes, currentMethod)
            },
            BinaryExpressionSyntax binary => binary with
            {
                Left = RewriteWithExpression(binary.Left, receiver, registerByName, localTypes, currentMethod),
                Right = RewriteWithExpression(binary.Right, receiver, registerByName, localTypes, currentMethod)
            },
            MatchExpressionSyntax matchExpression => matchExpression with
            {
                Expression = RewriteWithExpression(matchExpression.Expression, receiver, registerByName, localTypes, currentMethod),
                Arms = matchExpression.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels.Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod)).ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, registerByName, localTypes, currentMethod),
                        Expression = RewriteWithExpression(arm.Expression, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray()
            },
            CallExpressionSyntax call => call with
            {
                Target = RewriteWithCallTarget(call.Target, call.Arguments.Count, receiver, registerByName, localTypes, currentMethod),
                Arguments = call.Arguments
                    .Select(argument => argument with
                    {
                        Expression = RewriteWithExpression(argument.Expression, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray()
            },
            QueryExpressionSyntax query => query with
            {
                SourceExpression = RewriteWithExpression(query.SourceExpression, receiver, registerByName, localTypes, currentMethod),
                JoinSourceExpression = query.JoinSourceExpression is null
                    ? null
                    : RewriteWithExpression(query.JoinSourceExpression, receiver, registerByName, localTypes, currentMethod),
                JoinLeftExpression = query.JoinLeftExpression is null
                    ? null
                    : RewriteWithExpression(query.JoinLeftExpression, receiver, registerByName, localTypes, currentMethod),
                JoinRightExpression = query.JoinRightExpression is null
                    ? null
                    : RewriteWithExpression(query.JoinRightExpression, receiver, registerByName, localTypes, currentMethod),
                JoinIntoKeyword = query.JoinIntoKeyword,
                JoinIntoIdentifier = query.JoinIntoIdentifier,
                SecondSourceExpression = query.SecondSourceExpression is null
                    ? null
                    : RewriteWithExpression(query.SecondSourceExpression, receiver, registerByName, localTypes, currentMethod),
                LetExpression = query.LetExpression is null
                    ? null
                    : RewriteWithExpression(query.LetExpression, receiver, registerByName, localTypes, currentMethod),
                PredicateExpression = query.PredicateExpression is null
                    ? null
                    : RewriteWithExpression(query.PredicateExpression, receiver, registerByName, localTypes, currentMethod),
                OrderByExpression = query.OrderByExpression is null
                    ? null
                    : RewriteWithExpression(query.OrderByExpression, receiver, registerByName, localTypes, currentMethod),
                GroupExpression = query.GroupExpression is null
                    ? null
                    : RewriteWithExpression(query.GroupExpression, receiver, registerByName, localTypes, currentMethod),
                GroupByExpression = query.GroupByExpression is null
                    ? null
                    : RewriteWithExpression(query.GroupByExpression, receiver, registerByName, localTypes, currentMethod),
                SelectExpression = RewriteWithExpression(query.SelectExpression, receiver, registerByName, localTypes, currentMethod),
                ContinuationPredicateExpression = query.ContinuationPredicateExpression is null
                    ? null
                    : RewriteWithExpression(query.ContinuationPredicateExpression, receiver, registerByName, localTypes, currentMethod),
                ContinuationOrderByExpression = query.ContinuationOrderByExpression is null
                    ? null
                    : RewriteWithExpression(query.ContinuationOrderByExpression, receiver, registerByName, localTypes, currentMethod),
                ContinuationSelectExpression = query.ContinuationSelectExpression is null
                    ? null
                    : RewriteWithExpression(query.ContinuationSelectExpression, receiver, registerByName, localTypes, currentMethod),
                TakeExpression = query.TakeExpression is null
                    ? null
                    : RewriteWithExpression(query.TakeExpression, receiver, registerByName, localTypes, currentMethod),
                SkipExpression = query.SkipExpression is null
                    ? null
                    : RewriteWithExpression(query.SkipExpression, receiver, registerByName, localTypes, currentMethod)
            },
            MemberAccessExpressionSyntax memberAccess => memberAccess with
            {
                Receiver = RewriteWithExpression(memberAccess.Receiver, receiver, registerByName, localTypes, currentMethod)
            },
            PostfixElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                Target = RewriteWithExpression(elementAccess.Target, receiver, registerByName, localTypes, currentMethod),
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteWithExpression(index, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            ElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteWithExpression(index, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            AsExpressionSyntax asExpression => asExpression with
            {
                Expression = RewriteWithExpression(asExpression.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            TypeTestExpressionSyntax typeTest => typeTest with
            {
                Expression = RewriteWithExpression(typeTest.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            ProjectorExpressionSyntax projector => projector with
            {
                Members = projector.Members.Select(member => member with
                {
                    Expression = RewriteWithExpression(member.Expression, receiver, registerByName, localTypes, currentMethod)
                }).ToArray()
            },
            NewExpressionSyntax newExpression => newExpression with
            {
                Arguments = newExpression.Arguments.Select(argument => argument with
                {
                    Expression = RewriteWithExpression(argument.Expression, receiver, registerByName, localTypes, currentMethod)
                }).ToArray()
            },
            NewArrayExpressionSyntax newArray => newArray with
            {
                LengthExpressions = newArray.LengthExpressions.Select(length => RewriteWithExpression(length, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            _ => expression
        };
    }

    private ExpressionSyntax RewriteWithAssignmentTarget(
        ExpressionSyntax target,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        target is NameExpressionSyntax name &&
        name.Name.Parts.Count == 1 &&
        ShouldQualifyWithName(name.Name, registerByName, localTypes, currentMethod)
            ? QualifyWithReceiver(receiver, name.Name.Parts[0])
            : RewriteWithExpression(target, receiver, registerByName, localTypes, currentMethod);

    private ExpressionSyntax RewriteWithCallTarget(
        ExpressionSyntax target,
        int argumentCount,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (target is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            SemanticFacts.ResolveInvocation(
                name.Name,
                argumentCount,
                localTypes,
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod) is null)
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return RewriteWithExpression(target, receiver, registerByName, localTypes, currentMethod);
    }

    private bool ShouldQualifyWithName(
        QualifiedNameSyntax name,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        !registerByName.ContainsKey(name.ToDisplayString()) &&
        SemanticFacts.ResolveName(
            name,
            localTypes,
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod).Kind == NameResolutionKind.Unknown;

    private static MemberAccessExpressionSyntax QualifyWithReceiver(ExpressionSyntax receiver, SyntaxToken memberName) =>
        new(
            receiver,
            new SyntaxToken(SyntaxKind.DotToken, ".", null, memberName.Span),
            memberName);
}
