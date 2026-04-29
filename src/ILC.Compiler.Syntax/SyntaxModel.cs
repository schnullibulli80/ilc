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
    DelegateKeyword,
    ClassKeyword,
    RecordKeyword,
    InterfaceKeyword,
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
    FromKeyword,
    JoinKeyword,
    IntoKeyword,
    LetKeyword,
    WhereKeyword,
    SelectKeyword,
    OrderByKeyword,
    DescendingKeyword,
    TakeKeyword,
    SkipKeyword,
    DivKeyword,
    ToKeyword,
    DowntoKeyword,
    InKeyword,
    DoKeyword,
    StepKeyword,
    IncKeyword,
    DecKeyword,
    IncludeKeyword,
    ExcludeKeyword,
    TryKeyword,
    ExceptKeyword,
    FinallyKeyword,
    OnKeyword,
    NewKeyword,
    StaticKeyword,
    DefaultKeyword,
    ReadonlyKeyword,
    VirtualKeyword,
    OverrideKeyword,
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
    XorKeyword,
    NotInKeyword,
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
    Attribute,
    QualifiedName,
    TopLevelVariableDeclaration,
    TopLevelConstantDeclaration,
    EnumDeclaration,
    DelegateDeclaration,
    VariableDeclarator,
    ConstantDeclarator,
    EnumMember,
    TopLevelExpressionStatement,
    ClassDeclaration,
    InterfaceDeclaration,
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
    IncludeStatement,
    ExcludeStatement,
    ExpressionStatement,
    LiteralExpression,
    SetLiteralExpression,
    ProjectorMember,
    ProjectorExpression,
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
    QueryExpression,
    LambdaExpression,
    Argument,
    UnaryExpression,
    MatchExpression,
    MatchExpressionArm
}

public abstract record SyntaxNode(SyntaxKind Kind);

public sealed record NumericLiteralValue(
    string RawText,
    string Digits,
    int? Int32Value);

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

public sealed record TypeParameterListSyntax(
    SyntaxToken LessThanToken,
    IReadOnlyList<SyntaxToken> Parameters,
    SyntaxToken GreaterThanToken) : SyntaxNode(SyntaxKind.QualifiedName)
{
    public IReadOnlyList<string> GetParameterNames() => Parameters.Select(parameter => parameter.Text).ToArray();
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

public sealed record DelegateDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken DelegateKeyword,
    SyntaxToken SignatureKeyword,
    SyntaxToken Identifier,
    TypeParameterListSyntax? TypeParameters,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    SyntaxToken? CloseParenToken,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? ReturnType,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.DelegateDeclaration);

public sealed record TopLevelExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.TopLevelExpressionStatement);

public sealed record ClassDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken ClassKeyword,
    SyntaxToken Identifier,
    TypeParameterListSyntax? TypeParameters,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? BaseType,
    IReadOnlyList<QualifiedNameSyntax> InterfaceTypes,
    SyntaxToken BeginKeyword,
    IReadOnlyList<TypeMemberSyntax> Members,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.ClassDeclaration);

public sealed record InterfaceDeclarationSyntax(
    IReadOnlyList<SyntaxToken> Modifiers,
    SyntaxToken InterfaceKeyword,
    SyntaxToken Identifier,
    TypeParameterListSyntax? TypeParameters,
    SyntaxToken? ColonToken,
    IReadOnlyList<QualifiedNameSyntax> BaseInterfaces,
    SyntaxToken BeginKeyword,
    IReadOnlyList<TypeMemberSyntax> Members,
    SyntaxToken EndKeyword,
    SyntaxToken SemicolonToken) : MemberSyntax(SyntaxKind.InterfaceDeclaration);

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

public sealed record AttributeSyntax(
    SyntaxToken OpenBracketToken,
    QualifiedNameSyntax Name,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<ArgumentSyntax> Arguments,
    SyntaxToken? CloseParenToken,
    SyntaxToken CloseBracketToken) : SyntaxNode(SyntaxKind.Attribute);

