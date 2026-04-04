namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Syntax;

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

public sealed record IrInstruction(IrOpCode OpCode, IrValue? Destination, object? Operand);

public sealed record IrBasicBlock(string Name, IReadOnlyList<IrInstruction> Instructions);

public sealed record IrFunction(
    string Name,
    TypeSymbol ReturnType,
    IReadOnlyList<IrValue> Registers,
    IReadOnlyList<IrBasicBlock> Blocks,
    IReadOnlyList<IrArrayShape> ArrayShapes,
    IReadOnlyList<IrExceptionHandler> ExceptionHandlers);

public sealed class Lowerer
{
    private readonly IReadOnlyList<MethodSymbol> _knownMethods;
    private readonly IReadOnlyList<FieldSymbol> _knownFields;
    private readonly IReadOnlyList<ConstantSymbol> _knownConstants;
    private readonly IReadOnlyList<PropertySymbol> _knownProperties;
    private readonly IReadOnlyList<TypeSymbol> _knownTypes;
    private readonly Stack<(string BreakLabel, string ContinueLabel)> _loopLabels = new();
    private int _labelCounter;

    public Lowerer(IEnumerable<MethodSymbol>? knownMethods = null, IEnumerable<FieldSymbol>? knownFields = null, IEnumerable<TypeSymbol>? knownTypes = null, IEnumerable<PropertySymbol>? knownProperties = null, IEnumerable<ConstantSymbol>? knownConstants = null)
    {
        _knownMethods = knownMethods?.ToArray() ?? [];
        _knownFields = knownFields?.ToArray() ?? [];
        _knownConstants = knownConstants?.ToArray() ?? [];
        _knownTypes = knownTypes?.ToArray() ?? [];
        _knownProperties = knownProperties?.ToArray() ?? [];
    }

    public IrFunction Lower(MethodSymbol method)
    {
        var registers = new List<IrValue>();
        var registerByName = new Dictionary<string, IrValue>(StringComparer.Ordinal);
        var localTypes = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        var arrayShapesByName = new Dictionary<string, IReadOnlyList<IrValue>>(StringComparer.Ordinal);
        var exceptionHandlers = new List<IrExceptionHandler>();
        _labelCounter = 0;
        _loopLabels.Clear();

        ushort nextIndex = 0;
        if (method.DeclaringTypeName is not null && !method.IsStatic)
        {
            var selfType = new TypeSymbol(method.DeclaringTypeName, true);
            var selfRegister = new IrValue($"r{nextIndex}", selfType, nextIndex);
            registers.Add(selfRegister);
            registerByName["self"] = selfRegister;
            localTypes["self"] = selfType;
            nextIndex++;
        }

        for (ushort parameterIndex = 0; parameterIndex < method.Parameters.Count; parameterIndex++, nextIndex++)
        {
            var parameter = method.Parameters[parameterIndex];
            var register = new IrValue($"r{nextIndex}", parameter.Type, nextIndex);
            registers.Add(register);
            registerByName[parameter.Name] = register;
            localTypes[parameter.Name] = parameter.Type;
        }

        var instructions = new List<IrInstruction>();
        IrValue? returnRegister = null;

        if (method.ReturnType != TypeSymbol.Void)
        {
            var returnIndex = (ushort)registers.Count;
            returnRegister = new IrValue($"r{returnIndex}", method.ReturnType, returnIndex);
            registers.Add(returnRegister);
        }

        if (method.Declaration is null)
        {
            if (method.IsSynthetic && method.SyntheticMembers is not null)
            {
                LowerSyntheticTopLevelBody(method, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers);
            }
            else
            {
                if (returnRegister is not null)
                {
                    instructions.Add(new IrInstruction(IrOpCode.LoadConstant, returnRegister, 0));
                }

                instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
            }
        }
        else
        {
            LowerMethodBody(method.Declaration, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, false, method);
        }

        var entryBlock = new IrBasicBlock("entry", instructions);
        var arrayShapes = instructions
            .Where(instruction => instruction.OpCode == IrOpCode.NewArray && instruction.Operand is IrNewArrayTarget)
            .Select(instruction => ((IrNewArrayTarget)instruction.Operand!).Shape)
            .ToArray();
        return new IrFunction(method.Name, method.ReturnType, registers, [entryBlock], arrayShapes, exceptionHandlers);
    }

