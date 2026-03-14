namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Syntax;

public enum IrOpCode
{
    LoadConstant,
    Copy,
    Add,
    Subtract,
    Multiply,
    Divide,
    CompareEqual,
    CompareNotEqual,
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
    Call,
    CallVirtual,
    Branch,
    BranchIfFalse,
    Label,
    Return
}

public sealed record IrValue(string Name, TypeSymbol Type, ushort Index);
public sealed record IrArrayShape(IReadOnlyList<IrValue> Extents);

public sealed record IrCallTarget(MethodSymbol? Method, string DisplayName, IReadOnlyList<IrValue> Arguments, IrValue? Receiver = null, bool IsVirtual = false);

public sealed record IrFieldTarget(FieldSymbol Field, string DisplayName, IrValue? Receiver = null);
public sealed record IrNewArrayTarget(string ElementTypeName, IrValue LengthRegister, IrArrayShape Shape);
public sealed record IrArrayTarget(IrValue Array, IrValue? Index = null, IrArrayShape? Shape = null);

public sealed record IrInstruction(IrOpCode OpCode, IrValue? Destination, object? Operand);

public sealed record IrBasicBlock(string Name, IReadOnlyList<IrInstruction> Instructions);

public sealed record IrFunction(
    string Name,
    TypeSymbol ReturnType,
    IReadOnlyList<IrValue> Registers,
    IReadOnlyList<IrBasicBlock> Blocks,
    IReadOnlyList<IrArrayShape> ArrayShapes);

public sealed class Lowerer
{
    private readonly IReadOnlyList<MethodSymbol> _knownMethods;
    private readonly IReadOnlyList<FieldSymbol> _knownFields;
    private readonly IReadOnlyList<PropertySymbol> _knownProperties;
    private readonly IReadOnlyList<TypeSymbol> _knownTypes;
    private int _labelCounter;

    public Lowerer(IEnumerable<MethodSymbol>? knownMethods = null, IEnumerable<FieldSymbol>? knownFields = null, IEnumerable<TypeSymbol>? knownTypes = null, IEnumerable<PropertySymbol>? knownProperties = null)
    {
        _knownMethods = knownMethods?.ToArray() ?? [];
        _knownFields = knownFields?.ToArray() ?? [];
        _knownTypes = knownTypes?.ToArray() ?? [];
        _knownProperties = knownProperties?.ToArray() ?? [];
    }

    public IrFunction Lower(MethodSymbol method)
    {
        var registers = new List<IrValue>();
        var registerByName = new Dictionary<string, IrValue>(StringComparer.Ordinal);
        var localTypes = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        var arrayShapesByName = new Dictionary<string, IReadOnlyList<IrValue>>(StringComparer.Ordinal);
        _labelCounter = 0;

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
                LowerSyntheticTopLevelBody(method, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister);
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
            LowerMethodBody(method.Declaration, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, method);
        }