public sealed record MethodDeclarationSyntax(
    IReadOnlyList<AttributeSyntax> Attributes,
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
    SyntaxToken? WhenKeyword,
    ExpressionSyntax? Guard,
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

public sealed record IncludeStatementSyntax(
    SyntaxToken Keyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Target,
    SyntaxToken CommaToken,
    ExpressionSyntax Value,
    SyntaxToken CloseParenToken,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.IncludeStatement);

public sealed record ExcludeStatementSyntax(
    SyntaxToken Keyword,
    SyntaxToken OpenParenToken,
    ExpressionSyntax Target,
    SyntaxToken CommaToken,
    ExpressionSyntax Value,
    SyntaxToken CloseParenToken,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ExcludeStatement);

public sealed record ExpressionStatementSyntax(
    ExpressionSyntax Expression,
    SyntaxToken SemicolonToken) : StatementSyntax(SyntaxKind.ExpressionStatement);

public sealed record LiteralExpressionSyntax(
    SyntaxToken LiteralToken) : ExpressionSyntax(SyntaxKind.LiteralExpression);

public sealed record SetLiteralExpressionSyntax(
    SyntaxToken OpenBracketToken,
    IReadOnlyList<ExpressionSyntax> Elements,
    SyntaxToken CloseBracketToken) : ExpressionSyntax(SyntaxKind.SetLiteralExpression);

public sealed record ProjectorMemberSyntax(
    SyntaxToken Identifier,
    SyntaxToken AssignToken,
    ExpressionSyntax Expression) : SyntaxNode(SyntaxKind.ProjectorMember);

public sealed record ProjectorExpressionSyntax(
    SyntaxToken NewKeyword,
    SyntaxToken OpenBraceToken,
    IReadOnlyList<ProjectorMemberSyntax> Members,
    SyntaxToken CloseBraceToken) : ExpressionSyntax(SyntaxKind.ProjectorExpression);

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
    ExpressionSyntax Expression,
    SyntaxToken? ModifierKeyword = null) : SyntaxNode(SyntaxKind.Argument);

public sealed record CallExpressionSyntax(
    ExpressionSyntax Target,
    SyntaxToken OpenParenToken,
    IReadOnlyList<ArgumentSyntax> Arguments,
    SyntaxToken CloseParenToken) : ExpressionSyntax(SyntaxKind.CallExpression);

public sealed record QueryExpressionSyntax(
    SyntaxToken FromKeyword,
    SyntaxToken Identifier,
    SyntaxToken InKeyword,
    ExpressionSyntax SourceExpression,
    SyntaxToken? JoinKeyword,
    SyntaxToken? JoinIdentifier,
    SyntaxToken? JoinInKeyword,
    ExpressionSyntax? JoinSourceExpression,
    SyntaxToken? JoinOnKeyword,
    ExpressionSyntax? JoinLeftExpression,
    SyntaxToken? JoinEqualsKeyword,
    ExpressionSyntax? JoinRightExpression,
    SyntaxToken? JoinIntoKeyword,
    SyntaxToken? JoinIntoIdentifier,
    SyntaxToken? SecondFromKeyword,
    SyntaxToken? SecondIdentifier,
    SyntaxToken? SecondInKeyword,
    ExpressionSyntax? SecondSourceExpression,
    SyntaxToken? LetKeyword,
    SyntaxToken? LetIdentifier,
    SyntaxToken? LetAssignToken,
    ExpressionSyntax? LetExpression,
    SyntaxToken? WhereKeyword,
    ExpressionSyntax? PredicateExpression,
    SyntaxToken? OrderByKeyword,
    ExpressionSyntax? OrderByExpression,
    SyntaxToken? DescendingKeyword,
    SyntaxToken? ThenByCommaToken,
    SyntaxToken? ThenByKeyword,
    ExpressionSyntax? ThenByExpression,
    SyntaxToken? ThenByDescendingKeyword,
    SyntaxToken? GroupKeyword,
    ExpressionSyntax? GroupExpression,
    SyntaxToken? GroupByKeyword,
    ExpressionSyntax? GroupByExpression,
    SyntaxToken SelectKeyword,
    ExpressionSyntax SelectExpression,
    SyntaxToken? IntoKeyword,
    SyntaxToken? IntoIdentifier,
    SyntaxToken? ContinuationLetKeyword,
    SyntaxToken? ContinuationLetIdentifier,
    SyntaxToken? ContinuationLetAssignToken,
    ExpressionSyntax? ContinuationLetExpression,
    SyntaxToken? ContinuationWhereKeyword,
    ExpressionSyntax? ContinuationPredicateExpression,
    SyntaxToken? ContinuationOrderByKeyword,
    ExpressionSyntax? ContinuationOrderByExpression,
    SyntaxToken? ContinuationDescendingKeyword,
    SyntaxToken? ContinuationThenByCommaToken,
    SyntaxToken? ContinuationThenByKeyword,
    ExpressionSyntax? ContinuationThenByExpression,
    SyntaxToken? ContinuationThenByDescendingKeyword,
    SyntaxToken? ContinuationSelectKeyword,
    ExpressionSyntax? ContinuationSelectExpression,
    SyntaxToken? TakeKeyword,
    ExpressionSyntax? TakeExpression,
    SyntaxToken? SkipKeyword,
    ExpressionSyntax? SkipExpression) : ExpressionSyntax(SyntaxKind.QueryExpression);

public sealed record LambdaExpressionSyntax(
    SyntaxToken SignatureKeyword,
    SyntaxToken? OpenParenToken,
    IReadOnlyList<ParameterSyntax> Parameters,
    SyntaxToken? CloseParenToken,
    SyntaxToken? ColonToken,
    QualifiedNameSyntax? ReturnType,
    SyntaxToken ArrowToken,
    ExpressionSyntax Body) : ExpressionSyntax(SyntaxKind.LambdaExpression);

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
