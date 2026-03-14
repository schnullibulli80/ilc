namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    IdentifierToken,
    NumberToken,
    StringToken,
    NamespaceKeyword,
    UsesKeyword,
    ArrayKeyword,
    OfKeyword,
    VarKeyword,
    ClassKeyword,
    BeginKeyword,
    EndKeyword,
    MethodKeyword,
    FunctionKeyword,
    ProcedureKeyword,
    ConstructorKeyword,
    PropertyKeyword,
    ReadKeyword,
    WriteKeyword,
    GetKeyword,
    SetKeyword,
    InitKeyword,
    ReturnKeyword,
    IfKeyword,
    ThenKeyword,
    ElseKeyword,
    WhileKeyword,
    DoKeyword,
    NewKeyword,
    StaticKeyword,
    DefaultKeyword,
    ReadonlyKeyword,
    PublicKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    InternalKeyword,
    NilKeyword,
    TrueKeyword,
    FalseKeyword,
    SemicolonToken,
    ColonToken,
    CommaToken,
    DotToken,
    AssignToken,
    EqualsToken,
    NotEqualsToken,
    ArrowToken,
    LessToken,
    LessOrEqualsToken,
    GreaterToken,
    GreaterOrEqualsToken,
    PlusToken,
    MinusToken,
    StarToken,
    SlashToken,
    OpenParenToken,
    CloseParenToken,
    OpenBraceToken,
    CloseBraceToken,
    OpenBracketToken,
    CloseBracketToken,
    CompilationUnit,
    NamespaceDeclaration,
    UsesClause,
    QualifiedName,
    TopLevelVariableDeclaration,
    VariableDeclarator,
    TopLevelExpressionStatement,
    ClassDeclaration,
    FieldDeclaration,
    PropertyDeclaration,
    MethodDeclaration,
    Parameter,
    BlockStatement,
    IfStatement,
    WhileStatement,
    ReturnStatement,
    LocalVariableDeclarationStatement,
    ExpressionStatement,
    LiteralExpression,
    NewExpression,
    NewArrayExpression,
    NameExpression,
    ArrayLengthExpression,
    ElementAccessExpression,
    PostfixElementAccessExpression,
    MemberAccessExpression,
    AssignmentExpression,
    BinaryExpression,
    CallExpression,
    Argument
}

public abstract record SyntaxNode(SyntaxKind Kind);

public sealed record SyntaxToken(
    SyntaxKind Kind,
    string Text,
    object? Value,
    TextSpan Span) : SyntaxNode(Kind);

public abstract record MemberSyntax(SyntaxKind Kind) : SyntaxNode(Kind);

public abstract record TypeMemberSyntax(SyntaxKind Kind) : SyntaxNode(Kind);

public abstract record StatementSyntax(SyntaxKind Kind) : SyntaxNode(Kind);

public abstract record ExpressionSyntax(SyntaxKind Kind) : SyntaxNode(Kind);

public sealed record QualifiedNameSyntax(
    IReadOnlyList<SyntaxToken> Parts) : SyntaxNode(SyntaxKind.QualifiedName)
{
    public string ToDisplayString() => string.Join(".", Parts.Select(part => part.Text));
}

public sealed record NamespaceDeclarationSyntax(
    SyntaxToken NamespaceKeyword,
    QualifiedNameSyntax Name,
    SyntaxToken SemicolonToken) : SyntaxNode(SyntaxKind.NamespaceDeclaration);

public sealed record UsesClauseSyntax(
    SyntaxToken UsesKeyword,
    IReadOnlyList<QualifiedNameSyntax> Imports,
    SyntaxToken SemicolonToken) : SyntaxNode(SyntaxKind.UsesClause);

public sealed record VariableDeclaratorSyntax(
    SyntaxToken Identifier,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? TypeName,
    SyntaxToken? AssignToken,
    ExpressionSyntax? Initializer) : SyntaxNode(SyntaxKind.VariableDeclarator);

public sealed record TopLevelVariableDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken VarKeyword,
    IReadOnlyList<VariableDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.TopLevelVariableDeclaration);

public sealed record TopLevelExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.TopLevelExpressionStatement);

