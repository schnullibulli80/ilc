namespace ILC.Compiler.Binding;

using ILC.Compiler.Syntax;

public enum BoundReceiverKind
{
    None,
    Local,
    Self,
    Type,
    Expression
}

public sealed record BoundReceiver(
    BoundReceiverKind Kind,
    TypeSymbol Type,
    string? LocalName = null,
    TypeSymbol? TargetType = null,
    ExpressionSyntax? SourceExpression = null);

public enum BoundMemberReadKind
{
    Local,
    Field,
    Property,
    Constant
}

public sealed record BoundMemberRead(
    BoundMemberReadKind Kind,
    string DisplayName,
    TypeSymbol Type,
    BoundReceiver? Receiver = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? GetterMethod = null,
    FieldSymbol? ReadField = null,
    ConstantSymbol? Constant = null,
    ExpressionSyntax? SourceExpression = null);

public sealed record BoundLengthRead(
    string DisplayName,
    TypeSymbol TargetType,
    BoundReceiver? Receiver = null,
    ExpressionSyntax? SourceExpression = null);

public sealed record BoundSliceRead(
    string DisplayName,
    TypeSymbol TargetType,
    BoundReceiver? Receiver = null,
    ExpressionSyntax? TargetExpression = null,
    RangeExpressionSyntax? Range = null);

public sealed record BoundElementRead(
    string DisplayName,
    TypeSymbol ElementType,
    BoundReceiver? Receiver = null,
    PropertySymbol? IndexerProperty = null,
    MethodSymbol? GetterMethod = null,
    FieldSymbol? ReadField = null,
    TypeSymbol? IndexedType = null,
    ExpressionSyntax? TargetExpression = null);

public sealed record BoundElementWrite(
    string DisplayName,
    TypeSymbol ElementType,
    BoundReceiver? Receiver = null,
    PropertySymbol? IndexerProperty = null,
    MethodSymbol? SetterMethod = null,
    FieldSymbol? WriteField = null,
    TypeSymbol? IndexedType = null,
    ExpressionSyntax? TargetExpression = null);

public enum BoundWriteTargetKind
{
    Local,
    Field,
    Property,
    ElementAccess
}

public sealed record BoundWriteTarget(
    BoundWriteTargetKind Kind,
    string DisplayName,
    TypeSymbol Type,
    BoundReceiver? Receiver = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? SetterMethod = null,
    FieldSymbol? WriteField = null,
    ExpressionSyntax? SourceExpression = null);

public enum BoundCallKind
{
    Direct,
    Virtual,
    Intrinsic,
    Constructor
}

public sealed record BoundCall(
    BoundCallKind Kind,
    string DisplayName,
    MethodSymbol Method,
    TypeSymbol ReturnType,
    BoundReceiver? Receiver = null,
    IReadOnlyList<TypeSymbol>? ArgumentTypes = null,
    ExpressionSyntax? SourceExpression = null);
