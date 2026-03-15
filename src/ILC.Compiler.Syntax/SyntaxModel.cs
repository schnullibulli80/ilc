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
    SetKeyword,
    OfKeyword,
    VarKeyword,
    ConstKeyword,
    EnumKeyword,
    ClassKeyword,
    RecordKeyword,
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
    InitKeyword,
    ReturnKeyword,
    ExitKeyword,
    BreakKeyword,
    ContinueKeyword,
    RaiseKeyword,
    ThrowKeyword,
    CaseKeyword,
    MatchKeyword,
    IfKeyword,
    ThenKeyword,
    ElseKeyword,
    WhileKeyword,
    RepeatKeyword,
    UntilKeyword,
    ForKeyword,
    EachKeyword,
    ForeachKeyword,
    WithKeyword,
    DivKeyword,
    ToKeyword,
    DowntoKeyword,
    InKeyword,
    DoKeyword,
    StepKeyword,
    IncKeyword,
    DecKeyword,
    TryKeyword,
    ExceptKeyword,
    FinallyKeyword,
    OnKeyword,
    NewKeyword,
    StaticKeyword,
    DefaultKeyword,
    ReadonlyKeyword,
    OutKeyword,
    RefKeyword,
    PublicKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    InternalKeyword,
    ParamsKeyword,
    ExternKeyword,
    AsKeyword,
    IsKeyword,
    AndKeyword,
    NotKeyword,
    OrKeyword,
    WhenKeyword,
    ModKeyword,
    ShlKeyword,
    ShrKeyword,
    NilKeyword,
    TrueKeyword,
    FalseKeyword,
    SemicolonToken,
    ColonToken,
    CommaToken,
    DotToken,
    RangeToken,
    AssignToken,
    PlusAssignToken,
    MinusAssignToken,
    StarAssignToken,
    SlashAssignToken,
    DivAssignToken,
    ModAssignToken,
    AndAssignToken,
    OrAssignToken,
    XorAssignToken,
    ShlAssignToken,
    ShrAssignToken,
    NullCoalescingAssignToken,
    NullCoalescingToken,
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
    TopLevelConstantDeclaration,
    EnumDeclaration,
    VariableDeclarator,
    ConstantDeclarator,
    EnumMember,
    TopLevelExpressionStatement,
    ClassDeclaration,
    FieldDeclaration,
    ConstantDeclaration,
    PropertyDeclaration,
    MethodDeclaration,
    Parameter,
    BlockStatement,
    IfStatement,
    WhileStatement,
    RepeatStatement,
    ForStatement,
    ForeachStatement,
    WithStatement,
    CaseStatement,
    CaseClause,
    MatchStatement,
    MatchStatementArm,
    ReturnStatement,
    BreakStatement,
    ContinueStatement,
    RaiseStatement,
    TryStatement,
    ExceptionClause,
    LocalVariableDeclarationStatement,
    IncStatement,
    DecStatement,
    ExpressionStatement,
    LiteralExpression,
    SetLiteralExpression,
    NewExpression,
    NewArrayExpression,
    NameExpression,
    ArrayLengthExpression,
    ElementAccessExpression,
    PostfixElementAccessExpression,
    MemberAccessExpression,
    ParenthesizedExpression,
    AssignmentExpression,
    CompoundAssignmentExpression,
    BinaryExpression,
    MatchNotPattern,
    MatchOrPattern,
    MatchAndPattern,
    MatchRelationalPattern,
    RangeExpression,
    AsExpression,
    TypeTestExpression,
    CallExpression,
    Argument,
    UnaryExpression,
    MatchExpression,
    MatchExpressionArm
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

public sealed record UnaryExpressionSyntax(
    SyntaxToken OperatorToken,
    ExpressionSyntax Operand) : ExpressionSyntax(SyntaxKind.UnaryExpression);

public sealed record ParenthesizedExpressionSyntax(
    SyntaxToken OpenParenToken,
    ExpressionSyntax Expression,
    SyntaxToken CloseParenToken) : ExpressionSyntax(SyntaxKind.ParenthesizedExpression);

public sealed record QualifiedNameSyntax(
    IReadOnlyList<SyntaxToken> Parts) : SyntaxNode(SyntaxKind.QualifiedName)
{
    public string ToDisplayString() => string.Join(".", Parts.Select(part => part.Text));
}