public sealed record ClassDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken ClassKeyword,
    SyntaxToken Identifier,
    SyntaxToken BeginKeyword,
    IReadOnlyList<TypeMemberSyntax> Members,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.ClassDeclaration);

public sealed record FieldDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken VarKeyword,
    IReadOnlyList<VariableDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : TypeMemberSyntax(SyntaxKind.FieldDeclaration);

public sealed record PropertyDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken PropertyKeyword,
    SyntaxToken Identifier,
    SyntaxToken? OpenBracketToken,
    ParameterSyntax? IndexParameter,
    SyntaxToken? CloseBracketToken,
    SyntaxToken ColonToken,
    QualifiedNameSyntax TypeName,
    SyntaxToken? ReadKeyword,
    QualifiedNameSyntax? ReadTarget,
    SyntaxToken? WriteKeyword,
    QualifiedNameSyntax? WriteTarget,
    SyntaxToken? OpenBraceToken,
    IReadOnlyList<SyntaxToken> GetterModifiers,
    SyntaxToken? GetKeyword,
    SyntaxToken? GetSemicolonToken,
    IReadOnlyList<SyntaxToken> SetterModifiers,
    SyntaxToken? SetKeyword,
    SyntaxToken? SetSemicolonToken,
    IReadOnlyList<SyntaxToken> InitModifiers,
    SyntaxToken? InitKeyword,
    SyntaxToken? InitSemicolonToken,
    SyntaxToken? CloseBraceToken,
    SyntaxToken? BeginKeyword,
    IReadOnlyList<SyntaxToken> GetterBlockModifiers,
    SyntaxToken? GetterKeyword,
    BlockStatementSyntax? GetterBody,
    IReadOnlyList<SyntaxToken> SetterBlockModifiers,
    SyntaxToken? SetterKeyword,
    SyntaxToken? SetterParameter,
    BlockStatementSyntax? SetterBody,
    SyntaxToken? EndKeyword,
    SyntaxToken SemicolonToken) : TypeMemberSyntax(SyntaxKind.PropertyDeclaration);

public sealed record ParameterSyntax(
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    QualifiedNameSyntax TypeName) : SyntaxNode(SyntaxKind.Parameter);

public sealed record MethodDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken Keyword,
    SyntaxToken Identifier,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    SyntaxToken? CloseParenToken,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? ReturnType,
    SyntaxToken? ArrowToken,
    ExpressionSyntax? ExpressionBody,
    BlockStatementSyntax? Body,
    SyntaxToken TerminatorToken) : TypeMemberSyntax(SyntaxKind.MethodDeclaration);

public sealed record BlockStatementSyntax(
    SyntaxToken BeginKeyword,
    IReadOnlyList<StatementSyntax> Statements,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.BlockStatement);

public sealed record IfStatementSyntax(
    SyntaxToken IfKeyword,
    ExpressionSyntax Condition,
    SyntaxToken ThenKeyword,
    StatementSyntax ThenStatement,
    SyntaxToken? ElseKeyword,
    StatementSyntax? ElseStatement) : StatementSyntax(SyntaxKind.IfStatement);

public sealed record WhileStatementSyntax(
    SyntaxToken WhileKeyword,
    ExpressionSyntax Condition,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : StatementSyntax(SyntaxKind.WhileStatement);

public sealed record ReturnStatementSyntax(
    SyntaxToken ReturnKeyword,
    ExpressionSyntax? Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ReturnStatement);

public sealed record LocalVariableDeclarationStatementSyntax(
    SyntaxToken VarKeyword,
    IReadOnlyList<VariableDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.LocalVariableDeclarationStatement);

public sealed record ExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ExpressionStatement);

public sealed record LiteralExpressionSyntax(
    SyntaxToken LiteralToken) : ExpressionSyntax(SyntaxKind.LiteralExpression);

public sealed record NewExpressionSyntax(
    SyntaxToken NewKeyword,
    QualifiedNameSyntax TypeName,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ArgumentSyntax> Arguments,
    SyntaxToken CloseParenToken) : ExpressionSyntax(SyntaxKind.NewExpression);

public sealed record NewArrayExpressionSyntax(
    SyntaxToken NewKeyword,
    QualifiedNameSyntax ElementTypeName,
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> LengthExpressions,
    SyntaxToken CloseBracketToken) : ExpressionSyntax(SyntaxKind.NewArrayExpression);

