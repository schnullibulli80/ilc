namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private TypeMemberSyntax ParseTypeMember()
    {
        var attributes = ParseAttributes();
        var modifiers = ParseModifiers();
        if (attributes.Count > 0 &&
            Current.Kind is not (SyntaxKind.MethodKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ProcedureKeyword or SyntaxKind.ConstructorKeyword))
        {
            _diagnostics.Report(
                "ILC1004",
                "Attributes are currently only supported on methods.",
                DiagnosticSeverity.Error,
                attributes[0].OpenBracketToken.Span);
        }

        return Current.Kind switch
        {
            SyntaxKind.VarKeyword => ParseFieldDeclaration(modifiers),
            SyntaxKind.ConstKeyword => ParseConstantDeclaration(modifiers),
            SyntaxKind.PropertyKeyword => ParsePropertyDeclaration(modifiers),
            SyntaxKind.MethodKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ProcedureKeyword or SyntaxKind.ConstructorKeyword => ParseMethodDeclaration(attributes, modifiers),
            _ => ParseMethodDeclaration(attributes, modifiers)
        };
    }

    private IReadOnlyList<AttributeSyntax> ParseAttributes()
    {
        var attributes = new List<AttributeSyntax>();
        while (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            var openBracketToken = NextToken();
            var name = ParseQualifiedName();
            SyntaxToken? openParenToken = null;
            var arguments = new List<ArgumentSyntax>();
            SyntaxToken? closeParenToken = null;
            if (Current.Kind == SyntaxKind.OpenParenToken)
            {
                openParenToken = NextToken();
                if (Current.Kind != SyntaxKind.CloseParenToken)
                {
                    arguments.Add(ParseArgument());
                    while (Current.Kind is SyntaxKind.CommaToken or SyntaxKind.SemicolonToken)
                    {
                        NextToken();
                        arguments.Add(ParseArgument());
                    }
                }

                closeParenToken = Match(SyntaxKind.CloseParenToken);
            }

            var closeBracketToken = Match(SyntaxKind.CloseBracketToken);
            attributes.Add(new AttributeSyntax(openBracketToken, name, openParenToken, arguments, closeParenToken, closeBracketToken));
        }

        return attributes;
    }

    private FieldDeclarationSyntax ParseFieldDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var keyword = Match(SyntaxKind.VarKeyword);
        var declarators = new List<VariableDeclaratorSyntax> { ParseVariableDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseVariableDeclarator());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new FieldDeclarationSyntax(modifiers, keyword, declarators, semicolon);
    }

    private ConstantDeclarationSyntax ParseConstantDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var keyword = Match(SyntaxKind.ConstKeyword);
        var declarators = new List<ConstantDeclaratorSyntax> { ParseConstantDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseConstantDeclarator());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new ConstantDeclarationSyntax(modifiers, keyword, declarators, semicolon);
    }

    private ConstantDeclaratorSyntax ParseConstantDeclarator()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? colonToken = null;
        QualifiedNameSyntax? typeName = null;
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = NextToken();
            typeName = ParseTypeName();
        }

        var equalsToken = Match(SyntaxKind.EqualsToken);
        var initializer = ParseExpression();
        return new ConstantDeclaratorSyntax(identifier, colonToken, typeName, equalsToken, initializer);
    }

    private PropertyDeclarationSyntax ParsePropertyDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var propertyKeyword = Match(SyntaxKind.PropertyKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? openBracketToken = null;
        ParameterSyntax? indexParameter = null;
        SyntaxToken? closeBracketToken = null;
        if (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            openBracketToken = NextToken();
            indexParameter = ParseParameter();
            closeBracketToken = Match(SyntaxKind.CloseBracketToken);
        }

        var colonToken = Match(SyntaxKind.ColonToken);
        var typeName = ParseTypeName();
        SyntaxToken? readKeyword = null;
        QualifiedNameSyntax? readTarget = null;
        SyntaxToken? writeKeyword = null;
        QualifiedNameSyntax? writeTarget = null;
        SyntaxToken? openBraceToken = null;
        IReadOnlyList<SyntaxToken> getterModifiers = [];
        SyntaxToken? getKeyword = null;
        SyntaxToken? getSemicolonToken = null;
        IReadOnlyList<SyntaxToken> setterModifiers = [];
        SyntaxToken? setKeyword = null;
        SyntaxToken? setSemicolonToken = null;
        IReadOnlyList<SyntaxToken> initModifiers = [];
        SyntaxToken? initKeyword = null;
        SyntaxToken? initSemicolonToken = null;
        SyntaxToken? closeBraceToken = null;
        SyntaxToken? beginKeyword = null;
        IReadOnlyList<SyntaxToken> getterBlockModifiers = [];
        SyntaxToken? getterKeyword = null;
        BlockStatementSyntax? getterBody = null;
        IReadOnlyList<SyntaxToken> setterBlockModifiers = [];
        SyntaxToken? setterKeyword = null;
        SyntaxToken? setterParameter = null;
        BlockStatementSyntax? setterBody = null;
        SyntaxToken? endKeyword = null;

        if (Current.Kind == SyntaxKind.OpenBraceToken)
        {
            openBraceToken = NextToken();
            getterModifiers = ParseModifiers();
            getKeyword = Match(SyntaxKind.GetKeyword);
            getSemicolonToken = Match(SyntaxKind.SemicolonToken);
            setterModifiers = ParseModifiers();
            if (Current.Kind == SyntaxKind.SetKeyword)
            {
                setKeyword = NextToken();
                setSemicolonToken = Match(SyntaxKind.SemicolonToken);
            }
            else
            {
                initModifiers = setterModifiers;
                if (Current.Kind == SyntaxKind.InitKeyword)
                {
                    initKeyword = NextToken();
                    initSemicolonToken = Match(SyntaxKind.SemicolonToken);
                    setterModifiers = [];
                }
            }

            closeBraceToken = Match(SyntaxKind.CloseBraceToken);
        }
        else if (Current.Kind == SyntaxKind.BeginKeyword)
        {
            beginKeyword = NextToken();
            while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
            {
                if (Current.Kind == SyntaxKind.SemicolonToken)
                {
                    NextToken();
                    continue;
                }

                if (Current.Kind == SyntaxKind.GetKeyword)
                {
                    getterBlockModifiers = [];
                    getterKeyword = NextToken();
                    getterBody = ParseBlockStatement();
                    continue;
                }

                var accessorModifiers = ParseModifiers();
                if (Current.Kind == SyntaxKind.SetKeyword)
                {
                    setterBlockModifiers = accessorModifiers;
                    setterKeyword = NextToken();
                    if (Current.Kind == SyntaxKind.OpenParenToken)
                    {
                        NextToken();
                        setterParameter = Match(SyntaxKind.IdentifierToken);
                        Match(SyntaxKind.CloseParenToken);
                    }

                    setterBody = ParseBlockStatement();
                    continue;
                }

                if (Current.Kind == SyntaxKind.GetKeyword)
                {
                    getterBlockModifiers = accessorModifiers;
                    getterKeyword = NextToken();
                    getterBody = ParseBlockStatement();
                    continue;
                }

                break;
            }

            endKeyword = Match(SyntaxKind.EndKeyword);
        }
        else
        {
            readKeyword = Match(SyntaxKind.ReadKeyword);
            readTarget = ParseQualifiedName();
            if (Current.Kind == SyntaxKind.WriteKeyword)
            {
                writeKeyword = NextToken();
                writeTarget = ParseQualifiedName();
            }
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new PropertyDeclarationSyntax(
            modifiers,
            propertyKeyword,
            identifier,
            openBracketToken,
            indexParameter,
            closeBracketToken,
            colonToken,
            typeName,
            readKeyword,
            readTarget,
            writeKeyword,
            writeTarget,
            openBraceToken,
            getterModifiers,
            getKeyword,
            getSemicolonToken,
            setterModifiers,
            setKeyword,
            setSemicolonToken,
            initModifiers,
            initKeyword,
            initSemicolonToken,
            closeBraceToken,
            beginKeyword,
            getterBlockModifiers,
            getterKeyword,
            getterBody,
            setterBlockModifiers,
            setterKeyword,
            setterParameter,
            setterBody,
            endKeyword,
            semicolon);
    }

    private MethodDeclarationSyntax ParseMethodDeclaration(IReadOnlyList<AttributeSyntax> attributes, IReadOnlyList<SyntaxToken> modifiers)
    {
        var keyword = NextToken();
        var identifier = keyword.Kind == SyntaxKind.ConstructorKeyword
            ? new SyntaxToken(SyntaxKind.IdentifierToken, ".ctor", null, keyword.Span)
            : Match(SyntaxKind.IdentifierToken);

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
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = NextToken();
            returnType = ParseTypeName();
        }

        SyntaxToken? arrowToken = null;
        ExpressionSyntax? expressionBody = null;
        BlockStatementSyntax? body = null;
        SyntaxToken terminator;

        if (Current.Kind == SyntaxKind.ArrowToken)
        {
            arrowToken = NextToken();
            expressionBody = ParseExpression();
            terminator = Match(SyntaxKind.SemicolonToken);
        }
        else if (Current.Kind == SyntaxKind.SemicolonToken && Peek(1).Kind == SyntaxKind.BeginKeyword)
        {
            NextToken();
            body = ParseBlockStatement();
            terminator = body.SemicolonToken;
        }
        else if (Current.Kind == SyntaxKind.BeginKeyword)
        {
            body = ParseBlockStatement();
            terminator = body.SemicolonToken;
        }
        else
        {
            terminator = Match(SyntaxKind.SemicolonToken);
        }

        return new MethodDeclarationSyntax(
            attributes,
            modifiers,
            keyword,
            identifier,
            openParen,
            parameters,
            closeParen,
            colonToken,
            returnType,
            arrowToken,
            expressionBody,
            body,
            terminator);
    }

    private ParameterSyntax ParseParameter()
    {
        SyntaxToken? modifierKeyword = null;
        if (Current.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword or SyntaxKind.InKeyword or SyntaxKind.ParamsKeyword)
        {
            modifierKeyword = NextToken();
        }

        var identifier = Match(SyntaxKind.IdentifierToken);
        var colon = Match(SyntaxKind.ColonToken);
        var typeName = ParseTypeName();
        return new ParameterSyntax(modifierKeyword, identifier, colon, typeName);
    }

    private TopLevelExpressionStatementSyntax ParseTopLevelExpressionStatement()
    {
        var expression = ParseExpression();
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new TopLevelExpressionStatementSyntax(expression, semicolon);
    }

    private VariableDeclaratorSyntax ParseVariableDeclarator()
    {
        var identifier = Match(SyntaxKind.IdentifierToken);
        SyntaxToken? colonToken = null;
        QualifiedNameSyntax? typeName = null;
        SyntaxToken? assignToken = null;
        ExpressionSyntax? initializer = null;

        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = NextToken();
            typeName = ParseTypeName();
        }

        if (Current.Kind == SyntaxKind.AssignToken)
        {
            assignToken = NextToken();
            initializer = ParseExpression();
        }

        return new VariableDeclaratorSyntax(identifier, colonToken, typeName, assignToken, initializer);
    }

}