public sealed record NamespaceDeclarationSyntax(
    SyntaxToken NamespaceKeyword,
    QualifiedNameSyntax Name,
    SyntaxToken SemicolonToken) : SyntaxNode(SyntaxKind.NamespaceDeclaration);

public sealed record UsesImportSyntax(
    SyntaxToken? AliasIdentifier,
    SyntaxToken? EqualsToken,
    QualifiedNameSyntax NamespaceName) : SyntaxNode(SyntaxKind.QualifiedName)
{
    public string ToDisplayString() =>
        AliasIdentifier is null
            ? NamespaceName.ToDisplayString()
            : $"{AliasIdentifier.Text} = {NamespaceName.ToDisplayString()}";
}

public sealed record UsesClauseSyntax(
    SyntaxToken UsesKeyword,
    IReadOnlyList<UsesImportSyntax> Imports,
    SyntaxToken SemicolonToken) : SyntaxNode(SyntaxKind.UsesClause);

public sealed record VariableDeclaratorSyntax(
    SyntaxToken Identifier,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? TypeName,
    SyntaxToken? AssignToken,
    ExpressionSyntax? Initializer) : SyntaxNode(SyntaxKind.VariableDeclarator);

public sealed record ConstantDeclaratorSyntax(
    SyntaxToken Identifier,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? TypeName,
    SyntaxToken EqualsToken,
    ExpressionSyntax Initializer) : SyntaxNode(SyntaxKind.ConstantDeclarator);

public sealed record TopLevelVariableDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken VarKeyword,
    IReadOnlyList<VariableDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.TopLevelVariableDeclaration);

public sealed record TopLevelConstantDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken ConstKeyword,
    IReadOnlyList<ConstantDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.TopLevelConstantDeclaration);

public sealed record EnumMemberSyntax(
    SyntaxToken Identifier,
    SyntaxToken? EqualsToken,
    SyntaxToken? ValueToken) : SyntaxNode(SyntaxKind.EnumMember);

public sealed record EnumDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken EnumKeyword,
    SyntaxToken Identifier,
    SyntaxToken BeginKeyword,
    IReadOnlyList<EnumMemberSyntax> Members,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.EnumDeclaration);

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

public sealed record ConstantDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken ConstKeyword,
    IReadOnlyList<ConstantDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : TypeMemberSyntax(SyntaxKind.ConstantDeclaration);

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
    SyntaxToken? ModifierKeyword,
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
    StatementSyntax? ElseStatement,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.IfStatement);