public sealed record NameExpressionSyntax(
    QualifiedNameSyntax Name) : ExpressionSyntax(SyntaxKind.NameExpression);

public sealed record ArrayLengthExpressionSyntax(
    QualifiedNameSyntax Target) : ExpressionSyntax(SyntaxKind.ArrayLengthExpression);

public sealed record ElementAccessExpressionSyntax(
    QualifiedNameSyntax Target,
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> IndexExpressions,
    SyntaxToken CloseBracketToken) : ExpressionSyntax(SyntaxKind.ElementAccessExpression);

public sealed record PostfixElementAccessExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> IndexExpressions,
    SyntaxToken CloseBracketToken) : ExpressionSyntax(SyntaxKind.PostfixElementAccessExpression);

public sealed record MemberAccessExpressionSyntax(
    ExpressionSyntax Receiver,
    SyntaxToken DotToken,
    SyntaxToken MemberName) : ExpressionSyntax(SyntaxKind.MemberAccessExpression);

public sealed record AssignmentExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken AssignToken,
    ExpressionSyntax Expression) : ExpressionSyntax(SyntaxKind.AssignmentExpression);

public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    SyntaxToken OperatorToken,
    ExpressionSyntax Right) : ExpressionSyntax(SyntaxKind.BinaryExpression);

public sealed record ArgumentSyntax(
    ExpressionSyntax Expression) : SyntaxNode(SyntaxKind.Argument);

public sealed record CallExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ArgumentSyntax> Arguments,
    SyntaxToken CloseParenToken) : ExpressionSyntax(SyntaxKind.CallExpression);

public sealed record CompilationUnitSyntax(
    NamespaceDeclarationSyntax? Namespace,
    UsesClauseSyntax? Uses,
    IReadOnlyList<MemberSyntax> Members,
    SyntaxToken EndOfFileToken,
    IReadOnlyList<SyntaxToken> Tokens,
    string SourceText) : SyntaxNode(SyntaxKind.CompilationUnit);

