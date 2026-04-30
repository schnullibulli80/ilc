namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System;

public static partial class SemanticFacts
{
    public static BoundWriteTarget? BindWriteTarget(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        switch (target)
        {
            case NameExpressionSyntax nameExpression:
            {
                var name = nameExpression.Name;
                var displayName = name.ToDisplayString();
                if (name.Parts.Count == 1 && locals.TryGetValue(displayName, out var localType))
                {
                    return new BoundWriteTarget(
                        BoundWriteTargetKind.Local,
                        displayName,
                        localType,
                        null,
                        null,
                        null,
                        null,
                        null,
                        target);
                }

                var property = ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (property is not null && (property.WriteField is not null || property.SetterMethod is not null))
                {
                    return new BoundWriteTarget(
                        BoundWriteTargetKind.Property,
                        displayName,
                        property.Type,
                        property.IsStatic
                            ? null
                            : BindQualifiedValueReceiver(name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        null,
                        property,
                        property.SetterMethod,
                        property.WriteField,
                        target);
                }

                var resolution = ResolveName(name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (resolution.Field is not null)
                {
                    return new BoundWriteTarget(
                        BoundWriteTargetKind.Field,
                        displayName,
                        resolution.Field.Type,
                        resolution.Field.IsStatic
                            ? null
                            : BindQualifiedValueReceiver(name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        resolution.Field,
                        null,
                        null,
                        null,
                        target);
                }

                return null;
            }
            case MemberAccessExpressionSyntax memberAccess:
            {
                var displayName = GetExpressionDisplayName(memberAccess);
                var memberResolution = ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Property is not null && (memberResolution.Property.WriteField is not null || memberResolution.Property.SetterMethod is not null))
                {
                    return new BoundWriteTarget(
                        BoundWriteTargetKind.Property,
                        displayName,
                        memberResolution.Property.Type,
                        BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        null,
                        memberResolution.Property,
                        memberResolution.Property.SetterMethod,
                        memberResolution.Property.WriteField,
                        target);
                }

                if (memberResolution.Field is not null)
                {
                    return new BoundWriteTarget(
                        BoundWriteTargetKind.Field,
                        displayName,
                        memberResolution.Field.Type,
                        BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        memberResolution.Field,
                        null,
                        null,
                        null,
                        target);
                }

                return null;
            }
            case ElementAccessExpressionSyntax or PostfixElementAccessExpressionSyntax:
                return new BoundWriteTarget(
                    BoundWriteTargetKind.ElementAccess,
                    GetExpressionDisplayName(target),
                    InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                    null,
                    null,
                    null,
                    null,
                    null,
                    target);
            default:
                return null;
        }
    }

    public static BoundMemberRead? BindRead(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        switch (expression)
        {
            case NameExpressionSyntax nameExpression:
            {
                var displayName = nameExpression.Name.ToDisplayString();
                if (locals.TryGetValue(displayName, out var localType))
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Local,
                        displayName,
                        localType,
                        new BoundReceiver(BoundReceiverKind.Local, localType, LocalName: displayName, SourceExpression: expression),
                        SourceExpression: expression);
                }

                var property = ResolvePropertyReference(nameExpression.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (property is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Property,
                        displayName,
                        property.Type,
                        property.IsStatic
                            ? null
                            : BindQualifiedValueReceiver(nameExpression.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        null,
                        property,
                        property.GetterMethod,
                        property.ReadField,
                        null,
                        expression);
                }

                var constant = ResolveConstantReference(nameExpression.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (constant is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Constant,
                        displayName,
                        constant.Type,
                        null,
                        null,
                        null,
                        null,
                        null,
                        constant,
                        expression);
                }

                var resolvedName = ResolveName(nameExpression.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (resolvedName.Field is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Field,
                        displayName,
                        resolvedName.Field.Type,
                        resolvedName.Field.IsStatic
                            ? null
                            : BindQualifiedValueReceiver(nameExpression.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        resolvedName.Field,
                        null,
                        null,
                        null,
                        null,
                        expression);
                }

                return null;
            }
            case MemberAccessExpressionSyntax memberAccess:
            {
                var memberResolution = ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Property is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Property,
                        memberResolution.DisplayName,
                        memberResolution.Property.Type,
                        memberResolution.Property.IsStatic
                            ? null
                            : BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        null,
                        memberResolution.Property,
                        memberResolution.Property.GetterMethod,
                        memberResolution.Property.ReadField,
                        null,
                        expression);
                }

                if (memberResolution.Field is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Field,
                        memberResolution.DisplayName,
                        memberResolution.Field.Type,
                        memberResolution.Field.IsStatic
                            ? null
                            : BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        memberResolution.Field,
                        null,
                        null,
                        null,
                        null,
                        expression);
                }

                if (memberResolution.Constant is not null)
                {
                    return new BoundMemberRead(
                        BoundMemberReadKind.Constant,
                        memberResolution.DisplayName,
                        memberResolution.Constant.Type,
                        null,
                        null,
                        null,
                        null,
                        null,
                        memberResolution.Constant,
                        expression);
                }

                var qualifiedTarget = TryFlattenQualifiedTarget(memberAccess);
                if (qualifiedTarget is not null)
                {
                    var displayName = qualifiedTarget.ToDisplayString();
                    var qualifiedProperty = ResolvePropertyReference(qualifiedTarget, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (qualifiedProperty is not null)
                    {
                        return new BoundMemberRead(
                            BoundMemberReadKind.Property,
                            displayName,
                            qualifiedProperty.Type,
                            qualifiedProperty.IsStatic
                                ? null
                                : BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                            null,
                            qualifiedProperty,
                            qualifiedProperty.GetterMethod,
                            qualifiedProperty.ReadField,
                            null,
                            expression);
                    }

                    var qualifiedConstant = ResolveConstantReference(qualifiedTarget, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (qualifiedConstant is not null)
                    {
                        return new BoundMemberRead(
                            BoundMemberReadKind.Constant,
                            displayName,
                            qualifiedConstant.Type,
                            null,
                            null,
                            null,
                            null,
                            null,
                            qualifiedConstant,
                            expression);
                    }

                    var qualifiedResolution = ResolveName(qualifiedTarget, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (qualifiedResolution.Field is not null)
                    {
                        return new BoundMemberRead(
                            BoundMemberReadKind.Field,
                            displayName,
                            qualifiedResolution.Field.Type,
                            qualifiedResolution.Field.IsStatic
                                ? null
                                : BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                            qualifiedResolution.Field,
                            null,
                            null,
                            null,
                            null,
                            expression);
                    }
                }

                return null;
            }
            default:
                return null;
        }
    }

    public static BoundCall? BindCall(
        CallExpressionSyntax call,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        Func<string, IDisposable>? profiler = null)
    {
        InvocationResolution? invocation;
        using (profiler?.Invoke("ResolveInvocation"))
        {
            invocation = ResolveInvocation(
                call.Target,
                call.Arguments.Count,
                locals,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                profiler is null ? null : name => profiler($"ResolveInvocation.{name}"));
        }

        if (invocation?.Method is not null)
        {
            using var createProfile = profiler?.Invoke("CreateBoundCall");
            return CreateBoundCall(call, invocation, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, profiler);
        }

        QualifiedNameSyntax? qualifiedTarget;
        using (profiler?.Invoke("FlattenQualifiedTarget"))
        {
            qualifiedTarget = TryFlattenQualifiedTarget(call.Target);
        }

        if (qualifiedTarget is null)
        {
            return null;
        }

        using (profiler?.Invoke("ResolveFlattenedInvocation"))
        {
            invocation = ResolveInvocation(
                qualifiedTarget,
                call.Arguments.Count,
                locals,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                profiler is null ? null : name => profiler($"ResolveFlattenedInvocation.{name}"));
        }

        if (invocation?.Method is not null)
        {
            using var createProfile = profiler?.Invoke("CreateFlattenedBoundCall");
            return CreateBoundCall(call, invocation, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, profiler);
        }

        return null;
    }

    public static BoundElementRead? BindElementRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (IsSliceAccess(indexExpressions))
        {
            return null;
        }

        var indexedType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var indexer = ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer?.GetterMethod is not null)
        {
            return new BoundElementRead(
                $"{GetExpressionDisplayName(target)}[{indexer.IndexParameter?.Name ?? "index"}]",
                indexer.Type,
                indexer.GetterMethod.IsStatic
                    ? null
                    : BindIndexedOwnerReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                indexer,
                indexer.GetterMethod,
                null,
                indexedType,
                target);
        }

        if (indexer?.ReadField is not null)
        {
            return new BoundElementRead(
                $"{GetExpressionDisplayName(target)}[{indexer.IndexParameter?.Name ?? "index"}]",
                indexer.Type,
                indexer.ReadField.IsStatic
                    ? null
                    : BindIndexedOwnerReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                indexer,
                null,
                indexer.ReadField,
                indexedType,
                target);
        }

        if (IsIndexableType(indexedType))
        {
            return new BoundElementRead(
                $"{GetExpressionDisplayName(target)}[...]",
                GetElementType(indexedType) ?? indexedType,
                BindReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                null,
                null,
                null,
                indexedType,
                target);
        }

        return null;
    }

