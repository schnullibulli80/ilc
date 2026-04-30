namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private bool LowerStatement(
        StatementSyntax statement,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        using var profile = Profile($"LowerStatement:{statement.Kind}");
        var vmIpStart = instructions.Count;
        var terminated = false;
        switch (statement)
        {
            case BlockStatementSyntax block:
                foreach (var nestedStatement in block.Statements)
                {
                    if (LowerStatement(nestedStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod))
                    {
                        return true;
                    }
                }
                terminated = false;
                break;
            case LocalVariableDeclarationStatementSyntax localVariable:
                foreach (var declarator in localVariable.Declarators)
                {
                    TypeSymbol localType;
                    using (Profile("LocalVariableDeclaration.BindLocalType"))
                    {
                        localType = BindLocalType(declarator, localTypes, currentMethod);
                    }

                    var localRegister = new IrValue($"r{registers.Count}", localType, (ushort)registers.Count);
                    registers.Add(localRegister);
                    SetRegisterLocalType(registerByName, declarator.Identifier.Text, localRegister);
                    localTypes[declarator.Identifier.Text] = localType;
                    debugVariables.Add(new DebugVariableBuilder(declarator.Identifier.Text, localType, localRegister.Index, instructions.Count, IrDebugVariableKind.Local));

                    if (declarator.Initializer is NewArrayExpressionSyntax newArrayInitializer && newArrayInitializer.LengthExpressions.Count > 1)
                    {
                        arrayShapesByName[declarator.Identifier.Text] = LowerNewArrayInto(localRegister, newArrayInitializer, registerByName, registers, instructions, currentMethod);
                    }
                    else if (declarator.Initializer is not null)
                    {
                        using (Profile("LocalVariableDeclaration.LowerInitializer"))
                        {
                            LowerExpressionInto(declarator.Initializer, localRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                        }
                    }
                }
                terminated = false;
                break;
            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression is not null)
                {
                    LowerExpressionInto(returnStatement.Expression, returnRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
                terminated = true;
                break;
            case BreakStatementSyntax:
                if (_loopLabels.Count == 0)
                {
                    throw new InvalidOperationException("Cannot lower break outside a loop.");
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, _loopLabels.Peek().BreakLabel));
                terminated = true;
                break;
            case ContinueStatementSyntax:
                if (_loopLabels.Count == 0)
                {
                    throw new InvalidOperationException("Cannot lower continue outside a loop.");
                }

                instructions.Add(new IrInstruction(IrOpCode.Branch, null, _loopLabels.Peek().ContinueLabel));
                terminated = true;
                break;
            case RaiseStatementSyntax raiseStatement:
                LowerRaiseStatement(raiseStatement, registerByName, arrayShapesByName, registers, instructions, inExceptionHandler, currentMethod);
                terminated = true;
                break;
            case ExpressionStatementSyntax expressionStatement:
                LowerExpressionStatement(expressionStatement.Expression, registerByName, localTypes, arrayShapesByName, registers, instructions, currentMethod);
                terminated = false;
                break;
            case IncStatementSyntax incStatement:
                LowerIncDecStatement(incStatement.Target, SyntaxKind.PlusAssignToken, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                terminated = false;
                break;
            case DecStatementSyntax decStatement:
                LowerIncDecStatement(decStatement.Target, SyntaxKind.MinusAssignToken, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                terminated = false;
                break;
            case IncludeStatementSyntax includeStatement:
                LowerIncludeExcludeStatement(includeStatement.Target, includeStatement.Value, true, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                terminated = false;
                break;
            case ExcludeStatementSyntax excludeStatement:
                LowerIncludeExcludeStatement(excludeStatement.Target, excludeStatement.Value, false, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                terminated = false;
                break;
            case IfStatementSyntax ifStatement:
                terminated = LowerIfStatement(ifStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                break;
            case WhileStatementSyntax whileStatement:
                LowerWhileStatement(whileStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                terminated = false;
                break;
            case RepeatStatementSyntax repeatStatement:
                LowerRepeatStatement(repeatStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                terminated = false;
                break;
            case ForStatementSyntax forStatement:
                LowerForStatement(forStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                terminated = false;
                break;
            case ForeachStatementSyntax foreachStatement:
                LowerForeachStatement(foreachStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                terminated = false;
                break;
            case WithStatementSyntax withStatement:
                var rewrittenWithBody = RewriteWithStatement(withStatement.Body, withStatement.Receiver, registerByName, localTypes, currentMethod);
                return LowerStatement(rewrittenWithBody, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
            case CaseStatementSyntax caseStatement:
                terminated = LowerCaseStatement(caseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                break;
            case MatchStatementSyntax matchStatement:
                terminated = LowerMatchStatement(matchStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                break;
            case TryStatementSyntax tryStatement:
                terminated = LowerTryStatement(tryStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
                break;
            default:
                terminated = false;
                break;
        }

        if (statement is not BlockStatementSyntax)
        {
            using (Profile("AddDebugSourceMap"))
            {
                AddDebugSourceMap(debugSourceMaps, statement, vmIpStart, instructions.Count - 1);
            }
        }

        return terminated;
    }

    private void AddDebugSourceMap(List<IrDebugSourceMap> debugSourceMaps, SyntaxNode? node, int vmIpStart, int vmIpEnd)
    {
        if (node is null || vmIpEnd < vmIpStart)
        {
            return;
        }

        var span = GetSyntaxSpan(node);
        if (span is null || span.Value.Length < 0)
        {
            return;
        }

        debugSourceMaps.Add(new IrDebugSourceMap(span.Value, vmIpStart, vmIpEnd));
    }

    private TextSpan? GetSyntaxSpan(SyntaxNode node)
    {
        if (_syntaxSpanCache.TryGetValue(node, out var cachedSpan))
        {
            return cachedSpan;
        }

        var span = TryGetSyntaxSpan(node);
        _syntaxSpanCache[node] = span;
        return span;
    }

    private static TextSpan? TryGetSyntaxSpan(SyntaxNode node)
    {
        var minStart = int.MaxValue;
        var maxEnd = int.MinValue;
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        VisitSyntaxValue(node, visited, ref minStart, ref maxEnd);
        if (minStart == int.MaxValue || maxEnd < minStart)
        {
            return null;
        }

        return new TextSpan(minStart, maxEnd - minStart);
    }

    private static void VisitSyntaxValue(object? value, HashSet<object> visited, ref int minStart, ref int maxEnd)
    {
        if (value is null || value is string)
        {
            return;
        }

        if (value is SyntaxToken token)
        {
            minStart = Math.Min(minStart, token.Span.Start);
            maxEnd = Math.Max(maxEnd, token.Span.End);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                VisitSyntaxValue(item, visited, ref minStart, ref maxEnd);
            }

            return;
        }

        if (value is not SyntaxNode node || !visited.Add(node))
        {
            return;
        }

        foreach (var property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            VisitSyntaxValue(property.GetValue(node), visited, ref minStart, ref maxEnd);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        LowerExpressionInto(ifStatement.Condition, conditionRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var elseLabel = AllocateLabel("else");
        var endLabel = AllocateLabel("endif");

        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, elseLabel));
        var thenTerminates = LowerStatement(ifStatement.ThenStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);

        if (ifStatement.ElseStatement is not null)
        {
            if (!thenTerminates)
            {
                instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));
            }

            instructions.Add(new IrInstruction(IrOpCode.Label, null, elseLabel));
            var elseTerminates = LowerStatement(ifStatement.ElseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
        LowerStatement(whileStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
            if (LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod))
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
            SetRegisterLocalType(registerByName, forStatement.Identifier.Text, loopRegister);
            localTypes[forStatement.Identifier.Text] = TypeSymbol.Integer;
            debugVariables.Add(new DebugVariableBuilder(forStatement.Identifier.Text, TypeSymbol.Integer, loopRegister.Index, instructions.Count, IrDebugVariableKind.Local));
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
        LowerStatement(forStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
            RemoveRegisterLocalType(registerByName, forStatement.Identifier.Text);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
                GetLocalTypes(registerByName),
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                _knownTypes);
            var itemType = collectionTypeForDeclaration == TypeSymbol.String
                ? TypeSymbol.Char
                : SemanticFacts.GetElementType(collectionTypeForDeclaration)
                    ?? SemanticFacts.GetSetElementType(collectionTypeForDeclaration)
                    ?? SemanticFacts.ResolveEnumerablePattern(collectionTypeForDeclaration, _knownTypes)?.ElementType
                    ?? throw new InvalidOperationException($"Expression '{SemanticFacts.GetExpressionDisplayName(foreachStatement.Collection)}' is not enumerable.");
            itemRegister = new IrValue($"r{registers.Count}", itemType, (ushort)registers.Count);
            registers.Add(itemRegister);
            SetRegisterLocalType(registerByName, foreachStatement.Identifier.Text, itemRegister);
            localTypes[foreachStatement.Identifier.Text] = itemType;
            debugVariables.Add(new DebugVariableBuilder(foreachStatement.Identifier.Text, itemType, itemRegister.Index, instructions.Count, IrDebugVariableKind.Local));
            createdItemRegister = true;
        }

        var collectionType = SemanticFacts.InferExpressionType(
            foreachStatement.Collection,
            GetLocalTypes(registerByName),
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod,
            _knownTypes);
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
                debugVariables,
                debugSourceMaps,
                inExceptionHandler,
                currentMethod);

            if (createdItemRegister)
            {
                RemoveRegisterLocalType(registerByName, foreachStatement.Identifier.Text);
                localTypes.Remove(foreachStatement.Identifier.Text);
            }

            return;
        }

        if (SemanticFacts.ResolveEnumerablePattern(collectionType, _knownTypes) is { } enumerablePattern)
        {
            LowerForeachEnumerableStatement(
                foreachStatement,
                itemRegister,
                collectionRegister,
                enumerablePattern,
                registerByName,
                localTypes,
                arrayShapesByName,
                registers,
                instructions,
                returnRegister,
                exceptionHandlers,
                debugVariables,
                debugSourceMaps,
                inExceptionHandler,
                currentMethod);

            if (createdItemRegister)
            {
                RemoveRegisterLocalType(registerByName, foreachStatement.Identifier.Text);
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
        LowerStatement(foreachStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
        var oneRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, oneRegister, 1));
        instructions.Add(new IrInstruction(IrOpCode.Add, indexRegister, (indexRegister, oneRegister)));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, loopLabel));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));

        if (createdItemRegister)
        {
            RemoveRegisterLocalType(registerByName, foreachStatement.Identifier.Text);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
        LowerStatement(foreachStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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

    private void LowerForeachEnumerableStatement(
        ForeachStatementSyntax foreachStatement,
        IrValue itemRegister,
        IrValue collectionRegister,
        EnumerablePatternResolution enumerablePattern,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
        bool inExceptionHandler,
        MethodSymbol? currentMethod)
    {
        var enumeratorRegister = AllocateTemp(enumerablePattern.EnumeratorType, registers);
        instructions.Add(new IrInstruction(
            enumerablePattern.GetEnumeratorMethod.IsVirtual || collectionRegister.Type.Name.StartsWith("IEnumerable", StringComparison.Ordinal)
                ? IrOpCode.CallVirtual
                : IrOpCode.Call,
            enumeratorRegister,
            new IrCallTarget(
                enumerablePattern.GetEnumeratorMethod,
                $"{enumerablePattern.GetEnumeratorMethod.DeclaringTypeName}.{enumerablePattern.GetEnumeratorMethod.Name}",
                [],
                collectionRegister,
                enumerablePattern.GetEnumeratorMethod.IsVirtual || collectionRegister.Type.Name.StartsWith("IEnumerable", StringComparison.Ordinal))));

        var loopLabel = AllocateLabel("foreach_enumerable");
        var endLabel = AllocateLabel("endforeach_enumerable");
        var continueLabel = AllocateLabel("foreach_enumerable_continue");
        instructions.Add(new IrInstruction(IrOpCode.Label, null, loopLabel));

        var conditionRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(
            enumerablePattern.MoveNextMethod.IsVirtual || enumeratorRegister.Type.Name.StartsWith("IEnumerator", StringComparison.Ordinal)
                ? IrOpCode.CallVirtual
                : IrOpCode.Call,
            conditionRegister,
            new IrCallTarget(
                enumerablePattern.MoveNextMethod,
                $"{enumerablePattern.MoveNextMethod.DeclaringTypeName}.{enumerablePattern.MoveNextMethod.Name}",
                [],
                enumeratorRegister,
                enumerablePattern.MoveNextMethod.IsVirtual || enumeratorRegister.Type.Name.StartsWith("IEnumerator", StringComparison.Ordinal))));
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, conditionRegister, endLabel));

        instructions.Add(new IrInstruction(
            enumerablePattern.CurrentGetterMethod.IsVirtual || enumeratorRegister.Type.Name.StartsWith("IEnumerator", StringComparison.Ordinal)
                ? IrOpCode.CallVirtual
                : IrOpCode.Call,
            itemRegister,
            new IrCallTarget(
                enumerablePattern.CurrentGetterMethod,
                $"{enumerablePattern.CurrentGetterMethod.DeclaringTypeName}.{enumerablePattern.CurrentGetterMethod.Name}",
                [],
                enumeratorRegister,
                enumerablePattern.CurrentGetterMethod.IsVirtual || enumeratorRegister.Type.Name.StartsWith("IEnumerator", StringComparison.Ordinal))));

        _loopLabels.Push((endLabel, continueLabel));
        LowerStatement(foreachStatement.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
        _loopLabels.Pop();

        instructions.Add(new IrInstruction(IrOpCode.Label, null, continueLabel));
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
            var clauseTerminates = LowerStatement(caseStatement.Clauses[clauseIndex].Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
                elseTerminates = LowerStatement(elseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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
            var armTerminates = LowerStatement(matchStatement.Arms[armIndex].Body, armRegisterScopes[armIndex], armLocalScopes[armIndex], arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
                elseTerminates = LowerStatement(elseStatement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
            GetLocalTypes(registerByName),
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
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
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

            return LowerTryStatement(outerTry, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
            tryTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
                SetRegisterLocalType(registerByName, clause.Identifier.Text, clauseRegister);
                localTypes[clause.Identifier.Text] = clauseType;

                instructions.Add(new IrInstruction(IrOpCode.Label, null, clauseStartLabel));
                var clauseTerminates = LowerStatement(clause.Body, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, true, currentMethod);
                instructions.Add(new IrInstruction(IrOpCode.Label, null, clauseEndLabel));
                if (!clauseTerminates)
                {
                    instructions.Add(new IrInstruction(IrOpCode.Branch, null, afterTryLabel));
                    allHandlerPathsTerminate = false;
                }

                exceptionHandlers.Add(new IrExceptionHandler(tryStartLabel, tryEndLabel, clauseStartLabel, clauseEndLabel, clauseRegister.Index, clauseType.Name));

                if (hadPrevious)
                {
                    SetRegisterLocalType(registerByName, clause.Identifier.Text, previousValue!);
                    localTypes[clause.Identifier.Text] = previousValue!.Type;
                }
                else
                {
                    RemoveRegisterLocalType(registerByName, clause.Identifier.Text);
                    localTypes.Remove(clause.Identifier.Text);
                }
            }

            if (tryStatement.ExceptStatements.Count > 0)
            {
                instructions.Add(new IrInstruction(IrOpCode.Label, null, handlerStartLabel));
                var catchAllTerminates = false;
                foreach (var statement in tryStatement.ExceptStatements)
                {
                    catchAllTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, true, currentMethod);
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
            handlerTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, handlerIsExceptionHandler, currentMethod);
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
                    finallyNormalTerminates = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, false, currentMethod);
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
}