internal sealed class Lexer
{
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Lexer(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public List<SyntaxToken> Lex()
    {
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            var token = NextToken();
            if (token.Kind != SyntaxKind.BadToken)
            {
                tokens.Add(token);
            }

            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        return tokens;
    }

    private SyntaxToken NextToken()
    {
        SkipTrivia();

        if (_position >= _text.Length)
        {
            return new SyntaxToken(SyntaxKind.EndOfFileToken, string.Empty, null, new TextSpan(_position, 0));
        }

        var start = _position;
        var current = _text[_position];

        if (char.IsLetter(current) || current == '_')
        {
            _position++;
            while (_position < _text.Length &&
                   (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }

            var text = _text[start.._position];
            return new SyntaxToken(GetKeywordKind(text), text, null, new TextSpan(start, text.Length));
        }

        if (char.IsDigit(current))
        {
            _position++;
            while (_position < _text.Length &&
                   (char.IsDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }

            var text = _text[start.._position];
            object? value = int.TryParse(text.Replace("_", string.Empty), out var parsedValue) ? parsedValue : null;
            return new SyntaxToken(SyntaxKind.NumberToken, text, value, new TextSpan(start, text.Length));
        }

        if (current is '"' or '\'')
        {
            return LexString();
        }

        _position++;
        return current switch
        {
            ';' => new SyntaxToken(SyntaxKind.SemicolonToken, ";", null, new TextSpan(start, 1)),
            ':' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.AssignToken, ":="),
            ':' => new SyntaxToken(SyntaxKind.ColonToken, ":", null, new TextSpan(start, 1)),
            '<' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.LessOrEqualsToken, "<="),
            '<' when Peek() == '>' => LexTwoCharacterToken(SyntaxKind.NotEqualsToken, "<>"),
            '<' => new SyntaxToken(SyntaxKind.LessToken, "<", null, new TextSpan(start, 1)),
            '=' when Peek() == '>' => LexTwoCharacterToken(SyntaxKind.ArrowToken, "=>"),
            '=' => new SyntaxToken(SyntaxKind.EqualsToken, "=", null, new TextSpan(start, 1)),
            '>' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.GreaterOrEqualsToken, ">="),
            '>' => new SyntaxToken(SyntaxKind.GreaterToken, ">", null, new TextSpan(start, 1)),
            '+' => new SyntaxToken(SyntaxKind.PlusToken, "+", null, new TextSpan(start, 1)),
            '-' => new SyntaxToken(SyntaxKind.MinusToken, "-", null, new TextSpan(start, 1)),
            '*' => new SyntaxToken(SyntaxKind.StarToken, "*", null, new TextSpan(start, 1)),
            '/' => new SyntaxToken(SyntaxKind.SlashToken, "/", null, new TextSpan(start, 1)),
            ',' => new SyntaxToken(SyntaxKind.CommaToken, ",", null, new TextSpan(start, 1)),
            '.' => new SyntaxToken(SyntaxKind.DotToken, ".", null, new TextSpan(start, 1)),
            '(' => new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, new TextSpan(start, 1)),
            ')' => new SyntaxToken(SyntaxKind.CloseParenToken, ")", null, new TextSpan(start, 1)),
            '{' => new SyntaxToken(SyntaxKind.OpenBraceToken, "{", null, new TextSpan(start, 1)),
            '}' => new SyntaxToken(SyntaxKind.CloseBraceToken, "}", null, new TextSpan(start, 1)),
            '[' => new SyntaxToken(SyntaxKind.OpenBracketToken, "[", null, new TextSpan(start, 1)),
            ']' => new SyntaxToken(SyntaxKind.CloseBracketToken, "]", null, new TextSpan(start, 1)),
            _ => BadToken(start, current)
        };
    }

    private SyntaxToken LexTwoCharacterToken(SyntaxKind kind, string text)
    {
        var start = _position - 1;
        _position++;
        return new SyntaxToken(kind, text, null, new TextSpan(start, 2));
    }

    private SyntaxToken LexString()
    {
        var delimiter = _text[_position];
        var start = _position;
        _position++;

        while (_position < _text.Length && _text[_position] != delimiter)
        {
            _position++;
        }

        if (_position >= _text.Length)
        {
            _diagnostics.Report(
                "ILC1001",
                "Unterminated string literal.",
                DiagnosticSeverity.Error,
                new TextSpan(start, _text.Length - start));

            return new SyntaxToken(SyntaxKind.StringToken, _text[start..], _text[start..], new TextSpan(start, _text.Length - start));
        }

        _position++;
        var text = _text[start.._position];
        var value = text[1..^1];
        return new SyntaxToken(SyntaxKind.StringToken, text, value, new TextSpan(start, text.Length));
    }

    private SyntaxToken BadToken(int start, char current)
    {
        _diagnostics.Report(
            "ILC1000",
            $"Unexpected character '{current}'.",
            DiagnosticSeverity.Warning,
            new TextSpan(start, 1));

        return new SyntaxToken(SyntaxKind.BadToken, current.ToString(), null, new TextSpan(start, 1));
    }

    private char Peek() => _position < _text.Length ? _text[_position] : '\0';

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_position]))
            {
                _position++;
                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '/' && _text[_position + 1] == '/')
            {
                _position += 2;
                while (_position < _text.Length && _text[_position] != '\n')
                {
                    _position++;
                }

                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '(' && _text[_position + 1] == '*')
            {
                _position += 2;
                while (_position + 1 < _text.Length && !(_text[_position] == '*' && _text[_position + 1] == ')'))
                {
                    _position++;
                }

                if (_position + 1 < _text.Length)
                {
                    _position += 2;
                }

                continue;
            }

            break;
        }
    }

    private static SyntaxKind GetKeywordKind(string text) =>
        text switch
        {
            "namespace" => SyntaxKind.NamespaceKeyword,
            "uses" => SyntaxKind.UsesKeyword,
            "array" => SyntaxKind.ArrayKeyword,
            "of" => SyntaxKind.OfKeyword,
            "var" => SyntaxKind.VarKeyword,
            "class" => SyntaxKind.ClassKeyword,
            "begin" => SyntaxKind.BeginKeyword,
            "end" => SyntaxKind.EndKeyword,
            "method" => SyntaxKind.MethodKeyword,
            "function" => SyntaxKind.FunctionKeyword,
            "procedure" => SyntaxKind.ProcedureKeyword,
            "constructor" => SyntaxKind.ConstructorKeyword,
            "property" => SyntaxKind.PropertyKeyword,
            "read" => SyntaxKind.ReadKeyword,
            "write" => SyntaxKind.WriteKeyword,
            "get" => SyntaxKind.GetKeyword,
            "set" => SyntaxKind.SetKeyword,
            "init" => SyntaxKind.InitKeyword,
            "return" => SyntaxKind.ReturnKeyword,
            "if" => SyntaxKind.IfKeyword,
            "then" => SyntaxKind.ThenKeyword,
            "else" => SyntaxKind.ElseKeyword,
            "while" => SyntaxKind.WhileKeyword,
            "do" => SyntaxKind.DoKeyword,
            "new" => SyntaxKind.NewKeyword,
            "static" => SyntaxKind.StaticKeyword,
            "default" => SyntaxKind.DefaultKeyword,
            "readonly" => SyntaxKind.ReadonlyKeyword,
            "public" => SyntaxKind.PublicKeyword,
            "private" => SyntaxKind.PrivateKeyword,
            "protected" => SyntaxKind.ProtectedKeyword,
            "internal" => SyntaxKind.InternalKeyword,
            "nil" => SyntaxKind.NilKeyword,
            "true" => SyntaxKind.TrueKeyword,
            "false" => SyntaxKind.FalseKeyword,
            _ => SyntaxKind.IdentifierToken
        };
}