    private void LowerMethodBody(
        MethodDeclarationSyntax declaration,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler = false,
        MethodSymbol? currentMethod = null)
    {
        if (declaration.ExpressionBody is not null)
        {
            LowerReturnExpression(declaration.ExpressionBody, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
            return;
        }

        if (declaration.Body is null)
        {
            instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
            return;
        }

        var hasTerminated = false;
        foreach (var statement in declaration.Body.Statements)
        {
            hasTerminated = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            if (hasTerminated)
            {
                break;
            }
        }

        if (!hasTerminated)
        {
            instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
        }
    }

    private void LowerSyntheticTopLevelBody(
        MethodSymbol method,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers)
    {
        foreach (var member in method.SyntheticMembers ?? [])
        {
            switch (member)
            {
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        var localType = BindSyntheticTopLevelType(declarator, localTypes, method);
                        var localRegister = new IrValue($"r{registers.Count}", localType, (ushort)registers.Count);
                        registers.Add(localRegister);
                        registerByName[declarator.Identifier.Text] = localRegister;
                        localTypes[declarator.Identifier.Text] = localType;

                        if (declarator.Initializer is NewArrayExpressionSyntax newArrayInitializer && newArrayInitializer.LengthExpressions.Count > 1)
                        {
                            arrayShapesByName[declarator.Identifier.Text] = LowerNewArrayInto(localRegister, newArrayInitializer, registerByName, registers, instructions, method);
                        }
                        else if (declarator.Initializer is not null)
                        {
                            LowerExpressionInto(declarator.Initializer, localRegister, registerByName, arrayShapesByName, registers, instructions, method);
                        }
                    }
                    break;
                case TopLevelConstantDeclarationSyntax:
                    break;
                case TopLevelExpressionStatementSyntax expressionStatement:
                    LowerExpressionStatement(expressionStatement.Expression, registerByName, localTypes, arrayShapesByName, registers, instructions, method);
                    break;
            }
        }

        if (returnRegister is not null)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, returnRegister, 0));
        }

        instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
    }

    private bool LowerStatement(
        StatementSyntax statement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                foreach (var nestedStatement in block.Statements)
                {
                    if (LowerStatement(nestedStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod))
                    {
                        return true;
                    }
                }
                return false;
            case LocalVariableDeclarationStatementSyntax localVariable:
                foreach (var declarator in localVariable.Declarators)
                {
                    var localType = BindLocalType(declarator, localTypes, currentMethod);
                    var localRegister = new IrValue($"r{registers.Count}", localType, (ushort)registers.Count);
                    registers.Add(localRegister);
                    registerByName[declarator.Identifier.Text] = localRegister;
                    localTypes[declarator.Identifier.Text] = localType;

                    if (declarator.Initializer is NewArrayExpressionSyntax newArrayInitializer && newArrayInitializer.LengthExpressions.Count > 1)
                    {
                        arrayShapesByName[declarator.Identifier.Text] = LowerNewArrayInto(localRegister, newArrayInitializer, registerByName, registers, instructions, currentMethod);
                    }
                    else if (declarator.Initializer is not null)
                    {
                        LowerExpressionInto(declarator.Initializer, localRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    }
                }
                return false;
            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression is not null)
                {
                    LowerExpressionInto(returnStatement.Expression, returnRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
                return true;
            case BreakStatementSyntax:
                if (_loopLabels.Count == 0)
                {
                    throw new InvalidOperationException("Cannot lower break outside a loop.");
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, _loopLabels.Peek().BreakLabel));
                return true;
            case ContinueStatementSyntax:
                if (_loopLabels.Count == 0)
                {
                    throw new InvalidOperationException("Cannot lower continue outside a loop.");
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, _loopLabels.Peek().ContinueLabel));
                return true;
            case RaiseStatementSyntax raiseStatement:
                LowerRaiseStatement(raiseStatement, registerByName, arrayShapesByName, registers, instructions, inExceptionHandler, currentMethod);
                return true;
            case ExpressionStatementSyntax expressionStatement:
                LowerExpressionStatement(expressionStatement.Expression, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case IncStatementSyntax incStatement:
                LowerIncDecStatement(incStatement.Target, SyntaxKind.PlusAssignToken, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case DecStatementSyntax decStatement:
                LowerIncDecStatement(decStatement.Target, SyntaxKind.MinusAssignToken, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case IncludeStatementSyntax includeStatement:
                LowerIncludeExcludeStatement(includeStatement.Target, includeStatement.Value, true, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case ExcludeStatementSyntax excludeStatement:
                LowerIncludeExcludeStatement(excludeStatement.Target, excludeStatement.Value, false, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case IfStatementSyntax ifStatement:
                return LowerIfStatement(ifStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            case WhileStatementSyntax whileStatement:
                LowerWhileStatement(whileStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                return false;
            case RepeatStatementSyntax repeatStatement:
                LowerRepeatStatement(repeatStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                return false;
            case ForStatementSyntax forStatement:
                LowerForStatement(forStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                return false;
            case ForeachStatementSyntax foreachStatement:
                LowerForeachStatement(foreachStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                return false;
            case WithStatementSyntax withStatement:
                var rewrittenWithBody = RewriteWithStatement(withStatement.Body, withStatement.Receiver, registerByName, localTypes, currentMethod);
                return LowerStatement(rewrittenWithBody, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            case CaseStatementSyntax caseStatement:
                return LowerCaseStatement(caseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            case MatchStatementSyntax matchStatement:
                return LowerMatchStatement(matchStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            case TryStatementSyntax tryStatement:
                return LowerTryStatement(tryStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            default:
                return false;
        }
    }

    private bool LowerIfStatement(
        IfStatementSyntax ifStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(ifStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elseLabel = AllocateLabel("else");
        var endLabel = AllocateLabel("endif");

        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, elseLabel));
        var thenTerminates = LowerStatement(ifStatement.ThenStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);

        if (ifStatement.ElseStatement is not null)
        {
            if (!thenTerminates)
            {
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
            var elseTerminates = LowerStatement(ifStatement.ElseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            if (!thenTerminates || !elseTerminates)
            {
                instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
            }

            return thenTerminates && elseTerminates;
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
        return false;
    }

    private void LowerWhileStatement(
        WhileStatementSyntax whileStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var loopLabel = AllocateLabel("while");
        var endLabel = AllocateLabel("endwhile");
        var continueLabel = AllocateLabel("while_continue");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(whileStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, endLabel));
        _loopLabels.Push((endLabel, continueLabel));
        LowerStatement(whileStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
        _loopLabels.Pop();
        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private void LowerRepeatStatement(
        RepeatStatementSyntax repeatStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var loopLabel = AllocateLabel("repeat");
        var continueLabel = AllocateLabel("repeat_continue");
        var endLabel = AllocateLabel("endrepeat");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        _loopLabels.Push((endLabel, continueLabel));
        foreach (var statement in repeatStatement.Statements)
        {
            if (LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod))
            {
                break;
            }
        }
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(repeatStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private void LowerForStatement(
        ForStatementSyntax forStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var createdLoopRegister = false;
        if (!registerByName.TryGetValue(forStatement.Identifier.Text, out var loopRegister))
        {
            if (forStatement.VarKeyword is null)
            {
                throw new InvalidOperationException($"Unknown for-loop variable '{forStatement.Identifier.Text}'.");
            }

            loopRegister = new IrValue($"r{registers.Count}", TypeSymbol.Integer, (ushort)registers.Count);
            registers.Add(loopRegister);
            registerByName[forStatement.Identifier.Text] = loopRegister;
            localTypes[forStatement.Identifier.Text] = TypeSymbol.Integer;
            createdLoopRegister = true;
        }

        LowerExpressionInto(forStatement.LowerBound, loopRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        var upperBoundRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(forStatement.UpperBound, upperBoundRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        var stepRegister = AllocateTemp(TypeSymbol.Integer, registers);
        if (forStatement.StepExpression is not null)
        {
            LowerExpressionInto(forStatement.StepExpression, stepRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }
        else
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, stepRegister, 1));
        }

        var loopLabel = AllocateLabel("for");
        var endLabel = AllocateLabel("endfor");
        var continueLabel = AllocateLabel("for_continue");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        var isDownto = forStatement.ToKeyword.Kind == SyntaxKind.DowntoKeyword;
        instructions.Add(new IrInstruction(
            isDownto ? IrOpCode.CompareGreaterOrEqual : IrOpCode.CompareLessOrEqual,
            conditionRegister,
            (loopRegister, upperBoundRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, endLabel));

        _loopLabels.Push((endLabel, continueLabel));
        LowerStatement(forStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        instructions.Add(new IrInstruction(
            isDownto ? IrOpCode.Subtract : IrOpCode.Add,
            loopRegister,
            (loopRegister, stepRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));

        if (createdLoopRegister)
        {
            registerByName.Remove(forStatement.Identifier.Text);
            localTypes.Remove(forStatement.Identifier.Text);
        }
    }

    private void LowerForeachStatement(
        ForeachStatementSyntax foreachStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var createdItemRegister = false;
        if (!registerByName.TryGetValue(foreachStatement.Identifier.Text, out var itemRegister))
        {
            if (foreachStatement.VarKeyword is null)
            {
                throw new InvalidOperationException($"Unknown foreach variable '{foreachStatement.Identifier.Text}'.");
            }

            var collectionTypeForDeclaration = SemanticFacts.InferExpressionType(
                foreachStatement.Collection,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod);
            var itemType = collectionTypeForDeclaration == TypeSymbol.String
                ? TypeSymbol.Char
                : SemanticFacts.GetElementType(collectionTypeForDeclaration)
                    ?? SemanticFacts.GetSetElementType(collectionTypeForDeclaration)
                    ?? throw new InvalidOperationException($"Expression '{SemanticFacts.GetExpressionDisplayName(foreachStatement.Collection)}' is not enumerable.");
            itemRegister = new IrValue($"r{registers.Count}", itemType, (ushort)registers.Count);
            registers.Add(itemRegister);
            registerByName[foreachStatement.Identifier.Text] = itemRegister;
            localTypes[foreachStatement.Identifier.Text] = itemType;
            createdItemRegister = true;
        }

        var collectionType = SemanticFacts.InferExpressionType(
            foreachStatement.Collection,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var collectionRegister = AllocateTemp(collectionType, registers);
        LowerExpressionInto(foreachStatement.Collection, collectionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        if (SemanticFacts.IsSetType(collectionType))
        {
            LowerForeachSetStatement(
                foreachStatement,
                itemRegister,
                collectionRegister,
                registerByName,
                localTypes,
                arrayShapesByName,
                registers,
                instructions,
                returnRegister,
                exceptionHandlers,
                inExceptionHandler,
                currentMethod);

            if (createdItemRegister)
            {
                registerByName.Remove(foreachStatement.Identifier.Text);
                localTypes.Remove(foreachStatement.Identifier.Text);
            }

            return;
        }

        var indexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, indexRegister, 0));

        var lengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadLength, lengthRegister, new IrArrayTarget(collectionRegister)));

        var loopLabel = AllocateLabel("foreach");
        var endLabel = AllocateLabel("endforeach");
        var continueLabel = AllocateLabel("foreach_continue");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareLess, conditionRegister, (indexRegister, lengthRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, endLabel));

        instructions.Add(new IrInstruction(IrOpCode.LoadElement, itemRegister, new IrArrayTarget(collectionRegister, indexRegister)));
        _loopLabels.Push((endLabel, continueLabel));
        LowerStatement(foreachStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        var oneRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));
        instructions.Add(new IrInstruction(IrOpCode.Add, indexRegister, (indexRegister, oneRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));

        if (createdItemRegister)
        {
            registerByName.Remove(foreachStatement.Identifier.Text);
            localTypes.Remove(foreachStatement.Identifier.Text);
        }
    }

    private void LowerForeachSetStatement(
        ForeachStatementSyntax foreachStatement,
        IrValue itemRegister,
        IrValue collectionRegister,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var indexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, indexRegister, 0));

        var limitRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, limitRegister, 32));

        var oneRegister = AllocateTemp(collectionRegister.Type, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));

        var zeroRegister = AllocateTemp(collectionRegister.Type, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, zeroRegister, 0));

        var loopLabel = AllocateLabel("foreach_set");
        var skipBodyLabel = AllocateLabel("foreach_set_skip");
        var endLabel = AllocateLabel("endforeach_set");
        var continueLabel = AllocateLabel("foreach_set_continue");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var rangeConditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareLess, rangeConditionRegister, (indexRegister, limitRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, rangeConditionRegister, endLabel));

        var maskRegister = AllocateTemp(collectionRegister.Type, registers);
        instructions.Add(new IrInstruction(IrOpCode.ShiftLeft, maskRegister, (oneRegister, indexRegister)));

        var andRegister = AllocateTemp(collectionRegister.Type, registers);
        instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, andRegister, (collectionRegister, maskRegister)));

        var containsRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareNotEqual, containsRegister, (andRegister, zeroRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, containsRegister, skipBodyLabel));

        instructions.Add(new IrInstruction(IrOpCode.Copy, itemRegister, indexRegister));
        _loopLabels.Push((endLabel, continueLabel));
        LowerStatement(foreachStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        var stepRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, stepRegister, 1));
        instructions.Add(new IrInstruction(IrOpCode.Add, indexRegister, (indexRegister, stepRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, skipBodyLabel));
        var skipStepRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, skipStepRegister, 1));
        instructions.Add(new IrInstruction(IrOpCode.Add, indexRegister, (indexRegister, skipStepRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private bool LowerCaseStatement(
        CaseStatementSyntax caseStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var expressionType = SemanticFacts.InferExpressionType(
            caseStatement.Expression,
            localTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var caseRegister = AllocateTemp(expressionType, registers);
        LowerExpressionInto(caseStatement.Expression, caseRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var endLabel = AllocateLabel("endcase");
        var elseLabel = AllocateLabel("case_else");
        var clauseLabels = caseStatement.Clauses
            .Select(_ => AllocateLabel("case_clause"))
            .ToArray();
        var nextTestLabels = caseStatement.Clauses
            .Select(_ => AllocateLabel("case_next"))
            .ToArray();

        for (var clauseIndex = 0; clauseIndex < caseStatement.Clauses.Count; clauseIndex++)
        {
            var clause = caseStatement.Clauses[clauseIndex];
            var clauseLabel = clauseLabels[clauseIndex];
            var nextLabel = nextTestLabels[clauseIndex];
            var candidateLabel = AllocateLabel("case_candidate");
            foreach (var label in clause.Labels)
            {
                LowerCaseLabelMatch(label, expressionType, caseRegister, candidateLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
            }

            instructions.Add(new IrInstruction(IrOpCode.Branch, null, nextLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, candidateLabel));
            if (clause.Guard is not null)
            {
                var guardRegister = AllocateTemp(TypeSymbol.Boolean, registers);
                LowerExpressionInto(clause.Guard, guardRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, guardRegister, nextLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, nextLabel));
        }

        instructions.Add(new IrInstruction(IrOpCode.Branch, null, elseLabel));

        var allTerminate = caseStatement.Clauses.Count > 0 || caseStatement.ElseStatements.Count > 0;
        for (var clauseIndex = 0; clauseIndex < caseStatement.Clauses.Count; clauseIndex++)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, clauseLabels[clauseIndex]));
            var clauseTerminates = LowerStatement(caseStatement.Clauses[clauseIndex].Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            if (!clauseTerminates)
            {
                allTerminate = false;
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            }
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
        var elseTerminates = false;
        if (caseStatement.ElseStatements.Count > 0)
        {
            foreach (var elseStatement in caseStatement.ElseStatements)
            {
                elseTerminates = LowerStatement(elseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                if (elseTerminates)
                {
                    break;
                }
            }

            if (!elseTerminates)
            {
                allTerminate = false;
            }
        }
        else
        {
            allTerminate = false;
        }

        if (!elseTerminates)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        }

        if (!allTerminate)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
        }

        return allTerminate;
    }

    private bool LowerMatchStatement(
        MatchStatementSyntax matchStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var expressionType = SemanticFacts.InferExpressionType(
            matchStatement.Expression,
            localTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var matchRegister = AllocateTemp(expressionType, registers);
        LowerExpressionInto(matchStatement.Expression, matchRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var endLabel = AllocateLabel("endmatch");
        var elseLabel = AllocateLabel("match_else");
        var armLabels = matchStatement.Arms.Select(_ => AllocateLabel("match_arm")).ToArray();
        var nextTestLabels = matchStatement.Arms.Select(_ => AllocateLabel("match_next")).ToArray();
        var armRegisterScopes = new Dictionary<string, IrValue>[matchStatement.Arms.Count];
        var armLocalScopes = new Dictionary<string, TypeSymbol>[matchStatement.Arms.Count];

        for (var armIndex = 0; armIndex < matchStatement.Arms.Count; armIndex++)
        {
            var arm = matchStatement.Arms[armIndex];
            var armLabel = armLabels[armIndex];
            var nextLabel = nextTestLabels[armIndex];
            var candidateLabel = AllocateLabel("match_candidate");

            if (arm.IsWildcard)
            {
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, candidateLabel));
            }
            else if (arm.TypeName is not null)
            {
                LowerTypedMatchArmCheck(arm.TypeName, expressionType, matchRegister, candidateLabel, nextLabel, registers, instructions);
            }
            else
            {
                foreach (var label in arm.Labels)
                {
                    LowerCaseLabelMatch(label, expressionType, matchRegister, candidateLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, nextLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Label, null, candidateLabel));
            var (armRegisters, armTypes) = CreateMatchArmScope(arm.TypeName, arm.Identifier, registerByName, localTypes, matchRegister, registers, instructions);
            armRegisterScopes[armIndex] = armRegisters;
            armLocalScopes[armIndex] = armTypes;
            if (arm.Guard is not null)
            {
                var guardRegister = AllocateTemp(TypeSymbol.Boolean, registers);
                LowerExpressionInto(arm.Guard, guardRegister, armRegisters, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, guardRegister, nextLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Branch, null, armLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, nextLabel));
        }

        instructions.Add(new IrInstruction(IrOpCode.Branch, null, elseLabel));
        var allTerminate = matchStatement.Arms.Count > 0 || matchStatement.ElseStatements.Count > 0;
        for (var armIndex = 0; armIndex < matchStatement.Arms.Count; armIndex++)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, armLabels[armIndex]));
            var armTerminates = LowerStatement(matchStatement.Arms[armIndex].Body, armRegisterScopes[armIndex], armLocalScopes[armIndex], arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            if (!armTerminates)
            {
                allTerminate = false;
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            }
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
        var elseTerminates = false;
        if (matchStatement.ElseStatements.Count > 0)
        {
            foreach (var elseStatement in matchStatement.ElseStatements)
            {
                elseTerminates = LowerStatement(elseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
                if (elseTerminates)
                {
                    break;
                }
            }

            if (!elseTerminates)
            {
                allTerminate = false;
            }
        }
        else
        {
            allTerminate = false;
        }

        if (!elseTerminates)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        }

        if (!allTerminate)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
        }

        return allTerminate;
    }

    private void LowerCaseLabelMatch(
        ExpressionSyntax label,
        TypeSymbol expressionType,
        IrValue caseRegister,
        string clauseLabel,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (label is MatchNotPatternSyntax notPattern)
        {
            var innerMatchLabel = AllocateLabel("case_not_match");
            LowerCaseLabelMatch(notPattern.Pattern, expressionType, caseRegister, innerMatchLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, innerMatchLabel));
            return;
        }

        if (label is MatchOrPatternSyntax orPattern)
        {
            foreach (var pattern in orPattern.Patterns)
            {
                LowerCaseLabelMatch(pattern, expressionType, caseRegister, clauseLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
            }

            return;
        }

        if (label is MatchAndPatternSyntax andPattern)
        {
            var failLabel = AllocateLabel("case_next_label");
            foreach (var pattern in andPattern.Patterns)
            {
                LowerRelationalMatchPatternCheck(pattern, caseRegister, failLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
            }

            instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, failLabel));
            return;
        }

        if (label is MatchRelationalPatternSyntax relational)
        {
            var failLabel = AllocateLabel("case_next_label");
            LowerRelationalMatchPatternCheck(relational, caseRegister, failLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, failLabel));
            return;
        }

        if (label is RangeExpressionSyntax range)
        {
            var startType = SemanticFacts.InferExpressionType(range.Start, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
            var startRegister = AllocateTemp(startType, registers);
            var endRegister = AllocateTemp(startType, registers);
            LowerExpressionInto(range.Start, startRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            LowerExpressionInto(range.End, endRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

            var lowerBoundRegister = AllocateTemp(TypeSymbol.Boolean, registers);
            var upperBoundRegister = AllocateTemp(TypeSymbol.Boolean, registers);
            instructions.Add(new IrInstruction(IrOpCode.CompareGreaterOrEqual, lowerBoundRegister, (caseRegister, startRegister)));
            instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, lowerBoundRegister, AllocateLabel("case_next_label")));
            var lowerFailLabel = (string)instructions[^1].Operand!;
            instructions.Add(new IrInstruction(IrOpCode.CompareLessOrEqual, upperBoundRegister, (caseRegister, endRegister)));
            instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, upperBoundRegister, AllocateLabel("case_next_label")));
            var upperFailLabel = (string)instructions[^1].Operand!;
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, lowerFailLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, upperFailLabel));
            return;
        }

        var labelType = SemanticFacts.InferExpressionType(label, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var labelRegister = AllocateTemp(labelType, registers);
        LowerExpressionInto(label, labelRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        var comparisonRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(
            expressionType == TypeSymbol.String ? IrOpCode.CompareEqualString : IrOpCode.CompareEqual,
            comparisonRegister,
            (caseRegister, labelRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, comparisonRegister, AllocateLabel("case_next_label")));
        var matchLabel = (string)instructions[^1].Operand!;
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, clauseLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, matchLabel));
    }

    private void LowerRelationalMatchPatternCheck(
        MatchRelationalPatternSyntax relational,
        IrValue caseRegister,
        string failLabel,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var operandType = SemanticFacts.InferExpressionType(relational.Operand, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var operandRegister = AllocateTemp(operandType, registers);
        LowerExpressionInto(relational.Operand, operandRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        var comparisonRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        var opCode = relational.OperatorToken.Kind switch
        {
            SyntaxKind.LessToken => IrOpCode.CompareLess,
            SyntaxKind.LessOrEqualsToken => IrOpCode.CompareLessOrEqual,
            SyntaxKind.GreaterToken => IrOpCode.CompareGreater,
            SyntaxKind.GreaterOrEqualsToken => IrOpCode.CompareGreaterOrEqual,
            _ => throw new InvalidOperationException($"Cannot lower relational match pattern '{relational.OperatorToken.Text}'.")
        };
        instructions.Add(new IrInstruction(opCode, comparisonRegister, (caseRegister, operandRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, comparisonRegister, failLabel));
    }

    private void LowerRaiseStatement(
        RaiseStatementSyntax raiseStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        if (raiseStatement.Expression is null)
        {
            if (!inExceptionHandler)
            {
                throw new InvalidOperationException("Cannot lower bare raise outside an except handler.");
            }

            instructions.Add(new IrInstruction(IrOpCode.Rethrow, null, null));
            return;
        }

        var exceptionType = SemanticFacts.InferExpressionType(
            raiseStatement.Expression,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var exceptionRegister = AllocateTemp(exceptionType, registers);
        if (raiseStatement.Expression is not null)
        {
            LowerExpressionInto(raiseStatement.Expression, exceptionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }
        else
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, exceptionRegister, 0));
        }

        instructions.Add(new IrInstruction(IrOpCode.Throw, exceptionRegister, exceptionRegister));
    }

    private bool LowerTryStatement(
        TryStatementSyntax tryStatement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        if (tryStatement.ExceptKeyword is not null && tryStatement.FinallyKeyword is not null)
        {
            var syntheticEndToken = new SyntaxToken(SyntaxKind.EndKeyword, string.Empty, null, tryStatement.EndKeyword.Span);
            var syntheticSemicolon = new SyntaxToken(SyntaxKind.SemicolonToken, string.Empty, null, tryStatement.SemicolonToken.Span);

            var innerTry = new TryStatementSyntax(
                tryStatement.TryKeyword,
                tryStatement.TryStatements,
                tryStatement.ExceptKeyword,
                tryStatement.ExceptionClauses,
                tryStatement.ExceptStatements,
                null,
                [],
                syntheticEndToken,
                syntheticSemicolon);

            var outerTry = new TryStatementSyntax(
                tryStatement.TryKeyword,
                [innerTry],
                null,
                [],
                [],
                tryStatement.FinallyKeyword,
                tryStatement.FinallyStatements,
                tryStatement.EndKeyword,
                tryStatement.SemicolonToken);

            return LowerTryStatement(outerTry, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
        }

        var tryStartLabel = AllocateLabel("try_start");
        var tryEndLabel = AllocateLabel("try_end");
        var handlerStartLabel = AllocateLabel(tryStatement.FinallyKeyword is not null ? "finally_start" : "except_start");
        var handlerEndLabel = AllocateLabel(tryStatement.FinallyKeyword is not null ? "finally_end" : "except_end");
        var afterTryLabel = AllocateLabel("after_try");

        instructions.Add(new IrInstruction(IrOpCode.Label, null, tryStartLabel));
        var tryTerminates = false;
        foreach (var statement in tryStatement.TryStatements)
        {
            tryTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, inExceptionHandler, currentMethod);
            if (tryTerminates)
            {
                break;
            }
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, tryEndLabel));
        if (!tryTerminates)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, afterTryLabel));
        }

        if (tryStatement.ExceptionClauses.Count > 0)
        {
            var allHandlerPathsTerminate = true;
            foreach (var clause in tryStatement.ExceptionClauses)
            {
                var clauseStartLabel = AllocateLabel("except_on_start");
                var clauseEndLabel = AllocateLabel("except_on_end");
                var clauseType = ResolveTypeTestTarget(clause.TypeName);
                var clauseRegister = AllocateTemp(clauseType, registers);
                var previousValue = registerByName.TryGetValue(clause.Identifier.Text, out var existingRegister) ? existingRegister : null;
                var hadPrevious = previousValue is not null;
                registerByName[clause.Identifier.Text] = clauseRegister;
                localTypes[clause.Identifier.Text] = clauseType;

                instructions.Add(new IrInstruction(IrOpCode.Label, null, clauseStartLabel));
                var clauseTerminates = LowerStatement(clause.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, true, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.Label, null, clauseEndLabel));
                if (!clauseTerminates)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Branch, null, afterTryLabel));
                    allHandlerPathsTerminate = false;
                }

                exceptionHandlers.Add(new IrExceptionHandler(tryStartLabel, tryEndLabel, clauseStartLabel, clauseEndLabel, clauseRegister.Index, clauseType.Name));

                if (hadPrevious)
                {
                    registerByName[clause.Identifier.Text] = previousValue!;
                    localTypes[clause.Identifier.Text] = previousValue!.Type;
                }
                else
                {
                    registerByName.Remove(clause.Identifier.Text);
                    localTypes.Remove(clause.Identifier.Text);
                }
            }

            if (tryStatement.ExceptStatements.Count > 0)
            {
                instructions.Add(new IrInstruction(IrOpCode.Label, null, handlerStartLabel));
                var catchAllTerminates = false;
                foreach (var statement in tryStatement.ExceptStatements)
                {
                    catchAllTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, true, currentMethod);
                    if (catchAllTerminates)
                    {
                        break;
                    }
                }

                if (!catchAllTerminates)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Branch, null, afterTryLabel));
                    allHandlerPathsTerminate = false;
                }

                exceptionHandlers.Add(new IrExceptionHandler(tryStartLabel, tryEndLabel, handlerStartLabel, handlerEndLabel));
                instructions.Add(new IrInstruction(IrOpCode.Label, null, handlerEndLabel));
            }

            if (!allHandlerPathsTerminate)
            {
                instructions.Add(new IrInstruction(IrOpCode.Label, null, afterTryLabel));
            }

            return tryTerminates && allHandlerPathsTerminate;
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, handlerStartLabel));
        var handlerTerminates = false;
        var handlerStatements = tryStatement.FinallyKeyword is not null ? tryStatement.FinallyStatements : tryStatement.ExceptStatements;
        var handlerIsExceptionHandler = tryStatement.FinallyKeyword is null;
        foreach (var statement in handlerStatements)
        {
            handlerTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, handlerIsExceptionHandler, currentMethod);
            if (handlerTerminates)
            {
                break;
            }
        }

        if (tryStatement.FinallyKeyword is not null && !handlerTerminates)
        {
            instructions.Add(new IrInstruction(IrOpCode.Rethrow, null, null));
            handlerTerminates = true;
        }

        exceptionHandlers.Add(new IrExceptionHandler(tryStartLabel, tryEndLabel, handlerStartLabel, handlerEndLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, handlerEndLabel));
        if (tryStatement.FinallyKeyword is not null)
        {
            if (!tryTerminates)
            {
                instructions.Add(new IrInstruction(IrOpCode.Label, null, afterTryLabel));
                var finallyNormalTerminates = false;
                foreach (var statement in tryStatement.FinallyStatements)
                {
                    finallyNormalTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, false, currentMethod);
                    if (finallyNormalTerminates)
                    {
                        break;
                    }
                }

                return finallyNormalTerminates;
            }

            return true;
        }

        if (!tryTerminates || !handlerTerminates)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, afterTryLabel));
        }

        return tryTerminates && handlerTerminates;
    }

    private void LowerExpressionStatement(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var tempType = SemanticFacts.InferExpressionType(expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var tempRegister = new IrValue($"r{registers.Count}", tempType, (ushort)registers.Count);
        registers.Add(tempRegister);
        LowerExpressionInto(expression, tempRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerIncDecStatement(
        ExpressionSyntax target,
        SyntaxKind operatorKind,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var resultRegister = AllocateTemp(TypeSymbol.Integer, registers);
        var operatorToken = new SyntaxToken(operatorKind, operatorKind == SyntaxKind.PlusAssignToken ? "+=" : "-=", null, new ILC.Compiler.Core.TextSpan(0, 0));
        var oneToken = new SyntaxToken(SyntaxKind.NumberToken, "1", 1, new ILC.Compiler.Core.TextSpan(0, 0));
        LowerCompoundAssignmentInto(
            new CompoundAssignmentExpressionSyntax(target, operatorToken, new LiteralExpressionSyntax(oneToken)),
            resultRegister,
            registerByName,
            arrayShapesByName,
            registers,
            instructions,
            currentMethod);
    }

    private void LowerIncludeExcludeStatement(
        ExpressionSyntax target,
        ExpressionSyntax value,
        bool isInclude,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var targetType = SemanticFacts.InferExpressionType(target, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var elementType = SemanticFacts.GetSetElementType(targetType) ?? TypeSymbol.Integer;
        var currentValueRegister = AllocateTemp(targetType, registers);
        LowerExpressionInto(target, currentValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elementRegister = AllocateTemp(elementType, registers);
        LowerExpressionInto(value, elementRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var oneRegister = AllocateTemp(targetType, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));

        var maskRegister = AllocateTemp(targetType, registers);
        instructions.Add(new IrInstruction(IrOpCode.ShiftLeft, maskRegister, (oneRegister, elementRegister)));

        var resultRegister = AllocateTemp(targetType, registers);
        if (isInclude)
        {
            instructions.Add(new IrInstruction(IrOpCode.BitwiseOr, resultRegister, (currentValueRegister, maskRegister)));
        }
        else
        {
            var notMaskRegister = AllocateTemp(targetType, registers);
            instructions.Add(new IrInstruction(IrOpCode.BitwiseNot, notMaskRegister, maskRegister));
            instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, resultRegister, (currentValueRegister, notMaskRegister)));
        }

        StoreValueIntoTarget(target, resultRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerReturnExpression(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        MethodSymbol? currentMethod = null)
    {
        LowerExpressionInto(expression, returnRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerExpressionInto(
        ExpressionSyntax expression,
        IrValue? destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod = null)
    {
        if (destination is null)
        {
            return;
        }

        switch (expression)
        {
            case LiteralExpressionSyntax literal:
                instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, GetLiteralValue(literal)));
                return;
            case ParenthesizedExpressionSyntax parenthesized:
                LowerExpressionInto(parenthesized.Expression, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case SetLiteralExpressionSyntax setLiteral:
                instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, GetSetLiteralValue(setLiteral, registerByName, currentMethod)));
                return;
            case NewArrayExpressionSyntax newArrayExpression:
                var lengthRegister = LowerFlattenedArrayLength(newArrayExpression.LengthExpressions, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(
                    IrOpCode.NewArray,
                    destination,
                    new IrNewArrayTarget(
                        newArrayExpression.ElementTypeName.ToDisplayString(),
                        lengthRegister,
                        new IrArrayShape(GetShapeRegisters(newArrayExpression.LengthExpressions, arrayShapesByName, registerByName, registers, instructions, currentMethod)))));
                return;
            case NewExpressionSyntax newExpression:
                instructions.Add(new IrInstruction(IrOpCode.NewObject, destination, newExpression.TypeName.ToDisplayString()));
                var constructorArgs = new List<IrValue>();
                foreach (var argument in newExpression.Arguments)
                {
                    var argumentType = SemanticFacts.InferExpressionType(argument.Expression, new Dictionary<string, TypeSymbol>(), _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
                    var temp = AllocateTemp(argumentType, registers);
                    constructorArgs.Add(temp);
                    LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                var constructor = SemanticFacts.ResolveConstructor(newExpression.TypeName, constructorArgs.Count, _knownTypes, _knownMethods);
                if (constructor is not null)
                {
                    instructions.Add(new IrInstruction(
                        IrOpCode.CallVirtual,
                        destination,
                        new IrCallTarget(constructor, $"{newExpression.TypeName.ToDisplayString()}.{constructor.Name}", constructorArgs, destination, true)));
                }

                return;
            case NameExpressionSyntax name when registerByName.TryGetValue(name.Name.ToDisplayString(), out var sourceRegister):
                instructions.Add(new IrInstruction(IrOpCode.Copy, destination, sourceRegister));
                return;
            case NameExpressionSyntax name when ResolveConstant(name, registerByName, currentMethod) is { } constant:
                instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, constant.Value ?? 0));
                return;
            case ArrayLengthExpressionSyntax lengthExpression when ResolveBoundLengthRead(lengthExpression, registerByName, currentMethod) is { } boundLengthRead:
                LowerBoundLengthReadInto(boundLengthRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case ElementAccessExpressionSyntax elementAccess:
                if (ResolveBoundSliceRead(new NameExpressionSyntax(elementAccess.Target), elementAccess.IndexExpressions, registerByName, currentMethod) is { } boundSliceRead)
                {
                    LowerBoundSliceReadInto(boundSliceRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }
                if (ResolveBoundElementRead(new NameExpressionSyntax(elementAccess.Target), elementAccess.IndexExpressions, registerByName, currentMethod) is { } boundElementRead)
                {
                    LowerBoundElementReadInto(boundElementRead, elementAccess.IndexExpressions, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }
                return;
            case PostfixElementAccessExpressionSyntax elementAccess:
                if (ResolveBoundSliceRead(elementAccess.Target, elementAccess.IndexExpressions, registerByName, currentMethod) is { } boundPostfixSliceRead)
                {
                    LowerBoundSliceReadInto(boundPostfixSliceRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }
                if (ResolveBoundElementRead(elementAccess.Target, elementAccess.IndexExpressions, registerByName, currentMethod) is { } boundPostfixElementRead)
                {
                    LowerBoundElementReadInto(boundPostfixElementRead, elementAccess.IndexExpressions, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }
                return;
            case MemberAccessExpressionSyntax memberAccess when ResolveBoundLengthRead(memberAccess, registerByName, currentMethod) is { } boundLengthRead:
                LowerBoundLengthReadInto(boundLengthRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case MemberAccessExpressionSyntax memberAccess when ResolveBoundRead(memberAccess, registerByName, currentMethod) is { } boundRead:
                LowerBoundReadInto(boundRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case NameExpressionSyntax name when ResolveBoundRead(name, registerByName, currentMethod) is { Kind: not BoundMemberReadKind.Local } boundRead:
                LowerBoundReadInto(boundRead, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case NameExpressionSyntax name:
                throw new InvalidOperationException($"Cannot lower unknown name '{name.Name.ToDisplayString()}'.");
            case AssignmentExpressionSyntax assignment when assignment.Target is NameExpressionSyntax assignmentName && registerByName.TryGetValue(assignmentName.Name.ToDisplayString(), out var targetRegister):
                if (assignment.Expression is NewArrayExpressionSyntax assignedArray && assignedArray.LengthExpressions.Count > 1)
                {
                    arrayShapesByName[assignmentName.Name.ToDisplayString()] = LowerNewArrayInto(targetRegister, assignedArray, registerByName, registers, instructions, currentMethod);
                }
                else
                {
                    LowerExpressionInto(assignment.Expression, targetRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                if (destination.Index != targetRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, targetRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when assignment.Target is ElementAccessExpressionSyntax elementAssignment:
                var boundElementWrite = ResolveBoundElementWrite(new NameExpressionSyntax(elementAssignment.Target), elementAssignment.IndexExpressions, registerByName, currentMethod)
                    ?? throw new InvalidOperationException($"Cannot lower assignment target '{elementAssignment.Target.ToDisplayString()}[...]'.");
                var elementValueRegister = AllocateTemp(boundElementWrite.ElementType, registers);
                LowerExpressionInto(assignment.Expression, elementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                LowerBoundElementWriteInto(boundElementWrite, elementAssignment.IndexExpressions, elementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (destination.Index != elementValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, elementValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when assignment.Target is PostfixElementAccessExpressionSyntax elementAssignment:
                var boundPostfixElementWrite = ResolveBoundElementWrite(elementAssignment.Target, elementAssignment.IndexExpressions, registerByName, currentMethod)
                    ?? throw new InvalidOperationException($"Cannot lower assignment target '{GetExpressionDisplayName(elementAssignment.Target)}[...]'.");
                var postfixElementValueRegister = AllocateTemp(boundPostfixElementWrite.ElementType, registers);
                LowerExpressionInto(assignment.Expression, postfixElementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                LowerBoundElementWriteInto(boundPostfixElementWrite, elementAssignment.IndexExpressions, postfixElementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (destination.Index != postfixElementValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, postfixElementValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when ResolveBoundWriteTarget(assignment.Target, registerByName, currentMethod) is { Kind: not BoundWriteTargetKind.ElementAccess } boundWriteTarget:
                var assignmentValueRegister = AllocateTemp(boundWriteTarget.Type, registers);
                if (assignment.Expression is NewArrayExpressionSyntax propertyArray && propertyArray.LengthExpressions.Count > 1)
                {
                    arrayShapesByName[GetExpressionDisplayName(assignment.Target)] = LowerNewArrayInto(assignmentValueRegister, propertyArray, registerByName, registers, instructions, currentMethod);
                }
                else
                {
                    LowerExpressionInto(assignment.Expression, assignmentValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                StoreIntoBoundWriteTarget(boundWriteTarget, assignmentValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (destination.Index != assignmentValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, assignmentValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment:
                throw new InvalidOperationException($"Cannot lower assignment target '{GetExpressionDisplayName(assignment.Target)}'.");
            case CompoundAssignmentExpressionSyntax assignment:
                LowerCompoundAssignmentInto(assignment, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case UnaryExpressionSyntax unary:
                LowerUnaryExpressionInto(unary, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case BinaryExpressionSyntax binary:
                var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
                if (binary.OperatorToken.Kind is SyntaxKind.InKeyword or SyntaxKind.NotInKeyword)
                {
                    LowerSetMembershipInto(binary, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }

                if (binary.OperatorToken.Kind == SyntaxKind.NullCoalescingToken)
                {
                    LowerNullCoalescingInto(binary, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    return;
                }

                if (TryLowerSetBinary(binary, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                var leftType = GetComparisonOperandType(binary.Left, localTypes, currentMethod);
                var rightType = GetComparisonOperandType(binary.Right, localTypes, currentMethod);
                if (TryLowerRecordComparison(binary, destination, leftType, rightType, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                var isStringComparison = IsStringComparison(binary.OperatorToken.Kind, leftType, rightType);
                var isReferenceComparison = IsReferenceComparison(binary.OperatorToken.Kind, leftType, rightType)
                    || (binary.OperatorToken.Kind is SyntaxKind.EqualsToken or SyntaxKind.NotEqualsToken &&
                        !isStringComparison &&
                        (IsNilLiteral(binary.Left) || IsNilLiteral(binary.Right)));
                var operandType = isStringComparison
                    ? TypeSymbol.String
                    : isReferenceComparison
                        ? GetReferenceComparisonOperandType(leftType, rightType)
                        : IsComparisonOperator(binary.OperatorToken.Kind)
                            ? TypeSymbol.Integer
                            : destination.Type;
                var leftTemp = AllocateTemp(operandType, registers);
                var rightTemp = AllocateTemp(operandType, registers);
                LowerExpressionInto(binary.Left, leftTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                LowerExpressionInto(binary.Right, rightTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(MapBinaryOp(binary.OperatorToken.Kind, leftType, rightType, isReferenceComparison), destination, (leftTemp, rightTemp)));
                return;
            case MatchExpressionSyntax matchExpression:
                LowerMatchExpressionInto(matchExpression, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case AsExpressionSyntax asExpression:
                LowerAsExpressionInto(asExpression, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case TypeTestExpressionSyntax typeTest:
                LowerTypeTestInto(typeTest, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case CallExpressionSyntax call:
                var invocation = ResolveInvocationForLowering(call, registerByName, currentMethod);
                var boundCall = ResolveBoundCall(call, registerByName, currentMethod);
                if (boundCall?.Method is null)
                {
                    throw new InvalidOperationException(BuildCallDiagnosticMessage(call, registerByName, currentMethod));
                }

                invocation = new InvocationResolution(boundCall.Method, boundCall.Receiver?.Type, boundCall.Kind == BoundCallKind.Virtual);

                if (TryLowerTryParseIntrinsicCall(call, invocation, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                var argumentTemps = LowerCallArguments(
                    call,
                    invocation.Method,
                    registerByName,
                    arrayShapesByName,
                    registers,
                    instructions,
                    currentMethod);

                if (TryLowerStringIntrinsicCall(invocation, call.Target, argumentTemps, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                instructions.Add(new IrInstruction(
                    boundCall.Kind == BoundCallKind.Virtual ? IrOpCode.CallVirtual : IrOpCode.Call,
                    destination,
                    new IrCallTarget(
                        boundCall.Method,
                        boundCall.DisplayName,
                        argumentTemps,
                        ResolveBoundCallReceiver(boundCall, call.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        boundCall.Kind == BoundCallKind.Virtual)));
                return;
            default:
                throw new InvalidOperationException(
                    $"Cannot lower expression kind '{expression.Kind}' display='{GetExpressionDisplayName(expression)}' currentMethod='{(currentMethod?.DeclaringTypeName is null ? currentMethod?.Name : $"{currentMethod.DeclaringTypeName}.{currentMethod.Name}")}'.");
        }
    }

    private List<IrValue> LowerCallArguments(
        CallExpressionSyntax call,
        MethodSymbol method,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var argumentTemps = new List<IrValue>();
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var hasParams = method.Parameters.Count > 0 && method.Parameters[^1].PassingKind == ParameterPassingKind.Params;
        var fixedParameterCount = hasParams ? method.Parameters.Count - 1 : method.Parameters.Count;

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
        {
            if (hasParams && argumentIndex >= fixedParameterCount)
            {
                break;
            }

            var argument = call.Arguments[argumentIndex];
            var argumentType = SemanticFacts.InferExpressionType(argument.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
            var temp = AllocateTemp(argumentType, registers);
            argumentTemps.Add(temp);
            LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        if (!hasParams)
        {
            return argumentTemps;
        }

        var paramsParameter = method.Parameters[^1];
        var packedParamsRegister = LowerParamsArgumentArray(
            call,
            paramsParameter,
            fixedParameterCount,
            registerByName,
            arrayShapesByName,
            registers,
            instructions,
            currentMethod);
        argumentTemps.Add(packedParamsRegister);
        return argumentTemps;
    }

    private IrValue LowerParamsArgumentArray(
        CallExpressionSyntax call,
        ParameterSymbol paramsParameter,
        int fixedParameterCount,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var explicitArrayArgument = call.Arguments.Count == fixedParameterCount + 1
            ? call.Arguments[fixedParameterCount].Expression
            : null;
        if (explicitArrayArgument is not null)
        {
            var explicitArrayType = SemanticFacts.InferExpressionType(
                explicitArrayArgument,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod);
            if (explicitArrayType == paramsParameter.Type)
            {
                var explicitArrayRegister = AllocateTemp(paramsParameter.Type, registers);
                LowerExpressionInto(explicitArrayArgument, explicitArrayRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return explicitArrayRegister;
            }
        }

        var paramsValues = call.Arguments.Skip(fixedParameterCount).ToArray();
        var paramsArrayRegister = AllocateTemp(paramsParameter.Type, registers);
        var paramsLengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, paramsLengthRegister, paramsValues.Length));

        var elementType = SemanticFacts.GetElementType(paramsParameter.Type)
            ?? throw new InvalidOperationException($"Params parameter '{paramsParameter.Name}' must be an array type.");
        instructions.Add(new IrInstruction(
            IrOpCode.NewArray,
            paramsArrayRegister,
            new IrNewArrayTarget(elementType.Name, paramsLengthRegister, new IrArrayShape([paramsLengthRegister]))));

        for (var elementIndex = 0; elementIndex < paramsValues.Length; elementIndex++)
        {
            var indexRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, indexRegister, elementIndex));

            var valueRegister = AllocateTemp(elementType, registers);
            LowerExpressionInto(paramsValues[elementIndex].Expression, valueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.StoreElement, valueRegister, new IrArrayTarget(paramsArrayRegister, indexRegister)));
        }

        return paramsArrayRegister;
    }

    private void LowerNameReferenceInto(
        QualifiedNameSyntax name,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        LowerExpressionInto(new NameExpressionSyntax(name), destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private TypeSymbol BindLocalType(
        VariableDeclaratorSyntax declarator,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (declarator.TypeName is null)
        {
            return SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        }

        return SemanticFacts.ResolveTypeReference(declarator.TypeName.ToDisplayString(), _knownTypes)
            ?? new TypeSymbol(declarator.TypeName.ToDisplayString(), true);
    }

    private TypeSymbol BindSyntheticTopLevelType(
        VariableDeclaratorSyntax declarator,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol currentMethod)
    {
        if (declarator.TypeName is null)
        {
            return SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        }

        return SemanticFacts.ResolveTypeReference(declarator.TypeName.ToDisplayString(), _knownTypes)
            ?? new TypeSymbol(declarator.TypeName.ToDisplayString(), true);
    }

    private IrValue AllocateTemp(TypeSymbol type, List<IrValue> registers)
    {
        var temp = new IrValue($"r{registers.Count}", type, (ushort)registers.Count);
        registers.Add(temp);
        return temp;
    }

    private IrValue LowerFlattenedArrayLength(
        IReadOnlyList<ExpressionSyntax> lengthExpressions,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var totalLengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(lengthExpressions[0], totalLengthRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        for (var index = 1; index < lengthExpressions.Count; index++)
        {
            var nextLengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
            LowerExpressionInto(lengthExpressions[index], nextLengthRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            var productRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Multiply, productRegister, (totalLengthRegister, nextLengthRegister)));
            totalLengthRegister = productRegister;
        }

        return totalLengthRegister;
    }

    private IrValue LowerFlattenedElementIndex(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TypeSymbol arrayType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var flattenedIndexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(indexExpressions[0], flattenedIndexRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var dimensions = SemanticFacts.GetArrayDimensions(arrayType)
            .Select(length => length.HasValue ? CreateConstantShape(length.Value, registers, instructions) : CreateConstantShape(1, registers, instructions))
            .ToArray();
        for (var index = 1; index < indexExpressions.Count; index++)
        {
            var strideRegister = dimensions.Length > index
                ? dimensions[index]
                : CreateConstantShape(1, registers, instructions);
            var multipliedRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Multiply, multipliedRegister, (flattenedIndexRegister, strideRegister)));

            var nextIndexRegister = AllocateTemp(TypeSymbol.Integer, registers);
            LowerExpressionInto(indexExpressions[index], nextIndexRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

            var sumRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Add, sumRegister, (multipliedRegister, nextIndexRegister)));
            flattenedIndexRegister = sumRegister;
        }

        return flattenedIndexRegister;
    }

    private IrValue LowerFlattenedElementIndex(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TypeSymbol arrayType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var flattenedIndexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(indexExpressions[0], flattenedIndexRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var dimensions = TryGetDynamicShape(target, arrayShapesByName)
            ?? SemanticFacts.GetArrayDimensions(arrayType)
                .Select(length => length.HasValue ? CreateConstantShape(length.Value, registers, instructions) : CreateConstantShape(1, registers, instructions))
                .ToArray();
        for (var index = 1; index < indexExpressions.Count; index++)
        {
            var strideRegister = dimensions.Count > index
                ? dimensions[index]
                : CreateConstantShape(1, registers, instructions);
            var multipliedRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Multiply, multipliedRegister, (flattenedIndexRegister, strideRegister)));

            var nextIndexRegister = AllocateTemp(TypeSymbol.Integer, registers);
            LowerExpressionInto(indexExpressions[index], nextIndexRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

            var sumRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Add, sumRegister, (multipliedRegister, nextIndexRegister)));
            flattenedIndexRegister = sumRegister;
        }

        return flattenedIndexRegister;
    }

    private IReadOnlyList<IrValue> LowerNewArrayInto(
        IrValue destination,
        NewArrayExpressionSyntax newArrayExpression,
        Dictionary<string, IrValue> registerByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var shapeRegisters = new List<IrValue>();
        foreach (var lengthExpression in newArrayExpression.LengthExpressions)
        {
            var lengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
            LowerExpressionInto(lengthExpression, lengthRegister, registerByName, new Dictionary<string, IReadOnlyList<IrValue>>(StringComparer.Ordinal), registers, instructions, currentMethod);
            shapeRegisters.Add(lengthRegister);
        }

        var totalLengthRegister = shapeRegisters[0];
        for (var index = 1; index < shapeRegisters.Count; index++)
        {
            var productRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Multiply, productRegister, (totalLengthRegister, shapeRegisters[index])));
            totalLengthRegister = productRegister;
        }

        instructions.Add(new IrInstruction(
            IrOpCode.NewArray,
            destination,
            new IrNewArrayTarget(
                newArrayExpression.ElementTypeName.ToDisplayString(),
                totalLengthRegister,
                new IrArrayShape(shapeRegisters))));
        return shapeRegisters;
    }

    private IReadOnlyList<IrValue> GetShapeRegisters(
        IReadOnlyList<ExpressionSyntax> lengthExpressions,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        Dictionary<string, IrValue> registerByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var shapeRegisters = new List<IrValue>();
        foreach (var lengthExpression in lengthExpressions)
        {
            var lengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
            LowerExpressionInto(lengthExpression, lengthRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            shapeRegisters.Add(lengthRegister);
        }

        return shapeRegisters;
    }

    private static IReadOnlyList<IrValue>? TryGetDynamicShape(QualifiedNameSyntax target, Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName)
    {
        if (arrayShapesByName.TryGetValue(target.ToDisplayString(), out var shape))
        {
            return shape;
        }

        return null;
    }

    private static IrArrayShape? TryGetTargetShape(QualifiedNameSyntax target, Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName)
    {
        var shape = TryGetDynamicShape(target, arrayShapesByName);
        return shape is null ? null : new IrArrayShape(shape);
    }

    private IrValue CreateConstantShape(int value, List<IrValue> registers, List<IrInstruction> instructions)
    {
        var register = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, register, value));
        return register;
    }

    private void LowerTypeTestInto(
        TypeTestExpressionSyntax typeTest,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var expressionType = SemanticFacts.InferExpressionType(typeTest.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var targetType = ResolveTypeTestTarget(typeTest.TypeName);

        if (!SemanticFacts.IsCompatibleReferenceType(expressionType, targetType) && expressionType.Name != targetType.Name)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
            return;
        }

        if (!targetType.IsReferenceType)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 1));
            return;
        }

        var valueRegister = AllocateTemp(targetType, registers);
        LowerExpressionInto(typeTest.Expression, valueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        var nilRegister = AllocateTemp(TypeSymbol.Nil, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, nilRegister, 0));
        instructions.Add(new IrInstruction(IrOpCode.CompareNotEqualReference, destination, (valueRegister, nilRegister)));
    }

    private void LowerAsExpressionInto(
        AsExpressionSyntax asExpression,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var expressionType = SemanticFacts.InferExpressionType(asExpression.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var targetType = ResolveTypeTestTarget(asExpression.TypeName);

        if (expressionType == TypeSymbol.Nil)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
            return;
        }

        if (SemanticFacts.IsCompatibleReferenceType(expressionType, targetType) || expressionType.Name == targetType.Name)
        {
            LowerExpressionInto(asExpression.Expression, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return;
        }

        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
    }

    private TypeSymbol GetComparisonOperandType(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        var pseudoRegisters = localTypes.ToDictionary(
            pair => pair.Key,
            pair => new IrValue(pair.Key, pair.Value, 0),
            StringComparer.Ordinal);

        if (ResolveProperty(expression, pseudoRegisters, currentMethod) is { } property)
        {
            return property.Type;
        }

        if (ResolveStorageField(expression, pseudoRegisters, currentMethod, false) is { } field)
        {
            return field.Type;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var member = SemanticFacts.ResolveMemberAccess(memberAccess, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
            if (member.Type is not null)
            {
                return member.Type;
            }
        }

        var inferredType = SemanticFacts.InferExpressionType(expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        return inferredType;
    }

    private static object GetLiteralValue(LiteralExpressionSyntax literal) =>
        literal.LiteralToken.Kind switch
        {
            SyntaxKind.TrueKeyword => 1,
            SyntaxKind.FalseKeyword => 0,
            _ => SemanticFacts.GetLiteralValue(literal.LiteralToken) ?? 0
        };

    private int GetSetLiteralValue(
        SetLiteralExpressionSyntax setLiteral,
        IReadOnlyDictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var mask = 0;
        foreach (var element in setLiteral.Elements)
        {
            if (element is RangeExpressionSyntax range)
            {
                if (SemanticFacts.GetConstantValue(range.Start, localTypes, _knownFields, _knownConstants, _knownProperties, currentMethod) is not int startValue ||
                    SemanticFacts.GetConstantValue(range.End, localTypes, _knownFields, _knownConstants, _knownProperties, currentMethod) is not int endValue)
                {
                    throw new InvalidOperationException($"Cannot lower non-constant set literal range '{SemanticFacts.GetExpressionDisplayName(element)}'.");
                }

                var minValue = Math.Min(startValue, endValue);
                var maxValue = Math.Max(startValue, endValue);
                for (var rangeValue = minValue; rangeValue <= maxValue; rangeValue++)
                {
                    mask |= 1 << rangeValue;
                }

                continue;
            }

            if (SemanticFacts.GetConstantValue(element, localTypes, _knownFields, _knownConstants, _knownProperties, currentMethod) is not int value)
            {
                throw new InvalidOperationException($"Cannot lower non-constant set literal element '{SemanticFacts.GetExpressionDisplayName(element)}'.");
            }

            mask |= 1 << value;
        }

        return mask;
    }

    private void LowerSetMembershipInto(
        BinaryExpressionSyntax binary,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var rightType = SemanticFacts.InferExpressionType(binary.Right, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var setElementType = SemanticFacts.GetSetElementType(rightType)
            ?? throw new InvalidOperationException($"Cannot lower 'in' on non-set expression '{SemanticFacts.GetExpressionDisplayName(binary.Right)}'.");

        var setRegister = AllocateTemp(rightType, registers);
        LowerExpressionInto(binary.Right, setRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elementRegister = AllocateTemp(setElementType, registers);
        LowerExpressionInto(binary.Left, elementRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var oneRegister = AllocateTemp(SemanticFacts.CreateSetType(setElementType), registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));

        var maskRegister = AllocateTemp(SemanticFacts.CreateSetType(setElementType), registers);
        instructions.Add(new IrInstruction(IrOpCode.ShiftLeft, maskRegister, (oneRegister, elementRegister)));

        var andRegister = AllocateTemp(SemanticFacts.CreateSetType(setElementType), registers);
        instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, andRegister, (setRegister, maskRegister)));

        var zeroRegister = AllocateTemp(SemanticFacts.CreateSetType(setElementType), registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, zeroRegister, 0));

        if (binary.OperatorToken.Kind == SyntaxKind.NotInKeyword)
        {
            instructions.Add(new IrInstruction(IrOpCode.CompareEqual, destination, (andRegister, zeroRegister)));
        }
        else
        {
            instructions.Add(new IrInstruction(IrOpCode.CompareNotEqual, destination, (andRegister, zeroRegister)));
        }
    }

    private void LowerMatchExpressionInto(
        MatchExpressionSyntax matchExpression,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var expressionType = SemanticFacts.InferExpressionType(
            matchExpression.Expression,
            localTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var matchRegister = AllocateTemp(expressionType, registers);
        LowerExpressionInto(matchExpression.Expression, matchRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var armLabels = matchExpression.Arms.Select(_ => AllocateLabel("match_expr_arm")).ToArray();
        var nextTestLabels = matchExpression.Arms.Select(_ => AllocateLabel("match_expr_next")).ToArray();
        var armRegisterScopes = new Dictionary<string, IrValue>[matchExpression.Arms.Count];
        var armLocalScopes = new Dictionary<string, TypeSymbol>[matchExpression.Arms.Count];
        var endLabel = AllocateLabel("endmatch_expr");

        for (var armIndex = 0; armIndex < matchExpression.Arms.Count; armIndex++)
        {
            var arm = matchExpression.Arms[armIndex];
            var armLabel = armLabels[armIndex];
            var nextLabel = nextTestLabels[armIndex];
            var candidateLabel = AllocateLabel("match_expr_candidate");

            if (arm.IsWildcard)
            {
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, candidateLabel));
            }
            else if (arm.TypeName is not null)
            {
                LowerTypedMatchArmCheck(arm.TypeName, expressionType, matchRegister, candidateLabel, nextLabel, registers, instructions);
            }
            else
            {
                foreach (var label in arm.Labels)
                {
                    LowerCaseLabelMatch(label, expressionType, matchRegister, candidateLabel, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, nextLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Label, null, candidateLabel));
            var (armRegisters, armTypes) = CreateMatchArmScope(arm.TypeName, arm.Identifier, registerByName, localTypes, matchRegister, registers, instructions);
            armRegisterScopes[armIndex] = armRegisters;
            armLocalScopes[armIndex] = armTypes;
            if (arm.Guard is not null)
            {
                var guardRegister = AllocateTemp(TypeSymbol.Boolean, registers);
                LowerExpressionInto(arm.Guard, guardRegister, armRegisters, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, guardRegister, nextLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Branch, null, armLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, nextLabel));
        }

        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        for (var armIndex = 0; armIndex < matchExpression.Arms.Count; armIndex++)
        {
            instructions.Add(new IrInstruction(IrOpCode.Label, null, armLabels[armIndex]));
            LowerExpressionInto(matchExpression.Arms[armIndex].Expression, destination, armRegisterScopes[armIndex], arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        }

        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private void LowerTypedMatchArmCheck(
        QualifiedNameSyntax typeName,
        TypeSymbol expressionType,
        IrValue matchRegister,
        string successLabel,
        string failureLabel,
        List<IrValue> registers,
        List<IrInstruction> instructions)
    {
        var targetType = ResolveTypeTestTarget(typeName);
        if (!SemanticFacts.IsCompatibleReferenceType(expressionType, targetType) && expressionType.Name != targetType.Name)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, failureLabel));
            return;
        }

        var nilRegister = AllocateTemp(TypeSymbol.Nil, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, nilRegister, 0));
        var comparisonRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareNotEqualReference, comparisonRegister, (matchRegister, nilRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, comparisonRegister, failureLabel));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, successLabel));
    }

    private (Dictionary<string, IrValue> RegisterByName, Dictionary<string, TypeSymbol> LocalTypes) CreateMatchArmScope(
        QualifiedNameSyntax? typeName,
        SyntaxToken? identifier,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        IrValue matchRegister,
        List<IrValue> registers,
        List<IrInstruction> instructions)
    {
        var armRegisters = new Dictionary<string, IrValue>(registerByName, StringComparer.Ordinal);
        var armTypes = new Dictionary<string, TypeSymbol>(localTypes, StringComparer.Ordinal);
        if (typeName is null || identifier is null)
        {
            return (armRegisters, armTypes);
        }

        var targetType = ResolveTypeTestTarget(typeName);
        var captureRegister = AllocateTemp(targetType, registers);
        instructions.Add(new IrInstruction(IrOpCode.Copy, captureRegister, matchRegister));
        armRegisters[identifier.Text] = captureRegister;
        armTypes[identifier.Text] = targetType;
        return (armRegisters, armTypes);
    }

    private void LowerUnaryExpressionInto(
        UnaryExpressionSyntax unary,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var operandType = SemanticFacts.InferExpressionType(unary.Operand, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var operandRegister = AllocateTemp(operandType, registers);
        LowerExpressionInto(unary.Operand, operandRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        if (unary.OperatorToken.Kind == SyntaxKind.NotKeyword)
        {
            if (operandType == TypeSymbol.Boolean)
            {
                var zeroRegister = AllocateTemp(TypeSymbol.Boolean, registers);
                instructions.Add(new IrInstruction(IrOpCode.LoadConstant, zeroRegister, 0));
                instructions.Add(new IrInstruction(IrOpCode.CompareEqual, destination, (operandRegister, zeroRegister)));
                return;
            }

            instructions.Add(new IrInstruction(IrOpCode.BitwiseNot, destination, operandRegister));
            return;
        }

        if (unary.OperatorToken.Kind == SyntaxKind.PlusToken)
        {
            instructions.Add(new IrInstruction(IrOpCode.Copy, destination, operandRegister));
            return;
        }

        if (unary.OperatorToken.Kind == SyntaxKind.MinusToken)
        {
            var zeroRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, zeroRegister, 0));
            instructions.Add(new IrInstruction(IrOpCode.Subtract, destination, (zeroRegister, operandRegister)));
            return;
        }

        throw new InvalidOperationException($"Cannot lower unary operator '{unary.OperatorToken.Text}'.");
    }

    private void LowerNullCoalescingInto(
        BinaryExpressionSyntax binary,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var leftType = SemanticFacts.InferExpressionType(binary.Left, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var rightType = SemanticFacts.InferExpressionType(binary.Right, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var operandType = GetReferenceComparisonOperandType(leftType, rightType);
        if (operandType == TypeSymbol.Nil)
        {
            operandType = destination.Type;
        }

        var leftRegister = AllocateTemp(operandType, registers);
        LowerExpressionInto(binary.Left, leftRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var nilRegister = AllocateTemp(TypeSymbol.Nil, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, nilRegister, 0));

        var compareRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(GetEqualityCompareOp(operandType), compareRegister, (leftRegister, nilRegister)));

        var keepLeftLabel = AllocateLabel("coalesce_keep_left");
        var endLabel = AllocateLabel("coalesce_end");
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, compareRegister, keepLeftLabel));
        LowerExpressionInto(binary.Right, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, keepLeftLabel));
        instructions.Add(new IrInstruction(IrOpCode.Copy, destination, leftRegister));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private void LowerArraySliceInto(
        IrValue destination,
        QualifiedNameSyntax target,
        ExpressionSyntax sliceExpression,
        TypeSymbol arrayType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var sourceArrayRegister = AllocateTemp(arrayType, registers);
        LowerNameReferenceInto(target, sourceArrayRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        if (arrayType == TypeSymbol.String)
        {
            LowerStringSliceInto(destination, sourceArrayRegister, sliceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return;
        }

        LowerArraySliceCopyInto(destination, sourceArrayRegister, sliceExpression, arrayType, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerArraySliceInto(
        IrValue destination,
        ExpressionSyntax target,
        ExpressionSyntax sliceExpression,
        TypeSymbol arrayType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var sourceArrayRegister = ResolveReceiverExpression(target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
            ?? throw new InvalidOperationException($"Cannot lower slice receiver '{GetExpressionDisplayName(target)}'.");
        if (arrayType == TypeSymbol.String)
        {
            LowerStringSliceInto(destination, sourceArrayRegister, sliceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return;
        }

        LowerArraySliceCopyInto(destination, sourceArrayRegister, sliceExpression, arrayType, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerStringSliceInto(
        IrValue destination,
        IrValue sourceStringRegister,
        ExpressionSyntax sliceExpression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (sliceExpression is not RangeExpressionSyntax range)
        {
            throw new InvalidOperationException("String slice access expects a range expression.");
        }

        var startRegister = AllocateTemp(TypeSymbol.Integer, registers);
        var endRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(range.Start, startRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        LowerExpressionInto(range.End, endRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        instructions.Add(new IrInstruction(
            IrOpCode.SliceString,
            destination,
            new IrStringSliceTarget(sourceStringRegister, startRegister, endRegister)));
    }

    private void LowerArraySliceCopyInto(
        IrValue destination,
        IrValue sourceArrayRegister,
        ExpressionSyntax sliceExpression,
        TypeSymbol arrayType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (sliceExpression is not RangeExpressionSyntax range)
        {
            throw new InvalidOperationException("Slice access expects a range expression.");
        }

        var startRegister = AllocateTemp(TypeSymbol.Integer, registers);
        var endRegister = AllocateTemp(TypeSymbol.Integer, registers);
        LowerExpressionInto(range.Start, startRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        LowerExpressionInto(range.End, endRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var deltaRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.Subtract, deltaRegister, (endRegister, startRegister)));
        var oneRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));
        var lengthRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.Add, lengthRegister, (deltaRegister, oneRegister)));

        var elementTypeName = SemanticFacts.GetElementType(arrayType)?.Name
            ?? throw new InvalidOperationException($"Cannot determine slice element type for '{arrayType.Name}'.");
        instructions.Add(new IrInstruction(
            IrOpCode.NewArray,
            destination,
            new IrNewArrayTarget(
                elementTypeName,
                lengthRegister,
                new IrArrayShape([lengthRegister]))));

        var indexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, indexRegister, 0));
        var loopLabel = AllocateLabel("slice_loop");
        var endLabel = AllocateLabel("slice_end");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var continueRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareLess, continueRegister, (indexRegister, lengthRegister)));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, continueRegister, endLabel));

        var sourceIndexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.Add, sourceIndexRegister, (startRegister, indexRegister)));
        var elementType = SemanticFacts.GetElementType(arrayType) ?? TypeSymbol.Integer;
        var elementRegister = AllocateTemp(elementType, registers);
        instructions.Add(new IrInstruction(
            IrOpCode.LoadElement,
            elementRegister,
            new IrArrayTarget(sourceArrayRegister, sourceIndexRegister)));
        instructions.Add(new IrInstruction(
            IrOpCode.StoreElement,
            elementRegister,
            new IrArrayTarget(destination, indexRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Add, indexRegister, (indexRegister, oneRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
    }

    private bool TryLowerSetBinary(
        BinaryExpressionSyntax binary,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (binary.OperatorToken.Kind is not (SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken))
        {
            return false;
        }

        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var leftType = SemanticFacts.InferExpressionType(binary.Left, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var rightType = SemanticFacts.InferExpressionType(binary.Right, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        if (!SemanticFacts.IsSetType(leftType) || leftType != rightType)
        {
            return false;
        }

        var leftRegister = AllocateTemp(leftType, registers);
        var rightRegister = AllocateTemp(rightType, registers);
        LowerExpressionInto(binary.Left, leftRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        LowerExpressionInto(binary.Right, rightRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        switch (binary.OperatorToken.Kind)
        {
            case SyntaxKind.PlusToken:
                instructions.Add(new IrInstruction(IrOpCode.BitwiseOr, destination, (leftRegister, rightRegister)));
                return true;
            case SyntaxKind.StarToken:
                instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, destination, (leftRegister, rightRegister)));
                return true;
            case SyntaxKind.MinusToken:
                var notRightRegister = AllocateTemp(rightType, registers);
                instructions.Add(new IrInstruction(IrOpCode.BitwiseNot, notRightRegister, rightRegister));
                instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, destination, (leftRegister, notRightRegister)));
                return true;
            default:
                return false;
        }
    }

    private static bool IsComparisonOperator(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken;

    private static IrOpCode MapBinaryOp(SyntaxKind kind, TypeSymbol leftType, TypeSymbol rightType, bool isReferenceComparison)
    {
        if (IsStringComparison(kind, leftType, rightType))
        {
            return kind switch
            {
                SyntaxKind.EqualsToken => IrOpCode.CompareEqualString,
                SyntaxKind.NotEqualsToken => IrOpCode.CompareNotEqualString,
                _ => throw new InvalidOperationException($"Unsupported string comparison operator '{kind}'.")
            };
        }

        if (IsStringConcatenation(kind, leftType, rightType))
        {
            return IrOpCode.ConcatString;
        }

        if (isReferenceComparison)
        {
            return kind switch
            {
                SyntaxKind.EqualsToken => IrOpCode.CompareEqualReference,
                SyntaxKind.NotEqualsToken => IrOpCode.CompareNotEqualReference,
                _ => throw new InvalidOperationException($"Unsupported reference comparison operator '{kind}'.")
            };
        }

        return kind switch
        {
            SyntaxKind.PlusToken => IrOpCode.Add,
            SyntaxKind.ShlKeyword => IrOpCode.ShiftLeft,
            SyntaxKind.ShrKeyword => IrOpCode.ShiftRight,
            SyntaxKind.MinusToken => IrOpCode.Subtract,
            SyntaxKind.StarToken => IrOpCode.Multiply,
            SyntaxKind.SlashToken => IrOpCode.Divide,
            SyntaxKind.DivKeyword => IrOpCode.Divide,
            SyntaxKind.ModKeyword => IrOpCode.Modulo,
            SyntaxKind.EqualsToken => IrOpCode.CompareEqual,
            SyntaxKind.NotEqualsToken => IrOpCode.CompareNotEqual,
            SyntaxKind.LessToken => IrOpCode.CompareLess,
            SyntaxKind.LessOrEqualsToken => IrOpCode.CompareLessOrEqual,
            SyntaxKind.GreaterToken => IrOpCode.CompareGreater,
            SyntaxKind.GreaterOrEqualsToken => IrOpCode.CompareGreaterOrEqual,
            _ => IrOpCode.Add
        };
    }

    private static bool IsStringComparison(SyntaxKind kind, TypeSymbol leftType, TypeSymbol rightType) =>
        kind is SyntaxKind.EqualsToken or SyntaxKind.NotEqualsToken &&
        (leftType == TypeSymbol.String || rightType == TypeSymbol.String);

    private static bool IsStringConcatenation(SyntaxKind kind, TypeSymbol leftType, TypeSymbol rightType) =>
        kind == SyntaxKind.PlusToken &&
        leftType == TypeSymbol.String &&
        rightType == TypeSymbol.String;

    private bool TryLowerStringIntrinsicCall(
        InvocationResolution invocation,
        ExpressionSyntax target,
        IReadOnlyList<IrValue> arguments,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (invocation.Method.Name == "Parse" && invocation.Method.DeclaringTypeName == TypeSymbol.Integer.Name && invocation.Method.IsStatic && arguments.Count == 1)
        {
            instructions.Add(new IrInstruction(IrOpCode.ParseStringToInteger, destination, arguments[0]));
            return true;
        }

        if (invocation.Method.Name == "ToString" && invocation.Method.DeclaringTypeName == TypeSymbol.Integer.Name && arguments.Count == 0)
        {
            var integerReceiver = ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            if (integerReceiver is null)
            {
                return false;
            }

            instructions.Add(new IrInstruction(IrOpCode.ConvertIntegerToString, destination, integerReceiver));
            return true;
        }

        if (invocation.Method.DeclaringTypeName != TypeSymbol.String.Name)
        {
            return false;
        }

        var receiver = ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        if (receiver is null)
        {
            return false;
        }

        if (invocation.Method.Name == "Substring" && arguments.Count == 2)
        {
            var oneRegister = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));

            var lastIndex = AllocateTemp(TypeSymbol.Integer, registers);
            instructions.Add(new IrInstruction(IrOpCode.Add, lastIndex, (arguments[0], arguments[1])));
            instructions.Add(new IrInstruction(IrOpCode.Subtract, lastIndex, (lastIndex, oneRegister)));
            instructions.Add(new IrInstruction(IrOpCode.SliceString, destination, new IrStringSliceTarget(receiver, arguments[0], lastIndex)));
            return true;
        }

        if (invocation.Method.Name == "Replace" && arguments.Count == 2)
        {
            instructions.Add(new IrInstruction(IrOpCode.ReplaceString, destination, new IrStringReplaceTarget(receiver, arguments[0], arguments[1])));
            return true;
        }

        if (invocation.Method.Name == "Insert" && arguments.Count == 2)
        {
            instructions.Add(new IrInstruction(IrOpCode.InsertString, destination, new IrStringInsertTarget(receiver, arguments[0], arguments[1])));
            return true;
        }

        if (invocation.Method.Name == "Remove" && arguments.Count == 2)
        {
            instructions.Add(new IrInstruction(IrOpCode.RemoveString, destination, new IrStringRemoveTarget(receiver, arguments[0], arguments[1])));
            return true;
        }

        if (invocation.Method.Name == "ToUpper" && arguments.Count == 0)
        {
            instructions.Add(new IrInstruction(IrOpCode.ToUpperString, destination, receiver));
            return true;
        }

        if (invocation.Method.Name == "ToLower" && arguments.Count == 0)
        {
            instructions.Add(new IrInstruction(IrOpCode.ToLowerString, destination, receiver));
            return true;
        }

        if (invocation.Method.Name == "Trim" && arguments.Count == 0)
        {
            instructions.Add(new IrInstruction(IrOpCode.TrimString, destination, receiver));
            return true;
        }

        if (invocation.Method.Name == "TrimStart" && arguments.Count == 0)
        {
            instructions.Add(new IrInstruction(IrOpCode.TrimStartString, destination, receiver));
            return true;
        }

        if (invocation.Method.Name == "TrimEnd" && arguments.Count == 0)
        {
            instructions.Add(new IrInstruction(IrOpCode.TrimEndString, destination, receiver));
            return true;
        }

        if (arguments.Count != 1)
        {
            return false;
        }

        var opcode = invocation.Method.Name switch
        {
            "StartsWith" => IrOpCode.StartsWithString,
            "EndsWith" => IrOpCode.EndsWithString,
            "Contains" => IrOpCode.ContainsString,
            "IndexOf" => IrOpCode.IndexOfString,
            "LastIndexOf" => IrOpCode.LastIndexOfString,
            _ => (IrOpCode?)null
        };
        if (opcode is null)
        {
            return false;
        }

        instructions.Add(new IrInstruction(opcode.Value, destination, (receiver, arguments[0])));
        return true;
    }

    private bool TryLowerTryParseIntrinsicCall(
        CallExpressionSyntax call,
        InvocationResolution invocation,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (invocation.Method.Name != "TryParse" ||
            invocation.Method.DeclaringTypeName != TypeSymbol.Integer.Name ||
            !invocation.Method.IsStatic ||
            call.Arguments.Count != 2)
        {
            return false;
        }

        var sourceRegister = AllocateTemp(TypeSymbol.String, registers);
        LowerExpressionInto(call.Arguments[0].Expression, sourceRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var parsedRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.TryParseStringToInteger, destination, new IrStringTryParseTarget(sourceRegister, parsedRegister)));
        StoreValueIntoTarget(call.Arguments[1].Expression, parsedRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        return true;
    }

    private void StoreValueIntoTarget(
        ExpressionSyntax target,
        IrValue source,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target is NameExpressionSyntax nameExpression &&
            registerByName.TryGetValue(nameExpression.Name.ToDisplayString(), out var targetRegister))
        {
            instructions.Add(new IrInstruction(IrOpCode.Copy, targetRegister, source));
            return;
        }

        if (ResolveBoundWriteTarget(target, registerByName, currentMethod) is { Kind: not BoundWriteTargetKind.ElementAccess } boundWriteTarget)
        {
            StoreIntoBoundWriteTarget(boundWriteTarget, source, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return;
        }

        throw new InvalidOperationException($"Cannot lower writable target '{GetExpressionDisplayName(target)}'.");
    }

    private void LowerCompoundAssignmentInto(
        CompoundAssignmentExpressionSyntax assignment,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var localTypes = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var targetType = SemanticFacts.InferExpressionType(assignment.Target, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);

        var currentValueRegister = AllocateTemp(targetType, registers);
        LowerExpressionInto(assignment.Target, currentValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var rightValueRegister = AllocateTemp(targetType, registers);
        var resultRegister = AllocateTemp(targetType, registers);
        if (assignment.OperatorToken.Kind == SyntaxKind.NullCoalescingAssignToken)
        {
            var nilRegister = AllocateTemp(TypeSymbol.Nil, registers);
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, nilRegister, 0));
            var compareRegister = AllocateTemp(TypeSymbol.Boolean, registers);
            instructions.Add(new IrInstruction(GetEqualityCompareOp(targetType), compareRegister, (currentValueRegister, nilRegister)));

            var keepCurrentLabel = AllocateLabel("coalesce_keep_current");
            var endLabel = AllocateLabel("coalesce_end");
            instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, compareRegister, keepCurrentLabel));
            LowerExpressionInto(assignment.Expression, rightValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Copy, resultRegister, rightValueRegister));
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, keepCurrentLabel));
            instructions.Add(new IrInstruction(IrOpCode.Copy, resultRegister, currentValueRegister));
            instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
        }
        else if (assignment.OperatorToken.Kind == SyntaxKind.PlusAssignToken && targetType == TypeSymbol.String)
        {
            LowerExpressionInto(assignment.Expression, rightValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.ConcatString, resultRegister, (currentValueRegister, rightValueRegister)));
        }
        else if (assignment.OperatorToken.Kind == SyntaxKind.XorAssignToken)
        {
            LowerExpressionInto(assignment.Expression, rightValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            var orRegister = AllocateTemp(targetType, registers);
            var andRegister = AllocateTemp(targetType, registers);
            var notAndRegister = AllocateTemp(targetType, registers);
            instructions.Add(new IrInstruction(IrOpCode.BitwiseOr, orRegister, (currentValueRegister, rightValueRegister)));
            instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, andRegister, (currentValueRegister, rightValueRegister)));
            instructions.Add(new IrInstruction(IrOpCode.BitwiseNot, notAndRegister, andRegister));
            instructions.Add(new IrInstruction(IrOpCode.BitwiseAnd, resultRegister, (orRegister, notAndRegister)));
        }
        else
        {
            LowerExpressionInto(assignment.Expression, rightValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            var opcode = assignment.OperatorToken.Kind switch
            {
                SyntaxKind.PlusAssignToken => IrOpCode.Add,
                SyntaxKind.MinusAssignToken => IrOpCode.Subtract,
                SyntaxKind.StarAssignToken => IrOpCode.Multiply,
                SyntaxKind.SlashAssignToken => IrOpCode.Divide,
                SyntaxKind.DivAssignToken => IrOpCode.Divide,
                SyntaxKind.ModAssignToken => IrOpCode.Modulo,
                SyntaxKind.AndAssignToken => IrOpCode.BitwiseAnd,
                SyntaxKind.OrAssignToken => IrOpCode.BitwiseOr,
                SyntaxKind.ShlAssignToken => IrOpCode.ShiftLeft,
                SyntaxKind.ShrAssignToken => IrOpCode.ShiftRight,
                _ => throw new InvalidOperationException($"Unsupported compound assignment operator '{assignment.OperatorToken.Text}'.")
            };
            instructions.Add(new IrInstruction(opcode, resultRegister, (currentValueRegister, rightValueRegister)));
        }

        StoreValueIntoTarget(assignment.Target, resultRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        if (destination.Index != resultRegister.Index)
        {
            instructions.Add(new IrInstruction(IrOpCode.Copy, destination, resultRegister));
        }
    }

    private static bool IsReferenceComparison(SyntaxKind kind, TypeSymbol leftType, TypeSymbol rightType) =>
        kind is SyntaxKind.EqualsToken or SyntaxKind.NotEqualsToken &&
        !IsStringComparison(kind, leftType, rightType) &&
        (leftType.IsReferenceType || rightType.IsReferenceType);

    private bool TryLowerRecordComparison(
        BinaryExpressionSyntax binary,
        IrValue destination,
        TypeSymbol leftType,
        TypeSymbol rightType,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (binary.OperatorToken.Kind is not SyntaxKind.EqualsToken and not SyntaxKind.NotEqualsToken)
        {
            return false;
        }

        var recordType = ResolveRecordComparisonType(leftType, rightType);
        if (recordType is null)
        {
            return false;
        }

        var leftRegister = AllocateTemp(recordType, registers);
        var rightRegister = AllocateTemp(recordType, registers);
        LowerExpressionInto(binary.Left, leftRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        LowerExpressionInto(binary.Right, rightRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var nilRegister = AllocateTemp(TypeSymbol.Nil, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, nilRegister, 0));
        var leftNilRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        var rightNilRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareEqualReference, leftNilRegister, (leftRegister, nilRegister)));
        instructions.Add(new IrInstruction(IrOpCode.CompareEqualReference, rightNilRegister, (rightRegister, nilRegister)));

        var checkRightNilLabel = AllocateLabel("record_check_right_nil");
        var nilMismatchLabel = AllocateLabel("record_nil_mismatch");
        var fieldCompareLabel = AllocateLabel("record_field_compare");
        var resultTrueLabel = AllocateLabel("record_result_true");
        var resultFalseLabel = AllocateLabel("record_result_false");
        var endLabel = AllocateLabel("record_end");

        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, leftNilRegister, checkRightNilLabel));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, rightNilRegister, nilMismatchLabel));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, binary.OperatorToken.Kind == SyntaxKind.EqualsToken ? resultTrueLabel : resultFalseLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, checkRightNilLabel));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, rightNilRegister, fieldCompareLabel));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, nilMismatchLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, nilMismatchLabel));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, binary.OperatorToken.Kind == SyntaxKind.EqualsToken ? resultFalseLabel : resultTrueLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, fieldCompareLabel));
        foreach (var field in recordType.Fields.Where(field => !field.IsStatic))
        {
            var leftFieldRegister = AllocateTemp(field.Type, registers);
            var rightFieldRegister = AllocateTemp(field.Type, registers);
            instructions.Add(new IrInstruction(
                IrOpCode.LoadField,
                leftFieldRegister,
                new IrFieldTarget(field, $"{recordType.Name}.{field.Name}", leftRegister)));
            instructions.Add(new IrInstruction(
                IrOpCode.LoadField,
                rightFieldRegister,
                new IrFieldTarget(field, $"{recordType.Name}.{field.Name}", rightRegister)));

            var compareRegister = AllocateTemp(TypeSymbol.Boolean, registers);
            instructions.Add(new IrInstruction(GetEqualityCompareOp(field.Type), compareRegister, (leftFieldRegister, rightFieldRegister)));
            instructions.Add(new IrInstruction(
                IrOpCode.BranchIfFalse,
                compareRegister,
                binary.OperatorToken.Kind == SyntaxKind.EqualsToken ? resultFalseLabel : resultTrueLabel));
        }

        instructions.Add(new IrInstruction(IrOpCode.Branch, null, binary.OperatorToken.Kind == SyntaxKind.EqualsToken ? resultTrueLabel : resultFalseLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, resultTrueLabel));
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 1));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, resultFalseLabel));
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
        return true;
    }

    private NamedTypeSymbol? ResolveRecordComparisonType(TypeSymbol leftType, TypeSymbol rightType)
    {
        if (leftType.Name != rightType.Name)
        {
            return null;
        }

        return _knownTypes
            .OfType<NamedTypeSymbol>()
            .FirstOrDefault(type => type.IsRecord && type.Name == leftType.Name);
    }

    private static IrOpCode GetEqualityCompareOp(TypeSymbol type)
    {
        if (type == TypeSymbol.String)
        {
            return IrOpCode.CompareEqualString;
        }

        if (type.IsReferenceType)
        {
            return IrOpCode.CompareEqualReference;
        }

        return IrOpCode.CompareEqual;
    }

    private static TypeSymbol GetReferenceComparisonOperandType(TypeSymbol leftType, TypeSymbol rightType)
    {
        if (leftType.IsReferenceType && leftType != TypeSymbol.Nil)
        {
            return leftType;
        }

        if (rightType.IsReferenceType && rightType != TypeSymbol.Nil)
        {
            return rightType;
        }

        return TypeSymbol.Nil;
    }

    private static bool IsNilLiteral(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.NilKeyword };

    private TypeSymbol ResolveTypeTestTarget(QualifiedNameSyntax typeName)
    {
        var displayName = typeName.ToDisplayString();
        return SemanticFacts.TryResolveBuiltInType(displayName)
            ?? SemanticFacts.ResolveTypeReference(displayName, _knownTypes)
            ?? new TypeSymbol(displayName, true);
    }

    private string AllocateLabel(string prefix) => $"{prefix}_{++_labelCounter}";

    private MethodSymbol? ResolveMethod(string name, int argumentCount, MethodSymbol? currentMethod)
        => SemanticFacts.ResolveMethod(name, argumentCount, _knownMethods, currentMethod);

    private FieldSymbol? ResolveStorageField(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod, bool forWrite)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberResolution = SemanticFacts.ResolveMemberAccess(
                memberAccess,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                _knownTypes);
            if (memberResolution.Property is not null)
            {
                return forWrite ? memberResolution.Property.WriteField : memberResolution.Property.ReadField;
            }

            if (TryFlattenMemberAccess(memberAccess) is { } qualifiedName)
            {
                var qualifiedProperty = SemanticFacts.ResolvePropertyReference(
                    qualifiedName,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod,
                    _knownTypes);
                if (qualifiedProperty is not null)
                {
                    return forWrite ? qualifiedProperty.WriteField : qualifiedProperty.ReadField;
                }
            }

            return memberResolution.Field;
        }

        if (expression is not NameExpressionSyntax nameExpression)
        {
            return null;
        }

        var name = nameExpression.Name;
        var locals = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var property = SemanticFacts.ResolvePropertyReference(name, locals, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
        if (property is not null)
        {
            return forWrite ? property.WriteField : property.ReadField;
        }

        if (name.Parts.Count > 1 &&
            SemanticFacts.TryResolveValueReceiverType(name, locals, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes) is { } receiverType)
        {
            var instanceField = _knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(type => type.Name == receiverType.Name || type == receiverType)
                .SelectMany(type => EnumerateTypeHierarchy(type))
                .SelectMany(type => _knownFields.Where(field =>
                    !field.IsStatic &&
                    field.DeclaringTypeName == type.Name &&
                    field.Name == name.Parts[^1].Text))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return instanceField;
            }
        }

        return SemanticFacts.ResolveName(
            name,
            locals,
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod).Field;
    }

    private BoundWriteTarget? ResolveBoundWriteTarget(ExpressionSyntax target, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        SemanticFacts.BindWriteTarget(
            target,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private BoundCall? ResolveBoundCall(CallExpressionSyntax call, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod)
    {
        var boundCall = SemanticFacts.BindCall(
            call,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        if (boundCall is not null)
        {
            return boundCall;
        }

        if (ResolveInvocationForLowering(call, registerByName, currentMethod) is { Method: not null } fallbackInvocation)
        {
            return new BoundCall(
                fallbackInvocation.IsVirtual ? BoundCallKind.Virtual : BoundCallKind.Direct,
                SemanticFacts.GetExpressionDisplayName(call.Target),
                fallbackInvocation.Method,
                fallbackInvocation.Method.ReturnType,
                fallbackInvocation.ReceiverType is not null
                    ? new BoundReceiver(BoundReceiverKind.Expression, fallbackInvocation.ReceiverType, SourceExpression: call.Target)
                    : null,
                SourceExpression: call);
        }

        return null;
    }

    private string BuildCallDiagnosticMessage(CallExpressionSyntax call, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod)
    {
        var locals = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var displayName = SemanticFacts.GetExpressionDisplayName(call.Target);
        var receiverType = call.Target is MemberAccessExpressionSyntax memberAccessTarget
            ? SemanticFacts.InferExpressionType(memberAccessTarget.Receiver, locals, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod)
            : null;
        var directInvocation = SemanticFacts.ResolveInvocation(
            call.Target,
            call.Arguments.Count,
            locals,
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var flattenedTarget = call.Target switch
        {
            NameExpressionSyntax nameExpression => nameExpression.Name,
            MemberAccessExpressionSyntax memberAccess => TryFlattenMemberAccess(memberAccess),
            _ => null
        };
        var flattenedInvocation = flattenedTarget is null
            ? null
            : SemanticFacts.ResolveInvocation(
                flattenedTarget,
                call.Arguments.Count,
                locals,
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod);

        var candidateSummary = receiverType is null
            ? "<none>"
            : string.Join(
                ", ",
                _knownMethods
                    .Where(method => method.DeclaringTypeName == receiverType.Name)
                    .Select(method => $"{method.DeclaringTypeName}.{method.Name}/{method.Parameters.Count}[static={method.IsStatic},virtual={method.IsVirtual},override={method.IsOverride}]"));

        var localSummary = string.Join(
            ", ",
            locals.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}:{pair.Value.Name}"));

        return
            $"Cannot lower unresolved call '{displayName}/{call.Arguments.Count}'. " +
            $"currentMethod={(currentMethod?.DeclaringTypeName is null ? currentMethod?.Name : $"{currentMethod.DeclaringTypeName}.{currentMethod.Name}") ?? "<null>"}, " +
            $"targetKind={call.Target.Kind}, " +
            $"receiverType={(receiverType?.Name ?? "<null>")}, " +
            $"directInvocation={(directInvocation?.Method is null ? "<null>" : $"{directInvocation.Method.DeclaringTypeName}.{directInvocation.Method.Name}")}, " +
            $"flattenedTarget={(flattenedTarget?.ToDisplayString() ?? "<null>")}, " +
            $"flattenedInvocation={(flattenedInvocation?.Method is null ? "<null>" : $"{flattenedInvocation.Method.DeclaringTypeName}.{flattenedInvocation.Method.Name}")}, " +
            $"receiverCandidates=[{candidateSummary}], " +
            $"locals=[{localSummary}]";
    }

    private void StoreIntoBoundWriteTarget(
        BoundWriteTarget target,
        IrValue source,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        switch (target.Kind)
        {
            case BoundWriteTargetKind.Local:
                if (!string.IsNullOrEmpty(target.DisplayName) && registerByName.TryGetValue(target.DisplayName, out var targetRegister))
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, targetRegister, source));
                    return;
                }
                break;
            case BoundWriteTargetKind.Property when target.SetterMethod is not null:
                instructions.Add(new IrInstruction(
                    target.SetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                    source,
                    new IrCallTarget(
                        target.SetterMethod,
                        target.DisplayName,
                        [source],
                        ResolveBoundWriteReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        !target.SetterMethod.IsStatic)));
                return;
            case BoundWriteTargetKind.Property when target.WriteField is not null:
                instructions.Add(new IrInstruction(
                    target.WriteField.IsStatic ? IrOpCode.StoreStaticField : IrOpCode.StoreField,
                    source,
                    new IrFieldTarget(
                        target.WriteField,
                        target.DisplayName,
                        ResolveBoundWriteReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                return;
            case BoundWriteTargetKind.Field when target.Field is not null:
                instructions.Add(new IrInstruction(
                    target.Field.IsStatic ? IrOpCode.StoreStaticField : IrOpCode.StoreField,
                    source,
                    new IrFieldTarget(
                        target.Field,
                        target.DisplayName,
                        ResolveBoundWriteReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                return;
        }

        throw new InvalidOperationException($"Cannot store into bound target '{target.DisplayName}'.");
    }

    private void LowerBoundReadInto(
        BoundMemberRead target,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        switch (target.Kind)
        {
            case BoundMemberReadKind.Local:
                if (!string.IsNullOrEmpty(target.DisplayName) && registerByName.TryGetValue(target.DisplayName, out var sourceRegister))
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, sourceRegister));
                    return;
                }
                break;
            case BoundMemberReadKind.Constant when target.Constant is not null:
                instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, target.Constant.Value ?? 0));
                return;
            case BoundMemberReadKind.Property when target.GetterMethod is not null:
                instructions.Add(new IrInstruction(
                    target.GetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                    destination,
                    new IrCallTarget(
                        target.GetterMethod,
                        target.DisplayName,
                        [],
                        ResolveBoundReadReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        !target.GetterMethod.IsStatic)));
                return;
            case BoundMemberReadKind.Property when target.ReadField is not null:
                instructions.Add(new IrInstruction(
                    target.ReadField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                    destination,
                    new IrFieldTarget(
                        target.ReadField,
                        target.DisplayName,
                        ResolveBoundReadReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                return;
            case BoundMemberReadKind.Field when target.Field is not null:
                instructions.Add(new IrInstruction(
                    target.Field.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                    destination,
                    new IrFieldTarget(
                        target.Field,
                        target.DisplayName,
                        ResolveBoundReadReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                return;
        }

        throw new InvalidOperationException($"Cannot lower bound read '{target.DisplayName}'.");
    }

    private BoundMemberRead? ResolveBoundRead(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        SemanticFacts.BindRead(
            expression,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private BoundElementRead? ResolveBoundElementRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        SemanticFacts.BindElementRead(
            target,
            indexExpressions,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private BoundElementWrite? ResolveBoundElementWrite(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        SemanticFacts.BindElementWrite(
            target,
            indexExpressions,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private BoundSliceRead? ResolveBoundSliceRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        SemanticFacts.BindSliceRead(
            target,
            indexExpressions,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private BoundLengthRead? ResolveBoundLengthRead(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        SemanticFacts.BindLengthRead(
            expression,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);

    private IrValue? ResolveBoundReadReceiver(
        BoundMemberRead target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Receiver is null || target.Receiver.Kind == BoundReceiverKind.Type)
        {
            return null;
        }

        if (TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        if (target.Receiver?.SourceExpression is not null)
        {
            return ResolveReceiverExpression(target.Receiver.SourceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        return target.SourceExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveFieldReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };
    }

    private IrValue? ResolveBoundWriteReceiver(
        BoundWriteTarget target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Property?.IsStatic == true || target.Field?.IsStatic == true)
        {
            return null;
        }

        if (target.Receiver is null || target.Receiver.Kind == BoundReceiverKind.Type)
        {
            return null;
        }

        if (TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        if (target.Receiver.SourceExpression is not null)
        {
            return ResolveReceiverExpression(target.Receiver.SourceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        return target.SourceExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveFieldWriteReceiver(nameExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };
    }

    private IrValue? ResolveBoundCallReceiver(
        BoundCall target,
        ExpressionSyntax sourceTarget,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Method.IsStatic || target.Receiver is null || target.Receiver.Kind == BoundReceiverKind.Type)
        {
            return null;
        }

        if (TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        if (target.Receiver.SourceExpression is not null)
        {
            return ResolveReceiverExpression(target.Receiver.SourceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        return sourceTarget switch
        {
            NameExpressionSyntax nameExpression => ResolveCallReceiver(nameExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };
    }

    private IrValue? ResolveBoundElementReceiver(
        BoundElementRead target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Receiver is not null &&
            TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        return target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveIndexedReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolveReceiverExpression(target.TargetExpression!, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? ResolveBoundSliceReceiver(
        BoundSliceRead target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Receiver is not null &&
            TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        if (target.TargetExpression is null)
        {
            return null;
        }

        return target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveIndexedReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolveReceiverExpression(target.TargetExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? ResolveBoundLengthReceiver(
        BoundLengthRead target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Receiver is null || target.Receiver.Kind == BoundReceiverKind.Type)
        {
            return null;
        }

        if (TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        if (target.Receiver?.SourceExpression is not null)
        {
            return ResolveReceiverExpression(target.Receiver.SourceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        return target.SourceExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveIndexedReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolveReceiverExpression(target.SourceExpression!, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? ResolveBoundElementOwnerReceiver(
        BoundElementRead target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.GetterMethod?.IsStatic == true || target.ReadField?.IsStatic == true)
        {
            return null;
        }

        if (target.Receiver is not null &&
            TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        return target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => ResolvePropertyReceiver(nameExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolvePropertyReceiver(target.TargetExpression!, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? ResolveBoundElementWriteReceiver(
        BoundElementWrite target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.Receiver is not null &&
            TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        return target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => ResolveIndexedReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolveReceiverExpression(target.TargetExpression!, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? ResolveBoundElementWriteOwnerReceiver(
        BoundElementWrite target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (target.SetterMethod?.IsStatic == true || target.WriteField?.IsStatic == true)
        {
            return null;
        }

        if (target.Receiver is not null &&
            TryResolveBoundReceiver(target.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod) is { } resolvedReceiver)
        {
            return resolvedReceiver;
        }

        return target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => ResolvePropertyReceiver(nameExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => ResolvePropertyReceiver(target.TargetExpression!, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue? TryResolveBoundReceiver(
        BoundReceiver receiver,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (!string.IsNullOrEmpty(receiver.LocalName) && registerByName.TryGetValue(receiver.LocalName, out var localRegister))
        {
            return localRegister;
        }

        if (receiver.SourceExpression is not null)
        {
            return ResolveReceiverExpression(receiver.SourceExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        return null;
    }

    private void LowerBoundElementReadInto(
        BoundElementRead target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var indexedType = target.IndexedType ?? target.ElementType;
        var indexRegister = target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => LowerFlattenedElementIndex(nameExpression.Name, indexExpressions, indexedType, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => LowerFlattenedElementIndex(target.TargetExpression!, indexExpressions, indexedType, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };

        if (target.GetterMethod is not null)
        {
            instructions.Add(new IrInstruction(
                target.GetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                destination,
                new IrCallTarget(
                    target.GetterMethod,
                    target.DisplayName,
                    [indexRegister],
                    ResolveBoundElementOwnerReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                    !target.GetterMethod.IsStatic)));
            return;
        }

        if (target.ReadField is not null)
        {
            var backingArrayRegister = AllocateTemp(target.ReadField.Type, registers);
            instructions.Add(new IrInstruction(
                target.ReadField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                backingArrayRegister,
                new IrFieldTarget(
                    target.ReadField,
                    target.DisplayName,
                    ResolveBoundElementOwnerReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
            instructions.Add(new IrInstruction(
                IrOpCode.LoadElement,
                destination,
                new IrArrayTarget(
                    backingArrayRegister,
                    indexRegister,
                    target.TargetExpression is NameExpressionSyntax namedTarget
                        ? TryGetTargetShape(namedTarget.Name, arrayShapesByName)
                        : null)));
            return;
        }

        var arrayRegister = ResolveBoundElementReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(
            IrOpCode.LoadElement,
            destination,
            new IrArrayTarget(arrayRegister!, indexRegister)));
    }

    private void LowerBoundElementWriteInto(
        BoundElementWrite target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IrValue valueRegister,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var indexedType = target.IndexedType ?? target.ElementType;
        var indexRegister = target.TargetExpression switch
        {
            NameExpressionSyntax nameExpression => LowerFlattenedElementIndex(nameExpression.Name, indexExpressions, indexedType, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => LowerFlattenedElementIndex(target.TargetExpression!, indexExpressions, indexedType, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };

        if (target.SetterMethod is not null)
        {
            instructions.Add(new IrInstruction(
                target.SetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                valueRegister,
                new IrCallTarget(
                    target.SetterMethod,
                    target.DisplayName,
                    [indexRegister, valueRegister],
                    ResolveBoundElementWriteOwnerReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                    !target.SetterMethod.IsStatic)));
            return;
        }

        if (target.WriteField is not null)
        {
            var backingArrayRegister = AllocateTemp(target.WriteField.Type, registers);
            instructions.Add(new IrInstruction(
                target.WriteField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                backingArrayRegister,
                new IrFieldTarget(
                    target.WriteField,
                    target.DisplayName,
                    ResolveBoundElementWriteOwnerReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
            instructions.Add(new IrInstruction(
                IrOpCode.StoreElement,
                valueRegister,
                new IrArrayTarget(
                    backingArrayRegister,
                    indexRegister,
                    target.TargetExpression is NameExpressionSyntax namedTarget
                        ? TryGetTargetShape(namedTarget.Name, arrayShapesByName)
                        : null)));
            return;
        }

        var arrayRegister = ResolveBoundElementWriteReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(
            IrOpCode.StoreElement,
            valueRegister,
            new IrArrayTarget(
                arrayRegister!,
                indexRegister,
                target.TargetExpression is NameExpressionSyntax directNamedTarget
                    ? TryGetTargetShape(directNamedTarget.Name, arrayShapesByName)
                    : null)));
    }

    private void LowerBoundSliceReadInto(
        BoundSliceRead target,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var sourceRegister = ResolveBoundSliceReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
            ?? throw new InvalidOperationException($"Cannot lower bound slice receiver '{target.DisplayName}'.");
        var range = target.Range
            ?? throw new InvalidOperationException($"Cannot lower bound slice '{target.DisplayName}' without range expression.");

        if (target.TargetType == TypeSymbol.String)
        {
            LowerStringSliceInto(destination, sourceRegister, range, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return;
        }

        LowerArraySliceCopyInto(destination, sourceRegister, range, target.TargetType, registerByName, arrayShapesByName, registers, instructions, currentMethod);
    }

    private void LowerBoundLengthReadInto(
        BoundLengthRead target,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var receiverRegister = ResolveBoundLengthReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.LoadLength, destination, new IrArrayTarget(receiverRegister!)));
    }

    private ConstantSymbol? ResolveConstant(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        expression switch
        {
            NameExpressionSyntax nameExpression => SemanticFacts.ResolveConstantReference(
                nameExpression.Name,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod),
            MemberAccessExpressionSyntax memberAccess => SemanticFacts.ResolveMemberAccess(
                memberAccess,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod).Constant,
            _ => null
        };

    private PropertySymbol? ResolveProperty(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        expression switch
        {
            NameExpressionSyntax nameExpression => SemanticFacts.ResolvePropertyReference(
                nameExpression.Name,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                _knownTypes)
            ,
            MemberAccessExpressionSyntax memberAccess => SemanticFacts.ResolveMemberAccess(
                    memberAccess,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod,
                    _knownTypes).Property
                ?? (TryFlattenMemberAccess(memberAccess) is { } qualifiedName
                    ? SemanticFacts.ResolvePropertyReference(
                        qualifiedName,
                        registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                        _knownFields,
                        _knownConstants,
                        _knownProperties,
                        currentMethod,
                        _knownTypes)
                    : null),
            _ => null
        };

    private IEnumerable<NamedTypeSymbol> EnumerateTypeHierarchy(NamedTypeSymbol type)
    {
        var current = type;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current.Name))
        {
            yield return current;
            if (current.BaseType is null)
            {
                yield break;
            }

            current = _knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(candidate => candidate.Name == current.BaseType.Name);
            if (current is null)
            {
                yield break;
            }
        }
    }

    private InvocationResolution? ResolveInvocationForLowering(
        CallExpressionSyntax call,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod)
    {
        var locals = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var invocation = SemanticFacts.ResolveInvocation(
            call.Target,
            call.Arguments.Count,
            locals,
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        if (invocation is not null)
        {
            return invocation;
        }

        QualifiedNameSyntax? qualifiedTarget = call.Target switch
        {
            NameExpressionSyntax nameExpression => nameExpression.Name,
            MemberAccessExpressionSyntax targetMemberAccess => TryFlattenMemberAccess(targetMemberAccess),
            _ => null
        };

        if (qualifiedTarget is not null)
        {
            if (qualifiedTarget.Parts.Count >= 2)
            {
                var declaringTypeName = string.Join(".", qualifiedTarget.Parts.Take(qualifiedTarget.Parts.Count - 1).Select(part => part.Text));
                if (SemanticFacts.ResolveTypeReference(declaringTypeName, _knownTypes) is { } targetType)
                {
                    var staticMethod = _knownMethods.FirstOrDefault(method =>
                        method.IsStatic &&
                        method.DeclaringTypeName == targetType.Name &&
                        method.Name == qualifiedTarget.Parts[^1].Text &&
                        SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count));
                    if (staticMethod is not null)
                    {
                        return new InvocationResolution(staticMethod);
                    }
                }
            }

            if (SemanticFacts.TryResolveValueReceiverType(qualifiedTarget, locals, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes) is { } valueReceiverType)
            {
                var namedReceiverType = _knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == valueReceiverType.Name);
                if (namedReceiverType is not null)
                {
                    var instanceMethod = EnumerateTypeHierarchy(namedReceiverType)
                        .SelectMany(type => _knownMethods.Where(method =>
                            !method.IsStatic &&
                            method.DeclaringTypeName == type.Name &&
                            method.Name == qualifiedTarget.Parts[^1].Text &&
                            SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count)))
                        .FirstOrDefault();
                    if (instanceMethod is not null)
                    {
                        return new InvocationResolution(instanceMethod, valueReceiverType, instanceMethod.IsVirtual || instanceMethod.IsOverride);
                    }
                }
            }
        }

        if (call.Target is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Receiver is NameExpressionSyntax receiverName &&
                SemanticFacts.ResolveTypeReference(receiverName.Name.ToDisplayString(), _knownTypes) is { } targetType)
            {
                var staticMethod = _knownMethods.FirstOrDefault(method =>
                    method.IsStatic &&
                    method.DeclaringTypeName == targetType.Name &&
                    method.Name == memberAccess.MemberName.Text &&
                    SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count));
                if (staticMethod is not null)
                {
                    return new InvocationResolution(staticMethod);
                }
            }

            var receiverType = SemanticFacts.InferExpressionType(memberAccess.Receiver, locals, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
            var namedReceiverType = _knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == receiverType.Name);
            if (namedReceiverType is not null)
            {
                var instanceMethod = EnumerateTypeHierarchy(namedReceiverType)
                    .SelectMany(type => _knownMethods.Where(method =>
                        !method.IsStatic &&
                        method.DeclaringTypeName == type.Name &&
                        method.Name == memberAccess.MemberName.Text &&
                        SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count)))
                    .FirstOrDefault();
                if (instanceMethod is not null)
                {
                    return new InvocationResolution(instanceMethod, receiverType, instanceMethod.IsVirtual || instanceMethod.IsOverride);
                }
            }
        }

        return null;
    }

    private static QualifiedNameSyntax? TryFlattenMemberAccess(ExpressionSyntax expression)
    {
        var parts = new List<SyntaxToken>();
        ExpressionSyntax? current = expression;
        while (current is not null)
        {
            switch (current)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    parts.Insert(0, memberAccess.MemberName);
                    current = memberAccess.Receiver;
                    break;
                case NameExpressionSyntax nameExpression when nameExpression.Name.Parts.Count > 0:
                    parts.InsertRange(0, nameExpression.Name.Parts);
                    current = null;
                    break;
                default:
                    return null;
            }
        }

        return parts.Count == 0 ? null : new QualifiedNameSyntax(parts);
    }

    private IrValue? ResolvePropertyReceiver(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod) =>
        expression switch
        {
            NameExpressionSyntax nameExpression => ResolveCallReceiver(nameExpression, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };

    private static string GetExpressionDisplayName(ExpressionSyntax expression) => SemanticFacts.GetExpressionDisplayName(expression);

    private IrValue? ResolveFieldReceiver(
        QualifiedNameSyntax name,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod) =>
        ResolveMemberReceiver(name, registerByName, arrayShapesByName, registers, instructions, currentMethod);

    private IrValue? ResolveFieldWriteReceiver(
        ExpressionSyntax target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod) =>
        target switch
        {
            NameExpressionSyntax assignmentTargetName => assignmentTargetName.Name.Parts.Count > 1
                ? ResolveMemberReceiver(assignmentTargetName.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod)
                : ResolveFieldReceiver(assignmentTargetName.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAssignment => ResolveReceiverExpression(memberAssignment.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };

    private IrValue? ResolveIndexedReceiver(
        QualifiedNameSyntax target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod) =>
        target.Parts.Count > 1
            ? ResolveMemberReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
            : ResolveReceiverValue(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);

    private IrValue? ResolveCallReceiver(
        ExpressionSyntax target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod) =>
        target switch
        {
            NameExpressionSyntax nameExpression => ResolveMemberReceiver(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => null
        };

    private IrValue? ResolveReceiverExpression(
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        return receiver switch
        {
            NameExpressionSyntax nameExpression => ResolveReceiverValue(nameExpression.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ArrayLengthExpressionSyntax or CallExpressionSyntax => LowerReceiverExpression(receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => LowerReceiverExpression(receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
    }

    private IrValue LowerReceiverExpression(
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var receiverType = SemanticFacts.InferExpressionType(
            receiver,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod);
        var temp = AllocateTemp(receiverType, registers);
        if (ResolveBoundLengthRead(receiver, registerByName, currentMethod) is { } boundLengthRead)
        {
            LowerBoundLengthReadInto(boundLengthRead, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return temp;
        }

        LowerExpressionInto(receiver, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        return temp;
    }

    private IrValue? ResolveMemberReceiver(
        QualifiedNameSyntax memberAccess,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (memberAccess.Parts.Count == 1)
        {
            return currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic && registerByName.TryGetValue("self", out var implicitSelf)
                ? implicitSelf
                : null;
        }

        return ResolveReceiverValue(
            new QualifiedNameSyntax(memberAccess.Parts.Take(memberAccess.Parts.Count - 1).ToArray()),
            registerByName,
            arrayShapesByName,
            registers,
            instructions,
            currentMethod);
    }

    private IrValue? ResolveReceiverValue(
        QualifiedNameSyntax receiverName,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var displayName = receiverName.ToDisplayString();
        if (registerByName.TryGetValue(displayName, out var existing))
        {
            return existing;
        }

        if (displayName == "self" && registerByName.TryGetValue("self", out var explicitSelf))
        {
            return explicitSelf;
        }

        if (receiverName.Parts.Count == 1 &&
            currentMethod?.DeclaringTypeName is not null &&
            !currentMethod.IsStatic &&
            registerByName.TryGetValue("self", out var implicitSelf) &&
            SemanticFacts.TryResolveValueReferenceType(
                receiverName,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod) is not null)
        {
            return implicitSelf;
        }

        var receiverType = SemanticFacts.ResolveName(
            receiverName,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod).Type;
        if (receiverType is null)
        {
            return null;
        }

        var temp = AllocateTemp(receiverType, registers);
        LowerNameReferenceInto(receiverName, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        return temp;
    }

    private StatementSyntax RewriteWithStatement(
        StatementSyntax statement,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        statement switch
        {
            BlockStatementSyntax block => block with
            {
                Statements = block.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            ExpressionStatementSyntax expressionStatement => expressionStatement with
            {
                Expression = RewriteWithExpression(expressionStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            IncStatementSyntax incStatement => incStatement with
            {
                Target = RewriteWithExpression(incStatement.Target, receiver, registerByName, localTypes, currentMethod)
            },
            DecStatementSyntax decStatement => decStatement with
            {
                Target = RewriteWithExpression(decStatement.Target, receiver, registerByName, localTypes, currentMethod)
            },
            IncludeStatementSyntax includeStatement => includeStatement with
            {
                Target = RewriteWithExpression(includeStatement.Target, receiver, registerByName, localTypes, currentMethod),
                Value = RewriteWithExpression(includeStatement.Value, receiver, registerByName, localTypes, currentMethod)
            },
            ExcludeStatementSyntax excludeStatement => excludeStatement with
            {
                Target = RewriteWithExpression(excludeStatement.Target, receiver, registerByName, localTypes, currentMethod),
                Value = RewriteWithExpression(excludeStatement.Value, receiver, registerByName, localTypes, currentMethod)
            },
            ReturnStatementSyntax returnStatement when returnStatement.Expression is not null => returnStatement with
            {
                Expression = RewriteWithExpression(returnStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            RaiseStatementSyntax raiseStatement when raiseStatement.Expression is not null => raiseStatement with
            {
                Expression = RewriteWithExpression(raiseStatement.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            IfStatementSyntax ifStatement => ifStatement with
            {
                Condition = RewriteWithExpression(ifStatement.Condition, receiver, registerByName, localTypes, currentMethod),
                ThenStatement = RewriteWithStatement(ifStatement.ThenStatement, receiver, registerByName, localTypes, currentMethod),
                ElseStatement = ifStatement.ElseStatement is null
                    ? null
                    : RewriteWithStatement(ifStatement.ElseStatement, receiver, registerByName, localTypes, currentMethod)
            },
            WhileStatementSyntax whileStatement => whileStatement with
            {
                Condition = RewriteWithExpression(whileStatement.Condition, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(whileStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            RepeatStatementSyntax repeatStatement => repeatStatement with
            {
                Statements = repeatStatement.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                Condition = RewriteWithExpression(repeatStatement.Condition, receiver, registerByName, localTypes, currentMethod)
            },
            ForStatementSyntax forStatement => forStatement with
            {
                LowerBound = RewriteWithExpression(forStatement.LowerBound, receiver, registerByName, localTypes, currentMethod),
                UpperBound = RewriteWithExpression(forStatement.UpperBound, receiver, registerByName, localTypes, currentMethod),
                StepExpression = forStatement.StepExpression is null
                    ? null
                    : RewriteWithExpression(forStatement.StepExpression, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(forStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            ForeachStatementSyntax foreachStatement => foreachStatement with
            {
                Collection = RewriteWithExpression(foreachStatement.Collection, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(foreachStatement.Body, receiver, registerByName, localTypes, currentMethod)
            },
            CaseStatementSyntax caseStatement => caseStatement with
            {
                Expression = RewriteWithExpression(caseStatement.Expression, receiver, registerByName, localTypes, currentMethod),
                Clauses = caseStatement.Clauses
                    .Select(clause => clause with
                    {
                        Labels = clause.Labels
                            .Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod))
                            .ToArray(),
                        Guard = clause.Guard is null
                            ? null
                            : RewriteWithExpression(clause.Guard, receiver, registerByName, localTypes, currentMethod),
                        Body = RewriteWithStatement(clause.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = caseStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchStatementSyntax matchStatement => matchStatement with
            {
                Expression = RewriteWithExpression(matchStatement.Expression, receiver, registerByName, localTypes, currentMethod),
                Arms = matchStatement.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels
                            .Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod))
                            .ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, registerByName, localTypes, currentMethod),
                        Body = RewriteWithStatement(arm.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = matchStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            LocalVariableDeclarationStatementSyntax localVariable => localVariable with
            {
                Declarators = localVariable.Declarators
                    .Select(declarator => declarator.Initializer is null
                        ? declarator
                        : declarator with
                        {
                            Initializer = RewriteWithExpression(declarator.Initializer, receiver, registerByName, localTypes, currentMethod)
                        })
                    .ToArray()
            },
            TryStatementSyntax tryStatement => tryStatement with
            {
                TryStatements = tryStatement.TryStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                ExceptionClauses = tryStatement.ExceptionClauses
                    .Select(clause => clause with
                    {
                        Body = RewriteWithStatement(clause.Body, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray(),
                ExceptStatements = tryStatement.ExceptStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray(),
                FinallyStatements = tryStatement.FinallyStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            WithStatementSyntax nestedWith => nestedWith with
            {
                Receiver = RewriteWithExpression(nestedWith.Receiver, receiver, registerByName, localTypes, currentMethod),
                Body = RewriteWithStatement(nestedWith.Body, receiver, registerByName, localTypes, currentMethod)
            },
            _ => statement
        };

    private ExpressionSyntax RewriteWithExpression(
        ExpressionSyntax expression,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (expression is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            ShouldQualifyWithName(name.Name, registerByName, localTypes, currentMethod))
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return expression switch
        {
            AssignmentExpressionSyntax assignment => assignment with
            {
                Target = RewriteWithAssignmentTarget(assignment.Target, receiver, registerByName, localTypes, currentMethod),
                Expression = RewriteWithExpression(assignment.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            UnaryExpressionSyntax unary => unary with
            {
                Operand = RewriteWithExpression(unary.Operand, receiver, registerByName, localTypes, currentMethod)
            },
            MatchNotPatternSyntax notPattern => notPattern with
            {
                Pattern = RewriteWithExpression(notPattern.Pattern, receiver, registerByName, localTypes, currentMethod)
            },
            MatchOrPatternSyntax orPattern => orPattern with
            {
                Patterns = orPattern.Patterns
                    .Select(pattern => RewriteWithExpression(pattern, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchAndPatternSyntax andPattern => andPattern with
            {
                Patterns = andPattern.Patterns
                    .Select(pattern => (MatchRelationalPatternSyntax)RewriteWithExpression(pattern, receiver, registerByName, localTypes, currentMethod))
                    .ToArray()
            },
            MatchRelationalPatternSyntax relational => relational with
            {
                Operand = RewriteWithExpression(relational.Operand, receiver, registerByName, localTypes, currentMethod)
            },
            BinaryExpressionSyntax binary => binary with
            {
                Left = RewriteWithExpression(binary.Left, receiver, registerByName, localTypes, currentMethod),
                Right = RewriteWithExpression(binary.Right, receiver, registerByName, localTypes, currentMethod)
            },
            MatchExpressionSyntax matchExpression => matchExpression with
            {
                Expression = RewriteWithExpression(matchExpression.Expression, receiver, registerByName, localTypes, currentMethod),
                Arms = matchExpression.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels.Select(label => RewriteWithExpression(label, receiver, registerByName, localTypes, currentMethod)).ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, registerByName, localTypes, currentMethod),
                        Expression = RewriteWithExpression(arm.Expression, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray()
            },
            CallExpressionSyntax call => call with
            {
                Target = RewriteWithCallTarget(call.Target, call.Arguments.Count, receiver, registerByName, localTypes, currentMethod),
                Arguments = call.Arguments
                    .Select(argument => argument with
                    {
                        Expression = RewriteWithExpression(argument.Expression, receiver, registerByName, localTypes, currentMethod)
                    })
                    .ToArray()
            },
            MemberAccessExpressionSyntax memberAccess => memberAccess with
            {
                Receiver = RewriteWithExpression(memberAccess.Receiver, receiver, registerByName, localTypes, currentMethod)
            },
            PostfixElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                Target = RewriteWithExpression(elementAccess.Target, receiver, registerByName, localTypes, currentMethod),
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteWithExpression(index, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            ElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                IndexExpressions = elementAccess.IndexExpressions.Select(index => RewriteWithExpression(index, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            AsExpressionSyntax asExpression => asExpression with
            {
                Expression = RewriteWithExpression(asExpression.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            TypeTestExpressionSyntax typeTest => typeTest with
            {
                Expression = RewriteWithExpression(typeTest.Expression, receiver, registerByName, localTypes, currentMethod)
            },
            NewExpressionSyntax newExpression => newExpression with
            {
                Arguments = newExpression.Arguments.Select(argument => argument with
                {
                    Expression = RewriteWithExpression(argument.Expression, receiver, registerByName, localTypes, currentMethod)
                }).ToArray()
            },
            NewArrayExpressionSyntax newArray => newArray with
            {
                LengthExpressions = newArray.LengthExpressions.Select(length => RewriteWithExpression(length, receiver, registerByName, localTypes, currentMethod)).ToArray()
            },
            _ => expression
        };
    }

    private ExpressionSyntax RewriteWithAssignmentTarget(
        ExpressionSyntax target,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        target is NameExpressionSyntax name &&
        name.Name.Parts.Count == 1 &&
        ShouldQualifyWithName(name.Name, registerByName, localTypes, currentMethod)
            ? QualifyWithReceiver(receiver, name.Name.Parts[0])
            : RewriteWithExpression(target, receiver, registerByName, localTypes, currentMethod);

    private ExpressionSyntax RewriteWithCallTarget(
        ExpressionSyntax target,
        int argumentCount,
        ExpressionSyntax receiver,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (target is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            SemanticFacts.ResolveInvocation(
                name.Name,
                argumentCount,
                localTypes,
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod) is null)
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return RewriteWithExpression(target, receiver, registerByName, localTypes, currentMethod);
    }

    private bool ShouldQualifyWithName(
        QualifiedNameSyntax name,
        Dictionary<string, IrValue> registerByName,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod) =>
        !registerByName.ContainsKey(name.ToDisplayString()) &&
        SemanticFacts.ResolveName(
            name,
            localTypes,
            _knownTypes,
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod).Kind == NameResolutionKind.Unknown;

    private static MemberAccessExpressionSyntax QualifyWithReceiver(ExpressionSyntax receiver, SyntaxToken memberName) =>
        new(
            receiver,
            new SyntaxToken(SyntaxKind.DotToken, ".", null, memberName.Span),
            memberName);
}
