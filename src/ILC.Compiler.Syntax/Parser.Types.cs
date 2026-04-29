namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private QualifiedNameSyntax ParseQualifiedName()
    {
        var parts = new List<SyntaxToken> { Match(SyntaxKind.IdentifierToken) };
        while (Current.Kind == SyntaxKind.DotToken)
        {
            NextToken();
            parts.Add(Match(SyntaxKind.IdentifierToken));
        }

        return new QualifiedNameSyntax(parts);
    }

    private QualifiedNameSyntax ParseQualifiedTypeName()
    {
        var parts = new List<SyntaxToken> { ParseQualifiedTypeNamePart() };
        while (Current.Kind == SyntaxKind.DotToken)
        {
            NextToken();
            parts.Add(ParseQualifiedTypeNamePart());
        }

        return new QualifiedNameSyntax(parts);
    }

    private SyntaxToken ParseQualifiedTypeNamePart()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        if (Current.Kind != SyntaxKind.LessToken)
        {
            return identifier;
        }

        var start = identifier.Span.Start;
        var builder = new List<string> { identifier.Text, "<" };
        NextToken();
        while (Current.Kind != SyntaxKind.GreaterToken && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind == SyntaxKind.CommaToken)
            {
                builder.Add(", ");
                NextToken();
                continue;
            }

            var typeArgument = ParseTypeName();
            builder.Add(typeArgument.ToDisplayString());
        }

        var closeToken = Match(SyntaxKind.GreaterToken);
        builder.Add(">");
        return identifier with
        {
            Text = string.Concat(builder),
            Span = new TextSpan(start, closeToken.Span.End - start)
        };
    }

    private TypeParameterListSyntax? ParseOptionalTypeParameterList()
    {
        if (Current.Kind != SyntaxKind.LessToken)
        {
            return null;
        }

        var lessThan = Match(SyntaxKind.LessToken);
        var parameters = new List<SyntaxToken> { Match(SyntaxKind.IdentifierToken) };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            parameters.Add(Match(SyntaxKind.IdentifierToken));
        }

        var greaterThan = Match(SyntaxKind.GreaterToken);
        return new TypeParameterListSyntax(lessThan, parameters, greaterThan);
    }

    private QualifiedNameSyntax ParseTypeName()
    {
        if (Current.Kind == SyntaxKind.SetKeyword)
        {
            var setKeyword = NextToken();
            Match(SyntaxKind.OfKeyword);
            var elementType = ParseTypeName();
            var endSpan = elementType.Parts[^1].Span.End;
            var token = new SyntaxToken(
                SyntaxKind.IdentifierToken,
                $"set of {elementType.ToDisplayString()}",
                null,
                new TextSpan(setKeyword.Span.Start, endSpan - setKeyword.Span.Start));
            return new QualifiedNameSyntax([token]);
        }

        if (Current.Kind == SyntaxKind.ArrayKeyword)
        {
            var arrayKeyword = NextToken();
            var rank = 1;
            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                NextToken();
                if (Current.Kind != SyntaxKind.CloseBracketToken)
                {
                    ParseExpression();
                    while (Current.Kind == SyntaxKind.CommaToken)
                    {
                        NextToken();
                        ParseExpression();
                        rank++;
                    }
                }

                Match(SyntaxKind.CloseBracketToken);
            }

            var ofKeyword = Match(SyntaxKind.OfKeyword);
            var elementType = ParseTypeName();
            var suffix = rank == 1 ? "[]" : $"[{new string(',', rank - 1)}]";
            return new QualifiedNameSyntax([
                new SyntaxToken(
                    SyntaxKind.IdentifierToken,
                    $"{elementType.ToDisplayString()}{suffix}",
                    null,
                    new TextSpan(arrayKeyword.Span.Start, elementType.Parts[^1].Span.End - arrayKeyword.Span.Start))
            ]);
        }

        var name = ParseQualifiedTypeName();
        if (Current.Kind == SyntaxKind.OpenBracketToken && Peek(1).Kind == SyntaxKind.CloseBracketToken)
        {
            var openBracket = NextToken();
            var closeBracket = NextToken();
            var lastPart = name.Parts[^1];
            var mergedPart = lastPart with
            {
                Text = $"{lastPart.Text}[]",
                Span = new TextSpan(lastPart.Span.Start, closeBracket.Span.End - lastPart.Span.Start)
            };

            var parts = name.Parts.Take(name.Parts.Count - 1).Append(mergedPart).ToArray();
            return new QualifiedNameSyntax(parts);
        }

        return name;
    }

    private ExpressionSyntax ParseAssignableTarget()
    {
        ExpressionSyntax target = new NameExpressionSyntax(ParseQualifiedName());
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
                target = target switch
                {
                    NameExpressionSyntax targetName => new ElementAccessExpressionSyntax(targetName.Name, openBracket, indexExpressions, closeBracket),
                    _ => new PostfixElementAccessExpressionSyntax(target, openBracket, indexExpressions, closeBracket)
                };
                continue;
            }

            if (Current.Kind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
            {
                var dotToken = NextToken();
                var memberName = Match(SyntaxKind.IdentifierToken);
                target = new MemberAccessExpressionSyntax(target, dotToken, memberName);
                continue;
            }

            break;
        }

        return target;
    }

}