internal sealed class Parser
{
    private readonly IReadOnlyList<SyntaxToken> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Parser(IReadOnlyList<SyntaxToken> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

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
        var imports = new List<QualifiedNameSyntax> { ParseQualifiedName() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            imports.Add(ParseQualifiedName());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new UsesClauseSyntax(keyword, imports, semicolon);
    }

    private MemberSyntax ParseTopLevelMember()
    {
        var modifiers = ParseModifiers();

        return Current.Kind switch
        {
            SyntaxKind.VarKeyword => ParseTopLevelVariableDeclaration(modifiers),
            SyntaxKind.ClassKeyword => ParseClassDeclaration(modifiers),
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
            or SyntaxKind.ReadonlyKeyword)
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

    private ClassDeclarationSyntax ParseClassDeclaration(IReadOnlyList<SyntaxToken> modifiers)
    {
        var classKeyword = Match(SyntaxKind.ClassKeyword);
        var identifier = Match(SyntaxKind.IdentifierToken);
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
        return new ClassDeclarationSyntax(modifiers, classKeyword, identifier, beginKeyword, members, endKeyword, semicolon);
    }

    private TypeMemberSyntax ParseTypeMember()
    {
        var modifiers = ParseModifiers();
        return Current.Kind switch
        {
            SyntaxKind.VarKeyword => ParseFieldDeclaration(modifiers),
            SyntaxKind.PropertyKeyword => ParsePropertyDeclaration(modifiers),
            SyntaxKind.MethodKeyword or SyntaxKind.FunctionKeyword or SyntaxKind.ProcedureKeyword or SyntaxKind.ConstructorKeyword => ParseMethodDeclaration(modifiers),
            _ => ParseMethodDeclaration(modifiers)
        };
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

    private MethodDeclarationSyntax ParseMethodDeclaration(IReadOnlyList<SyntaxToken> modifiers)
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
                while (Current.Kind == SyntaxKind.CommaToken)
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
        var identifier = Match(SyntaxKind.IdentifierToken);
        var colon = Match(SyntaxKind.ColonToken);
        var typeName = ParseTypeName();
        return new ParameterSyntax(identifier, colon, typeName);
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

    private StatementSyntax ParseStatement()
    {
        return Current.Kind switch
        {
            SyntaxKind.BeginKeyword => ParseBlockStatement(),
            SyntaxKind.IfKeyword => ParseIfStatement(),
            SyntaxKind.WhileKeyword => ParseWhileStatement(),
            SyntaxKind.ReturnKeyword => ParseReturnStatement(),
            SyntaxKind.VarKeyword => ParseLocalVariableDeclarationStatement(),
            _ => ParseExpressionStatement()
        };
    }

    private BlockStatementSyntax ParseBlockStatement()
    {
        var beginKeyword = Match(SyntaxKind.BeginKeyword);
        var statements = new List<StatementSyntax>();
        while (Current.Kind != SyntaxKind.EndKeyword && Current.Kind != SyntaxKind.EndOfFileToken)
        {
            statements.Add(ParseStatement());
        }

        var endKeyword = Match(SyntaxKind.EndKeyword);
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new BlockStatementSyntax(beginKeyword, statements, endKeyword, semicolon);
    }

    private ReturnStatementSyntax ParseReturnStatement()
    {
        var keyword = Match(SyntaxKind.ReturnKeyword);
        ExpressionSyntax? expression = null;
        if (Current.Kind != SyntaxKind.SemicolonToken)
        {
            expression = ParseExpression();
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new ReturnStatementSyntax(keyword, expression, semicolon);
    }

    private IfStatementSyntax ParseIfStatement()
    {
        var ifKeyword = Match(SyntaxKind.IfKeyword);
        var condition = ParseExpression();
        var thenKeyword = Match(SyntaxKind.ThenKeyword);
        var thenStatement = ParseStatement();
        SyntaxToken? elseKeyword = null;
        StatementSyntax? elseStatement = null;
        if (Current.Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = NextToken();
            elseStatement = ParseStatement();
        }

        return new IfStatementSyntax(ifKeyword, condition, thenKeyword, thenStatement, elseKeyword, elseStatement);
    }

    private WhileStatementSyntax ParseWhileStatement()
    {
        var whileKeyword = Match(SyntaxKind.WhileKeyword);
        var condition = ParseExpression();
        var doKeyword = Match(SyntaxKind.DoKeyword);
        var body = ParseStatement();
        return new WhileStatementSyntax(whileKeyword, condition, doKeyword, body);
    }

    private LocalVariableDeclarationStatementSyntax ParseLocalVariableDeclarationStatement()
    {
        var keyword = Match(SyntaxKind.VarKeyword);
        var declarators = new List<VariableDeclaratorSyntax> { ParseVariableDeclarator() };
        while (Current.Kind == SyntaxKind.CommaToken)
        {
            NextToken();
            declarators.Add(ParseVariableDeclarator());
        }

        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new LocalVariableDeclarationStatementSyntax(keyword, declarators, semicolon);
    }

    private ExpressionStatementSyntax ParseExpressionStatement()
    {
        var expression = ParseExpression();
        var semicolon = Match(SyntaxKind.SemicolonToken);
        return new ExpressionStatementSyntax(expression, semicolon);
    }

    private ExpressionSyntax ParseExpression() => ParseAssignmentExpression();

    private ExpressionSyntax ParseAssignmentExpression()
    {
        if (Current.Kind == SyntaxKind.IdentifierToken && IsAssignmentTarget())
        {
            var start = _position;
            var target = ParseAssignableTarget();
            if (Current.Kind == SyntaxKind.AssignToken)
            {
                var assignToken = NextToken();
                var expression = ParseAssignmentExpression();
                return new AssignmentExpressionSyntax(target, assignToken, expression);
            }

            _position = start;
        }

        return ParseComparisonExpression();
    }

    private ExpressionSyntax ParseComparisonExpression()
    {
        var left = ParseAdditiveExpression();
        while (Current.Kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken)
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
        var left = ParsePrimaryExpression();
        while (Current.Kind is SyntaxKind.StarToken or SyntaxKind.SlashToken)
        {
            var operatorToken = NextToken();
            var right = ParsePrimaryExpression();
            left = new BinaryExpressionSyntax(left, operatorToken, right);
        }

        return left;
    }

    private ExpressionSyntax ParsePrimaryExpression()
    {
        if (Current.Kind is SyntaxKind.NumberToken or SyntaxKind.StringToken or SyntaxKind.NilKeyword or SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword)
        {
            return new LiteralExpressionSyntax(NextToken());
        }

        if (Current.Kind == SyntaxKind.NewKeyword)
        {
            var newKeyword = NextToken();
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
                arguments.Add(new ArgumentSyntax(ParseExpression()));
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    arguments.Add(new ArgumentSyntax(ParseExpression()));
                }
            }

            var closeParen = Match(SyntaxKind.CloseParenToken);
            return ParsePostfixExpression(new NewExpressionSyntax(newKeyword, typeName, openParen, arguments, closeParen));
        }

        var name = ParseQualifiedName();
        ExpressionSyntax expression;
        if (Current.Kind == SyntaxKind.OpenBracketToken)
        {
            var openBracket = NextToken();
            var indexExpressions = new List<ExpressionSyntax> { ParseExpression() };
            while (Current.Kind == SyntaxKind.CommaToken)
            {
                NextToken();
                indexExpressions.Add(ParseExpression());
            }

            var closeBracket = Match(SyntaxKind.CloseBracketToken);
            expression = new ElementAccessExpressionSyntax(name, openBracket, indexExpressions, closeBracket);
            return ParsePostfixExpression(expression);
        }

        if (name.Parts.Count >= 2 && name.Parts[^1].Text == "Length")
        {
            expression = new ArrayLengthExpressionSyntax(new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray()));
            return ParsePostfixExpression(expression);
        }

        expression = new NameExpressionSyntax(name);
        return ParsePostfixExpression(expression);
    }

    private ExpressionSyntax ParsePostfixExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            if (Current.Kind == SyntaxKind.OpenBracketToken)
            {
                var openBracket = NextToken();
                var indexExpressions = new List<ExpressionSyntax> { ParseExpression() };
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    indexExpressions.Add(ParseExpression());
                }

                var closeBracket = Match(SyntaxKind.CloseBracketToken);
                expression = expression is NameExpressionSyntax nameExpression
                    ? new ElementAccessExpressionSyntax(nameExpression.Name, openBracket, indexExpressions, closeBracket)
                    : new PostfixElementAccessExpressionSyntax(expression, openBracket, indexExpressions, closeBracket);
                continue;
            }

