namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private void LowerExpressionInto(
        ExpressionSyntax expression,
        IrValue? destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod = null)
    {
        using var profile = Profile($"LowerExpressionInto:{expression.Kind}");
        if (destination is null)
        {
            return;
        }

        if (TryLowerDelegateMethodGroupInto(expression, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
        {
            return;
        }

        if (TryLowerDelegateLambdaInto(expression, destination, registerByName, registers, instructions, currentMethod))
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
            case ProjectorExpressionSyntax projectorExpression:
                var projectorType = SemanticFacts.ResolveProjectorType(
                    projectorExpression,
                    GetLocalTypes(registerByName),
                    _knownTypes,
                    _knownMethods,
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod);

                if (projectorType is null)
                {
                    throw new InvalidOperationException("Cannot lower unresolved projector expression.");
                }

                var projectorConstructor = projectorType.Methods.FirstOrDefault(method =>
                    method.IsConstructor &&
                    method.DeclaringTypeName == projectorType.Name &&
                    method.Parameters.Count == projectorExpression.Members.Count);
                var projectorArgs = new List<IrValue>();
                for (var memberIndex = 0; memberIndex < projectorExpression.Members.Count; memberIndex++)
                {
                    var member = projectorExpression.Members[memberIndex];
                    var expectedMemberType = projectorConstructor is not null && memberIndex < projectorConstructor.Parameters.Count
                        ? projectorConstructor.Parameters[memberIndex].Type
                        : SemanticFacts.InferExpressionType(
                            member.Expression,
                            GetLocalTypes(registerByName),
                            _knownMethods,
                            _knownFields,
                            _knownConstants,
                            _knownProperties,
                            currentMethod,
                            _knownTypes);
                    var temp = AllocateTemp(expectedMemberType, registers);
                    projectorArgs.Add(temp);
                    LowerExpressionInto(member.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                instructions.Add(new IrInstruction(IrOpCode.NewObject, destination, projectorType.Name));
                if (projectorConstructor is not null)
                {
                    instructions.Add(new IrInstruction(
                        IrOpCode.CallVirtual,
                        destination,
                        new IrCallTarget(projectorConstructor, $"{projectorType.Name}.{projectorConstructor.Name}", projectorArgs, destination, true)));
                }

                return;
            case NewExpressionSyntax newExpression:
                TypeSymbol? syntaxConstructedObjectType;
                using (Profile("NewExpression.ResolveSyntaxType"))
                {
                    syntaxConstructedObjectType = TryCloseTypeReferenceForCurrentMethod(
                        new TypeSymbol(newExpression.TypeName.ToDisplayString(), true),
                        currentMethod,
                        _knownTypes)
                        ?? TryCloseTypeReferenceByDisplayNameForCurrentMethod(newExpression.TypeName.ToDisplayString(), currentMethod, _knownTypes);
                }

                TypeSymbol? inferredConstructedObjectType = null;
                TypeSymbol constructedObjectType;
                if (syntaxConstructedObjectType is not null)
                {
                    constructedObjectType = syntaxConstructedObjectType;
                }
                else
                {
                    using (Profile("NewExpression.InferConstructedType"))
                    {
                        var inferredType = SemanticFacts.InferExpressionType(
                            newExpression,
                            GetLocalTypes(registerByName),
                            _knownMethods,
                            _knownFields,
                            _knownConstants,
                            _knownProperties,
                            currentMethod,
                            _knownTypes);
                        inferredConstructedObjectType = inferredType;
                        constructedObjectType = TryCloseTypeReferenceForCurrentMethod(inferredType, currentMethod, _knownTypes)
                            ?? inferredType;
                    }
                }

                TypeSymbol resolvedConstructedObjectType;
                using (Profile("NewExpression.ResolveConstructedType"))
                {
                    resolvedConstructedObjectType = SemanticFacts.ResolveTypeReference(constructedObjectType.Name, _knownTypes) ?? constructedObjectType;
                }

                MethodSymbol? constructor;
                using (Profile("NewExpression.ResolveConstructor"))
                {
                    constructor = resolvedConstructedObjectType is NamedTypeSymbol namedConstructedType
                        ? namedConstructedType.Methods.FirstOrDefault(method =>
                            method.IsConstructor &&
                            method.DeclaringTypeName == resolvedConstructedObjectType.Name &&
                            method.Parameters.Count == newExpression.Arguments.Count)
                        : SemanticFacts.ResolveConstructor(newExpression.TypeName, newExpression.Arguments.Count, _knownTypes, _knownMethods);
                }

                using (Profile("NewExpression.CloseConstructor"))
                {
                    constructor = TryCloseMethodForCurrentMethod(constructor, currentMethod, _knownTypes);
                }

                TypeSymbol effectiveConstructedObjectType;
                using (Profile("NewExpression.ResolveEffectiveType"))
                {
                    effectiveConstructedObjectType = !string.IsNullOrWhiteSpace(constructor?.DeclaringTypeName)
                        ? SemanticFacts.ResolveTypeReference(constructor.DeclaringTypeName!, _knownTypes) ?? new TypeSymbol(constructor.DeclaringTypeName!, true)
                        : constructedObjectType;
                }

                var constructorArgumentTypes = new List<TypeSymbol>();
                var constructorArgs = new List<IrValue>();
                for (var argumentIndex = 0; argumentIndex < newExpression.Arguments.Count; argumentIndex++)
                {
                    var argument = newExpression.Arguments[argumentIndex];
                    var expectedArgumentType = constructor is not null && argumentIndex < constructor.Parameters.Count
                        ? constructor.Parameters[argumentIndex].Type
                        : null;
                    var argumentType = expectedArgumentType;
                    if (argumentType is null)
                    {
                        using (Profile("NewExpression.InferArgumentType"))
                        {
                            argumentType = SemanticFacts.InferExpressionType(
                                argument.Expression,
                                GetLocalTypes(registerByName),
                                _knownMethods,
                                _knownFields,
                                _knownConstants,
                                _knownProperties,
                                currentMethod,
                                _knownTypes);
                        }
                    }

                    constructorArgumentTypes.Add(argumentType);
                    var temp = AllocateTemp(argumentType, registers);
                    constructorArgs.Add(temp);
                    using (Profile("NewExpression.LowerArgumentExpression"))
                    {
                        LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    }
                }

                (TypeSymbol ConstructedType, MethodSymbol Constructor)? resolvedConstruction = null;
                if (constructor is null || constructor.DeclaringTypeName != effectiveConstructedObjectType.Name)
                {
                    using (Profile("NewExpression.ResolveByArgumentTypes"))
                    {
                        resolvedConstruction = TryResolveConstructedTypeByArgumentTypes(newExpression.TypeName.ToDisplayString(), constructorArgumentTypes, _knownTypes);
                    }
                }

                if (resolvedConstruction is { } construction)
                {
                    effectiveConstructedObjectType = construction.ConstructedType;
                    constructor = construction.Constructor;
                }

                if (IsEnumerablePipelineConstruction(currentMethod, newExpression) &&
                    ContainsOpenGenericPlaceholder(effectiveConstructedObjectType))
                {
                    throw new InvalidOperationException(
                        "Enumerable pipeline construction remained open after specialization: " +
                        $"method='{currentMethod?.DeclaringTypeName}.{currentMethod?.Name}', " +
                        $"syntax='{newExpression.TypeName.ToDisplayString()}', " +
                        $"syntaxClosed='{syntaxConstructedObjectType?.Name ?? "<null>"}', " +
                        $"inferred='{inferredConstructedObjectType?.Name ?? "<skipped>"}', " +
                        $"constructed='{constructedObjectType.Name}', " +
                        $"resolved='{resolvedConstructedObjectType.Name}', " +
                        $"effective='{effectiveConstructedObjectType.Name}', " +
                        $"constructor='{constructor?.DeclaringTypeName ?? "<null>"}.{constructor?.Name ?? "<null>"}', " +
                        $"ctorArgs=[{string.Join(", ", constructorArgumentTypes.Select(type => type.Name))}]");
                }

                using (Profile("NewExpression.ValidateConstructedType"))
                {
                    ValidateConstructedObjectType(newExpression, effectiveConstructedObjectType, currentMethod);
                }
                instructions.Add(new IrInstruction(IrOpCode.NewObject, destination, effectiveConstructedObjectType.Name));
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
            case ArrayLengthExpressionSyntax lengthExpression:
            {
                var targetExpression = new NameExpressionSyntax(lengthExpression.Target);
                var lengthLocalTypes = GetLocalTypes(registerByName);
                var targetType = SemanticFacts.InferExpressionType(targetExpression, lengthLocalTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
                if (SemanticFacts.HasLengthProperty(targetType))
                {
                    var receiverRegister = AllocateTemp(targetType, registers);
                    LowerExpressionInto(targetExpression, receiverRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                    instructions.Add(new IrInstruction(IrOpCode.LoadLength, destination, new IrArrayTarget(receiverRegister)));
                    return;
                }

                break;
            }
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
                var localTypes = GetLocalTypes(registerByName);
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

                if (TryLowerStringConcatenationChain(binary, destination, localTypes, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                TypeSymbol leftType;
                TypeSymbol rightType;
                using (Profile("BinaryExpression.ResolveOperandTypes"))
                {
                    leftType = GetComparisonOperandType(binary.Left, localTypes, currentMethod);
                    rightType = GetComparisonOperandType(binary.Right, localTypes, currentMethod);
                }

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
                using (Profile("BinaryExpression.LowerLeft"))
                {
                    LowerExpressionInto(binary.Left, leftTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

                using (Profile("BinaryExpression.LowerRight"))
                {
                    LowerExpressionInto(binary.Right, rightTemp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }

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
            case QueryExpressionSyntax query:
                var queryLocalTypes = GetLocalTypes(registerByName);
                ExpressionSyntax? translatedQuery;
                using (Profile("TranslateQueryExpression"))
                {
                    if (!SemanticFacts.TryTranslateQueryExpression(
                            query,
                            queryLocalTypes,
                            _knownTypes,
                            _knownMethods,
                            _knownFields,
                            _knownConstants,
                            _knownProperties,
                            currentMethod,
                            out translatedQuery))
                    {
                        throw new InvalidOperationException(
                            $"Cannot lower query expression because source '{SemanticFacts.GetExpressionDisplayName(query.SourceExpression)}' is not enumerable.");
                    }
                }

                if (translatedQuery is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot lower query expression because source '{SemanticFacts.GetExpressionDisplayName(query.SourceExpression)}' is not enumerable.");
                }

                LowerExpressionInto(translatedQuery, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                return;
            case CallExpressionSyntax call:
                var boundCall = ResolveBoundCall(call, registerByName, currentMethod);
                if (boundCall?.Method is null)
                {
                    throw new InvalidOperationException(BuildCallDiagnosticMessage(call, registerByName, currentMethod));
                }

                var invocation = new InvocationResolution(boundCall.Method, boundCall.Receiver?.Type, boundCall.Kind == BoundCallKind.Virtual);

                if (TryLowerTryParseIntrinsicCall(call, invocation, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                if (TryLowerConvertTryToIntegerCall(call, invocation, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                if (TryLowerDictionaryTryGetValueCall(call, boundCall, invocation, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                var preparedCallFrame = PrepareCallFrame(
                    call,
                    boundCall,
                    invocation.Method,
                    registerByName,
                    arrayShapesByName,
                    registers,
                    instructions,
                    currentMethod);

                if (TryLowerStringIntrinsicCall(boundCall, invocation, call.Target, preparedCallFrame.Arguments, destination, registerByName, arrayShapesByName, registers, instructions, currentMethod))
                {
                    return;
                }

                instructions.Add(new IrInstruction(
                    boundCall.Kind == BoundCallKind.Virtual ? IrOpCode.CallVirtual : IrOpCode.Call,
                    destination,
                    new IrCallTarget(
                        boundCall.Method,
                        boundCall.DisplayName,
                        preparedCallFrame.Arguments,
                        preparedCallFrame.Receiver ?? ResolveBoundCallReceiver(boundCall, call.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod),
                        boundCall.Kind == BoundCallKind.Virtual)));

                foreach (var (target, source) in preparedCallFrame.CopyBacks)
                {
                    StoreValueIntoTarget(target, source, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }
                return;
            default:
                throw new InvalidOperationException(
                    $"Cannot lower expression kind '{expression.Kind}' display='{GetExpressionDisplayName(expression)}' currentMethod='{(currentMethod?.DeclaringTypeName is null ? currentMethod?.Name : $"{currentMethod.DeclaringTypeName}.{currentMethod.Name}")}'.");
        }
    }

    private bool TryLowerDelegateMethodGroupInto(
        ExpressionSyntax expression,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var delegateType = SemanticFacts.ResolveTypeReference(destination.Type.Name, _knownTypes) as NamedTypeSymbol;
        if (delegateType is not { IsDelegate: true })
        {
            return false;
        }

        MethodSymbol? targetMethod = null;
        IrValue? targetObject = null;

        switch (expression)
        {
            case NameExpressionSyntax name:
            {
                var resolution = SemanticFacts.ResolveName(
                    name.Name,
                    GetLocalTypes(registerByName),
                    _knownTypes,
                    _knownMethods,
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod);
                if (resolution.Kind != NameResolutionKind.MethodGroup || resolution.Method is null)
                {
                    return false;
                }

                targetMethod = resolution.Method;
                break;
            }
            case MemberAccessExpressionSyntax memberAccess:
            {
                var memberResolution = SemanticFacts.ResolveMemberAccess(
                    memberAccess,
                    GetLocalTypes(registerByName),
                    _knownMethods,
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod,
                    _knownTypes);
                if (memberResolution.Method is null)
                {
                    return false;
                }

                targetMethod = memberResolution.Method;
                if (!targetMethod.IsStatic)
                {
                    targetObject = AllocateTemp(memberAccess.Receiver is NameExpressionSyntax receiverName && registerByName.TryGetValue(receiverName.Name.ToDisplayString(), out var knownReceiver)
                        ? knownReceiver.Type
                        : SemanticFacts.InferExpressionType(
                            memberAccess.Receiver,
                            GetLocalTypes(registerByName),
                            _knownMethods,
                            _knownFields,
                            _knownConstants,
                            _knownProperties,
                            currentMethod,
                            _knownTypes),
                        registers);
                    LowerExpressionInto(memberAccess.Receiver, targetObject, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                }
                break;
            }
            default:
                return false;
        }

        var constructor = delegateType.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.Count == 2);
        if (targetMethod is null || constructor is null)
        {
            if (currentMethod?.DeclaringTypeName == "Program" && currentMethod.Name == "Main")
            {
                var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
                var lambdaCandidates = _knownMethods
                    .Where(method => method.LambdaSource is not null)
                    .Select(method => $"{method.DeclaringTypeName}.{method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}")
                    .ToArray();
                throw new InvalidOperationException(
                    "Failed to lower delegate target in Program.Main: " +
                    $"delegate='{delegateType.Name}', " +
                    $"invoke='{invokeMethod?.DeclaringTypeName ?? delegateType.Name}.{invokeMethod?.Name ?? "<null>"}', " +
                    $"candidates=[{string.Join(" | ", lambdaCandidates)}]");
            }

            return false;
        }

        instructions.Add(new IrInstruction(IrOpCode.NewObject, destination, delegateType.Name));

        var boundTargetObject = targetObject ?? AllocateTemp(TypeSymbol.Object, registers);
        if (targetObject is null)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, boundTargetObject, 0));
        }

        var methodIdRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, methodIdRegister, targetMethod));

        instructions.Add(new IrInstruction(
            IrOpCode.CallVirtual,
            destination,
            new IrCallTarget(
                constructor,
                $"{delegateType.Name}.{constructor.Name}",
                [boundTargetObject, methodIdRegister],
                destination,
                true)));

        return true;
    }

    private bool TryLowerDelegateLambdaInto(
        ExpressionSyntax expression,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        var delegateType = SemanticFacts.ResolveTypeReference(destination.Type.Name, _knownTypes) as NamedTypeSymbol;
        if (delegateType is not { IsDelegate: true })
        {
            return false;
        }

        static bool TypesMatch(TypeSymbol left, TypeSymbol right) =>
            ReferenceEquals(left, right) ||
            string.Equals(left.Name, right.Name, StringComparison.Ordinal);

        var targetMethod = _knownMethods.FirstOrDefault(method => method.LambdaSource is not null && method.LambdaSource.Equals(lambda));
        if (targetMethod is null)
        {
            var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
            if (invokeMethod is not null)
            {
                var matchingLambdaMethods = _knownMethods
                    .Where(method =>
                        method.LambdaSource is not null &&
                        method.Parameters.Count == invokeMethod.Parameters.Count &&
                        TypesMatch(method.ReturnType, invokeMethod.ReturnType))
                    .Where(method => method.Parameters.Zip(invokeMethod.Parameters, (left, right) => TypesMatch(left.Type, right.Type)).All(matches => matches))
                    .ToArray();
                var bodyDisplayName = SemanticFacts.GetExpressionDisplayName(lambda.Body);
                var exactBodyMatches = matchingLambdaMethods
                    .Where(method => method.LambdaSource is not null && method.LambdaSource.Body.Equals(lambda.Body))
                    .ToArray();
                if (exactBodyMatches.Length > 0)
                {
                    targetMethod = exactBodyMatches[0];
                }
                else
                {
                    exactBodyMatches = matchingLambdaMethods
                        .Where(method => method.LambdaSource is not null && SemanticFacts.GetExpressionDisplayName(method.LambdaSource.Body) == bodyDisplayName)
                        .ToArray();
                    if (exactBodyMatches.Length > 0)
                    {
                        targetMethod = exactBodyMatches[0];
                    }
                }

                if (targetMethod is null && matchingLambdaMethods.Length > 0)
                {
                    targetMethod = matchingLambdaMethods[0];
                }
            }
        }

        var constructor = delegateType.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.Count == 2);
        if (targetMethod is null || constructor is null)
        {
            if (currentMethod?.DeclaringTypeName == "Program" &&
                currentMethod.Name == "Main")
            {
                var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
                var helperCandidates = _knownMethods
                    .Where(method => method.LambdaSource is not null)
                    .Select(method =>
                    {
                        var parameterTypes = string.Join(", ", method.Parameters.Select(parameter => parameter.Type.Name));
                        var lambdaBody = method.LambdaSource is null
                            ? "<none>"
                            : SemanticFacts.GetExpressionDisplayName(method.LambdaSource.Body);
                        return $"{method.DeclaringTypeName}.{method.Name}({parameterTypes}):{method.ReturnType.Name} static={method.IsStatic} body={lambdaBody}";
                    })
                    .ToArray();
                var invokeSignature = invokeMethod is null
                    ? "<missing>"
                    : $"({string.Join(", ", invokeMethod.Parameters.Select(parameter => parameter.Type.Name))}):{invokeMethod.ReturnType.Name}";
                throw new InvalidOperationException(
                    $"Failed to resolve delegate lambda target for delegate '{delegateType.Name}' invoke='{invokeSignature}' " +
                    $"lambdaBody='{SemanticFacts.GetExpressionDisplayName(lambda.Body)}' currentMethod='{currentMethod.DeclaringTypeName}.{currentMethod.Name}'. " +
                    $"constructorFound={(constructor is not null).ToString()} helperCandidates=[{string.Join(" | ", helperCandidates)}]");
            }

            return false;
        }

        instructions.Add(new IrInstruction(IrOpCode.NewObject, destination, delegateType.Name));

        IrValue targetObjectRegister;
        if (!targetMethod.IsStatic &&
            _knownTypes.FirstOrDefault(type => type.Name == targetMethod.DeclaringTypeName) is NamedTypeSymbol closureType &&
            closureType.Fields.Count > 0)
        {
            targetObjectRegister = AllocateTemp(closureType, registers);
            instructions.Add(new IrInstruction(IrOpCode.NewObject, targetObjectRegister, closureType.Name));
            foreach (var captureField in closureType.Fields.Where(field => !field.IsStatic))
            {
                if (!registerByName.TryGetValue(captureField.Name, out var capturedRegister))
                {
                    throw new InvalidOperationException(
                        $"Cannot lower captured lambda field '{captureField.Name}' for lambda in '{currentMethod?.DeclaringTypeName}.{currentMethod?.Name}'.");
                }

                instructions.Add(new IrInstruction(
                    IrOpCode.StoreField,
                    capturedRegister,
                    new IrFieldTarget(captureField, $"{closureType.Name}.{captureField.Name}", targetObjectRegister)));
            }
        }
        else
        {
            targetObjectRegister = AllocateTemp(TypeSymbol.Object, registers);
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, targetObjectRegister, 0));
        }

        var methodIdRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, methodIdRegister, targetMethod));

        instructions.Add(new IrInstruction(
            IrOpCode.CallVirtual,
            destination,
            new IrCallTarget(
                constructor,
                $"{delegateType.Name}.{constructor.Name}",
                [targetObjectRegister, methodIdRegister],
                destination,
                true)));

        return true;
    }

    private PreparedCallFrame PrepareCallFrame(
        CallExpressionSyntax call,
        BoundCall boundCall,
        MethodSymbol method,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        using var profile = Profile("PrepareCallFrame");
        var hasByRef = Profiled(
            "PrepareCallFrame.CheckByRef",
            () => method.Parameters.Any(parameter =>
                parameter.PassingKind == ParameterPassingKind.Out || parameter.PassingKind == ParameterPassingKind.Ref));
        if (!hasByRef)
        {
            return new PreparedCallFrame(
                Profiled(
                    "PrepareCallFrame.LowerNonByRefArguments",
                    () => LowerCallArguments(call, method, registerByName, arrayShapesByName, registers, instructions, currentMethod)),
                []);
        }

        var hasParams = method.Parameters.Count > 0 && method.Parameters[^1].PassingKind == ParameterPassingKind.Params;
        var fixedParameterCount = hasParams ? method.Parameters.Count - 1 : method.Parameters.Count;
        var evaluatedArguments = new List<IrValue>();
        var copyBacks = new List<(ExpressionSyntax Target, IrValue Source)>();

        using (Profile("PrepareCallFrame.EvaluateByRefArguments"))
        {
            for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
            {
                if (hasParams && argumentIndex >= fixedParameterCount)
                {
                    break;
                }

                var argument = call.Arguments[argumentIndex];
                var parameter = method.Parameters[argumentIndex];
                var temp = AllocateTemp(parameter.Type, registers);
                evaluatedArguments.Add(temp);

                switch (parameter.PassingKind)
                {
                    case ParameterPassingKind.Out:
                        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, temp, 0));
                        break;
                    case ParameterPassingKind.Ref:
                        using (Profile("PrepareCallFrame.LowerRefArgument"))
                        {
                            LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                        }
                        break;
                    default:
                        using (Profile("PrepareCallFrame.LowerByRefCompatibleArgument"))
                        {
                            LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
                        }
                        break;
                }
            }
        }

        if (hasParams)
        {
            evaluatedArguments.Add(Profiled(
                "PrepareCallFrame.LowerParamsArgumentArray",
                () => LowerParamsArgumentArray(
                    call,
                    method.Parameters[^1],
                    fixedParameterCount,
                    registerByName,
                    arrayShapesByName,
                    registers,
                    instructions,
                    currentMethod)));
        }

        IrValue? packedReceiver = null;
        var receiver = method.IsStatic
            ? null
            : Profiled(
                "PrepareCallFrame.ResolveReceiver",
                () => ResolveBoundCallReceiver(boundCall, call.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod));
        if (receiver is not null)
        {
            packedReceiver = AllocateTemp(receiver.Type, registers);
            instructions.Add(new IrInstruction(IrOpCode.Copy, packedReceiver, receiver));
        }

        var packedArguments = new List<IrValue>(evaluatedArguments.Count);
        using (Profile("PrepareCallFrame.PackArguments"))
        {
            for (var argumentIndex = 0; argumentIndex < evaluatedArguments.Count; argumentIndex++)
            {
                var packedArgument = AllocateTemp(evaluatedArguments[argumentIndex].Type, registers);
                instructions.Add(new IrInstruction(IrOpCode.Copy, packedArgument, evaluatedArguments[argumentIndex]));
                packedArguments.Add(packedArgument);

                if (argumentIndex < fixedParameterCount)
                {
                    var parameter = method.Parameters[argumentIndex];
                    if (parameter.PassingKind == ParameterPassingKind.Out || parameter.PassingKind == ParameterPassingKind.Ref)
                    {
                        copyBacks.Add((call.Arguments[argumentIndex].Expression, packedArgument));
                    }
                }
            }
        }

        return new PreparedCallFrame(packedArguments, copyBacks, packedReceiver);
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
        using var profile = Profile("LowerCallArguments");
        var argumentTemps = new List<IrValue>();
        var localTypes = GetLocalTypes(registerByName);
        var hasParams = method.Parameters.Count > 0 && method.Parameters[^1].PassingKind == ParameterPassingKind.Params;
        var fixedParameterCount = hasParams ? method.Parameters.Count - 1 : method.Parameters.Count;

        for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
        {
            if (hasParams && argumentIndex >= fixedParameterCount)
            {
                break;
            }

            var argument = call.Arguments[argumentIndex];
            var parameter = argumentIndex < method.Parameters.Count ? method.Parameters[argumentIndex] : null;
            if (parameter is not null &&
                (parameter.PassingKind == ParameterPassingKind.Out || parameter.PassingKind == ParameterPassingKind.Ref))
            {
                throw new InvalidOperationException(
                    $"Cannot lower by-reference call '{SemanticFacts.GetExpressionDisplayName(call.Target)}' without a dedicated intrinsic or runtime byref support.");
            }

            var argumentType = parameter?.Type ??
                Profiled(
                    "LowerCallArguments.InferArgumentType",
                    () => SemanticFacts.InferExpressionType(argument.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod));
            var temp = AllocateTemp(argumentType, registers);
            argumentTemps.Add(temp);
            try
            {
                using var lowerArgumentProfile = Profile("LowerCallArguments.LowerArgumentExpression");
                LowerExpressionInto(argument.Expression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            }
            catch (InvalidOperationException ex) when (argument.Expression is LambdaExpressionSyntax lambda)
            {
                throw new InvalidOperationException(
                    $"Failed to lower lambda call argument for target '{SemanticFacts.GetExpressionDisplayName(call.Target)}' " +
                    $"parameter='{parameter?.Name ?? "<none>"}:{parameter?.Type.Name ?? argumentType.Name}' " +
                    $"currentMethod='{(currentMethod?.DeclaringTypeName is null ? currentMethod?.Name : $"{currentMethod.DeclaringTypeName}.{currentMethod.Name}")}' " +
                    $"lambdaReturn='{lambda.ReturnType?.ToDisplayString() ?? "<void>"}' " +
                    $"lambdaBody='{SemanticFacts.GetExpressionDisplayName(lambda.Body)}'. " +
                    $"Inner: {ex.Message}",
                    ex);
            }
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
        using var profile = Profile("LowerParamsArgumentArray");
        var explicitArrayArgument = call.Arguments.Count == fixedParameterCount + 1
            ? call.Arguments[fixedParameterCount].Expression
            : null;
        if (explicitArrayArgument is not null)
        {
            var explicitArrayType = SemanticFacts.InferExpressionType(
                explicitArrayArgument,
                GetLocalTypes(registerByName),
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
            var inferredType = SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
            return TryCloseTypeReferenceForCurrentMethod(inferredType, currentMethod, _knownTypes) ?? inferredType;
        }

        var resolvedType = SemanticFacts.ResolveTypeReference(declarator.TypeName.ToDisplayString(), _knownTypes)
            ?? new TypeSymbol(declarator.TypeName.ToDisplayString(), true);
        return TryCloseTypeReferenceForCurrentMethod(resolvedType, currentMethod, _knownTypes) ?? resolvedType;
    }

    private TypeSymbol BindSyntheticTopLevelType(
        VariableDeclaratorSyntax declarator,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol currentMethod)
    {
        if (declarator.TypeName is null)
        {
            var inferredType = SemanticFacts.InferExpressionType(declarator.Initializer, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
            return TryCloseTypeReferenceForCurrentMethod(inferredType, currentMethod, _knownTypes) ?? inferredType;
        }

        var resolvedType = SemanticFacts.ResolveTypeReference(declarator.TypeName.ToDisplayString(), _knownTypes)
            ?? new TypeSymbol(declarator.TypeName.ToDisplayString(), true);
        return TryCloseTypeReferenceForCurrentMethod(resolvedType, currentMethod, _knownTypes) ?? resolvedType;
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
        var localTypes = GetLocalTypes(registerByName);
        var expressionType = SemanticFacts.InferExpressionType(typeTest.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var targetType = ResolveTypeTestTarget(typeTest.TypeName);

        if (!targetType.IsReferenceType)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 1));
            return;
        }

        if (!expressionType.IsReferenceType && expressionType != TypeSymbol.Nil)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
            return;
        }

        var valueRegister = AllocateTemp(expressionType, registers);
        LowerExpressionInto(typeTest.Expression, valueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.TypeIsReference, destination, new IrTypeCheckTarget(valueRegister, targetType.Name)));
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
        var localTypes = GetLocalTypes(registerByName);
        var expressionType = SemanticFacts.InferExpressionType(asExpression.Expression, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod);
        var targetType = ResolveTypeTestTarget(asExpression.TypeName);

        if (expressionType == TypeSymbol.Nil)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
            return;
        }

        if (!targetType.IsReferenceType)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
            return;
        }

        var valueRegister = AllocateTemp(expressionType, registers);
        LowerExpressionInto(asExpression.Expression, valueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.AsReference, destination, new IrTypeCheckTarget(valueRegister, targetType.Name)));
    }

    private TypeSymbol GetComparisonOperandType(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        MethodSymbol? currentMethod)
    {
        if (expression is NameExpressionSyntax nameExpression)
        {
            var property = SemanticFacts.ResolvePropertyReference(
                nameExpression.Name,
                localTypes,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                _knownTypes);
            if (property is not null)
            {
                return property.Type;
            }

            var nameResolution = SemanticFacts.ResolveName(
                nameExpression.Name,
                localTypes,
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod);
            if (nameResolution.Field is not null)
            {
                return nameResolution.Field.Type;
            }
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
        var localTypes = GetLocalTypes(registerByName);
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
        var localTypes = GetLocalTypes(registerByName);
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
        var localTypes = GetLocalTypes(registerByName);
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
        if (!targetType.IsReferenceType)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, failureLabel));
            return;
        }

        if (!expressionType.IsReferenceType && expressionType != TypeSymbol.Nil)
        {
            instructions.Add(new IrInstruction(IrOpCode.Branch, null, failureLabel));
            return;
        }

        var comparisonRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.TypeIsReference, comparisonRegister, new IrTypeCheckTarget(matchRegister, targetType.Name)));
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
        var localTypes = GetLocalTypes(registerByName);
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
        var localTypes = GetLocalTypes(registerByName);
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

        var localTypes = GetLocalTypes(registerByName);
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

    private bool TryLowerStringConcatenationChain(
        BinaryExpressionSyntax binary,
        IrValue destination,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (binary.OperatorToken.Kind != SyntaxKind.PlusToken || destination.Type != TypeSymbol.String)
        {
            return false;
        }

        var operands = new List<ExpressionSyntax>();
        using (Profile("StringConcatChain.CollectOperands"))
        {
            CollectStringConcatOperands(binary, operands);
        }

        if (operands.Count <= 2)
        {
            return false;
        }

        using (Profile("StringConcatChain.ValidateOperandTypes"))
        {
            foreach (var operand in operands)
            {
                var operandType = SemanticFacts.InferExpressionType(operand, localTypes, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
                if (operandType != TypeSymbol.String)
                {
                    return false;
                }
            }
        }

        using (Profile("StringConcatChain.LowerOperands"))
        {
            var accumulator = AllocateTemp(TypeSymbol.String, registers);
            LowerExpressionInto(operands[0], accumulator, registerByName, arrayShapesByName, registers, instructions, currentMethod);

            for (var index = 1; index < operands.Count; index++)
            {
                var right = AllocateTemp(TypeSymbol.String, registers);
                LowerExpressionInto(operands[index], right, registerByName, arrayShapesByName, registers, instructions, currentMethod);

                var output = index == operands.Count - 1
                    ? destination
                    : AllocateTemp(TypeSymbol.String, registers);
                instructions.Add(new IrInstruction(IrOpCode.ConcatString, output, (accumulator, right)));
                accumulator = output;
            }
        }

        return true;

        static void CollectStringConcatOperands(ExpressionSyntax expression, List<ExpressionSyntax> operands)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                CollectStringConcatOperands(parenthesized.Expression, operands);
                return;
            }

            if (expression is BinaryExpressionSyntax nested && nested.OperatorToken.Kind == SyntaxKind.PlusToken)
            {
                CollectStringConcatOperands(nested.Left, operands);
                CollectStringConcatOperands(nested.Right, operands);
                return;
            }

            operands.Add(expression);
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
        BoundCall? boundCall,
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
            var integerReceiver = boundCall is null
                ? ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
                : ResolveBoundCallReceiver(boundCall, target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
                    ?? ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
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

        var receiver = boundCall is null
            ? ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
            : ResolveBoundCallReceiver(boundCall, target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
                ?? ResolveCallReceiver(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);
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

    private bool TryLowerDictionaryTryGetValueCall(
        CallExpressionSyntax call,
        BoundCall boundCall,
        InvocationResolution invocation,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (invocation.Method.Name != "TryGetValue" ||
            invocation.Method.IsStatic ||
            invocation.Method.Parameters.Count != 2 ||
            invocation.Method.Parameters[1].PassingKind != ParameterPassingKind.Out ||
            call.Arguments.Count != 2 ||
            !IsDictionaryType(invocation.ReceiverType ?? boundCall.Receiver?.Type))
        {
            return false;
        }

        var receiver = ResolveBoundCallReceiver(boundCall, call.Target, registerByName, arrayShapesByName, registers, instructions, currentMethod)
            ?? throw new InvalidOperationException($"Cannot lower dictionary receiver for '{SemanticFacts.GetExpressionDisplayName(call.Target)}'.");
        var receiverType = invocation.ReceiverType ?? boundCall.Receiver?.Type
            ?? throw new InvalidOperationException($"Cannot resolve dictionary receiver type for '{SemanticFacts.GetExpressionDisplayName(call.Target)}'.");
        var keyType = invocation.Method.Parameters[0].Type;
        var valueType = invocation.Method.Parameters[1].Type;

        var keyRegister = AllocateTemp(keyType, registers);
        LowerExpressionInto(call.Arguments[0].Expression, keyRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);

        var indexOfKeyMethod = _knownMethods.FirstOrDefault(method =>
            method.DeclaringTypeName == receiverType.Name &&
            method.Name == "IndexOfKey" &&
            !method.IsStatic &&
            method.Parameters.Count == 1 &&
            method.Parameters[0].Type.Name == keyType.Name);
        if (indexOfKeyMethod is null)
        {
            throw new InvalidOperationException($"Cannot lower dictionary TryGetValue without '{receiverType.Name}.IndexOfKey'.");
        }

        var valuesField = _knownFields.FirstOrDefault(field =>
            field.DeclaringTypeName == receiverType.Name &&
            field.Name == "Values" &&
            !field.IsStatic);
        if (valuesField is null)
        {
            throw new InvalidOperationException($"Cannot lower dictionary TryGetValue without '{receiverType.Name}.Values'.");
        }

        var indexRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(
            IrOpCode.Call,
            indexRegister,
            new IrCallTarget(indexOfKeyMethod, $"{receiverType.Name}.{indexOfKeyMethod.Name}", [keyRegister], receiver)));

        var zeroRegister = AllocateTemp(TypeSymbol.Integer, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, zeroRegister, 0));

        var missingRegister = AllocateTemp(TypeSymbol.Boolean, registers);
        instructions.Add(new IrInstruction(IrOpCode.CompareLess, missingRegister, (indexRegister, zeroRegister)));

        var hitLabel = AllocateLabel("dict_try_get_hit");
        var endLabel = AllocateLabel("dict_try_get_end");
        instructions.Add(new IrInstruction(IrOpCode.BranchIfFalse, missingRegister, hitLabel));

        var defaultValueRegister = AllocateTemp(valueType, registers);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, defaultValueRegister, 0));
        StoreValueIntoTarget(call.Arguments[1].Expression, defaultValueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 0));
        instructions.Add(new IrInstruction(IrOpCode.Branch, null, endLabel));

        instructions.Add(new IrInstruction(IrOpCode.Label, null, hitLabel));
        var valuesRegister = AllocateTemp(valuesField.Type, registers);
        instructions.Add(new IrInstruction(
            IrOpCode.LoadField,
            valuesRegister,
            new IrFieldTarget(valuesField, $"{receiverType.Name}.{valuesField.Name}", receiver)));

        var valueRegister = AllocateTemp(valueType, registers);
        instructions.Add(new IrInstruction(
            IrOpCode.LoadElement,
            valueRegister,
            new IrArrayTarget(valuesRegister, indexRegister)));
        StoreValueIntoTarget(call.Arguments[1].Expression, valueRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        instructions.Add(new IrInstruction(IrOpCode.LoadConstant, destination, 1));
        instructions.Add(new IrInstruction(IrOpCode.Label, null, endLabel));
        return true;
    }

    private bool TryLowerConvertTryToIntegerCall(
        CallExpressionSyntax call,
        InvocationResolution invocation,
        IrValue destination,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (invocation.Method.Name != "TryToInteger" ||
            invocation.Method.DeclaringTypeName != "Convert" ||
            !invocation.Method.IsStatic ||
            invocation.Method.Parameters.Count != 2 ||
            invocation.Method.Parameters[1].PassingKind != ParameterPassingKind.Out ||
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

    private static bool IsDictionaryType(TypeSymbol? type) =>
        type is NamedTypeSymbol namedType
            ? namedType.Name == "Dictionary" || namedType.GenericDefinition?.Name == "Dictionary"
            : type?.Name == "Dictionary" || (type?.Name?.StartsWith("Dictionary<", StringComparison.Ordinal) ?? false);

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
        var localTypes = GetLocalTypes(registerByName);
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
}