public sealed record WhileStatementSyntax(
    SyntaxToken WhileKeyword,
    ExpressionSyntax Condition,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : StatementSyntax(SyntaxKind.WhileStatement);

public sealed record RepeatStatementSyntax(
    SyntaxToken RepeatKeyword,
    IReadOnlyList<StatementSyntax> Statements,
    SyntaxToken UntilKeyword,
    ExpressionSyntax Condition,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.RepeatStatement);

public sealed record ForStatementSyntax(
    SyntaxToken ForKeyword,
    SyntaxToken? VarKeyword,
    SyntaxToken Identifier,
    SyntaxToken AssignToken,
    ExpressionSyntax LowerBound,
    SyntaxToken ToKeyword,
    ExpressionSyntax UpperBound,
    SyntaxToken? StepKeyword,
    ExpressionSyntax? StepExpression,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : StatementSyntax(SyntaxKind.ForStatement);

public sealed record ForeachStatementSyntax(
    SyntaxToken LoopKeyword,
    SyntaxToken? EachKeyword,
    SyntaxToken? VarKeyword,
    SyntaxToken Identifier,
    SyntaxToken InKeyword,
    ExpressionSyntax Collection,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : StatementSyntax(SyntaxKind.ForeachStatement);

public sealed record WithStatementSyntax(
    SyntaxToken WithKeyword,
    ExpressionSyntax Receiver,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : StatementSyntax(SyntaxKind.WithStatement);

public sealed record CaseStatementSyntax(
    SyntaxToken CaseKeyword,
    ExpressionSyntax Expression,
    SyntaxToken OfKeyword,
    IReadOnlyList<CaseClauseSyntax> Clauses,
    SyntaxToken? ElseKeyword,
    IReadOnlyList<StatementSyntax> ElseStatements,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.CaseStatement);

public sealed record CaseClauseSyntax(
    IReadOnlyList<ExpressionSyntax> Labels,
    SyntaxToken ColonToken,
    StatementSyntax Body) : SyntaxNode(SyntaxKind.CaseClause);

public sealed record MatchStatementSyntax(
    SyntaxToken MatchKeyword,
    ExpressionSyntax Expression,
    SyntaxToken WithKeyword,
    IReadOnlyList<MatchStatementArmSyntax> Arms,
    SyntaxToken? ElseKeyword,
    IReadOnlyList<StatementSyntax> ElseStatements,
    SyntaxToken EndKeyword,
    SyntaxToken? EndMatchKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.MatchStatement);

public sealed record MatchStatementArmSyntax(
    IReadOnlyList<ExpressionSyntax> Labels,
    bool IsWildcard,
    QualifiedNameSyntax? TypeName,
    SyntaxToken? Identifier,
    SyntaxToken? WhenKeyword,
    ExpressionSyntax? Guard,
    SyntaxToken ArrowToken,
    StatementSyntax Body) : SyntaxNode(SyntaxKind.MatchStatementArm);

public sealed record ReturnStatementSyntax(
    SyntaxToken ReturnKeyword,
    ExpressionSyntax? Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ReturnStatement);

public sealed record BreakStatementSyntax(
    SyntaxToken BreakKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.BreakStatement);

public sealed record ContinueStatementSyntax(
    SyntaxToken ContinueKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ContinueStatement);

public sealed record RaiseStatementSyntax(
    SyntaxToken Keyword,
    ExpressionSyntax? Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.RaiseStatement);

public sealed record TryStatementSyntax(
    SyntaxToken TryKeyword,
    IReadOnlyList<StatementSyntax> TryStatements,
    SyntaxToken? ExceptKeyword,
    IReadOnlyList<ExceptionClauseSyntax> ExceptionClauses,
    IReadOnlyList<StatementSyntax> ExceptStatements,
    SyntaxToken? FinallyKeyword,
    IReadOnlyList<StatementSyntax> FinallyStatements,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.TryStatement);

public sealed record ExceptionClauseSyntax(
    SyntaxToken OnKeyword,
    SyntaxToken Identifier,
    SyntaxToken ColonToken,
    QualifiedNameSyntax TypeName,
    SyntaxToken DoKeyword,
    StatementSyntax Body) : SyntaxNode(SyntaxKind.ExceptionClause);

public sealed record LocalVariableDeclarationStatementSyntax(
    SyntaxToken VarKeyword,
    IReadOnlyList<VariableDeclaratorSyntax> Declarators,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.LocalVariableDeclarationStatement);

public sealed record IncStatementSyntax(
    SyntaxToken Keyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Target,
    SyntaxToken CloseParenToken,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.IncStatement);

public sealed record DecStatementSyntax(
    SyntaxToken Keyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Target,
    SyntaxToken CloseParenToken,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.DecStatement);

public sealed record ExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ExpressionStatement);

public sealed record LiteralExpressionSyntax(
    SyntaxToken LiteralToken) : ExpressionSyntax(SyntaxKind.LiteralExpression);

public sealed record SetLiteralExpressionSyntax(
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> Elements,
    SyntaxToken CloseBracketToken) : ExpressionSyntax(SyntaxKind.SetLiteralExpression);

public sealed record RangeExpressionSyntax(
    ExpressionSyntax Start,
    SyntaxToken RangeToken,
    ExpressionSyntax End) : ExpressionSyntax(SyntaxKind.RangeExpression);

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

public sealed record CompoundAssignmentExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OperatorToken,
    ExpressionSyntax Expression) : ExpressionSyntax(SyntaxKind.CompoundAssignmentExpression);

public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    SyntaxToken OperatorToken,
    ExpressionSyntax Right) : ExpressionSyntax(SyntaxKind.BinaryExpression);

public sealed record MatchNotPatternSyntax(
    SyntaxToken NotKeyword,
    ExpressionSyntax Pattern) : ExpressionSyntax(SyntaxKind.MatchNotPattern);

public sealed record MatchOrPatternSyntax(
    IReadOnlyList<ExpressionSyntax> Patterns) : ExpressionSyntax(SyntaxKind.MatchOrPattern);

public sealed record MatchAndPatternSyntax(
    IReadOnlyList<MatchRelationalPatternSyntax> Patterns) : ExpressionSyntax(SyntaxKind.MatchAndPattern);

public sealed record MatchRelationalPatternSyntax(
    SyntaxToken OperatorToken,
    ExpressionSyntax Operand) : ExpressionSyntax(SyntaxKind.MatchRelationalPattern);

public sealed record TypeTestExpressionSyntax(
    ExpressionSyntax Expression,
    SyntaxToken IsKeyword,
    QualifiedNameSyntax TypeName) : ExpressionSyntax(SyntaxKind.TypeTestExpression);

public sealed record AsExpressionSyntax(
    ExpressionSyntax Expression,
    SyntaxToken AsKeyword,
    QualifiedNameSyntax TypeName) : ExpressionSyntax(SyntaxKind.AsExpression);

public sealed record ArgumentSyntax(
    ExpressionSyntax Expression) : SyntaxNode(SyntaxKind.Argument);

public sealed record CallExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ArgumentSyntax> Arguments,
    SyntaxToken CloseParenToken) : ExpressionSyntax(SyntaxKind.CallExpression);