            if (Current.Kind == SyntaxKind.DotToken && Peek(1).Kind == SyntaxKind.IdentifierToken)
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
                    arguments.Add(new ArgumentSyntax(ParseExpression()));
                    while (Current.Kind == SyntaxKind.CommaToken)
                    {
                        NextToken();
                        arguments.Add(new ArgumentSyntax(ParseExpression()));
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

    private QualifiedNameSyntax ParseTypeName()
    {
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

        var name = ParseQualifiedName();
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
                var indexExpressions = new List<ExpressionSyntax> { ParseExpression() };
                while (Current.Kind == SyntaxKind.CommaToken)
                {
                    NextToken();
                    indexExpressions.Add(ParseExpression());
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

    private SyntaxToken Match(SyntaxKind kind)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        _diagnostics.Report(
            "ILC1002",
            $"Expected token '{kind}', but found '{Current.Kind}'.",
            DiagnosticSeverity.Error,
            Current.Span);

        if (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            _position++;
        }

        return new SyntaxToken(kind, string.Empty, null, new TextSpan(Current.Span.Start, 0));
    }

    private SyntaxToken NextToken()
    {
        var current = Current;
        _position++;
        return current;
    }

    private SyntaxToken Current => Peek(0);

    private bool IsAssignmentTarget()
    {
        var offset = 0;
        if (Peek(offset).Kind != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        offset++;
        while (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
        {
            offset += 2;
        }

        while (true)
        {
            if (Peek(offset).Kind == SyntaxKind.OpenBracketToken)
            {
                offset++;
                var depth = 1;
                while (depth > 0 && Peek(offset).Kind != SyntaxKind.EndOfFileToken)
                {
                    if (Peek(offset).Kind == SyntaxKind.OpenBracketToken)
                    {
                        depth++;
                    }
                    else if (Peek(offset).Kind == SyntaxKind.CloseBracketToken)
                    {
                        depth--;
                    }

                    offset++;
                }
                continue;
            }

            if (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
            {
                offset += 2;
                continue;
            }

            break;
        }

        return Peek(offset).Kind == SyntaxKind.AssignToken;
    }

    private SyntaxToken Peek(int offset)
    {
        var index = _position + offset;
        return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
    }
}

public sealed class SyntaxTree
{
    private SyntaxTree(CompilationUnitSyntax root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    public CompilationUnitSyntax Root { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(string sourceText)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(sourceText, diagnostics);
        var tokens = lexer.Lex();
        var parser = new Parser(tokens, diagnostics);
        var root = parser.ParseCompilationUnit(sourceText);
        return new SyntaxTree(root, diagnostics);
    }
}