        var entryBlock = new IrBasicBlock("entry", instructions);
        var arrayShapes = instructions
            .Where(instruction => instruction.OpCode == IrOpCode.NewArray && instruction.Operand is IrNewArrayTarget)
            .Select(instruction => ((IrNewArrayTarget)instruction.Operand!).Shape)
            .ToArray();
        return new IrFunction(method.Name, method.ReturnType, registers, [entryBlock], arrayShapes);
    }

    private void LowerMethodBody(
        MethodDeclarationSyntax declaration,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
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
            hasTerminated = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
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
        IrValue? returnRegister)
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
        MethodSymbol? currentMethod)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                foreach (var nestedStatement in block.Statements)
                {
                    if (LowerStatement(nestedStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod))
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
            case ExpressionStatementSyntax expressionStatement:
                LowerExpressionStatement(expressionStatement.Expression, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
                return false;
            case IfStatementSyntax ifStatement:
                return LowerIfStatement(ifStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
            case WhileStatementSyntax whileStatement:
                LowerWhileStatement(whileStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
                return false;
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
        MethodSymbol? currentMethod)
    {
        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(ifStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elseLabel = AllocateLabel("else");
        var endLabel = AllocateLabel("endif");

        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, elseLabel));
        var thenTerminates = LowerStatement(ifStatement.ThenStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);

        if (ifStatement.ElseStatement is not null)
        {
            if (!thenTerminates)
            {
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
            var elseTerminates = LowerStatement(ifStatement.ElseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
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
        MethodSymbol? currentMethod)
    {
        var loopLabel = AllocateLabel("while");
        var endLabel = AllocateLabel("endwhile");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(whileStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, endLabel));
        LowerStatement(whileStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
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
        var tempType = SemanticFacts.InferExpressionType(expression, localTypes, _knownMethods, _knownFields, _knownProperties, currentMethod);
        var tempRegister = new IrValue($"r{registers.Count}", tempType, (ushort)registers.Count);
        registers.Add(tempRegister);
        LowerExpressionInto(expression, tempRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
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
                    var argumentType = SemanticFacts.InferExpressionType(argument.Expression, new Dictionary<string, TypeSymbol>(), _knownMethods, _knownFields, _knownProperties, currentMethod);
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
            case ArrayLengthExpressionSyntax lengthExpression:
                var arrayRegisterForLength = AllocateTemp(
                    SemanticFacts.InferExpressionType(
                        new NameExpressionSyntax(lengthExpression.Target),
                        registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                        _knownMethods,
                        _knownFields,
                        _knownProperties,
                        currentMethod),
                    registers);
                LowerNameReferenceInto(lengthExpression.Target, arrayRegisterForLength, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.LoadLength, destination, new IrArrayTarget(arrayRegisterForLength)));
                return;
            case ElementAccessExpressionSyntax elementAccess:
                var arrayType = SemanticFacts.InferExpressionType(
                    new NameExpressionSyntax(elementAccess.Target),
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod);
                var indexRegister = LowerFlattenedElementIndex(elementAccess.Target, elementAccess.IndexExpressions, arrayType, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (ResolveIndexerProperty(new NameExpressionSyntax(elementAccess.Target), registerByName, currentMethod) is { } indexer && indexer.ReadField is not null)
                {
                    var backingArrayRegister = AllocateTemp(indexer.ReadField.Type, registers);
                    instructions.Add(new IrInstruction(
                        indexer.ReadField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                        backingArrayRegister,
                        new IrFieldTarget(
                            indexer.ReadField,
                            $"{elementAccess.Target.ToDisplayString()}[{indexer.IndexParameter?.Name ?? "index"}]",
                            ResolveIndexedReceiver(elementAccess.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                    instructions.Add(new IrInstruction(
                        IrOpCode.LoadElement,
                        destination,
                        new IrArrayTarget(backingArrayRegister, indexRegister, TryGetTargetShape(elementAccess.Target, arrayShapesByName))));
                    return;
                }

                var arrayRegister = AllocateTemp(
                    SemanticFacts.InferExpressionType(
                        new NameExpressionSyntax(elementAccess.Target),
                        registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                        _knownMethods,
                        _knownFields,
                        _knownProperties,
                        currentMethod),
                    registers);
                LowerNameReferenceInto(elementAccess.Target, arrayRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(
                    IrOpCode.LoadElement,
                    destination,
                    new IrArrayTarget(arrayRegister, indexRegister, TryGetTargetShape(elementAccess.Target, arrayShapesByName))));
                return;
            case PostfixElementAccessExpressionSyntax elementAccess:
                var postfixArrayType = SemanticFacts.InferExpressionType(
                    elementAccess.Target,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod);
                var postfixIndexRegister = LowerFlattenedElementIndex(
                    elementAccess.Target,
                    elementAccess.IndexExpressions,
                    postfixArrayType,
                    registerByName,
                    arrayShapesByName,
                    registers,
                    instructions,
                    currentMethod);
                if (ResolveIndexerProperty(elementAccess.Target, registerByName, currentMethod) is { } postfixIndexer && postfixIndexer.ReadField is not null)
                {
                    var postfixBackingArrayRegister = AllocateTemp(postfixIndexer.ReadField.Type, registers);
                    instructions.Add(new IrInstruction(
                        postfixIndexer.ReadField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                        postfixBackingArrayRegister,
                        new IrFieldTarget(
                            postfixIndexer.ReadField,
                            $"{GetExpressionDisplayName(elementAccess.Target)}[{postfixIndexer.IndexParameter?.Name ?? "index"}]",
                            ResolvePropertyReceiver(elementAccess.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                    instructions.Add(new IrInstruction(
                        IrOpCode.LoadElement,
                        destination,
                        new IrArrayTarget(postfixBackingArrayRegister, postfixIndexRegister)));
                    return;
                }

                var postfixArrayRegister = ResolveReceiverExpression(elementAccess.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(
                    IrOpCode.LoadElement,
                    destination,
                    new IrArrayTarget(postfixArrayRegister!, postfixIndexRegister)));
                return;
            case MemberAccessExpressionSyntax memberAccess when memberAccess.MemberName.Text == "Length":
                var receiverRegisterForLength = ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.LoadLength, destination, new IrArrayTarget(receiverRegisterForLength!)));
                return;
            case MemberAccessExpressionSyntax memberAccess when SemanticFacts.ResolveMemberAccess(
                    memberAccess,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod) is { Property.GetterMethod: not null } memberProperty:
                instructions.Add(new IrInstruction(
                    memberProperty.Property!.GetterMethod!.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                    destination,
                    new IrCallTarget(
                        memberProperty.Property.GetterMethod,
                        memberProperty.DisplayName,
                        [],
                        ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        !memberProperty.Property.GetterMethod.IsStatic)));
                return;
            case MemberAccessExpressionSyntax memberAccess when SemanticFacts.ResolveMemberAccess(
                    memberAccess,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod) is { Field: not null } memberField:
                instructions.Add(new IrInstruction(
                    memberField.Field!.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                    destination,
                    new IrFieldTarget(
                        memberField.Field,
                        memberField.DisplayName,
                        ResolveReceiverExpression(memberAccess.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                return;
            case NameExpressionSyntax name when ResolveProperty(name, registerByName, currentMethod) is { } property && property.GetterMethod is not null:
                instructions.Add(new IrInstruction(
                    property.GetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                    destination,
                    new IrCallTarget(
                        property.GetterMethod,
                        name.Name.ToDisplayString(),
                        [],
                        ResolvePropertyReceiver(name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        !property.GetterMethod.IsStatic)));
                return;
            case NameExpressionSyntax name when ResolveStorageField(name, registerByName, currentMethod, false) is { } field:
                instructions.Add(new IrInstruction(
                    field.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                    destination,
                    new IrFieldTarget(
                        field,
                        name.Name.ToDisplayString(),
                        ResolveFieldReceiver(name.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
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
                var elementValueRegister = AllocateTemp(destination.Type, registers);
                var targetArrayType = SemanticFacts.InferExpressionType(
                    new NameExpressionSyntax(elementAssignment.Target),
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod);
                var targetIndexRegister = LowerFlattenedElementIndex(elementAssignment.Target, elementAssignment.IndexExpressions, targetArrayType, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                LowerExpressionInto(assignment.Expression, elementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (ResolveIndexerProperty(new NameExpressionSyntax(elementAssignment.Target), registerByName, currentMethod) is { } writableIndexer && writableIndexer.WriteField is not null)
                {
                    var backingArrayRegister = AllocateTemp(writableIndexer.WriteField.Type, registers);
                    instructions.Add(new IrInstruction(
                        writableIndexer.WriteField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                        backingArrayRegister,
                        new IrFieldTarget(
                            writableIndexer.WriteField,
                            $"{elementAssignment.Target.ToDisplayString()}[{writableIndexer.IndexParameter?.Name ?? "index"}]",
                            ResolveIndexedReceiver(elementAssignment.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                    instructions.Add(new IrInstruction(
                        IrOpCode.StoreElement,
                        elementValueRegister,
                        new IrArrayTarget(backingArrayRegister, targetIndexRegister, TryGetTargetShape(elementAssignment.Target, arrayShapesByName))));
                }
                else
                {
                    var targetArrayRegister = AllocateTemp(
                        SemanticFacts.InferExpressionType(
                            new NameExpressionSyntax(elementAssignment.Target),
                            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                            _knownMethods,
                            _knownFields,
                            _knownProperties,
                            currentMethod),
                        registers);
                    LowerNameReferenceInto(elementAssignment.Target, targetArrayRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    instructions.Add(new IrInstruction(
                        IrOpCode.StoreElement,
                        elementValueRegister,
                        new IrArrayTarget(targetArrayRegister, targetIndexRegister, TryGetTargetShape(elementAssignment.Target, arrayShapesByName))));
                }

                if (destination.Index != elementValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, elementValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when assignment.Target is PostfixElementAccessExpressionSyntax elementAssignment:
                var postfixElementValueRegister = AllocateTemp(destination.Type, registers);
                var postfixTargetArrayType = SemanticFacts.InferExpressionType(
                    elementAssignment.Target,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod);
                var postfixTargetIndexRegister = LowerFlattenedElementIndex(
                    elementAssignment.Target,
                    elementAssignment.IndexExpressions,
                    postfixTargetArrayType,
                    registerByName,
                    arrayShapesByName,
                    registers,
                    instructions,
                    currentMethod);
                LowerExpressionInto(assignment.Expression, postfixElementValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                if (ResolveIndexerProperty(elementAssignment.Target, registerByName, currentMethod) is { } writablePostfixIndexer && writablePostfixIndexer.WriteField is not null)
                {
                    var postfixBackingArrayRegister = AllocateTemp(writablePostfixIndexer.WriteField.Type, registers);
                    instructions.Add(new IrInstruction(
                        writablePostfixIndexer.WriteField.IsStatic ? IrOpCode.LoadStaticField : IrOpCode.LoadField,
                        postfixBackingArrayRegister,
                        new IrFieldTarget(
                            writablePostfixIndexer.WriteField,
                            $"{GetExpressionDisplayName(elementAssignment.Target)}[{writablePostfixIndexer.IndexParameter?.Name ?? "index"}]",
                            ResolvePropertyReceiver(elementAssignment.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod))));
                    instructions.Add(new IrInstruction(
                        IrOpCode.StoreElement,
                        postfixElementValueRegister,
                        new IrArrayTarget(postfixBackingArrayRegister, postfixTargetIndexRegister)));
                    if (destination.Index != postfixElementValueRegister.Index)
                    {
                        instructions.Add(new IrInstruction(IrOpCode.Copy, destination, postfixElementValueRegister));
                    }

                    return;
                }

                var postfixTargetArrayRegister = ResolveReceiverExpression(elementAssignment.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(
                    IrOpCode.StoreElement,
                    postfixElementValueRegister,
                    new IrArrayTarget(postfixTargetArrayRegister!, postfixTargetIndexRegister)));
                if (destination.Index != postfixElementValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, postfixElementValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when ResolveProperty(assignment.Target, registerByName, currentMethod) is { } property && property.SetterMethod is not null:
                var propertyValueRegister = AllocateTemp(property.Type, registers);
                if (assignment.Expression is NewArrayExpressionSyntax propertyArray && propertyArray.LengthExpressions.Count > 1)
                {
                    arrayShapesByName[GetExpressionDisplayName(assignment.Target)] = LowerNewArrayInto(propertyValueRegister, propertyArray, registerByName, registers, instructions, currentMethod);
                }
                else
                {
                    LowerExpressionInto(assignment.Expression, propertyValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(
                    property.SetterMethod.IsStatic ? IrOpCode.Call : IrOpCode.CallVirtual,
                    destination,
                    new IrCallTarget(
                        property.SetterMethod,
                        GetExpressionDisplayName(assignment.Target),
                        [propertyValueRegister],
                        ResolvePropertyReceiver(assignment.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        !property.SetterMethod.IsStatic)));
                if (destination.Index != propertyValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, propertyValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment when ResolveStorageField(assignment.Target, registerByName, currentMethod, true) is { } field:
                var fieldValueRegister = AllocateTemp(field.Type, registers);
                if (assignment.Expression is NewArrayExpressionSyntax fieldArray && fieldArray.LengthExpressions.Count > 1)
                {
                    arrayShapesByName[GetExpressionDisplayName(assignment.Target)] = LowerNewArrayInto(fieldValueRegister, fieldArray, registerByName, registers, instructions, currentMethod);
                }
                else
                {
                    LowerExpressionInto(assignment.Expression, fieldValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(
                    field.IsStatic ? IrOpCode.StoreStaticField : IrOpCode.StoreField,
                    fieldValueRegister,
                    new IrFieldTarget(
                        field,
                        GetExpressionDisplayName(assignment.Target),
                        assignment.Target switch
                        {
                            NameExpressionSyntax assignmentTargetName => ResolveFieldReceiver(assignmentTargetName.Name, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                            MemberAccessExpressionSyntax memberAssignment => ResolveReceiverExpression(memberAssignment.Receiver, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                            _ => null
                        })));
                if (destination.Index != fieldValueRegister.Index)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Copy, destination, fieldValueRegister));
                }
                return;
            case AssignmentExpressionSyntax assignment:
                throw new InvalidOperationException($"Cannot lower assignment target '{GetExpressionDisplayName(assignment.Target)}'.");
            case BinaryExpressionSyntax binary:
                var operandType = IsComparisonOperator(binary.OperatorToken.Kind) ? TypeSymbol.Integer : destination.Type;
                var leftTemp = AllocateTemp(operandType, registers);
                var rightTemp = AllocateTemp(operandType, registers);
                LowerExpressionInto(binary.Left, leftTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                LowerExpressionInto(binary.Right, rightTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                instructions.Add(new IrInstruction(MapBinaryOp(binary.OperatorToken.Kind), destination, (leftTemp, rightTemp)));
                return;
            case CallExpressionSyntax call:
                var argumentTemps = new List<IrValue>();
                foreach (var argument in call.Arguments)
                {
                    var argumentType = SemanticFacts.InferExpressionType(argument.Expression, new Dictionary<string, TypeSymbol>(), _knownMethods, _knownFields, _knownProperties, currentMethod);
                    var temp = AllocateTemp(argumentType, registers);
                    argumentTemps.Add(temp);
                    LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                var invocation = SemanticFacts.ResolveInvocation(
                    call.Target,
                    argumentTemps.Count,
                    registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                    [],
                    _knownMethods,
                    _knownFields,
                    _knownProperties,
                    currentMethod);
                if (invocation?.Method is null)
                {
                    throw new InvalidOperationException($"Cannot lower unresolved call '{SemanticFacts.GetExpressionDisplayName(call.Target)}/{argumentTemps.Count}'.");
                }

                instructions.Add(new IrInstruction(
                    invocation.IsVirtual ? IrOpCode.CallVirtual : IrOpCode.Call,
                    destination,
                    new IrCallTarget(
                        invocation.Method,
                        SemanticFacts.GetExpressionDisplayName(call.Target),
                        argumentTemps,
                        ResolveCallReceiver(call.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        invocation.IsVirtual)));
                return;
            default:
                throw new InvalidOperationException($"Cannot lower expression kind '{expression.Kind}'.");
        }
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
            return SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownProperties, currentMethod);
        }

        return declarator.TypeName.ToDisplayString() switch
        {
            "Boolean" => TypeSymbol.Boolean,
            "Char" => TypeSymbol.Char,
            "String" => TypeSymbol.String,
            "Integer" => TypeSymbol.Integer,
            _ => new TypeSymbol(declarator.TypeName.ToDisplayString(), true)
        };
    }

    private TypeSymbol BindSyntheticTopLevelType(
        VariableDeclaratorSyntax declarator,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol currentMethod)
    {
        if (declarator.TypeName is null)
        {
            return SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownProperties, currentMethod);
        }

        return declarator.TypeName.ToDisplayString() switch
        {
            "Boolean" => TypeSymbol.Boolean,
            "Char" => TypeSymbol.Char,
            "String" => TypeSymbol.String,
            "Integer" => TypeSymbol.Integer,
            _ => new TypeSymbol(declarator.TypeName.ToDisplayString(), true)
        };
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

    private static object GetLiteralValue(LiteralExpressionSyntax literal) =>
        literal.LiteralToken.Kind switch
        {
            SyntaxKind.TrueKeyword => 1,
            SyntaxKind.FalseKeyword => 0,
            _ => literal.LiteralToken.Value ?? 0
        };

    private static bool IsComparisonOperator(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken;

    private static IrOpCode MapBinaryOp(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.PlusToken => IrOpCode.Add,
            SyntaxKind.MinusToken => IrOpCode.Subtract,
            SyntaxKind.StarToken => IrOpCode.Multiply,
            SyntaxKind.SlashToken => IrOpCode.Divide,
            SyntaxKind.EqualsToken => IrOpCode.CompareEqual,
            SyntaxKind.NotEqualsToken => IrOpCode.CompareNotEqual,
            SyntaxKind.LessToken => IrOpCode.CompareLess,
            SyntaxKind.LessOrEqualsToken => IrOpCode.CompareLessOrEqual,
            SyntaxKind.GreaterToken => IrOpCode.CompareGreater,
            SyntaxKind.GreaterOrEqualsToken => IrOpCode.CompareGreaterOrEqual,
            _ => IrOpCode.Add
        };

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
                _knownProperties,
                currentMethod);
            if (memberResolution.Property is not null)
            {
                return forWrite ? memberResolution.Property.WriteField : memberResolution.Property.ReadField;
            }

            return memberResolution.Field;
        }

        if (expression is not NameExpressionSyntax nameExpression)
        {
            return null;
        }

        var name = nameExpression.Name;
        var locals = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        var property = SemanticFacts.ResolvePropertyReference(name, locals, _knownFields, _knownProperties, currentMethod);
        if (property is not null)
        {
            return forWrite ? property.WriteField : property.ReadField;
        }

        return SemanticFacts.ResolveName(
            name,
            locals,
            [],
            _knownMethods,
            _knownFields,
            _knownProperties,
            currentMethod).Field;
    }

    private PropertySymbol? ResolveProperty(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        expression switch
        {
            NameExpressionSyntax nameExpression => SemanticFacts.ResolvePropertyReference(
                nameExpression.Name,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownFields,
                _knownProperties,
                currentMethod)
            ,
            MemberAccessExpressionSyntax memberAccess => SemanticFacts.ResolveMemberAccess(
                memberAccess,
                registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
                _knownMethods,
                _knownFields,
                _knownProperties,
                currentMethod).Property,
            _ => null
        };

    private PropertySymbol? ResolveIndexerProperty(ExpressionSyntax target, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod) =>
        SemanticFacts.ResolveIndexerReference(
            target,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            _knownMethods,
            _knownFields,
            _knownProperties,
            currentMethod);

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
            _knownProperties,
            currentMethod);
        var temp = AllocateTemp(receiverType, registers);
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
                _knownProperties,
                currentMethod) is not null)
        {
            return implicitSelf;
        }

        var receiverType = SemanticFacts.ResolveName(
            receiverName,
            registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal),
            [],
            _knownMethods,
            _knownFields,
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
}
