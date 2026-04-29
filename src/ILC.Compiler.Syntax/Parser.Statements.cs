namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private StatementSyntax ParseStatement()
    {
        return Current.Kind switch
        {
            SyntaxKind.BeginKeyword => ParseBlockStatement(),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.RepeatKeyword => ParseRepeatStatement(),
            SyntaxKind.ForKeyword when Peek(1).Kind == SyntaxKind.EachKeyword => ParseForeachStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.ForeachKeyword => ParseForeachStatement(),
            SyntaxKind.WithKeyword => ParseWithStatement(),
            SyntaxKind.CaseKeyword => ParseCaseStatement(),
            SyntaxKind.MatchKeyword => ParseMatchStatement(),
            SyntaxKind.ReturnKeyword or SyntaxKind.ExitKeyword => ParseReturnStatement(),
            SyntaxKind.BreakKeyword => ParseBreakStatement(),
            SyntaxKind.ContinueKeyword => ParseContinueStatement(),
            SyntaxKind.IncKeyword => ParseIncDecStatement(true),
            SyntaxKind.DecKeyword => ParseIncDecStatement(false),
            SyntaxKind.IncludeKeyword => ParseIncludeExcludeStatement(true),
            SyntaxKind.ExcludeKeyword => ParseIncludeExcludeStatement(false),
            SyntaxKind.RaiseKeyword or SyntaxKind.ThrowKeyword => ParseRaiseStatement(),
            SyntaxKind.TryKeyword => ParseTryStatement(),
            SyntaxKind.VarKeyword => ParseLocalVariableDeclarationStatement(),
            _ => ParseExpressionStatement()
        };
    }

    private BlockStatementSyntax ParseBlockStatement(bool requireSemicolon = true)
    {
        var beginKeyword = Match(SyntaxKind.BeginKeyword);
        var statements = new List<StatementSyntax>();
        while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            statements.Add(ParseStatement());
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(endKeyword.Span.Start + endKeyword.Span.Length, 0));
        return new BlockStatementSyntax(beginKeyword, statements, endKeyword, semicolon);
    }

    private ReturnStatementSyntax ParseReturnStatement(bool requireSemicolon = true)
    {
        var keyword = Current.Kind == SyntaxKind.ExitKeyword
            ? Match(SyntaxKind.ExitKeyword)
            : Match(SyntaxKind.ReturnKeyword);
        ExpressionSyntax? expression = null;
        if (Current.Kind != SyntaxKind.SemicolonToken && !(Current.Kind == SyntaxKind.ElseKeyword && !requireSemicolon))
        {
            expression = ParseExpression();
        }

        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new ReturnStatementSyntax(keyword, expression, semicolon);
    }

    private BreakStatementSyntax ParseBreakStatement(bool requireSemicolon = true)
    {
        var keyword = Match(SyntaxKind.BreakKeyword);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new BreakStatementSyntax(keyword, semicolon);
    }

    private ContinueStatementSyntax ParseContinueStatement(bool requireSemicolon = true)
    {
        var keyword = Match(SyntaxKind.ContinueKeyword);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new ContinueStatementSyntax(keyword, semicolon);
    }

    private StatementSyntax ParseIncDecStatement(bool isIncrement, bool requireSemicolon = true)
    {
        var keyword = isIncrement
            ? Match(SyntaxKind.IncKeyword)
            : Match(SyntaxKind.DecKeyword);
        var openParen = Match(SyntaxKind.OpenParenToken);
        var target = ParseExpression();
        var closeParen = Match(SyntaxKind.CloseParenToken);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return isIncrement
            ? new IncStatementSyntax(keyword, openParen, target, closeParen, semicolon)
            : new DecStatementSyntax(keyword, openParen, target, closeParen, semicolon);
    }

    private StatementSyntax ParseIncludeExcludeStatement(bool isInclude, bool requireSemicolon = true)
    {
        var keyword = isInclude
            ? Match(SyntaxKind.IncludeKeyword)
            : Match(SyntaxKind.ExcludeKeyword);
        var openParen = Match(SyntaxKind.OpenParenToken);
        var target = ParseExpression();
        var comma = Match(SyntaxKind.CommaToken);
        var value = ParseExpression();
        var closeParen = Match(SyntaxKind.CloseParenToken);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return isInclude
            ? new IncludeStatementSyntax(keyword, openParen, target, comma, value, closeParen, semicolon)
            : new ExcludeStatementSyntax(keyword, openParen, target, comma, value, closeParen, semicolon);
    }

    private RaiseStatementSyntax ParseRaiseStatement(bool requireSemicolon = true)
    {
        var keyword = Current.Kind == SyntaxKind.ThrowKeyword
            ? Match(SyntaxKind.ThrowKeyword)
            : Match(SyntaxKind.RaiseKeyword);
        ExpressionSyntax? expression = null;
        if (Current.Kind != SyntaxKind.SemicolonToken && !(Current.Kind == SyntaxKind.ElseKeyword && !requireSemicolon))
        {
            expression = ParseExpression();
        }

        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new RaiseStatementSyntax(keyword, expression, semicolon);
    }

    private TryStatementSyntax ParseTryStatement(bool requireSemicolon = true)
    {
        var tryKeyword = Match(SyntaxKind.TryKeyword);
        var tryStatements = new List<StatementSyntax>();
        while (Current.Kind is not SyntaxKind.ExceptKeyword and not SyntaxKind.FinallyKeyword and not SyntaxKind.EndOfFileToken)
        {
            tryStatements.Add(ParseStatement());
        }

        SyntaxToken? exceptKeyword = null;
        var exceptionClauses = new List<ExceptionClauseSyntax>();
        var exceptStatements = new List<StatementSyntax>();
        SyntaxToken? finallyKeyword = null;
        var finallyStatements = new List<StatementSyntax>();

        if (Current.Kind == SyntaxKind.ExceptKeyword)
        {
            exceptKeyword = Match(SyntaxKind.ExceptKeyword);
            if (Current.Kind == SyntaxKind.OnKeyword)
            {
                while (Current.Kind == SyntaxKind.OnKeyword)
                {
                    var onKeyword = Match(SyntaxKind.OnKeyword);
                    var identifier = Match(SyntaxKind.IdentifierToken);
                    var colonToken = Match(SyntaxKind.ColonToken);
                    var typeName = ParseTypeName();
                    var doKeyword = Match(SyntaxKind.DoKeyword);
                    var body = ParseStatement();
                    exceptionClauses.Add(new ExceptionClauseSyntax(onKeyword, identifier, colonToken, typeName, doKeyword, body));
                }
            }

            while (Current.Kind is not SyntaxKind.FinallyKeyword and not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
            {
                exceptStatements.Add(ParseStatement());
            }
        }

        if (Current.Kind == SyntaxKind.FinallyKeyword)
        {
            finallyKeyword = Match(SyntaxKind.FinallyKeyword);
            while (Current.Kind is not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
            {
                finallyStatements.Add(ParseStatement());
            }
        }
        else if (exceptKeyword is null)
        {
            _diagnostics.Report(
                "ILC1004",
                "Expected 'except' or 'finally' in try statement.",
                DiagnosticSeverity.Error,
                Current.Span);
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(endKeyword.Span.Start + endKeyword.Span.Length, 0));
        return new TryStatementSyntax(tryKeyword, tryStatements, exceptKeyword, exceptionClauses, exceptStatements, finallyKeyword, finallyStatements, endKeyword, semicolon);
    }

    private IfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var condition = ParseExpression();
        var thenKeyword = Match(SyntaxKind.ThenKeyword);
        var thenStatement = ParseIfEmbeddedStatement();
        SyntaxToken? elseKeyword = null;
        StatementSyntax? elseStatement = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = NextToken();
            elseStatement = ParseIfEmbeddedStatement();
        }

        var semicolon = elseStatement is IfStatementSyntax || (elseStatement is null && thenStatement is IfStatementSyntax)
            ? new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0))
            : Match(SyntaxKind.SemicolonToken);
        return new IfStatementSyntax(ifKeyword, condition, thenKeyword, thenStatement, elseKeyword, elseStatement, semicolon);
    }

    private WhileStatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Match(SyntaxKind.WhileKeyword);
        var condition = ParseExpression();
        var doKeyword = Match(SyntaxKind.DoKeyword);
        var body = ParseStatement();
        return new WhileStatementSyntax(whileKeyword, condition, doKeyword, body);
    }

    private RepeatStatementSyntax ParseRepeatStatement()
    {
        var repeatKeyword = Match(SyntaxKind.RepeatKeyword);
        var statements = new List<StatementSyntax>();
        while (Current.Kind is not SyntaxKind.UntilKeyword and not SyntaxKind.EndOfFileToken)
        {
            statements.Add(ParseStatement());
        }

        var untilKeyword = Match(SyntaxKind.UntilKeyword);
        var condition = ParseExpression();
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new RepeatStatementSyntax(repeatKeyword, statements, untilKeyword, condition, semicolon);
    }

    private ForStatementSyntax ParseForStatement()
    {
        var forKeyword = Match(SyntaxKind.ForKeyword);
        SyntaxToken? varKeyword = null;
        if (Current.Kind == SyntaxKind.VarKeyword)
        {
            varKeyword = Match(SyntaxKind.VarKeyword);
        }

        var identifier = Match(SyntaxKind.IdentifierToken);
        var assignToken = Match(SyntaxKind.AssignToken);
        var lowerBound = ParseExpression();
        var toKeyword = Current.Kind == SyntaxKind.DowntoKeyword
            ? Match(SyntaxKind.DowntoKeyword)
            : Match(SyntaxKind.ToKeyword);
        var upperBound = ParseExpression();
        SyntaxToken? stepKeyword = null;
        ExpressionSyntax? stepExpression = null;
        if (Current.Kind == SyntaxKind.StepKeyword)
        {
            stepKeyword = Match(SyntaxKind.StepKeyword);
            stepExpression = ParseExpression();
        }

        var doKeyword = Match(SyntaxKind.DoKeyword);
        var body = ParseStatement();
        return new ForStatementSyntax(forKeyword, varKeyword, identifier, assignToken, lowerBound, toKeyword, upperBound, stepKeyword, stepExpression, doKeyword, body);
    }

    private ForeachStatementSyntax ParseForeachStatement()
    {
        SyntaxToken loopKeyword;
        SyntaxToken? eachKeyword = null;
        if (Current.Kind == SyntaxKind.ForeachKeyword)
        {
            loopKeyword = Match(SyntaxKind.ForeachKeyword);
        }
        else
        {
            loopKeyword = Match(SyntaxKind.ForKeyword);
            eachKeyword = Match(SyntaxKind.EachKeyword);
        }

        SyntaxToken? varKeyword = null;
        if (Current.Kind == SyntaxKind.VarKeyword)
        {
            varKeyword = Match(SyntaxKind.VarKeyword);
        }

        var identifier = Match(SyntaxKind.IdentifierToken);
        var inKeyword = Match(SyntaxKind.InKeyword);
        var collection = ParseExpression();
        var doKeyword = Match(SyntaxKind.DoKeyword);
        var body = ParseStatement();
        return new ForeachStatementSyntax(loopKeyword, eachKeyword, varKeyword, identifier, inKeyword, collection, doKeyword, body);
    }

    private WithStatementSyntax ParseWithStatement()
    {
        var withKeyword = Match(SyntaxKind.WithKeyword);
        var receiver = ParseExpression();
        var doKeyword = Match(SyntaxKind.DoKeyword);
        var body = ParseStatement();
        return new WithStatementSyntax(withKeyword, receiver, doKeyword, body);
    }

    private CaseStatementSyntax ParseCaseStatement(bool requireSemicolon = true)
    {
        var caseKeyword = Match(SyntaxKind.CaseKeyword);
        var expression = ParseExpression();
        var ofKeyword = Match(SyntaxKind.OfKeyword);
        var clauses = new List<CaseClauseSyntax>();
        SyntaxToken? elseKeyword = null;
        var elseStatements = new List<StatementSyntax>();

        while (Current.Kind is not SyntaxKind.ElseKeyword and not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
        {
            var labels = new List<ExpressionSyntax> { ParseCaseLabel() };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                labels.Add(ParseCaseLabel());
            }

            SyntaxToken? whenKeyword = null;
            ExpressionSyntax? guard = null;
            if (Current.Kind == SyntaxKind.WhenKeyword)
            {
                whenKeyword = Match(SyntaxKind.WhenKeyword);
                guard = ParseExpression();
            }

            var colonToken = Match(SyntaxKind.ColonToken);
            var body = ParseStatement();
            clauses.Add(new CaseClauseSyntax(labels, whenKeyword, guard, colonToken, body));
        }

        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = Match(SyntaxKind.ElseKeyword);
            while (Current.Kind is not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
            {
                elseStatements.Add(ParseStatement());
            }
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(endKeyword.Span.Start + endKeyword.Span.Length, 0));
        return new CaseStatementSyntax(caseKeyword, expression, ofKeyword, clauses, elseKeyword, elseStatements, endKeyword, semicolon);
    }

    private MatchStatementSyntax ParseMatchStatement(bool requireSemicolon = true)
    {
        var matchKeyword = Match(SyntaxKind.MatchKeyword);
        var expression = ParseExpression();
        var withKeyword = Match(SyntaxKind.WithKeyword);
        var arms = new List<MatchStatementArmSyntax>();
        SyntaxToken? elseKeyword = null;
        var elseStatements = new List<StatementSyntax>();

        while (Current.Kind is not SyntaxKind.ElseKeyword and not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
        {
            var (labels, isWildcard, typeName, identifier, whenKeyword, guard) = ParseMatchPattern();
            var arrowToken = Match(SyntaxKind.ArrowToken);
            var body = ParseStatement();
            arms.Add(new MatchStatementArmSyntax(labels, isWildcard, typeName, identifier, whenKeyword, guard, arrowToken, body));
        }

        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = Match(SyntaxKind.ElseKeyword);
            while (Current.Kind is not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
            {
                elseStatements.Add(ParseStatement());
            }
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        SyntaxToken? endMatchKeyword = null;
        if (Current.Kind == SyntaxKind.MatchKeyword)
        {
            endMatchKeyword = Match(SyntaxKind.MatchKeyword);
        }

        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan((endMatchKeyword ?? endKeyword).Span.End, 0));
        return new MatchStatementSyntax(matchKeyword, expression, withKeyword, arms, elseKeyword, elseStatements, endKeyword, endMatchKeyword, semicolon);
    }

    private (IReadOnlyList<ExpressionSyntax> Labels, bool IsWildcard, QualifiedNameSyntax? TypeName, SyntaxToken? Identifier, SyntaxToken? WhenKeyword, ExpressionSyntax? Guard) ParseMatchPattern()
    {
        if (Current.Kind == SyntaxKind.IdentifierToken &&
            Current.Text == "_" &&
            (Peek(1).Kind == SyntaxKind.ArrowToken || Peek(1).Kind == SyntaxKind.WhenKeyword))
        {
            NextToken();
            SyntaxToken? whenKeyword = null;
            ExpressionSyntax? guard = null;
            if (Current.Kind == SyntaxKind.WhenKeyword)
            {
                whenKeyword = Match(SyntaxKind.WhenKeyword);
                guard = ParseExpression();
            }

            return ([], true, null, null, whenKeyword, guard);
        }

        if (IsMatchTypePattern())
        {
            var typeName = ParseTypeName();
            var identifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? whenKeyword = null;
            ExpressionSyntax? guard = null;
            if (Current.Kind == SyntaxKind.WhenKeyword)
            {
                whenKeyword = Match(SyntaxKind.WhenKeyword);
                guard = ParseExpression();
            }

            return ([], false, typeName, identifier, whenKeyword, guard);
        }

        var labels = new List<ExpressionSyntax> { ParseMatchLabel() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            labels.Add(ParseMatchLabel());
        }

        SyntaxToken? labelsWhenKeyword = null;
        ExpressionSyntax? labelsGuard = null;
        if (Current.Kind == SyntaxKind.WhenKeyword)
        {
            labelsWhenKeyword = Match(SyntaxKind.WhenKeyword);
            labelsGuard = ParseExpression();
        }

        return (labels, false, null, null, labelsWhenKeyword, labelsGuard);
    }

    private bool IsMatchTypePattern()
    {
        if (Current.Kind != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        var offset = 1;
        while (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
        {
            offset += 2;
        }

        return Peek(offset).Kind == SyntaxKind.IdentifierToken;
    }

    private ExpressionSyntax ParseMatchLabel()
    {
        var firstPattern = ParseMatchAndLabel();
        if (Current.Kind != SyntaxKind.OrKeyword)
        {
            return firstPattern;
        }

        var patterns = new List<ExpressionSyntax> { firstPattern };
        while (Current.Kind == SyntaxKind.OrKeyword)
        {
            NextToken();
            patterns.Add(ParseMatchAndLabel());
        }

        return new MatchOrPatternSyntax(patterns);
    }

    private ExpressionSyntax ParseMatchAndLabel()
    {
        if (Current.Kind == SyntaxKind.NotKeyword)
        {
            var notKeyword = Match(SyntaxKind.NotKeyword);
            return new MatchNotPatternSyntax(notKeyword, ParseMatchLabel());
        }

        if (Current.Kind is SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken)
        {
            var patterns = new List<MatchRelationalPatternSyntax> { ParseMatchRelationalPattern() };
            while (Current.Kind == SyntaxKind.AndKeyword)
            {
                NextToken();
                patterns.Add(ParseMatchRelationalPattern());
            }

            return patterns.Count == 1
                ? patterns[0]
                : new MatchAndPatternSyntax(patterns);
        }

        var start = ParseComparisonExpression();
        if (Current.Kind == SyntaxKind.RangeToken)
        {
            var rangeToken = NextToken();
            var end = ParseComparisonExpression();
            return new RangeExpressionSyntax(start, rangeToken, end);
        }

        return start;
    }

    private MatchRelationalPatternSyntax ParseMatchRelationalPattern()
    {
        var operatorToken = NextToken();
        var operand = ParseComparisonExpression();
        return new MatchRelationalPatternSyntax(operatorToken, operand);
    }

    private ExpressionSyntax ParseCaseLabel()
    {
        var start = ParseExpression();
        if (Current.Kind == SyntaxKind.RangeToken)
        {
            var rangeToken = NextToken();
            var end = ParseExpression();
            return new RangeExpressionSyntax(start, rangeToken, end);
        }

        return start;
    }

    private LocalVariableDeclarationStatementSyntax ParseLocalVariableDeclarationStatement(bool requireSemicolon = true)
    {
        var keyword = Match(SyntaxKind.VarKeyword);
        var declarators = new List<VariableDeclaratorSyntax> { ParseVariableDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseVariableDeclarator());
        }

        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new LocalVariableDeclarationStatementSyntax(keyword, declarators, semicolon);
    }

    private ExpressionStatementSyntax ParseExpressionStatement(bool requireSemicolon = true)
    {
        var expression = ParseExpression();
        var semicolon = requireSemicolon
            ? Match(SyntaxKind.SemicolonToken)
            : new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, new TextSpan(Current.Span.Start, 0));
        return new ExpressionStatementSyntax(expression, semicolon);
    }

    private StatementSyntax ParseIfEmbeddedStatement() =>
        Current.Kind switch
        {
            SyntaxKind.BeginKeyword => ParseBlockStatement(false),
            SyntaxKind.ReturnKeyword or SyntaxKind.ExitKeyword => ParseReturnStatement(false),
            SyntaxKind.BreakKeyword => ParseBreakStatement(false),
            SyntaxKind.ContinueKeyword => ParseContinueStatement(false),
            SyntaxKind.IncKeyword => ParseIncDecStatement(true, false),
            SyntaxKind.DecKeyword => ParseIncDecStatement(false, false),
            SyntaxKind.IncludeKeyword => ParseIncludeExcludeStatement(true, false),
            SyntaxKind.ExcludeKeyword => ParseIncludeExcludeStatement(false, false),
            SyntaxKind.RaiseKeyword or SyntaxKind.ThrowKeyword => ParseRaiseStatement(false),
            SyntaxKind.TryKeyword => ParseTryStatement(false),
            SyntaxKind.CaseKeyword => ParseCaseStatement(false),
            SyntaxKind.MatchKeyword => ParseMatchStatement(false),
            SyntaxKind.VarKeyword => ParseLocalVariableDeclarationStatement(false),
            _ => ParseStatementWithoutRequiredSemicolon()
        };

    private StatementSyntax ParseStatementWithoutRequiredSemicolon() =>
        Current.Kind switch
        {
            SyntaxKind.BeginKeyword => ParseBlockStatement(false),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.RepeatKeyword => ParseRepeatStatement(),
            SyntaxKind.ForKeyword => ParseForStatement(),
            SyntaxKind.ForeachKeyword => ParseForeachStatement(),
            SyntaxKind.WithKeyword => ParseWithStatement(),
            SyntaxKind.CaseKeyword => ParseCaseStatement(false),
            SyntaxKind.MatchKeyword => ParseMatchStatement(false),
            SyntaxKind.ReturnKeyword or SyntaxKind.ExitKeyword => ParseReturnStatement(false),
            SyntaxKind.BreakKeyword => ParseBreakStatement(false),
            SyntaxKind.ContinueKeyword => ParseContinueStatement(false),
            SyntaxKind.IncKeyword => ParseIncDecStatement(true, false),
            SyntaxKind.DecKeyword => ParseIncDecStatement(false, false),
            SyntaxKind.IncludeKeyword => ParseIncludeExcludeStatement(true, false),
            SyntaxKind.ExcludeKeyword => ParseIncludeExcludeStatement(false, false),
            SyntaxKind.RaiseKeyword or SyntaxKind.ThrowKeyword => ParseRaiseStatement(false),
            SyntaxKind.TryKeyword => ParseTryStatement(false),
            SyntaxKind.VarKeyword => ParseLocalVariableDeclarationStatement(false),
            _ => ParseExpressionStatement(false)
        };

}
