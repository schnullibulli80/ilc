namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public enum IrOpCode
{
    LoadConstant,
    Copy,
    Add,
    ShiftLeft,
    ShiftRight,
    BitwiseAnd,
    BitwiseOr,
    BitwiseNot,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    CompareEqual,
    CompareNotEqual,
    CompareEqualString,
    CompareNotEqualString,
    CompareEqualReference,
    CompareNotEqualReference,
    TypeIsReference,
    AsReference,
    CompareLess,
    CompareLessOrEqual,
    CompareGreater,
    CompareGreaterOrEqual,
    NewObject,
    NewArray,
    LoadField,
    StoreField,
    LoadStaticField,
    StoreStaticField,
    LoadElement,
    StoreElement,
    LoadLength,
    SliceString,
    ConcatString,
    ReplaceString,
    InsertString,
    RemoveString,
    ToUpperString,
    ToLowerString,
    TrimString,
    TrimStartString,
    TrimEndString,
    ParseStringToInteger,
    TryParseStringToInteger,
    ConvertIntegerToString,
    StartsWithString,
    EndsWithString,
    ContainsString,
    IndexOfString,
    LastIndexOfString,
    Throw,
    Rethrow,
    Call,
    CallVirtual,
    Branch,
    BranchIfFalse,
    Label,
    Return
}

public sealed record IrValue(string Name, TypeSymbol Type, ushort Index);
public sealed record IrArrayShape(IReadOnlyList<IrValue> Extents);
public sealed record IrExceptionHandler(string TryStartLabel, string TryEndLabel, string HandlerStartLabel, string HandlerEndLabel, ushort TargetRegister = ushort.MaxValue, string? CatchTypeName = null);

public sealed record IrCallTarget(MethodSymbol? Method, string DisplayName, IReadOnlyList<IrValue> Arguments, IrValue? Receiver = null, bool IsVirtual = false);

public sealed record IrFieldTarget(FieldSymbol Field, string DisplayName, IrValue? Receiver = null);
public sealed record IrNewArrayTarget(string ElementTypeName, IrValue LengthRegister, IrArrayShape Shape);
public sealed record IrArrayTarget(IrValue Array, IrValue? Index = null, IrArrayShape? Shape = null);
public sealed record IrStringSliceTarget(IrValue Source, IrValue Start, IrValue End);
public sealed record IrStringReplaceTarget(IrValue Source, IrValue OldValue, IrValue NewValue);
public sealed record IrStringInsertTarget(IrValue Source, IrValue Index, IrValue Value);
public sealed record IrStringRemoveTarget(IrValue Source, IrValue Index, IrValue Length);
public sealed record IrStringTryParseTarget(IrValue Source, IrValue ParsedValue);
public sealed record IrTypeCheckTarget(IrValue Value, string TypeName);
public sealed record PreparedCallFrame(IReadOnlyList<IrValue> Arguments, IReadOnlyList<(ExpressionSyntax Target, IrValue Source)> CopyBacks, IrValue? Receiver = null);

public sealed record IrInstruction(IrOpCode OpCode, IrValue? Destination, object? Operand);

public sealed record IrBasicBlock(string Name, IReadOnlyList<IrInstruction> Instructions);

public enum IrDebugVariableKind
{
    Self,
    Parameter,
    Local
}

public sealed record IrDebugVariable(
    string Name,
    TypeSymbol Type,
    ushort RegisterIndex,
    int VmIpStart,
    int VmIpEnd,
    IrDebugVariableKind Kind);

public sealed record IrDebugSourceMap(
    TextSpan Span,
    int VmIpStart,
    int VmIpEnd);

public sealed record IrFunction(
    string Name,
    TypeSymbol ReturnType,
    IReadOnlyList<IrValue> Registers,
    IReadOnlyList<IrBasicBlock> Blocks,
    IReadOnlyList<IrArrayShape> ArrayShapes,
    IReadOnlyList<IrExceptionHandler> ExceptionHandlers,
    IReadOnlyList<IrDebugVariable> DebugVariables,
    IReadOnlyList<IrDebugSourceMap> DebugSourceMaps);