public sealed record MatchExpressionSyntax(
    SyntaxToken MatchKeyword,
    ExpressionSyntax Expression,
    SyntaxToken WithKeyword,
    IReadOnlyList<MatchExpressionArmSyntax> Arms,
    SyntaxToken EndKeyword) : ExpressionSyntax(SyntaxKind.MatchExpression);

public sealed record MatchExpressionArmSyntax(
    IReadOnlyList<ExpressionSyntax> Labels,
    bool IsWildcard,
    QualifiedNameSyntax? TypeName,
    SyntaxToken? Identifier,
    SyntaxToken? WhenKeyword,
    ExpressionSyntax? Guard,
    SyntaxToken ArrowToken,
    ExpressionSyntax Expression) : SyntaxNode(SyntaxKind.MatchExpressionArm);

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
            if (_position < _text.Length && _text[_position] == '=')
            {
                if (text == "and")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.AndAssignToken, "and=", null, new TextSpan(start, 4));
                }

                if (text == "or")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.OrAssignToken, "or=", null, new TextSpan(start, 3));
                }

                if (text == "xor")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.XorAssignToken, "xor=", null, new TextSpan(start, 4));
                }

                if (text == "mod")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ModAssignToken, "mod=", null, new TextSpan(start, 4));
                }

                if (text == "div")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.DivAssignToken, "div=", null, new TextSpan(start, 4));
                }

                if (text == "shl")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ShlAssignToken, "shl=", null, new TextSpan(start, 4));
                }

                if (text == "shr")
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ShrAssignToken, "shr=", null, new TextSpan(start, 4));
                }
            }

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
            '+' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.PlusAssignToken, "+="),
            '+' => new SyntaxToken(SyntaxKind.PlusToken, "+", null, new TextSpan(start, 1)),
            '-' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.MinusAssignToken, "-="),
            '-' => new SyntaxToken(SyntaxKind.MinusToken, "-", null, new TextSpan(start, 1)),
            '*' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.StarAssignToken, "*="),
            '*' => new SyntaxToken(SyntaxKind.StarToken, "*", null, new TextSpan(start, 1)),
            '/' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.SlashAssignToken, "/="),
            '/' => new SyntaxToken(SyntaxKind.SlashToken, "/", null, new TextSpan(start, 1)),
            '?' when Peek() == '?' && Peek(1) == '=' => LexThreeCharacterToken(SyntaxKind.NullCoalescingAssignToken, "??="),
            '?' when Peek() == '?' => LexTwoCharacterToken(SyntaxKind.NullCoalescingToken, "??"),
            ',' => new SyntaxToken(SyntaxKind.CommaToken, ",", null, new TextSpan(start, 1)),
            '.' when Peek() == '.' => LexTwoCharacterToken(SyntaxKind.RangeToken, ".."),
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

    private SyntaxToken LexThreeCharacterToken(SyntaxKind kind, string text)
    {
        var start = _position - 1;
        _position += 2;
        return new SyntaxToken(kind, text, null, new TextSpan(start, 3));
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

    private char Peek() => Peek(0);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _text.Length ? _text[index] : '\0';
    }

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
            "set" => SyntaxKind.SetKeyword,
            "of" => SyntaxKind.OfKeyword,
            "var" => SyntaxKind.VarKeyword,
            "const" => SyntaxKind.ConstKeyword,
            "enum" => SyntaxKind.EnumKeyword,
            "class" => SyntaxKind.ClassKeyword,
            "record" => SyntaxKind.RecordKeyword,
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
            "init" => SyntaxKind.InitKeyword,
            "return" => SyntaxKind.ReturnKeyword,
            "exit" => SyntaxKind.ExitKeyword,
            "break" => SyntaxKind.BreakKeyword,
            "continue" => SyntaxKind.ContinueKeyword,
            "raise" => SyntaxKind.RaiseKeyword,
            "throw" => SyntaxKind.ThrowKeyword,
            "case" => SyntaxKind.CaseKeyword,
            "match" => SyntaxKind.MatchKeyword,
            "if" => SyntaxKind.IfKeyword,
            "then" => SyntaxKind.ThenKeyword,
            "else" => SyntaxKind.ElseKeyword,
            "while" => SyntaxKind.WhileKeyword,
            "repeat" => SyntaxKind.RepeatKeyword,
            "until" => SyntaxKind.UntilKeyword,
            "for" => SyntaxKind.ForKeyword,
            "each" => SyntaxKind.EachKeyword,
            "foreach" => SyntaxKind.ForeachKeyword,
            "with" => SyntaxKind.WithKeyword,
            "div" => SyntaxKind.DivKeyword,
            "to" => SyntaxKind.ToKeyword,
            "downto" => SyntaxKind.DowntoKeyword,
            "in" => SyntaxKind.InKeyword,
            "do" => SyntaxKind.DoKeyword,
            "step" => SyntaxKind.StepKeyword,
            "inc" => SyntaxKind.IncKeyword,
            "dec" => SyntaxKind.DecKeyword,
            "try" => SyntaxKind.TryKeyword,
            "except" => SyntaxKind.ExceptKeyword,
            "finally" => SyntaxKind.FinallyKeyword,
            "on" => SyntaxKind.OnKeyword,
            "new" => SyntaxKind.NewKeyword,
            "static" => SyntaxKind.StaticKeyword,
            "default" => SyntaxKind.DefaultKeyword,
            "readonly" => SyntaxKind.ReadonlyKeyword,
            "out" => SyntaxKind.OutKeyword,
            "ref" => SyntaxKind.RefKeyword,
            "public" => SyntaxKind.PublicKeyword,
            "private" => SyntaxKind.PrivateKeyword,
            "protected" => SyntaxKind.ProtectedKeyword,
            "internal" => SyntaxKind.InternalKeyword,
            "params" => SyntaxKind.ParamsKeyword,
            "extern" => SyntaxKind.ExternKeyword,
            "as" => SyntaxKind.AsKeyword,
            "is" => SyntaxKind.IsKeyword,
            "and" => SyntaxKind.AndKeyword,
            "not" => SyntaxKind.NotKeyword,
            "or" => SyntaxKind.OrKeyword,
            "when" => SyntaxKind.WhenKeyword,
            "mod" => SyntaxKind.ModKeyword,
            "shl" => SyntaxKind.ShlKeyword,
            "shr" => SyntaxKind.ShrKeyword,
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
            SyntaxKind.ClassKeyword or SyntaxKind.RecordKeyword => ParseClassDeclaration(modifiers),
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

    private TypeMemberSyntax ParseTypeMember()
    {
        var modifiers = ParseModifiers();
        return Current.Kind switch
        {
            SyntaxKind.VarKeyword => ParseFieldDeclaration(modifiers),
            SyntaxKind.ConstKeyword => ParseConstantDeclaration(modifiers),
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

            var colonToken = Match(SyntaxKind.ColonToken);
            var body = ParseStatement();
            clauses.Add(new CaseClauseSyntax(labels, colonToken, body));
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

        var start = ParseExpression();
        if (Current.Kind == SyntaxKind.RangeToken)
        {
            var rangeToken = NextToken();
            var end = ParseExpression();
            return new RangeExpressionSyntax(start, rangeToken, end);
        }

        return start;
    }

    private MatchRelationalPatternSyntax ParseMatchRelationalPattern()
    {
        var operatorToken = NextToken();
        var operand = ParseExpression();
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
            SyntaxKind.RaiseKeyword or SyntaxKind.ThrowKeyword => ParseRaiseStatement(false),
            SyntaxKind.TryKeyword => ParseTryStatement(false),
            SyntaxKind.VarKeyword => ParseLocalVariableDeclarationStatement(false),
            _ => ParseExpressionStatement(false)
        };

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
        var left = ParseComparisonExpression();
        if (Current.Kind == SyntaxKind.NullCoalescingToken)
        {
            var operatorToken = NextToken();
            var right = ParseNullCoalescingExpression();
            return new BinaryExpressionSyntax(left, operatorToken, right);
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

        if (name.Parts.Count >= 2 && name.Parts[^1].Text == "Length")
        {
            expression = new ArrayLengthExpressionSyntax(new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray()));
            return ParsePostfixExpression(expression);
        }

        expression = new NameExpressionSyntax(name);
        return ParsePostfixExpression(expression);
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

        return Peek(offset).Kind is SyntaxKind.AssignToken
            or SyntaxKind.PlusAssignToken
            or SyntaxKind.MinusAssignToken
            or SyntaxKind.StarAssignToken
            or SyntaxKind.SlashAssignToken
            or SyntaxKind.DivAssignToken
            or SyntaxKind.ModAssignToken
            or SyntaxKind.AndAssignToken
            or SyntaxKind.OrAssignToken
            or SyntaxKind.XorAssignToken
            or SyntaxKind.ShlAssignToken
            or SyntaxKind.ShrAssignToken
            or SyntaxKind.NullCoalescingAssignToken;
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
        var root = NormalizeUsesAliases(parser.ParseCompilationUnit(sourceText));
        return new SyntaxTree(root, diagnostics);
    }

    public static SyntaxTree Merge(SyntaxTree primary, IEnumerable<SyntaxTree> importedTrees)
    {
        var imported = importedTrees.ToArray();
        if (imported.Length == 0)
        {
            return primary;
        }

        var mergedRoot = new CompilationUnitSyntax(
            primary.Root.Namespace,
            primary.Root.Uses,
            primary.Root.Members.Concat(imported.SelectMany(tree => tree.Root.Members)).ToArray(),
            primary.Root.EndOfFileToken,
            primary.Root.Tokens.Concat(imported.SelectMany(tree => tree.Root.Tokens)).ToArray(),
            primary.Root.SourceText);

        var diagnostics = primary.Diagnostics.Concat(imported.SelectMany(tree => tree.Diagnostics)).ToList();
        diagnostics.AddRange(DetectImportAmbiguities(primary.Root, imported));
        return new SyntaxTree(mergedRoot, diagnostics);
    }

    private static IReadOnlyList<Diagnostic> DetectImportAmbiguities(CompilationUnitSyntax primaryRoot, IReadOnlyList<SyntaxTree> importedTrees)
    {
        if (primaryRoot.Uses is null)
        {
            return [];
        }

        var unaliasedImports = primaryRoot.Uses.Imports
            .Where(importSyntax => importSyntax.AliasIdentifier is null)
            .ToDictionary(
                importSyntax => importSyntax.NamespaceName.ToDisplayString(),
                importSyntax => importSyntax.NamespaceName.Parts[0].Span,
                StringComparer.Ordinal);
        if (unaliasedImports.Count < 2)
        {
            return [];
        }

        var importedTypes = importedTrees
            .Select(tree => new
            {
                Namespace = tree.Root.Namespace?.Name.ToDisplayString(),
                TypeNames = tree.Root.Members
                    .Select(member => member switch
                    {
                        ClassDeclarationSyntax classDeclaration => classDeclaration.Identifier.Text,
                        EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.Text,
                        _ => null
                    })
                    .Where(typeName => typeName is not null)
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item => item.Namespace is not null && unaliasedImports.ContainsKey(item.Namespace))
            .ToArray();

        return importedTypes
            .SelectMany(item => item.TypeNames.Select(typeName => (item.Namespace!, TypeName: typeName)))
            .GroupBy(item => item.TypeName, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Item1).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group =>
            {
                var namespaces = group.Select(item => item.Item1).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var span = unaliasedImports[namespaces[0]];
                return new Diagnostic(
                    "ILC2184",
                    $"Imported type '{group.Key}' is ambiguous between namespaces {string.Join(", ", namespaces)}. Use an alias-qualified import.",
                    DiagnosticSeverity.Error,
                    span);
            })
            .ToArray();
    }

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
            ClassDeclarationSyntax classDeclaration => classDeclaration with
            {
                Members = classDeclaration.Members.Select(memberSyntax => NormalizeTypeMember(memberSyntax, aliases)).ToArray()
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
                ReturnType = NormalizeQualifiedName(methodDeclaration.ReturnType, aliases),
                Parameters = methodDeclaration.Parameters.Select(parameter => NormalizeParameter(parameter, aliases)!).ToArray(),
                ExpressionBody = NormalizeExpression(methodDeclaration.ExpressionBody, aliases),
                Body = NormalizeBlock(methodDeclaration.Body, aliases)
            },
            _ => member
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
