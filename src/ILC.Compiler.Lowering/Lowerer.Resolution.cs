namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private MethodSymbol? ResolveMethod(string name, int argumentCount, MethodSymbol? currentMethod)
        => SemanticFacts.ResolveMethod(name, argumentCount, _knownMethods, currentMethod, _knownTypes);

    private FieldSymbol? ResolveStorageField(ExpressionSyntax expression, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod, bool forWrite)
    {
        using var profile = Profile("ResolveStorageField");
        if (expression is MemberAccessExpressionSyntax memberAccess)
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
            if (memberResolution.Property is not null)
            {
                return forWrite ? memberResolution.Property.WriteField : memberResolution.Property.ReadField;
            }

            if (TryFlattenMemberAccess(memberAccess) is { } qualifiedName)
            {
                var qualifiedProperty = SemanticFacts.ResolvePropertyReference(
                    qualifiedName,
                    GetLocalTypes(registerByName),
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
        var locals = GetLocalTypes(registerByName);
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

    private BoundWriteTarget? ResolveBoundWriteTarget(ExpressionSyntax target, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod)
    {
        using var profile = Profile("ResolveBoundWriteTarget");
        var locals = GetLocalTypes(registerByName);
        if (TryResolveSimpleFieldWriteTarget(target, locals, currentMethod, out var simpleTarget))
        {
            return simpleTarget;
        }

        return Profiled(
            "ResolveBoundWriteTarget.BindWriteTarget",
            () => SemanticFacts.BindWriteTarget(
                target,
                locals,
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));
    }

    private bool TryResolveSimpleFieldWriteTarget(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        MethodSymbol? currentMethod,
        out BoundWriteTarget? boundTarget)
    {
        using var profile = Profile("ResolveBoundWriteTarget.SimpleField");
        boundTarget = null;
        if (target is not NameExpressionSyntax { Name.Parts.Count: 1 } nameExpression ||
            currentMethod?.DeclaringTypeName is null ||
            currentMethod.IsStatic)
        {
            return false;
        }

        var displayName = nameExpression.Name.ToDisplayString();
        if (locals.ContainsKey(displayName) ||
            ResolveKnownTypeReference(currentMethod.DeclaringTypeName) is not NamedTypeSymbol declaringType)
        {
            return false;
        }

        foreach (var type in EnumerateTypeHierarchy(declaringType))
        {
            var writableProperty = type.Properties.FirstOrDefault(property =>
                property.Name == displayName &&
                !property.IsStatic &&
                (property.WriteField is not null || property.SetterMethod is not null));
            if (writableProperty is not null)
            {
                return false;
            }

            var field = type.Fields.FirstOrDefault(field => !field.IsStatic && field.Name == displayName);
            if (field is not null)
            {
                boundTarget = new BoundWriteTarget(
                    BoundWriteTargetKind.Field,
                    displayName,
                    field.Type,
                    new BoundReceiver(BoundReceiverKind.Self, new TypeSymbol(currentMethod.DeclaringTypeName, true), LocalName: "self"),
                    field,
                    null,
                    null,
                    null,
                    target);
                return true;
            }
        }

        return false;
    }

    private BoundCall? ResolveBoundCall(CallExpressionSyntax call, Dictionary<string, IrValue> registerByName, MethodSymbol? currentMethod)
    {
        using var profile = Profile("ResolveBoundCall");
        var boundCall = Profiled(
            "ResolveBoundCall.BindCall",
            () => SemanticFacts.BindCall(
                call,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                name => Profile($"BindCall.{name}")));
        if (boundCall is not null)
        {
            return boundCall;
        }

        var fallbackInvocation = Profiled(
            "ResolveBoundCall.ResolveInvocationForLowering",
            () => ResolveInvocationForLowering(call, registerByName, currentMethod));
        if (fallbackInvocation is { Method: not null })
        {
            using var createFallbackProfile = Profile("ResolveBoundCall.CreateFallbackBoundCall");
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
        var locals = GetLocalTypes(registerByName);
        var displayName = SemanticFacts.GetExpressionDisplayName(call.Target);
        var receiverType = call.Target is MemberAccessExpressionSyntax memberAccessTarget
            ? SemanticFacts.InferExpressionType(memberAccessTarget.Receiver, locals, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes)
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
        var qualifiedTargetInfo = flattenedTarget is null
            ? null
            : GetQualifiedTargetDebugInfo(flattenedTarget);

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
            $"qualifiedTargetInfo={qualifiedTargetInfo ?? "<null>"}, " +
            $"receiverCandidates=[{candidateSummary}], " +
            $"locals=[{localSummary}]";

        string? GetQualifiedTargetDebugInfo(QualifiedNameSyntax qualifiedTarget)
        {
            if (qualifiedTarget.Parts.Count < 2)
            {
                return null;
            }

            var declaringTypeName = string.Join(".", qualifiedTarget.Parts.Take(qualifiedTarget.Parts.Count - 1).Select(part => part.Text));
            var targetType = SemanticFacts.ResolveTypeReference(declaringTypeName, _knownTypes);
            var namedTargetType = targetType as NamedTypeSymbol;
            var typeMethodSummary = namedTargetType is null
                ? "<none>"
                : string.Join(
                    ", ",
                    namedTargetType.Methods
                        .Where(method => method.Name == qualifiedTarget.Parts[^1].Text)
                        .Select(method => $"{method.DeclaringTypeName}.{method.Name}/{method.Parameters.Count}[static={method.IsStatic}]"));
            var knownMethodSummary = string.Join(
                ", ",
                _knownMethods
                    .Where(method =>
                        method.DeclaringTypeName == (targetType?.Name ?? declaringTypeName) &&
                        method.Name == qualifiedTarget.Parts[^1].Text)
                    .Select(method => $"{method.DeclaringTypeName}.{method.Name}/{method.Parameters.Count}[static={method.IsStatic}]"));
            var relatedTypes = string.Join(
                ", ",
                _knownTypes
                    .OfType<NamedTypeSymbol>()
                    .Where(type => type.Name == qualifiedTarget.Parts[0].Text)
                    .Select(type => $"{type.Name}[arity={type.GenericArity}]"));

            return
                $"declaringTypeName={declaringTypeName}; " +
                $"resolvedType={(targetType?.Name ?? "<null>")}; " +
                $"typeMethods=[{typeMethodSummary}]; " +
                $"knownMethods=[{knownMethodSummary}]; " +
                $"relatedTypes=[{relatedTypes}]";
        }
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
        Profiled(
            "ResolveBoundRead",
            () => SemanticFacts.BindRead(
                expression,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));

    private BoundElementRead? ResolveBoundElementRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        Profiled(
            "ResolveBoundElementRead",
            () => SemanticFacts.BindElementRead(
                target,
                indexExpressions,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));

    private BoundElementWrite? ResolveBoundElementWrite(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        Profiled(
            "ResolveBoundElementWrite",
            () => SemanticFacts.BindElementWrite(
                target,
                indexExpressions,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));

    private BoundSliceRead? ResolveBoundSliceRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        Profiled(
            "ResolveBoundSliceRead",
            () => SemanticFacts.BindSliceRead(
                target,
                indexExpressions,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));

    private BoundLengthRead? ResolveBoundLengthRead(
        ExpressionSyntax expression,
        Dictionary<string, IrValue> registerByName,
        MethodSymbol? currentMethod) =>
        Profiled(
            "ResolveBoundLengthRead",
            () => SemanticFacts.BindLengthRead(
                expression,
                GetLocalTypes(registerByName),
                _knownTypes,
                _knownMethods,
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod));

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

        if (target.TargetExpression is not null &&
            target.TargetExpression is not NameExpressionSyntax &&
            target.IndexedType is not null)
        {
            var temp = AllocateTemp(target.IndexedType, registers);
            LowerExpressionInto(target.TargetExpression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return temp;
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

        if (target.TargetExpression is MemberAccessExpressionSyntax memberAccessTarget)
        {
            return ResolvePropertyReceiver(memberAccessTarget, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        if (target.TargetExpression is NameExpressionSyntax qualifiedNameTarget &&
            qualifiedNameTarget.Name.Parts.Count > 1)
        {
            var receiverName = qualifiedNameTarget.Name.Parts[0].Text;
            if (receiverName == "self" || registerByName.ContainsKey(receiverName))
            {
                return ResolvePropertyReceiver(qualifiedNameTarget, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            }
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

        if (target.TargetExpression is not null &&
            target.TargetExpression is not NameExpressionSyntax &&
            target.IndexedType is not null)
        {
            var temp = AllocateTemp(target.IndexedType, registers);
            LowerExpressionInto(target.TargetExpression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return temp;
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

        if (target.TargetExpression is MemberAccessExpressionSyntax memberAccessTarget)
        {
            return ResolvePropertyReceiver(memberAccessTarget, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        }

        if (target.TargetExpression is NameExpressionSyntax qualifiedNameTarget &&
            qualifiedNameTarget.Name.Parts.Count > 1)
        {
            var receiverName = qualifiedNameTarget.Name.Parts[0].Text;
            if (receiverName == "self" || registerByName.ContainsKey(receiverName))
            {
                return ResolvePropertyReceiver(qualifiedNameTarget, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            }
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
            var temp = AllocateTemp(receiver.Type, registers);
            LowerExpressionInto(receiver.SourceExpression, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return temp;
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
        var indexRegister = LowerBoundElementIndexArgument(target.IndexerProperty?.IndexParameter?.Type, target.IndexedType, target.TargetExpression, indexExpressions, registerByName, arrayShapesByName, registers, instructions, currentMethod);

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
        var indexRegister = LowerBoundElementIndexArgument(target.IndexerProperty?.IndexParameter?.Type, target.IndexedType, target.TargetExpression, indexExpressions, registerByName, arrayShapesByName, registers, instructions, currentMethod);

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

    private IrValue LowerBoundElementIndexArgument(
        TypeSymbol? indexParameterType,
        TypeSymbol? indexedType,
        ExpressionSyntax? targetExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        if (indexExpressions.Count == 1 && indexParameterType is not null)
        {
            var indexRegister = AllocateTemp(indexParameterType, registers);
            LowerExpressionInto(indexExpressions[0], indexRegister, registerByName, arrayShapesByName, registers, instructions, currentMethod);
            return indexRegister;
        }

        var elementOwnerType = indexedType ?? TypeSymbol.Integer;
        return targetExpression switch
        {
            NameExpressionSyntax nameExpression => LowerFlattenedElementIndex(nameExpression.Name, indexExpressions, elementOwnerType, registerByName, arrayShapesByName, registers, instructions, currentMethod),
            _ => LowerFlattenedElementIndex(targetExpression!, indexExpressions, elementOwnerType, registerByName, arrayShapesByName, registers, instructions, currentMethod)
        };
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
                GetLocalTypes(registerByName),
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod),
            MemberAccessExpressionSyntax memberAccess => SemanticFacts.ResolveMemberAccess(
                memberAccess,
                GetLocalTypes(registerByName),
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
                GetLocalTypes(registerByName),
                _knownFields,
                _knownConstants,
                _knownProperties,
                currentMethod,
                _knownTypes)
            ,
            MemberAccessExpressionSyntax memberAccess => SemanticFacts.ResolveMemberAccess(
                    memberAccess,
                    GetLocalTypes(registerByName),
                    _knownMethods,
                    _knownFields,
                    _knownConstants,
                    _knownProperties,
                    currentMethod,
                    _knownTypes).Property
                ?? (TryFlattenMemberAccess(memberAccess) is { } qualifiedName
                    ? SemanticFacts.ResolvePropertyReference(
                        qualifiedName,
                        GetLocalTypes(registerByName),
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
        var locals = GetLocalTypes(registerByName);
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
                    var staticMethod = (targetType as NamedTypeSymbol)?.Methods.FirstOrDefault(method =>
                            method.IsStatic &&
                            method.Name == qualifiedTarget.Parts[^1].Text &&
                            SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count))
                        ?? _knownMethods.FirstOrDefault(method =>
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
                var staticMethod = (targetType as NamedTypeSymbol)?.Methods.FirstOrDefault(method =>
                        method.IsStatic &&
                        method.Name == memberAccess.MemberName.Text &&
                        SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count))
                    ?? _knownMethods.FirstOrDefault(method =>
                        method.IsStatic &&
                        method.DeclaringTypeName == targetType.Name &&
                        method.Name == memberAccess.MemberName.Text &&
                        SemanticFacts.SupportsArgumentCount(method, call.Arguments.Count));
                if (staticMethod is not null)
                {
                    return new InvocationResolution(staticMethod);
                }
            }

            var receiverType = SemanticFacts.InferExpressionType(memberAccess.Receiver, locals, _knownMethods, _knownFields, _knownConstants, _knownProperties, currentMethod, _knownTypes);
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
        registerByName.TryGetValue(target.ToDisplayString(), out var existing)
            ? existing
            : LoadIndexedTargetValue(target, registerByName, arrayShapesByName, registers, instructions, currentMethod);

    private IrValue? LoadIndexedTargetValue(
        QualifiedNameSyntax target,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        MethodSymbol? currentMethod)
    {
        var targetType = SemanticFacts.InferExpressionType(
            new NameExpressionSyntax(target),
            GetLocalTypes(registerByName),
            _knownMethods,
            _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod,
            _knownTypes);
        var temp = AllocateTemp(targetType, registers);
        LowerNameReferenceInto(target, temp, registerByName, arrayShapesByName, registers, instructions, currentMethod);
        return temp;
    }

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
            GetLocalTypes(registerByName),
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
                GetLocalTypes(registerByName),
                _knownFields,
            _knownConstants,
            _knownProperties,
            currentMethod) is not null)
        {
            return implicitSelf;
        }

        var receiverType = SemanticFacts.ResolveName(
            receiverName,
            GetLocalTypes(registerByName),
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
}