    public static BoundSliceRead? BindSliceRead(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (!IsSliceAccess(indexExpressions) || indexExpressions[0] is not RangeExpressionSyntax range)
        {
            return null;
        }

        var targetType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (!HasLengthProperty(targetType))
        {
            return null;
        }

        return new BoundSliceRead(
            $"{GetExpressionDisplayName(target)}[..]",
            targetType,
            BindReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            target,
            range);
    }

    public static BoundElementWrite? BindElementWrite(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (IsSliceAccess(indexExpressions))
        {
            return null;
        }

        var indexedType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var indexer = ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer?.SetterMethod is not null)
        {
            return new BoundElementWrite(
                $"{GetExpressionDisplayName(target)}[{indexer.IndexParameter?.Name ?? "index"}]",
                indexer.Type,
                indexer.SetterMethod.IsStatic
                    ? null
                    : BindIndexedOwnerReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                indexer,
                indexer.SetterMethod,
                null,
                indexedType,
                target);
        }

        if (indexer?.WriteField is not null)
        {
            return new BoundElementWrite(
                $"{GetExpressionDisplayName(target)}[{indexer.IndexParameter?.Name ?? "index"}]",
                indexer.Type,
                indexer.WriteField.IsStatic
                    ? null
                    : BindIndexedOwnerReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                indexer,
                null,
                indexer.WriteField,
                indexedType,
                target);
        }

