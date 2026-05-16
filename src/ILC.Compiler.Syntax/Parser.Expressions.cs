namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private ExpressionSyntax ParseExpression() => ParseAssignmentExpression();

    private ExpressionSyntax ParseAssignmentExpression()
    {
        if (IsIdentifierLike(Current.Kind) && IsAssignmentTarget())
        {
            var start = _position;
            var target = ParseAssignableTarget();
            if (Current.Kind == SyntaxKind.AssignToken)
            {
                var assignToken = NextToken();
                var expression = ParseAssignmentExpression();
                return new AssignmentExpressionSyntax(target, assignToken, expression);
            }
            else if (Current.Kind is SyntaxKind.PlusAssignToken or SyntaxKind.MinusAssignToken or SyntaxKind.StarAssignToken or SyntaxKind.SlashAssignToken or SyntaxKind.DivAssignToken or SyntaxKind.ModAssignToken or SyntaxKind.AndAssignToken or SyntaxKind.OrAssignToken or SyntaxKind.XorAssignToken or SyntaxKind.ShlAssignToken or SyntaxKind.ShrAssignToken or SyntaxKind.NullCoalescingAssignToken)
            {
                var operatorToken = NextToken();
                var expression = ParseAssignmentExpression();
                return new CompoundAssignmentExpressionSyntax(target, operatorToken, expression);
            }

            _position = start;
        }

        return ParseNullCoalescingExpression();
    }

    private ExpressionSyntax ParseNullCoalescingExpression()
    {
        var left = ParseLogicalExpression();
        if (Current.Kind == SyntaxKind.NullCoalescingToken)
        {
            var operatorToken = NextToken();
            var right = ParseNullCoalescingExpression();
            return new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseLogicalExpression()
    {
        var left = ParseComparisonExpression();
        while (Current.Kind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.XorKeyword)
        {
            var operatorToken = NextToken();
            var right = ParseComparisonExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseComparisonExpression()
    {
        var left = ParseShiftExpression();
        while (Current.Kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken
            or SyntaxKind.InKeyword
            or SyntaxKind.NotKeyword
            or SyntaxKind.AsKeyword
            or SyntaxKind.IsKeyword)
        {
            if (Current.Kind == SyntaxKind.AsKeyword)
            {
                var asKeyword = NextToken();
                var typeName = ParseTypeName();
                left = new AsExpressionSyntax(left, asKeyword, typeName);
                continue;
            }

            if (Current.Kind == SyntaxKind.IsKeyword)
            {
                var isKeyword = NextToken();
                var typeName = ParseTypeName();
                left = new TypeTestExpressionSyntax(left, isKeyword, typeName);
                continue;
            }

            if (Current.Kind == SyntaxKind.NotKeyword && Peek(1).Kind == SyntaxKind.InKeyword)
            {
                var notKeyword = NextToken();
                var inKeyword = Match(SyntaxKind.InKeyword);
                var notInOperatorToken = new SyntaxToken(
                    SyntaxKind.NotInKeyword,
                    "not in",
                    null,
                    new TextSpan(notKeyword.Span.Start, (inKeyword.Span.Start + inKeyword.Span.Length) - notKeyword.Span.Start));
                var notInRight = ParseShiftExpression();
                left = new BinaryExpressionSyntax(left, notInOperatorToken, notInRight);
                continue;
            }

            var operatorToken = NextToken();
            var right = ParseShiftExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseShiftExpression()
    {
        var left = ParseAdditiveExpression();
        while (Current.Kind is SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword)
        {
            var operatorToken = NextToken();
            var right = ParseAdditiveExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseAdditiveExpression()
    {
        var left = ParseMultiplicativeExpression();
        while (Current.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken)
        {
            var operatorToken = NextToken();
            var right = ParseMultiplicativeExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseMultiplicativeExpression()
    {
        var left = ParseUnaryExpression();
        while (Current.Kind is SyntaxKind.StarToken or SyntaxKind.SlashToken or SyntaxKind.DivKeyword or SyntaxKind.ModKeyword)
        {
            var operatorToken = NextToken();
            var right = ParseUnaryExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParseUnaryExpression()
    {
        if (Current.Kind is SyntaxKind.NotKeyword or SyntaxKind.MinusToken or SyntaxKind.PlusToken)
        {
            var operatorToken = NextToken();
            var operand = ParseUnaryExpression();
            return new UnaryExpressionSyntax(operatorToken, operand);
        }

        return ParsePrimaryExpression();
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        if (Current.Kind == SyntaxKind.MatchKeyword)
        {
            return ParseMatchExpression();
        }

        if (Current.Kind == SyntaxKind.FromKeyword)
        {
            return ParseQueryExpression();
        }

        if (Current.Kind is SyntaxKind.FunctionKeyword or SyntaxKind.ProcedureKeyword)
        {
            return ParseLambdaExpression();
        }

        if (Current.Kind == SyntaxKind.OpenParenToken)
        {
            var openParen = NextToken();
            var innerExpression = ParseExpression();
            var closeParen = Match(SyntaxKind.CloseParenToken);
            return ParsePostfixExpression(new ParenthesizedExpressionSyntax(openParen, innerExpression, closeParen));
        }

        if (Current.Kind is SyntaxKind.NumberToken or SyntaxKind.StringToken or SyntaxKind.NilKeyword or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword)
        {
            return new LiteralExpressionSyntax(NextToken());
        }

        if (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            var openBracket = NextToken();
            var elements = new List<ExpressionSyntax>();
            if (Current.Kind != SyntaxKind.CloseBracketToken)
            {
                elements.Add(ParseSetLiteralElement());
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    elements.Add(ParseSetLiteralElement());
                }
            }

            var closeBracket = Match(SyntaxKind.CloseBracketToken);
            return new SetLiteralExpressionSyntax(openBracket, elements, closeBracket);
        }

        if (Current.Kind == SyntaxKind.NewKeyword)
        {
            var newKeyword = NextToken();
            if (Current.Kind == SyntaxKind.OpenBraceToken)
            {
                var openBrace = Match(SyntaxKind.OpenBraceToken);
                var members = new List<ProjectorMemberSyntax>();
                if (Current.Kind != SyntaxKind.CloseBraceToken)
                {
                    members.Add(ParseProjectorMember());
                    while (Current.Kind == SyntaxKind.CommaToken)
                    {
                        NextToken();
                        members.Add(ParseProjectorMember());
                    }
                }

                var closeBrace = Match(SyntaxKind.CloseBraceToken);
                return ParsePostfixExpression(new ProjectorExpressionSyntax(newKeyword, openBrace, members, closeBrace));
            }

            var typeName = ParseTypeName();
            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                var openBracket = NextToken();
                var lengthExpressions = new List<ExpressionSyntax> { ParseExpression() };
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    lengthExpressions.Add(ParseExpression());
                }

                var closeBracket = Match(SyntaxKind.CloseBracketToken);
                return new NewArrayExpressionSyntax(newKeyword, typeName, openBracket, lengthExpressions, closeBracket);
            }

            var openParen = Match(SyntaxKind.OpenParenToken);
            var arguments = new List<ArgumentSyntax>();
            if (Current.Kind != SyntaxKind.CloseParenToken)
            {
                arguments.Add(ParseArgument());
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    arguments.Add(ParseArgument());
                }
            }

            var closeParen = Match(SyntaxKind.CloseParenToken);
            return ParsePostfixExpression(new NewExpressionSyntax(newKeyword, typeName, openParen, arguments, closeParen));
        }

        var name = ParseExpressionQualifiedName();
        ExpressionSyntax expression;
        if (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            var openBracket = NextToken();
            var indexExpressions = new List<ExpressionSyntax> { ParseIndexExpression() };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                indexExpressions.Add(ParseIndexExpression());
            }

            var closeBracket = Match(SyntaxKind.CloseBracketToken);
            expression = new ElementAccessExpressionSyntax(name, openBracket, indexExpressions, closeBracket);
            return ParsePostfixExpression(expression);
        }

        if (Current.Kind == SyntaxKind.OpenParenToken && name.Parts.Count >= 2)
        {
            expression = BuildMemberAccessExpression(name);
            return ParsePostfixExpression(expression);
        }

        if (name.Parts.Count >= 2 && string.Equals(name.Parts[^1].Text, "Length", StringComparison.OrdinalIgnoreCase))
        {
            expression = new ArrayLengthExpressionSyntax(new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray()));
            return ParsePostfixExpression(expression);
        }

        expression = new NameExpressionSyntax(name);
        return ParsePostfixExpression(expression);
    }

    private ProjectorMemberSyntax ParseProjectorMember()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        var assignToken = Match(SyntaxKind.AssignToken);
        var expression = ParseExpression();
        return new ProjectorMemberSyntax(identifier, assignToken, expression);
    }

    private QualifiedNameSyntax ParseExpressionQualifiedName()
    {
        var sawGeneric = false;
        if (TryScanExpressionQualifiedTypeName(_position, out var nextPosition, ref sawGeneric) &&
            sawGeneric &&
            PeekAbsolute(nextPosition).Kind == SyntaxKind.DotToken)
        {
            return ParseExpressionQualifiedTypeReceiver(nextPosition);
        }

        return ParseQualifiedName();
    }

    private bool LooksLikeQualifiedTypeNameExpression()
    {
        var sawGeneric = false;
        return TryScanExpressionQualifiedTypeName(_position, out var nextPosition, ref sawGeneric) &&
               sawGeneric &&
               PeekAbsolute(nextPosition).Kind == SyntaxKind.DotToken;
    }

    private bool TryScanExpressionQualifiedTypeName(int position, out int nextPosition, ref bool sawGeneric)
    {
        if (!TryScanExpressionQualifiedTypeNamePart(position, out position, ref sawGeneric))
        {
            nextPosition = position;
            return false;
        }

        while (PeekAbsolute(position).Kind == SyntaxKind.DotToken &&
               IsIdentifierLike(PeekAbsolute(position + 1).Kind))
        {
            if (sawGeneric && PeekAbsolute(position + 2).Kind != SyntaxKind.LessToken)
            {
                break;
            }

            position++;
            if (!TryScanExpressionQualifiedTypeNamePart(position, out position, ref sawGeneric))
            {
                nextPosition = position;
                return false;
            }
        }

        nextPosition = position;
        return true;
    }

    private bool TryScanExpressionQualifiedTypeNamePart(int position, out int nextPosition, ref bool sawGeneric)
    {
        if (!IsIdentifierLike(PeekAbsolute(position).Kind))
        {
            nextPosition = position;
            return false;
        }

        position++;
        if (PeekAbsolute(position).Kind == SyntaxKind.LessToken)
        {
            if (!TryScanExpressionTypeArgumentList(position, out position, ref sawGeneric))
            {
                nextPosition = position;
                return false;
            }
        }

        nextPosition = position;
        return true;
    }

    private bool TryScanExpressionTypeArgumentList(int position, out int nextPosition, ref bool sawGeneric)
    {
        if (PeekAbsolute(position).Kind != SyntaxKind.LessToken)
        {
            nextPosition = position;
            return false;
        }

        position++;
        if (!TryScanExpressionQualifiedTypeName(position, out position, ref sawGeneric))
        {
            nextPosition = position;
            return false;
        }

        while (PeekAbsolute(position).Kind == SyntaxKind.CommaToken)
        {
            position++;
            if (!TryScanExpressionQualifiedTypeName(position, out position, ref sawGeneric))
            {
                nextPosition = position;
                return false;
            }
        }

        if (PeekAbsolute(position).Kind != SyntaxKind.GreaterToken)
        {
            nextPosition = position;
            return false;
        }

        sawGeneric = true;
        nextPosition = position + 1;
        return true;
    }

    private QualifiedNameSyntax ParseExpressionQualifiedTypeReceiver(int stopPosition)
    {
        var parts = new List<SyntaxToken> { ParseQualifiedTypeNamePart() };
        while (_position < stopPosition && Current.Kind == SyntaxKind.DotToken)
        {
            if (parts.Count > 0 &&
                parts.Any(part => part.Text.Contains('<', StringComparison.Ordinal)) &&
                IsIdentifierLike(Peek(1).Kind) &&
                Peek(2).Kind != SyntaxKind.LessToken)
            {
                break;
            }

            NextToken();
            parts.Add(ParseQualifiedTypeNamePart());
        }

        return new QualifiedNameSyntax(parts);
    }

    private LambdaExpressionSyntax ParseLambdaExpression()
    {
        var signatureKeyword = Current.Kind switch
        {
            SyntaxKind.FunctionKeyword => Match(SyntaxKind.FunctionKeyword),
            SyntaxKind.ProcedureKeyword => Match(SyntaxKind.ProcedureKeyword),
            _ => Match(SyntaxKind.FunctionKeyword)
        };

        SyntaxToken? openParen = null;
        var parameters = new List<ParameterSyntax>();
        SyntaxToken? closeParen = null;
        if (Current.Kind == SyntaxKind.OpenParenToken)
        {
            openParen = NextToken();
            if (Current.Kind != SyntaxKind.CloseParenToken)
            {
                parameters.Add(ParseParameter());
                while (Current.Kind is SyntaxKind.CommaToken or SyntaxKind.SemicolonToken)
                {
                    NextToken();
                    parameters.Add(ParseParameter());
                }
            }

            closeParen = Match(SyntaxKind.CloseParenToken);
        }

        SyntaxToken? colonToken = null;
        QualifiedNameSyntax? returnType = null;
        if (signatureKeyword.Kind == SyntaxKind.FunctionKeyword && Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = NextToken();
            returnType = ParseTypeName();
        }

        var arrowToken = Match(SyntaxKind.ArrowToken);
        var body = ParseExpression();
        return new LambdaExpressionSyntax(signatureKeyword, openParen, parameters, closeParen, colonToken, returnType, arrowToken, body);
    }

    private QueryExpressionSyntax ParseQueryExpression()
    {
        var fromKeyword = Match(SyntaxKind.FromKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var inKeyword = Match(SyntaxKind.InKeyword);
        var sourceExpression = ParseExpression();

        SyntaxToken? joinKeyword = null;
        SyntaxToken? joinIdentifier = null;
        SyntaxToken? joinInKeyword = null;
        ExpressionSyntax? joinSourceExpression = null;
        SyntaxToken? joinOnKeyword = null;
        ExpressionSyntax? joinLeftExpression = null;
        SyntaxToken? joinEqualsKeyword = null;
        ExpressionSyntax? joinRightExpression = null;
        SyntaxToken? joinIntoKeyword = null;
        SyntaxToken? joinIntoIdentifier = null;
        if (Current.Kind == SyntaxKind.JoinKeyword)
        {
            joinKeyword = Match(SyntaxKind.JoinKeyword);
            joinIdentifier = Match(SyntaxKind.IdentifierToken);
            joinInKeyword = Match(SyntaxKind.InKeyword);
            joinSourceExpression = ParseExpression();
            joinOnKeyword = Match(SyntaxKind.OnKeyword);
            joinLeftExpression = ParseExpression();
            if (IsIdentifierLike(Current.Kind) &&
                string.Equals(Current.Text, "equals", StringComparison.OrdinalIgnoreCase))
            {
                joinEqualsKeyword = NextToken();
            }
            else
            {
                joinEqualsKeyword = Match(SyntaxKind.IdentifierToken);
            }

            joinRightExpression = ParseExpression();
            if (Current.Kind == SyntaxKind.IntoKeyword)
            {
                joinIntoKeyword = Match(SyntaxKind.IntoKeyword);
                joinIntoIdentifier = Match(SyntaxKind.IdentifierToken);
            }
        }

        SyntaxToken? secondFromKeyword = null;
        SyntaxToken? secondIdentifier = null;
        SyntaxToken? secondInKeyword = null;
        ExpressionSyntax? secondSourceExpression = null;
        if (joinKeyword is null && Current.Kind == SyntaxKind.FromKeyword)
        {
            secondFromKeyword = Match(SyntaxKind.FromKeyword);
            secondIdentifier = Match(SyntaxKind.IdentifierToken);
            secondInKeyword = Match(SyntaxKind.InKeyword);
            secondSourceExpression = ParseExpression();
        }

        SyntaxToken? letKeyword = null;
        SyntaxToken? letIdentifier = null;
        SyntaxToken? letAssignToken = null;
        ExpressionSyntax? letExpression = null;
        if (Current.Kind == SyntaxKind.LetKeyword)
        {
            letKeyword = Match(SyntaxKind.LetKeyword);
            letIdentifier = Match(SyntaxKind.IdentifierToken);
            letAssignToken = Match(SyntaxKind.AssignToken);
            letExpression = ParseExpression();
        }

        SyntaxToken? whereKeyword = null;
        ExpressionSyntax? predicateExpression = null;
        if (Current.Kind == SyntaxKind.WhereKeyword)
        {
            whereKeyword = Match(SyntaxKind.WhereKeyword);
            predicateExpression = ParseExpression();
        }

        SyntaxToken? orderByKeyword = null;
        ExpressionSyntax? orderByExpression = null;
        SyntaxToken? descendingKeyword = null;
        SyntaxToken? thenByCommaToken = null;
        SyntaxToken? thenByKeyword = null;
        ExpressionSyntax? thenByExpression = null;
        SyntaxToken? thenByDescendingKeyword = null;
        if (Current.Kind == SyntaxKind.OrderByKeyword)
        {
            orderByKeyword = Match(SyntaxKind.OrderByKeyword);
            orderByExpression = ParseExpression();
            if (IsIdentifierLike(Current.Kind) &&
                string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
            {
                descendingKeyword = NextToken();
            }

            if (Current.Kind == SyntaxKind.CommaToken)
            {
                thenByCommaToken = Match(SyntaxKind.CommaToken);
                thenByExpression = ParseExpression();
                if (IsIdentifierLike(Current.Kind) &&
                    string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
                {
                    thenByDescendingKeyword = NextToken();
                }
            }
            else if (IsIdentifierLike(Current.Kind) &&
                     string.Equals(Current.Text, "thenby", StringComparison.OrdinalIgnoreCase))
            {
                thenByKeyword = NextToken();
                thenByExpression = ParseExpression();
                if (IsIdentifierLike(Current.Kind) &&
                    string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
                {
                    thenByDescendingKeyword = NextToken();
                }
            }
        }

        SyntaxToken? groupKeyword = null;
        ExpressionSyntax? groupExpression = null;
        SyntaxToken? groupByKeyword = null;
        ExpressionSyntax? groupByExpression = null;
        SyntaxToken selectKeyword;
        ExpressionSyntax selectExpression;
        if (IsIdentifierLike(Current.Kind) &&
            string.Equals(Current.Text, "group", StringComparison.OrdinalIgnoreCase))
        {
            groupKeyword = NextToken();
            groupExpression = ParseExpression();
            if (IsIdentifierLike(Current.Kind) &&
                string.Equals(Current.Text, "by", StringComparison.OrdinalIgnoreCase))
            {
                groupByKeyword = NextToken();
            }
            else
            {
                groupByKeyword = Match(SyntaxKind.IdentifierToken);
            }

            groupByExpression = ParseExpression();
            selectKeyword = new SyntaxToken(SyntaxKind.SelectKeyword, "select", null, new TextSpan(0, 0));
            selectExpression = groupExpression;
        }
        else
        {
            selectKeyword = Match(SyntaxKind.SelectKeyword);
            selectExpression = ParseExpression();
        }

        SyntaxToken? intoKeyword = null;
        SyntaxToken? intoIdentifier = null;
        SyntaxToken? continuationLetKeyword = null;
        SyntaxToken? continuationLetIdentifier = null;
        SyntaxToken? continuationLetAssignToken = null;
        ExpressionSyntax? continuationLetExpression = null;
        SyntaxToken? continuationWhereKeyword = null;
        ExpressionSyntax? continuationPredicateExpression = null;
        SyntaxToken? continuationOrderByKeyword = null;
        ExpressionSyntax? continuationOrderByExpression = null;
        SyntaxToken? continuationDescendingKeyword = null;
        SyntaxToken? continuationThenByCommaToken = null;
        SyntaxToken? continuationThenByKeyword = null;
        ExpressionSyntax? continuationThenByExpression = null;
        SyntaxToken? continuationThenByDescendingKeyword = null;
        SyntaxToken? continuationSelectKeyword = null;
        ExpressionSyntax? continuationSelectExpression = null;
        if (Current.Kind == SyntaxKind.IntoKeyword)
        {
            intoKeyword = Match(SyntaxKind.IntoKeyword);
            intoIdentifier = Match(SyntaxKind.IdentifierToken);
            if (Current.Kind == SyntaxKind.LetKeyword)
            {
                continuationLetKeyword = Match(SyntaxKind.LetKeyword);
                continuationLetIdentifier = Match(SyntaxKind.IdentifierToken);
                continuationLetAssignToken = Match(SyntaxKind.AssignToken);
                continuationLetExpression = ParseExpression();
            }

            if (Current.Kind == SyntaxKind.WhereKeyword)
            {
                continuationWhereKeyword = Match(SyntaxKind.WhereKeyword);
                continuationPredicateExpression = ParseExpression();
            }

            if (Current.Kind == SyntaxKind.OrderByKeyword)
            {
                continuationOrderByKeyword = Match(SyntaxKind.OrderByKeyword);
                continuationOrderByExpression = ParseExpression();
                if (IsIdentifierLike(Current.Kind) &&
                    string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
                {
                    continuationDescendingKeyword = NextToken();
                }

                if (Current.Kind == SyntaxKind.CommaToken)
                {
                    continuationThenByCommaToken = Match(SyntaxKind.CommaToken);
                    continuationThenByExpression = ParseExpression();
                    if (IsIdentifierLike(Current.Kind) &&
                        string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
                    {
                        continuationThenByDescendingKeyword = NextToken();
                    }
                }
                else if (IsIdentifierLike(Current.Kind) &&
                         string.Equals(Current.Text, "thenby", StringComparison.OrdinalIgnoreCase))
                {
                    continuationThenByKeyword = NextToken();
                    continuationThenByExpression = ParseExpression();
                    if (IsIdentifierLike(Current.Kind) &&
                        string.Equals(Current.Text, "descending", StringComparison.OrdinalIgnoreCase))
                    {
                        continuationThenByDescendingKeyword = NextToken();
                    }
                }
            }

            continuationSelectKeyword = Match(SyntaxKind.SelectKeyword);
            continuationSelectExpression = ParseExpression();
        }

        SyntaxToken? takeKeyword = null;
        ExpressionSyntax? takeExpression = null;
        if (Current.Kind == SyntaxKind.TakeKeyword)
        {
            takeKeyword = Match(SyntaxKind.TakeKeyword);
            takeExpression = ParseExpression();
        }

        SyntaxToken? skipKeyword = null;
        ExpressionSyntax? skipExpression = null;
        if (Current.Kind == SyntaxKind.SkipKeyword)
        {
            skipKeyword = Match(SyntaxKind.SkipKeyword);
            skipExpression = ParseExpression();
        }

        return new QueryExpressionSyntax(
            fromKeyword,
            identifier,
            inKeyword,
            sourceExpression,
            joinKeyword,
            joinIdentifier,
            joinInKeyword,
            joinSourceExpression,
            joinOnKeyword,
            joinLeftExpression,
            joinEqualsKeyword,
            joinRightExpression,
            joinIntoKeyword,
            joinIntoIdentifier,
            secondFromKeyword,
            secondIdentifier,
            secondInKeyword,
            secondSourceExpression,
            letKeyword,
            letIdentifier,
            letAssignToken,
            letExpression,
            whereKeyword,
            predicateExpression,
            orderByKeyword,
            orderByExpression,
            descendingKeyword,
            thenByCommaToken,
            thenByKeyword,
            thenByExpression,
            thenByDescendingKeyword,
            groupKeyword,
            groupExpression,
            groupByKeyword,
            groupByExpression,
            selectKeyword,
            selectExpression,
            intoKeyword,
            intoIdentifier,
            continuationLetKeyword,
            continuationLetIdentifier,
            continuationLetAssignToken,
            continuationLetExpression,
            continuationWhereKeyword,
            continuationPredicateExpression,
            continuationOrderByKeyword,
            continuationOrderByExpression,
            continuationDescendingKeyword,
            continuationThenByCommaToken,
            continuationThenByKeyword,
            continuationThenByExpression,
            continuationThenByDescendingKeyword,
            continuationSelectKeyword,
            continuationSelectExpression,
            takeKeyword,
            takeExpression,
            skipKeyword,
            skipExpression);
    }

    private MatchExpressionSyntax ParseMatchExpression()
    {
        var matchKeyword = Match(SyntaxKind.MatchKeyword);
        var expression = ParseExpression();
        var withKeyword = Match(SyntaxKind.WithKeyword);
        var arms = new List<MatchExpressionArmSyntax>();
        while (Current.Kind is not SyntaxKind.EndKeyword and not SyntaxKind.EndOfFileToken)
        {
            var (labels, isWildcard, typeName, identifier, whenKeyword, guard) = ParseMatchPattern();
            var arrowToken = Match(SyntaxKind.ArrowToken);
            var armExpression = ParseExpression();
            arms.Add(new MatchExpressionArmSyntax(labels, isWildcard, typeName, identifier, whenKeyword, guard, arrowToken, armExpression));
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        return new MatchExpressionSyntax(matchKeyword, expression, withKeyword, arms, endKeyword);
    }

    private ExpressionSyntax ParsePostfixExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                var openBracket = NextToken();
                var indexExpressions = new List<ExpressionSyntax> { ParseIndexExpression() };
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    indexExpressions.Add(ParseIndexExpression());
                }

                var closeBracket = Match(SyntaxKind.CloseBracketToken);
                expression = expression is NameExpressionSyntax nameExpression
                    ? new ElementAccessExpressionSyntax(nameExpression.Name, openBracket, indexExpressions, closeBracket)
                    : new PostfixElementAccessExpressionSyntax(expression, openBracket, indexExpressions, closeBracket);
                continue;
            }

            if (Current.Kind == SyntaxKind.DotToken && IsIdentifierLike(Peek(1).Kind))
            {
                var dotToken = NextToken();
                var memberName = Match(SyntaxKind.IdentifierToken);
                expression = new MemberAccessExpressionSyntax(expression, dotToken, memberName);
                continue;
            }

            if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                var openParen = NextToken();
                var arguments = new List<ArgumentSyntax>();
                if (Current.Kind != SyntaxKind.CloseParenToken)
                {
                    arguments.Add(ParseArgument());
                    while (Current.Kind == SyntaxKind.CommaToken)
                    {
                        NextToken();
                        arguments.Add(ParseArgument());
                    }
                }

                var closeParen = Match(SyntaxKind.CloseParenToken);
                expression = new CallExpressionSyntax(expression, openParen, arguments, closeParen);
                continue;
            }

            break;
        }

        return expression;
    }

    private ArgumentSyntax ParseArgument()
    {
        SyntaxToken? modifierKeyword = null;
        if (Current.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword or SyntaxKind.InKeyword)
        {
            modifierKeyword = NextToken();
        }

        return new ArgumentSyntax(ParseExpression(), modifierKeyword);
    }

    private static ExpressionSyntax BuildMemberAccessExpression(QualifiedNameSyntax name)
    {
        ExpressionSyntax expression = new NameExpressionSyntax(new QualifiedNameSyntax([name.Parts[0]]));
        for (var index = 1; index < name.Parts.Count; index++)
        {
            var memberName = name.Parts[index];
            var dotToken = new SyntaxToken(SyntaxKind.DotToken, ".", null, memberName.Span);
            expression = new MemberAccessExpressionSyntax(expression, dotToken, memberName);
        }

        return expression;
    }

    private ExpressionSyntax ParseIndexExpression()
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

    private ExpressionSyntax ParseSetLiteralElement()
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

}
