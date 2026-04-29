namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    public CompilationUnitSyntax ParseCompilationUnit(string sourceText)
    {
        NamespaceDeclarationSyntax? namespaceDeclaration = null;
        UsesClauseSyntax? usesClause = null;

        if (Current.Kind == SyntaxKind.NamespaceKeyword)
        {
            namespaceDeclaration = ParseNamespaceDeclaration();
        }

        if (Current.Kind == SyntaxKind.UsesKeyword)
        {
            usesClause = ParseUsesClause();
        }

        var members = new List<MemberSyntax>();
        while (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            members.Add(ParseTopLevelMember());
        }

        return new CompilationUnitSyntax(
            namespaceDeclaration,
            usesClause,
            members,
            Match(SyntaxKind.EndOfFileToken),
            _tokens,
            sourceText);
    }

    private NamespaceDeclarationSyntax ParseNamespaceDeclaration()
    {
        var keyword = Match(SyntaxKind.NamespaceKeyword);
        var name = ParseQualifiedName();
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new NamespaceDeclarationSyntax(keyword, name, semicolon);
    }

    private UsesClauseSyntax ParseUsesClause()
    {
        var keyword = Match(SyntaxKind.UsesKeyword);
        var imports = new List<UsesImportSyntax> { ParseUsesImport() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            imports.Add(ParseUsesImport());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new UsesClauseSyntax(keyword, imports, semicolon);
    }

    private UsesImportSyntax ParseUsesImport()
    {
        if (Current.Kind == SyntaxKind.IdentifierToken && Peek(1).Kind == SyntaxKind.EqualsToken)
        {
            var aliasIdentifier = Match(SyntaxKind.IdentifierToken);
            var equalsToken = Match(SyntaxKind.EqualsToken);
            var namespaceName = ParseQualifiedName();
            return new UsesImportSyntax(aliasIdentifier, equalsToken, namespaceName);
        }

        return new UsesImportSyntax(null, null, ParseQualifiedName());
    }

    private MemberSyntax ParseTopLevelMember()
    {
        var modifiers = ParseModifiers();

        return Current.Kind switch
        {
            SyntaxKind.VarKeyword => ParseTopLevelVariableDeclaration(modifiers),
            SyntaxKind.ConstKeyword => ParseTopLevelConstantDeclaration(modifiers),
            SyntaxKind.EnumKeyword => ParseEnumDeclaration(modifiers),
            SyntaxKind.DelegateKeyword => ParseDelegateDeclaration(modifiers),
            SyntaxKind.ClassKeyword or SyntaxKind.RecordKeyword => ParseClassDeclaration(modifiers),
            SyntaxKind.InterfaceKeyword => ParseInterfaceDeclaration(modifiers),
            _ => ParseTopLevelExpressionStatement()
        };
    }

    private IReadOnlyList<SyntaxToken> ParseModifiers()
    {
        var modifiers = new List<SyntaxToken>();
        while (Current.Kind is SyntaxKind.PublicKeyword
            or SyntaxKind.PrivateKeyword
            or SyntaxKind.ProtectedKeyword
            or SyntaxKind.InternalKeyword
            or SyntaxKind.StaticKeyword
            or SyntaxKind.DefaultKeyword
            or SyntaxKind.ReadonlyKeyword
            or SyntaxKind.VirtualKeyword
            or SyntaxKind.OverrideKeyword
            or SyntaxKind.ExternKeyword)
        {
            modifiers.Add(NextToken());
        }

        return modifiers;
    }

    private TopLevelVariableDeclarationSyntax ParseTopLevelVariableDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var keyword = Match(SyntaxKind.VarKeyword);
        var declarators = new List<VariableDeclaratorSyntax> { ParseVariableDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseVariableDeclarator());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new TopLevelVariableDeclarationSyntax(modifiers, keyword, declarators, semicolon);
    }

    private TopLevelConstantDeclarationSyntax ParseTopLevelConstantDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var keyword = Match(SyntaxKind.ConstKeyword);
        var declarators = new List<ConstantDeclaratorSyntax> { ParseConstantDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseConstantDeclarator());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new TopLevelConstantDeclarationSyntax(modifiers, keyword, declarators, semicolon);
    }

    private ClassDeclarationSyntax ParseClassDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var classKeyword = Current.Kind == SyntaxKind.RecordKeyword
            ? Match(SyntaxKind.RecordKeyword)
            : Match(SyntaxKind.ClassKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var typeParameters = ParseOptionalTypeParameterList();
        SyntaxToken? colonToken = null;
        QualifiedNameSyntax? baseType = null;
        var interfaceTypes = new List<QualifiedNameSyntax>();
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = Match(SyntaxKind.ColonToken);
            baseType = ParseTypeName();
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                interfaceTypes.Add(ParseTypeName());
            }
        }

        var beginKeyword = Match(SyntaxKind.BeginKeyword);
        var members = new List<TypeMemberSyntax>();

        while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                NextToken();
                continue;
            }

            members.Add(ParseTypeMember());
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new ClassDeclarationSyntax(
            modifiers,
            classKeyword,
            identifier,
            typeParameters,
            colonToken,
            baseType,
            interfaceTypes,
            beginKeyword,
            members,
            endKeyword,
            semicolon);
    }

    private InterfaceDeclarationSyntax ParseInterfaceDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var interfaceKeyword = Match(SyntaxKind.InterfaceKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var typeParameters = ParseOptionalTypeParameterList();
        SyntaxToken? colonToken = null;
        var baseInterfaces = new List<QualifiedNameSyntax>();
        if (Current.Kind == SyntaxKind.ColonToken)
        {
            colonToken = Match(SyntaxKind.ColonToken);
            baseInterfaces.Add(ParseTypeName());
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                baseInterfaces.Add(ParseTypeName());
            }
        }

        var beginKeyword = Match(SyntaxKind.BeginKeyword);
        var members = new List<TypeMemberSyntax>();
        while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                NextToken();
                continue;
            }

            members.Add(ParseTypeMember());
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new InterfaceDeclarationSyntax(
            modifiers,
            interfaceKeyword,
            identifier,
            typeParameters,
            colonToken,
            baseInterfaces,
            beginKeyword,
            members,
            endKeyword,
            semicolon);
    }

    private EnumDeclarationSyntax ParseEnumDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var enumKeyword = Match(SyntaxKind.EnumKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
        var beginKeyword = Match(SyntaxKind.BeginKeyword);
        var members = new List<EnumMemberSyntax>();

        while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind == SyntaxKind.SemicolonToken)
            {
                NextToken();
                continue;
            }

            var memberIdentifier = Match(SyntaxKind.IdentifierToken);
            SyntaxToken? equalsToken = null;
            SyntaxToken? valueToken = null;
            if (Current.Kind == SyntaxKind.EqualsToken)
            {
                equalsToken = NextToken();
                valueToken = Match(SyntaxKind.NumberToken);
            }

            members.Add(new EnumMemberSyntax(memberIdentifier, equalsToken, valueToken));

            if (Current.Kind == SyntaxKind.CommaToken || Current.Kind == SyntaxKind.SemicolonToken)
            {
                NextToken();
            }
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new EnumDeclarationSyntax(modifiers, enumKeyword, identifier, beginKeyword, members, endKeyword, semicolon);
    }

    private DelegateDeclarationSyntax ParseDelegateDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var delegateKeyword = Match(SyntaxKind.DelegateKeyword);
        var signatureKeyword = Current.Kind switch
        {
            SyntaxKind.FunctionKeyword => Match(SyntaxKind.FunctionKeyword),
            SyntaxKind.ProcedureKeyword => Match(SyntaxKind.ProcedureKeyword),
            _ => Match(SyntaxKind.FunctionKeyword)
        };
        var identifier = Match(SyntaxKind.IdentifierToken);
        var typeParameters = ParseOptionalTypeParameterList();

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

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new DelegateDeclarationSyntax(
            modifiers,
            delegateKeyword,
            signatureKeyword,
            identifier,
            typeParameters,
            openParen,
            parameters,
            closeParen,
            colonToken,
            returnType,
            semicolon);
    }

}