        if (IsIndexableType(indexedType))
        {
            return new BoundElementWrite(
                $"{GetExpressionDisplayName(target)}[...]",
                GetElementType(indexedType) ?? indexedType,
                BindReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                null,
                null,
                null,
                indexedType,
                target);
        }

        return null;
    }

    public static BoundLengthRead? BindLengthRead(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        switch (expression)
        {
            case ArrayLengthExpressionSyntax arrayLength:
            {
                var targetExpression = new NameExpressionSyntax(arrayLength.Target);
                var targetType = InferExpressionType(targetExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (!HasLengthProperty(targetType))
                {
                    return null;
                }

                return new BoundLengthRead(
                    $"{arrayLength.Target.ToDisplayString()}.Length",
                    targetType,
                    BindReceiver(targetExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                    targetExpression);
            }
            case MemberAccessExpressionSyntax memberAccess when memberAccess.MemberName.Text == "Length":
            {
                var targetType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (!HasLengthProperty(targetType))
                {
                    targetType = memberAccess.Receiver switch
                    {
                        ElementAccessExpressionSyntax elementAccess => GetIndexedElementType(
                            elementAccess.Target,
                            elementAccess.IndexExpressions,
                            locals,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            currentMethod,
                            knownTypes),
                        PostfixElementAccessExpressionSyntax postfixElementAccess => GetIndexedElementType(
                            postfixElementAccess.Target,
                            postfixElementAccess.IndexExpressions,
                            locals,
                            knownMethods,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            currentMethod,
                            knownTypes),
                        _ => targetType
                    };
                }

                if (!HasLengthProperty(targetType) &&
                    BindRead(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) is { } boundReceiverRead)
                {
                    targetType = boundReceiverRead.Type;
                }

                if (!HasLengthProperty(targetType) &&
                    memberAccess.Receiver switch
                    {
                        ElementAccessExpressionSyntax elementAccess => BindElementRead(
                            new NameExpressionSyntax(elementAccess.Target),
                            elementAccess.IndexExpressions,
                            locals,
                            knownTypes,
                            knownMethods,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            currentMethod),
                        PostfixElementAccessExpressionSyntax postfixElementAccess => BindElementRead(
                            postfixElementAccess.Target,
                            postfixElementAccess.IndexExpressions,
                            locals,
                            knownTypes,
                            knownMethods,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            currentMethod),
                        _ => null
                    } is { } boundElementRead)
                {
                    targetType = boundElementRead.ElementType;
                }

                if (!HasLengthProperty(targetType))
                {
                    return null;
                }

                return new BoundLengthRead(
                    $"{GetExpressionDisplayName(memberAccess.Receiver)}.Length",
                    targetType,
                    BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                    memberAccess.Receiver);
            }
            default:
                return null;
        }
    }

    private static BoundCall CreateBoundCall(
        CallExpressionSyntax call,
        InvocationResolution invocation,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        Func<string, IDisposable>? profiler = null)
    {
        MethodSymbol normalizedMethod;
        using (profiler?.Invoke("CreateBoundCall.NormalizeMethod"))
        {
            normalizedMethod = NormalizeBoundInvocationMethod(invocation.Method, knownTypes);
        }

        invocation = invocation with { Method = normalizedMethod };

        bool isDirectDelegateInvoke;
        using (profiler?.Invoke("CreateBoundCall.DetectDelegateInvoke"))
        {
            var isExplicitInvokeMemberAccess =
                call.Target is MemberAccessExpressionSyntax { MemberName.Text: "Invoke" };
            isDirectDelegateInvoke =
                !isExplicitInvokeMemberAccess &&
                invocation.Method.Name == "Invoke" &&
                !invocation.Method.IsStatic &&
                invocation.Method.DeclaringTypeName is not null &&
                ResolveTypeReference(invocation.Method.DeclaringTypeName, knownTypes) is NamedTypeSymbol { IsDelegate: true };
        }

        BoundReceiver? receiver;
        using (profiler?.Invoke("CreateBoundCall.BindReceiver"))
        {
            receiver = invocation.Method.IsStatic
                ? null
                : isDirectDelegateInvoke
                    ? BindKnownReceiver(call.Target, invocation.ReceiverType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    : call.Target switch
                {
                    MemberAccessExpressionSyntax memberAccess => BindKnownReceiver(memberAccess.Receiver, invocation.ReceiverType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                    NameExpressionSyntax nameExpression when nameExpression.Name.Parts.Count > 1 => BindReceiver(
                        new QualifiedNameSyntax(nameExpression.Name.Parts.Take(nameExpression.Name.Parts.Count - 1).ToArray()),
                        locals,
                        knownFields,
                        knownConstants,
                        knownProperties,
                        currentMethod,
                        knownTypes),
                    _ => currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic
                        ? new BoundReceiver(BoundReceiverKind.Self, new TypeSymbol(currentMethod.DeclaringTypeName, true), LocalName: "self")
                        : null
                };
        }

        var kind = invocation.Method.IsConstructor
            ? BoundCallKind.Constructor
            : invocation.Method.IsSynthetic && invocation.Method.HostImportKind != HostImportKind.None
                ? BoundCallKind.Intrinsic
                : invocation.IsVirtual
                    ? BoundCallKind.Virtual
                    : BoundCallKind.Direct;

        return new BoundCall(
            kind,
            GetExpressionDisplayName(call.Target),
            invocation.Method,
            invocation.Method.ReturnType,
            receiver,
            SourceExpression: call);
    }

    private static BoundReceiver? BindKnownReceiver(
        ExpressionSyntax receiver,
        TypeSymbol? receiverType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (receiver is NameExpressionSyntax nameExpression && nameExpression.Name.Parts.Count == 1)
        {
            var displayName = nameExpression.Name.ToDisplayString();
            if (displayName == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
            {
                return new BoundReceiver(BoundReceiverKind.Self, new TypeSymbol(currentMethod.DeclaringTypeName, true), LocalName: "self");
            }

            if (locals.TryGetValue(displayName, out var localType))
            {
                return new BoundReceiver(BoundReceiverKind.Local, localType, LocalName: displayName);
            }

            if (ResolveTypeReference(displayName, knownTypes) is { } targetType)
            {
                return new BoundReceiver(BoundReceiverKind.Type, targetType, TargetType: targetType);
            }
        }

        if (receiverType is not null)
        {
            return new BoundReceiver(BoundReceiverKind.Expression, receiverType, SourceExpression: receiver);
        }

        return BindReceiver(receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    private static MethodSymbol NormalizeBoundInvocationMethod(MethodSymbol method, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (string.IsNullOrWhiteSpace(method.DeclaringTypeName))
        {
            return method;
        }

        static int CountOpenGenericMarkers(TypeSymbol type) =>
            type.Name.Contains("<T", StringComparison.Ordinal) ||
            type.Name.Contains(", T", StringComparison.Ordinal) ||
            type.Name.EndsWith("<T>", StringComparison.Ordinal)
                ? 1
                : 0;

        var openGenericMarkerCount =
            CountOpenGenericMarkers(method.ReturnType) +
            method.Parameters.Sum(parameter => CountOpenGenericMarkers(parameter.Type));
        if (openGenericMarkerCount == 0)
        {
            return method;
        }

        if (ResolveTypeReference(method.DeclaringTypeName, knownTypes) is not NamedTypeSymbol declaringType)
        {
            return method;
        }

        var normalized = declaringType.Methods
            .Where(candidate =>
                candidate.Name == method.Name &&
                candidate.IsStatic == method.IsStatic &&
                candidate.IsConstructor == method.IsConstructor &&
                candidate.Parameters.Count == method.Parameters.Count)
            .OrderBy(candidate => candidate.Parameters.Sum(parameter => CountOpenGenericMarkers(parameter.Type)) + CountOpenGenericMarkers(candidate.ReturnType))
            .FirstOrDefault();

        return normalized ?? method;
    }

    private static BoundReceiver? BindReceiver(
        ExpressionSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var receiverType = InferExpressionType(receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        return receiver switch
        {
            NameExpressionSyntax nameExpression => BindReceiver(nameExpression.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
            _ => new BoundReceiver(BoundReceiverKind.Expression, receiverType, SourceExpression: receiver)
        };
    }

    private static BoundReceiver? BindImplicitReceiver(MethodSymbol? currentMethod) =>
        currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic
            ? new BoundReceiver(BoundReceiverKind.Self, new TypeSymbol(currentMethod.DeclaringTypeName, true), LocalName: "self")
            : null;

    private static BoundReceiver? BindIndexedOwnerReceiver(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (target is NameExpressionSyntax targetName &&
            targetName.Name.Parts.Count == 1 &&
            ResolvePropertyReference(targetName.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { IndexParameter: not null } targetIndexerProperty)
        {
            var currentDeclaringTypeName = currentMethod?.DeclaringTypeName;
            if (currentDeclaringTypeName is not null &&
                targetIndexerProperty.DeclaringTypeName == currentDeclaringTypeName)
            {
                return BindImplicitReceiver(currentMethod);
            }
        }

        var targetValueType = target switch
        {
            NameExpressionSyntax nameExpression => TryResolveValueReferenceType(nameExpression.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
            _ => null
        };
        if (targetValueType is not null)
        {
            return new BoundReceiver(BoundReceiverKind.Expression, targetValueType, SourceExpression: target);
        }

        if (BindRead(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) is { Receiver: not null } boundRead)
        {
            return boundRead.Receiver;
        }

        return target switch
        {
            MemberAccessExpressionSyntax memberAccess => BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            NameExpressionSyntax nameExpression => BindQualifiedValueReceiver(nameExpression.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            _ => BindReceiver(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
        };
    }

    private static BoundReceiver? BindQualifiedValueReceiver(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (name.Parts.Count <= 1)
        {
            return BindImplicitReceiver(currentMethod);
        }

        var receiverName = new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray());
        return BindReceiver(receiverName, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            ?? new BoundReceiver(
                BoundReceiverKind.Expression,
                InferExpressionType(new NameExpressionSyntax(receiverName), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                SourceExpression: new NameExpressionSyntax(receiverName));
    }

    private static BoundReceiver? BindReceiver(
        QualifiedNameSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var displayName = receiver.ToDisplayString();
        if (receiver.Parts.Count == 1)
        {
            if (displayName == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
            {
                return new BoundReceiver(BoundReceiverKind.Self, new TypeSymbol(currentMethod.DeclaringTypeName, true), LocalName: "self");
            }

            if (locals.TryGetValue(displayName, out var localType))
            {
                return new BoundReceiver(BoundReceiverKind.Local, localType, LocalName: displayName);
            }

            if (ResolveTypeReference(displayName, knownTypes) is { } targetType)
            {
                return new BoundReceiver(BoundReceiverKind.Type, targetType, TargetType: targetType);
            }
        }

        if (TryResolveValueReferenceType(receiver, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { } valueType)
        {
            return new BoundReceiver(BoundReceiverKind.Expression, valueType, SourceExpression: new NameExpressionSyntax(receiver));
        }

        if (ResolveTypeReference(displayName, knownTypes) is { } qualifiedTargetType)
        {
            return new BoundReceiver(BoundReceiverKind.Type, qualifiedTargetType, TargetType: qualifiedTargetType);
        }

        return null;
    }

    private static QualifiedNameSyntax? TryFlattenQualifiedTarget(ExpressionSyntax expression)
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

}
