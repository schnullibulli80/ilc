namespace ILC.Compiler.Bytecode;

using ILC.Compiler.Binding;
using ILC.Compiler.Lowering;
using System.Diagnostics;

public enum OpCode : byte
{
    Nop = 0x00,
    LdI32 = 0x02,
    LdStr = 0x03,
    Mov = 0x09,
    AddI32 = 0x10,
    ShlI32 = 0x17,
    ShrI32 = 0x54,
    AndI32 = 0x14,
    OrI32 = 0x15,
    NotI32 = 0x16,
    SubI32 = 0x11,
    MulI32 = 0x12,
    DivI32 = 0x13,
    ModI32 = 0x55,
    CmpEqI32 = 0x18,
    CmpNeI32 = 0x19,
    CmpLtI32 = 0x1A,
    CmpLeI32 = 0x1B,
    CmpGtI32 = 0x1C,
    CmpGeI32 = 0x1D,
    CmpEqStr = 0x1E,
    CmpNeStr = 0x1F,
    CmpEqRef = 0x20,
    CmpNeRef = 0x21,
    IsTypeRef = 0x78,
    AsTypeRef = 0x79,
    ConcatStr = 0x22,
    StartsWithStr = 0x23,
    EndsWithStr = 0x24,
    ContainsStr = 0x25,
    IndexOfStr = 0x26,
    LastIndexOfStr = 0x27,
    ReplaceStr = 0x28,
    InsertStr = 0x29,
    RemoveStr = 0x2A,
    ToUpperStr = 0x2B,
    ToLowerStr = 0x2C,
    TrimStr = 0x2D,
    TrimStartStr = 0x2E,
    TrimEndStr = 0x2F,
    StrToI32 = 0x50,
    TryStrToI32 = 0x53,
    I32ToStr = 0x52,
    Throw = 0x80,
    Rethrow = 0x81,
    NewObj = 0x60,
    NewArr = 0x61,
    LdField = 0x36,
    StField = 0x37,
    LdSField = 0x38,
    StSField = 0x39,
    LdElem = 0x74,
    StElem = 0x75,
    LdLen = 0x76,
    SliceStr = 0x77,
    Call = 0x30,
    CallVirt = 0x51,
    Br = 0x31,
    BrFalse = 0x32,
    Ret = 0x34
}

public enum InstructionImmediateKind : byte
{
    None = 0,
    InlineInt32 = 1,
    StringTableIndex = 2,
    ConstantTableIndex = 3
}

public sealed record Instruction(
    OpCode OpCode,
    ushort Destination = 0,
    ushort Left = 0,
    ushort Right = 0,
    int Immediate = 0,
    InstructionImmediateKind ImmediateKind = InstructionImmediateKind.None);

public sealed record BytecodeArrayShapeInfo(ushort ArrayRegister, IReadOnlyList<ushort> ExtentRegisters);
public sealed record BytecodeModuleArrayShapeInfo(uint FunctionId, ushort ArrayRegister, IReadOnlyList<ushort> ExtentRegisters);
public sealed record BytecodeExceptionHandlerInfo(ushort TryStart, ushort TryEnd, ushort HandlerStart, ushort HandlerEnd, ushort TargetRegister, uint CatchTypeId);
public sealed record BytecodeModuleExceptionHandlerInfo(uint FunctionId, ushort TryStart, ushort TryEnd, ushort HandlerStart, ushort HandlerEnd, ushort TargetRegister, uint CatchTypeId);

public sealed record BytecodeDllImportMetadata(
    string LibraryName,
    string EntryPoint,
    NativeCallingConvention CallingConvention,
    NativeStringReturnMarshalling StringReturnMarshalling,
    string? StringFreeEntryPoint);

public sealed record BytecodeDebugVmIpRange(
    int IrVmIp,
    int BytecodeVmIpStart,
    int BytecodeVmIpEnd);

public sealed record BytecodeFunction(
    uint FunctionId,
    string Name,
    ushort RegisterCount,
    ushort ArgumentCount,
    HostImportKind HostImportKind,
    BytecodeDllImportMetadata? DllImport,
    IReadOnlyList<Instruction> Instructions,
    IReadOnlyList<BytecodeArrayShapeInfo> ArrayShapes,
    IReadOnlyList<BytecodeExceptionHandlerInfo> ExceptionHandlers,
    IReadOnlyList<string> StringLiterals,
    IReadOnlyList<BytecodeDebugVmIpRange> DebugVmIpRanges);

public sealed record BytecodeModule(
    IReadOnlyList<BytecodeFunction> Functions,
    IReadOnlyList<BytecodeModuleArrayShapeInfo> ArrayShapes,
    IReadOnlyList<BytecodeModuleExceptionHandlerInfo> ExceptionHandlers,
    IReadOnlyList<TypeSymbol> Types);

public sealed record ReachableCompilationClosure(
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<FieldSymbol> Fields,
    IReadOnlyList<TypeSymbol> Types,
    IReadOnlyList<PropertySymbol> Properties,
    ReachabilityStats Stats);

public sealed record ReachabilityCounter(string Name, int Count);
public sealed record ReachabilityDurationCounter(string Name, TimeSpan Elapsed, int Count);

public sealed record ReachabilityStats(
    int RootMethods,
    int DeclaredMethods,
    int DeclaredFields,
    int DeclaredProperties,
    int DeclaredTypes,
    int MethodsEnqueued,
    int RootMethodsEnqueued,
    int DirectCallMethodsEnqueued,
    int MethodConstantMethodsEnqueued,
    int PropertyAccessorMethodsEnqueued,
    int NativeCallbackMethodsEnqueued,
    int DeclaringTypeMethodsEnqueued,
    int TypeMemberMethodsEnqueued,
    int InterfaceDispatchMethodsEnqueued,
    int MethodsProcessed,
    int MethodsLowered,
    int MethodCacheHits,
    int MethodReplacements,
    int InstructionDependenciesVisited,
    int TypeReferenceCacheHits,
    int TypeReferenceCacheMisses,
    int FieldsAdded,
    int PropertiesAdded,
    int FieldsPreseeded,
    int PropertiesPreseeded,
    int TypesAddedOrReplaced,
    int TypesPreseededOrReplaced,
    int DeclaringTypeExpansions,
    int TypeMemberExpansions,
    int InterfaceDispatchExpansions,
    int LowererRebuilds,
    int LowererCacheInvalidationRequests,
    int LowererCacheInvalidations,
    int LowererFieldInvalidationRequests,
    int LowererPropertyInvalidationRequests,
    int LowererTypeInvalidationRequests,
    int LowererFieldInvalidations,
    int LowererPropertyInvalidations,
    int LowererTypeInvalidations,
    int LowererClosedGenericTypeInvalidationsSuppressed,
    TimeSpan SeedDeclaredTypesTime,
    TimeSpan PreseedTypeSurfacesTime,
    TimeSpan SeedGlobalFieldsTime,
    TimeSpan SeedGlobalPropertiesTime,
    TimeSpan SeedRootMethodsTime,
    TimeSpan LowererBuildTime,
    TimeSpan MethodLoweringTime,
    TimeSpan DependencyScanTime,
    TimeSpan HandlerScanTime,
    IReadOnlyList<ReachabilityDurationCounter> MethodLoweringDurations,
    IReadOnlyList<ReachabilityDurationCounter> DependencyScanDurations,
    IReadOnlyList<LoweringProfileEntry> LoweringProfile,
    IReadOnlyList<ReachabilityCounter> LowererFieldInvalidationNames,
    IReadOnlyList<ReachabilityCounter> LowererPropertyInvalidationNames,
    IReadOnlyList<ReachabilityCounter> LowererTypeInvalidationNames);

public static class ReachableCompilationBuilder
{
    private enum LowererInvalidationReason
    {
        Field,
        Property,
        Type
    }

    private enum MethodReachabilityReason
    {
        Root,
        DirectCall,
        MethodConstant,
        PropertyAccessor,
        NativeCallback,
        DeclaringType,
        TypeMember,
        InterfaceDispatch
    }

    public static ReachableCompilationClosure Build(
        IEnumerable<MethodSymbol> rootMethods,
        IEnumerable<MethodSymbol> methods,
        IEnumerable<FieldSymbol> fields,
        IEnumerable<TypeSymbol> types,
        IEnumerable<PropertySymbol> properties,
        IEnumerable<ConstantSymbol>? constants = null,
        LoweringProfiler? loweringProfiler = null)
    {
        var rootMethodList = rootMethods.ToArray();
        var allMethods = SymbolLists.CreateMethods(methods);
        var allFields = SymbolLists.CreateFields(fields);
        var allProperties = SymbolLists.CreateProperties(properties);
        var declaredTypes = types.ToArray();
        var constantList = SymbolLists.CreateConstants(constants ?? []);
        var methodMap = new Dictionary<string, MethodSymbol>(StringComparer.Ordinal);
        var fieldMap = new Dictionary<string, FieldSymbol>(StringComparer.Ordinal);
        var typeMap = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        var propertyMap = new Dictionary<string, PropertySymbol>(StringComparer.Ordinal);
        var expandedTypeMembers = new HashSet<string>(StringComparer.Ordinal);
        var expandedDeclaringTypeMembers = new HashSet<string>(StringComparer.Ordinal);
        var expandedInterfaceDispatchMembers = new HashSet<string>(StringComparer.Ordinal);
        var preseededTypeMembers = new HashSet<string>(StringComparer.Ordinal);
        var irCache = new Dictionary<string, IrFunction>(StringComparer.Ordinal);
        var processedMethodDependencies = new HashSet<string>(StringComparer.Ordinal);
        var pendingMethodKeys = new Queue<string>();
        IReadOnlyList<FieldSymbol>? knownFieldsCache = null;
        TypeSymbol[]? knownTypesCache = null;
        IReadOnlyList<PropertySymbol>? knownPropertiesCache = null;
        Lowerer? lowererCache = null;
        var methodsEnqueued = 0;
        var rootMethodsEnqueued = 0;
        var directCallMethodsEnqueued = 0;
        var methodConstantMethodsEnqueued = 0;
        var propertyAccessorMethodsEnqueued = 0;
        var nativeCallbackMethodsEnqueued = 0;
        var declaringTypeMethodsEnqueued = 0;
        var typeMemberMethodsEnqueued = 0;
        var interfaceDispatchMethodsEnqueued = 0;
        var methodsProcessed = 0;
        var methodsLowered = 0;
        var methodCacheHits = 0;
        var methodReplacements = 0;
        var instructionDependenciesVisited = 0;
        var typeReferenceCacheHits = 0;
        var typeReferenceCacheMisses = 0;
        var fieldsAdded = 0;
        var propertiesAdded = 0;
        var fieldsPreseeded = 0;
        var propertiesPreseeded = 0;
        var typesAddedOrReplaced = 0;
        var typesPreseededOrReplaced = 0;
        var declaringTypeExpansions = 0;
        var typeMemberExpansions = 0;
        var interfaceDispatchExpansions = 0;
        var lowererRebuilds = 0;
        var lowererCacheInvalidationRequests = 0;
        var lowererCacheInvalidations = 0;
        var lowererFieldInvalidationRequests = 0;
        var lowererPropertyInvalidationRequests = 0;
        var lowererTypeInvalidationRequests = 0;
        var lowererFieldInvalidations = 0;
        var lowererPropertyInvalidations = 0;
        var lowererTypeInvalidations = 0;
        var lowererClosedGenericTypeInvalidationsSuppressed = 0;
        var lowererFieldInvalidationNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowererPropertyInvalidationNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowererTypeInvalidationNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var methodLoweringDurations = new Dictionary<string, (TimeSpan Elapsed, int Count)>(StringComparer.Ordinal);
        var dependencyScanDurations = new Dictionary<string, (TimeSpan Elapsed, int Count)>(StringComparer.Ordinal);
        var typeReferenceCache = new Dictionary<string, TypeSymbol?>(StringComparer.Ordinal);
        TypeSymbol[]? typeResolutionCandidatesCache = null;
        var seedDeclaredTypesTime = TimeSpan.Zero;
        var preseedTypeSurfacesTime = TimeSpan.Zero;
        var seedGlobalFieldsTime = TimeSpan.Zero;
        var seedGlobalPropertiesTime = TimeSpan.Zero;
        var seedRootMethodsTime = TimeSpan.Zero;
        var lowererBuildTime = TimeSpan.Zero;
        var methodLoweringTime = TimeSpan.Zero;
        var dependencyScanTime = TimeSpan.Zero;
        var handlerScanTime = TimeSpan.Zero;

        AddElapsed(ref seedDeclaredTypesTime, () =>
        {
            foreach (var type in declaredTypes)
            {
                AddOrPreferRicherType(typeMap, type);
            }
        });

        AddElapsed(ref preseedTypeSurfacesTime, PreseedTypeSurfaces);

        AddElapsed(ref seedGlobalFieldsTime, () =>
        {
            foreach (var field in allFields)
            {
                AddField(field);
            }
        });

        AddElapsed(ref seedGlobalPropertiesTime, () =>
        {
            foreach (var property in allProperties)
            {
                AddProperty(property, includeAccessors: false);
            }
        });

        AddElapsed(ref seedRootMethodsTime, () =>
        {
            foreach (var method in rootMethodList)
            {
                AddMethod(method, MethodReachabilityReason.Root);
            }
        });

        while (pendingMethodKeys.Count > 0)
        {
            var methodKey = pendingMethodKeys.Dequeue();
            if (!methodMap.TryGetValue(methodKey, out var method) ||
                !processedMethodDependencies.Add(methodKey))
            {
                continue;
            }

            methodsProcessed++;
            var lowerer = GetLowerer();
            if (!irCache.TryGetValue(methodKey, out var ir))
            {
                try
                {
                    IrFunction? lowered = null;
                    AddElapsed(
                        ref methodLoweringTime,
                        elapsed => IncrementDuration(methodLoweringDurations, FormatMethodDiagnostic(method), elapsed),
                        () => lowered = lowerer.Lower(method));
                    ir = lowered!;
                    irCache[methodKey] = ir;
                    methodsLowered++;
                }
                catch (Exception ex) when (IsEnumerablePipelineMethod(method))
                {
                    var candidates = methodMap.Values
                        .Where(candidate => candidate.Name == method.Name)
                        .OrderBy(candidate => candidate.DeclaringTypeName, StringComparer.Ordinal)
                        .ThenBy(candidate => candidate.Parameters.Count)
                        .Select(FormatMethodDiagnostic)
                        .ToArray();
                    throw new InvalidOperationException(
                        $"Reachability lowering failed for enumerable pipeline method. current={FormatMethodDiagnostic(method)} candidates=[{string.Join(" | ", candidates)}] inner={ex.Message}",
                        ex);
                }
            }
            else
            {
                methodCacheHits++;
            }

            AddElapsed(
                ref dependencyScanTime,
                elapsed => IncrementDuration(dependencyScanDurations, FormatMethodDiagnostic(method), elapsed),
                () =>
                {
                    foreach (var block in ir.Blocks)
                    {
                        foreach (var instruction in block.Instructions)
                        {
                            instructionDependenciesVisited++;
                            AddInstructionDependencies(instruction);
                        }
                    }
                });

            AddElapsed(ref handlerScanTime, () =>
            {
                foreach (var handler in ir.ExceptionHandlers)
                {
                    AddTypeByName(handler.CatchTypeName, includeMembers: false);
                }
            });
        }

        Lowerer GetLowerer()
        {
            if (lowererCache is not null)
            {
                return lowererCache;
            }

            lowererRebuilds++;
            AddElapsed(ref lowererBuildTime, () =>
                lowererCache = new Lowerer(allMethods, GetKnownFields(), GetKnownTypes(), GetKnownProperties(), constantList, loweringProfiler));
            return lowererCache;
        }

        IReadOnlyList<FieldSymbol> GetKnownFields() =>
            knownFieldsCache ??= SymbolLists.CreateFields(
                allFields
                    .Concat(fieldMap.Values)
                    .GroupBy(field => GetFieldKey(field), StringComparer.Ordinal)
                    .Select(group => group.First()));

        TypeSymbol[] GetKnownTypes() =>
            knownTypesCache ??= declaredTypes
                .Concat(typeMap.Values)
                .GroupBy(GetTypeIdentityKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(type => type is NamedTypeSymbol namedType
                        ? (namedType.Methods?.Count ?? 0) +
                          (namedType.Fields?.Count ?? 0) +
                          (namedType.Properties?.Count ?? 0) +
                          (namedType.InterfaceTypes?.Count ?? 0) +
                          (namedType.TypeArguments?.Count ?? 0)
                        : 0)
                    .First())
                .ToArray();

        TypeSymbol[] GetTypeResolutionCandidates() =>
            typeResolutionCandidatesCache ??= typeMap.Values
                .Concat(declaredTypes)
                .GroupBy(GetTypeIdentityKey, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(type => type is NamedTypeSymbol namedType
                        ? (namedType.Methods?.Count ?? 0) +
                          (namedType.Fields?.Count ?? 0) +
                          (namedType.Properties?.Count ?? 0) +
                          (namedType.InterfaceTypes?.Count ?? 0) +
                          (namedType.TypeArguments?.Count ?? 0)
                        : 0)
                    .First())
                .ToArray();

        IReadOnlyList<PropertySymbol> GetKnownProperties() =>
            knownPropertiesCache ??= SymbolLists.CreateProperties(
                allProperties
                    .Concat(propertyMap.Values)
                    .GroupBy(property => $"{property.DeclaringTypeName ?? "<global>"}::{property.Name}", StringComparer.Ordinal)
                    .Select(group => group.First()));

        void PreseedTypeSurfaces()
        {
            foreach (var method in allMethods)
            {
                PreseedType(method.ReturnType);
                PreseedTypeByName(method.DeclaringTypeName);
                foreach (var parameter in method.Parameters)
                {
                    PreseedType(parameter.Type);
                }
            }

            foreach (var field in allFields)
            {
                PreseedType(field.Type);
                PreseedTypeByName(field.DeclaringTypeName);
            }

            foreach (var property in allProperties)
            {
                PreseedType(property.Type);
                PreseedTypeByName(property.DeclaringTypeName);
                if (property.IndexParameter is not null)
                {
                    PreseedType(property.IndexParameter.Type);
                }
            }
        }

        void PreseedTypeByName(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            if (typeMap.TryGetValue(typeName, out var existing))
            {
                PreseedType(existing);
                return;
            }

            var resolvedType = ResolveTypeReferenceCached(typeName);
            PreseedType(resolvedType);
        }

        void PreseedType(TypeSymbol? type)
        {
            if (type is null)
            {
                return;
            }

            var existingType = typeMap.GetValueOrDefault(type.Name);
            AddOrPreferRicherType(typeMap, type);
            if (!ReferenceEquals(existingType, typeMap[type.Name]))
            {
                typesPreseededOrReplaced++;
                InvalidateTypeResolutionCache();
            }

            if (type is not NamedTypeSymbol namedType)
            {
                return;
            }

            PreseedType(namedType.BaseType);
            foreach (var interfaceType in namedType.InterfaceTypes ?? [])
            {
                PreseedType(interfaceType);
            }

            foreach (var typeArgument in namedType.TypeArguments ?? [])
            {
                PreseedType(typeArgument);
            }

            if (SemanticFacts.IsOpenGenericDefinition(namedType) ||
                !preseededTypeMembers.Add(namedType.Name))
            {
                return;
            }

            foreach (var field in namedType.Fields ?? [])
            {
                PreseedField(field);
            }

            foreach (var property in namedType.Properties ?? [])
            {
                PreseedProperty(property);
            }
        }

        void PreseedField(FieldSymbol? field)
        {
            if (field is null)
            {
                return;
            }

            var key = GetFieldKey(field);
            if (!fieldMap.ContainsKey(key))
            {
                fieldMap[key] = field;
                fieldsPreseeded++;
            }

            PreseedType(field.Type);
            PreseedTypeByName(field.DeclaringTypeName);
        }

        void PreseedProperty(PropertySymbol? property)
        {
            if (property is null)
            {
                return;
            }

            var key = GetPropertyKey(property);
            if (!propertyMap.ContainsKey(key))
            {
                propertyMap[key] = property;
                propertiesPreseeded++;
            }

            PreseedType(property.Type);
            if (property.IndexParameter is not null)
            {
                PreseedType(property.IndexParameter.Type);
            }

            PreseedField(property.ReadField);
            PreseedField(property.WriteField);
            PreseedTypeByName(property.DeclaringTypeName);
        }

        void InvalidateLowererCache(LowererInvalidationReason reason, string? subjectName = null)
        {
            lowererCacheInvalidationRequests++;
            CountLowererInvalidationRequest(reason);
            if (knownFieldsCache is null &&
                knownTypesCache is null &&
                knownPropertiesCache is null &&
                lowererCache is null)
            {
                return;
            }

            lowererCacheInvalidations++;
            CountLowererInvalidation(reason, subjectName);
            knownFieldsCache = null;
            knownTypesCache = null;
            knownPropertiesCache = null;
            lowererCache = null;
        }

        void InvalidateTypeResolutionCache()
        {
            typeReferenceCache.Clear();
            typeResolutionCandidatesCache = null;
        }

        void CountLowererInvalidationRequest(LowererInvalidationReason reason)
        {
            switch (reason)
            {
                case LowererInvalidationReason.Field:
                    lowererFieldInvalidationRequests++;
                    break;
                case LowererInvalidationReason.Property:
                    lowererPropertyInvalidationRequests++;
                    break;
                case LowererInvalidationReason.Type:
                    lowererTypeInvalidationRequests++;
                    break;
            }
        }

        void CountLowererInvalidation(LowererInvalidationReason reason, string? subjectName)
        {
            switch (reason)
            {
                case LowererInvalidationReason.Field:
                    lowererFieldInvalidations++;
                    IncrementCounter(lowererFieldInvalidationNames, subjectName);
                    break;
                case LowererInvalidationReason.Property:
                    lowererPropertyInvalidations++;
                    IncrementCounter(lowererPropertyInvalidationNames, subjectName);
                    break;
                case LowererInvalidationReason.Type:
                    lowererTypeInvalidations++;
                    IncrementCounter(lowererTypeInvalidationNames, subjectName);
                    break;
            }
        }

        void AddInstructionDependencies(IrInstruction instruction)
        {
            switch (instruction.OpCode)
            {
                case IrOpCode.NewObject:
                    AddTypeByName((string)instruction.Operand!, includeMembers: false);
                    break;
                case IrOpCode.NewArray:
                {
                    var newArrayTarget = (IrNewArrayTarget)instruction.Operand!;
                    AddTypeByName(newArrayTarget.ElementTypeName, includeMembers: false);
                    break;
                }
                case IrOpCode.Call:
                case IrOpCode.CallVirtual:
                {
                    var callTarget = (IrCallTarget)instruction.Operand!;
                    AddMethod(callTarget.Method, MethodReachabilityReason.DirectCall);
                    if (callTarget.Receiver is not null)
                    {
                        AddType(callTarget.Receiver.Type, includeMembers: false);
                    }

                    foreach (var argument in callTarget.Arguments)
                    {
                        AddType(argument.Type, includeMembers: false);
                    }

                    break;
                }
                case IrOpCode.LoadConstant when instruction.Operand is MethodSymbol methodOperand:
                    AddMethod(methodOperand, MethodReachabilityReason.MethodConstant);
                    break;
                case IrOpCode.LoadField:
                case IrOpCode.LoadStaticField:
                case IrOpCode.StoreField:
                case IrOpCode.StoreStaticField:
                {
                    var fieldTarget = (IrFieldTarget)instruction.Operand!;
                    AddField(fieldTarget.Field);
                    if (fieldTarget.Receiver is not null)
                    {
                        AddType(fieldTarget.Receiver.Type, includeMembers: false);
                    }

                    break;
                }
                case IrOpCode.TypeIsReference:
                case IrOpCode.AsReference:
                {
                    var typeCheckTarget = (IrTypeCheckTarget)instruction.Operand!;
                    AddTypeByName(typeCheckTarget.TypeName, includeMembers: false);
                    AddType(typeCheckTarget.Value.Type, includeMembers: false);
                    break;
                }
            }
        }

        void AddMethod(MethodSymbol? method, MethodReachabilityReason reason)
        {
            if (method is null)
            {
                return;
            }

            method = NormalizeMethod(method);

            var key = GetMethodKey(method);
            if (!methodMap.TryGetValue(key, out var existingMethod))
            {
                methodMap[key] = method;
                pendingMethodKeys.Enqueue(key);
                methodsEnqueued++;
                CountMethodEnqueue(reason);
            }
            else if (IsRicherMethod(method, existingMethod))
            {
                methodMap[key] = method;
                irCache.Remove(key);
                processedMethodDependencies.Remove(key);
                pendingMethodKeys.Enqueue(key);
                methodsEnqueued++;
                CountMethodEnqueue(reason);
                methodReplacements++;
            }

            AddType(method.ReturnType, includeMembers: false);
            foreach (var parameter in method.Parameters)
            {
                AddType(parameter.Type, includeMembers: false);
            }

            AddNativeCallbackDelegateInvokeMethods(method);
            AddTypeByName(method.DeclaringTypeName, includeMembers: false);
            if (!string.IsNullOrEmpty(method.DeclaringTypeName) &&
                ResolveTypeReferenceCached(method.DeclaringTypeName) is NamedTypeSymbol declaringType)
            {
                AddInterfaceDispatchMethods(declaringType);

                if (!expandedDeclaringTypeMembers.Add(method.DeclaringTypeName))
                {
                    return;
                }

                foreach (var declaringField in declaringType.Fields ?? [])
                {
                    AddField(declaringField);
                }

                foreach (var declaringProperty in declaringType.Properties ?? [])
                {
                    AddProperty(declaringProperty, includeAccessors: false);
                }

                declaringTypeExpansions++;
            }
        }

        void CountMethodEnqueue(MethodReachabilityReason reason)
        {
            switch (reason)
            {
                case MethodReachabilityReason.Root:
                    rootMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.DirectCall:
                    directCallMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.MethodConstant:
                    methodConstantMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.PropertyAccessor:
                    propertyAccessorMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.NativeCallback:
                    nativeCallbackMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.DeclaringType:
                    declaringTypeMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.TypeMember:
                    typeMemberMethodsEnqueued++;
                    break;
                case MethodReachabilityReason.InterfaceDispatch:
                    interfaceDispatchMethodsEnqueued++;
                    break;
            }
        }

        MethodSymbol NormalizeMethod(MethodSymbol method)
        {
            if (string.IsNullOrWhiteSpace(method.DeclaringTypeName))
            {
                return method;
            }

            if (ResolveTypeReferenceCached(method.DeclaringTypeName) is not NamedTypeSymbol declaringType)
            {
                return method;
            }

            var normalized = declaringType.Methods.FirstOrDefault(candidate =>
                candidate.Name == method.Name &&
                candidate.IsConstructor == method.IsConstructor &&
                candidate.IsStatic == method.IsStatic &&
                candidate.Parameters.Count == method.Parameters.Count);
            return normalized ?? method;
        }

        void AddNativeCallbackDelegateInvokeMethods(MethodSymbol method)
        {
            if (method.DllImport is null)
            {
                return;
            }

            foreach (var parameter in method.Parameters)
            {
                if (ResolveNativeCallbackDelegateType(parameter.Type) is not { } delegateType)
                {
                    continue;
                }

                var invokeMethod = delegateType.Methods.FirstOrDefault(candidate =>
                    candidate.Name == "Invoke" &&
                    !candidate.IsStatic &&
                    !candidate.IsConstructor);
                AddMethod(invokeMethod, MethodReachabilityReason.NativeCallback);
            }
        }

        NamedTypeSymbol? ResolveNativeCallbackDelegateType(TypeSymbol type)
        {
            if (type is NamedTypeSymbol { IsDelegate: true } namedType)
            {
                return namedType;
            }

            return ResolveTypeReferenceCached(type.Name) is NamedTypeSymbol { IsDelegate: true } resolvedType
                ? resolvedType
                : null;
        }

        void AddInterfaceDispatchMethods(NamedTypeSymbol concreteType)
        {
            if (!IsRuntimeConcreteType(concreteType) ||
                !expandedInterfaceDispatchMembers.Add(concreteType.Name))
            {
                return;
            }

            interfaceDispatchExpansions++;
            var namedTypeLookup = declaredTypes
                .Concat(typeMap.Values)
                .OfType<NamedTypeSymbol>()
                .GroupBy(type => type.Name, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(type =>
                            (type.Methods?.Count ?? 0) +
                            (type.Fields?.Count ?? 0) +
                            (type.Properties?.Count ?? 0) +
                            (type.InterfaceTypes?.Count ?? 0) +
                            (type.TypeArguments?.Count ?? 0))
                        .First(),
                    StringComparer.Ordinal);

            foreach (var interfaceType in GetImplementedInterfaces(concreteType, namedTypeLookup))
            {
                AddType(interfaceType, includeMembers: false);

                foreach (var interfaceMethod in interfaceType.Methods.Where(candidate => !candidate.IsConstructor))
                {
                    AddMethod(interfaceMethod, MethodReachabilityReason.InterfaceDispatch);
                    var implementationMethod = FindInterfaceImplementation(concreteType, interfaceMethod, namedTypeLookup);
                    AddMethod(implementationMethod, MethodReachabilityReason.InterfaceDispatch);
                }
            }
        }

        void AddField(FieldSymbol? field)
        {
            if (field is null)
            {
                return;
            }

            var key = GetFieldKey(field);
            if (!fieldMap.ContainsKey(key))
            {
                fieldMap[key] = field;
                fieldsAdded++;
                InvalidateLowererCache(LowererInvalidationReason.Field, field.DeclaringTypeName ?? "<global>");
            }

            AddType(field.Type, includeMembers: false);
            AddTypeByName(field.DeclaringTypeName, includeMembers: false);
        }

        void AddProperty(PropertySymbol? property, bool includeAccessors = true)
        {
            if (property is null)
            {
                return;
            }

            var key = GetPropertyKey(property);
            if (!propertyMap.ContainsKey(key))
            {
                propertyMap[key] = property;
                propertiesAdded++;
                InvalidateLowererCache(LowererInvalidationReason.Property, property.DeclaringTypeName ?? "<global>");
            }

            AddType(property.Type, includeMembers: false);
            if (property.IndexParameter is not null)
            {
                AddType(property.IndexParameter.Type, includeMembers: false);
            }

            AddField(property.ReadField);
            AddField(property.WriteField);
            if (includeAccessors)
            {
                AddMethod(property.GetterMethod, MethodReachabilityReason.PropertyAccessor);
                AddMethod(property.SetterMethod, MethodReachabilityReason.PropertyAccessor);
            }

            AddTypeByName(property.DeclaringTypeName, includeMembers: false);
        }

        void AddTypeByName(string? typeName, bool includeMembers)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            if (typeMap.TryGetValue(typeName, out var existing))
            {
                AddType(existing, includeMembers);
                return;
            }

            var resolvedType = ResolveTypeReferenceCached(typeName);
            if (resolvedType is not null)
            {
                AddType(resolvedType, includeMembers);
            }
        }

        TypeSymbol? ResolveTypeReferenceCached(string typeName)
        {
            if (typeReferenceCache.TryGetValue(typeName, out var cachedType))
            {
                typeReferenceCacheHits++;
                return cachedType;
            }

            typeReferenceCacheMisses++;
            var resolvedType = SemanticFacts.ResolveTypeReference(typeName, GetTypeResolutionCandidates());
            typeReferenceCache[typeName] = resolvedType;
            return resolvedType;
        }

        void AddType(TypeSymbol? type, bool includeMembers)
        {
            if (type is null)
            {
                return;
            }

            var existingType = typeMap.GetValueOrDefault(type.Name);
            AddOrPreferRicherType(typeMap, type);
            if (!ReferenceEquals(existingType, typeMap[type.Name]))
            {
                typesAddedOrReplaced++;
                InvalidateTypeResolutionCache();
                if (ShouldInvalidateLowererForTypeChange(typeMap[type.Name]))
                {
                    InvalidateLowererCache(LowererInvalidationReason.Type, type.Name);
                }
                else
                {
                    lowererClosedGenericTypeInvalidationsSuppressed++;
                }
            }

            if (type is not NamedTypeSymbol namedType)
            {
                return;
            }

            if (namedType.BaseType is not null)
            {
                AddType(namedType.BaseType, includeMembers: false);
            }

            foreach (var interfaceType in namedType.InterfaceTypes ?? [])
            {
                AddType(interfaceType, includeMembers: false);
            }

            foreach (var typeArgument in namedType.TypeArguments ?? [])
            {
                AddType(typeArgument, includeMembers: false);
            }

            AddInterfaceDispatchMethods(namedType);

            if (!includeMembers || SemanticFacts.IsOpenGenericDefinition(namedType) || !expandedTypeMembers.Add(namedType.Name))
            {
                return;
            }

            typeMemberExpansions++;
            foreach (var field in namedType.Fields ?? [])
            {
                AddField(field);
            }

            foreach (var property in namedType.Properties ?? [])
            {
                AddProperty(property);
            }

            foreach (var method in namedType.Methods ?? [])
            {
                AddMethod(method, MethodReachabilityReason.TypeMember);
            }
        }

        return new ReachableCompilationClosure(
            methodMap.Values.ToArray(),
            fieldMap.Values.ToArray(),
            typeMap.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray(),
            propertyMap.Values.ToArray(),
            new ReachabilityStats(
                rootMethodList.Length,
                allMethods.Count,
                allFields.Count,
                allProperties.Count,
                declaredTypes.Length,
                methodsEnqueued,
                rootMethodsEnqueued,
                directCallMethodsEnqueued,
                methodConstantMethodsEnqueued,
                propertyAccessorMethodsEnqueued,
                nativeCallbackMethodsEnqueued,
                declaringTypeMethodsEnqueued,
                typeMemberMethodsEnqueued,
                interfaceDispatchMethodsEnqueued,
                methodsProcessed,
                methodsLowered,
                methodCacheHits,
                methodReplacements,
                instructionDependenciesVisited,
                typeReferenceCacheHits,
                typeReferenceCacheMisses,
                fieldsAdded,
                propertiesAdded,
                fieldsPreseeded,
                propertiesPreseeded,
                typesAddedOrReplaced,
                typesPreseededOrReplaced,
                declaringTypeExpansions,
                typeMemberExpansions,
                interfaceDispatchExpansions,
                lowererRebuilds,
                lowererCacheInvalidationRequests,
                lowererCacheInvalidations,
                lowererFieldInvalidationRequests,
                lowererPropertyInvalidationRequests,
                lowererTypeInvalidationRequests,
                lowererFieldInvalidations,
                lowererPropertyInvalidations,
                lowererTypeInvalidations,
                lowererClosedGenericTypeInvalidationsSuppressed,
                seedDeclaredTypesTime,
                preseedTypeSurfacesTime,
                seedGlobalFieldsTime,
                seedGlobalPropertiesTime,
                seedRootMethodsTime,
                lowererBuildTime,
                methodLoweringTime,
                dependencyScanTime,
                handlerScanTime,
                BuildReachabilityDurationCounters(methodLoweringDurations),
                BuildReachabilityDurationCounters(dependencyScanDurations),
                loweringProfiler?.Snapshot() ?? [],
                BuildReachabilityCounters(lowererFieldInvalidationNames),
                BuildReachabilityCounters(lowererPropertyInvalidationNames),
                BuildReachabilityCounters(lowererTypeInvalidationNames)));
    }

    private static IReadOnlyList<ReachabilityCounter> BuildReachabilityCounters(Dictionary<string, int> counters) =>
        counters
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ReachabilityCounter(pair.Key, pair.Value))
            .ToArray();

    private static IReadOnlyList<ReachabilityDurationCounter> BuildReachabilityDurationCounters(Dictionary<string, (TimeSpan Elapsed, int Count)> counters) =>
        counters
            .OrderByDescending(pair => pair.Value.Elapsed)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ReachabilityDurationCounter(pair.Key, pair.Value.Elapsed, pair.Value.Count))
            .ToArray();

    private static void AddElapsed(ref TimeSpan accumulator, Action action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            action();
        }
        finally
        {
            accumulator += Stopwatch.GetElapsedTime(startedAt);
        }
    }

    private static void AddElapsed(ref TimeSpan accumulator, Action<TimeSpan> afterElapsed, Action action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            action();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            accumulator += elapsed;
            afterElapsed(elapsed);
        }
    }

    private static void IncrementCounter(Dictionary<string, int> counters, string? subjectName)
    {
        if (string.IsNullOrWhiteSpace(subjectName))
        {
            return;
        }

        counters[subjectName] = counters.GetValueOrDefault(subjectName) + 1;
    }

    private static void IncrementDuration(Dictionary<string, (TimeSpan Elapsed, int Count)> counters, string name, TimeSpan elapsed)
    {
        var existing = counters.GetValueOrDefault(name);
        counters[name] = (existing.Elapsed + elapsed, existing.Count + 1);
    }

    private static bool ShouldInvalidateLowererForTypeChange(TypeSymbol type) =>
        !IsClosedConstructedGenericType(type);

    private static bool IsClosedConstructedGenericType(TypeSymbol type)
    {
        if (type is NamedTypeSymbol { GenericDefinition: not null, TypeArguments.Count: > 0 } namedType)
        {
            return namedType.TypeArguments.All(IsClosedTypeArgument);
        }

        if (!TryParseConstructedTypeName(type.Name, out var genericArgumentNames))
        {
            return false;
        }

        return genericArgumentNames.All(argumentName =>
            SemanticFacts.ResolveTypeReference(argumentName, []) is not TypeParameterSymbol &&
            !IsLikelyTypeParameterName(argumentName));
    }

    private static bool IsClosedTypeArgument(TypeSymbol type) =>
        type is not TypeParameterSymbol &&
        (!TryParseConstructedTypeName(type.Name, out var genericArgumentNames) ||
         genericArgumentNames.All(argumentName => !IsLikelyTypeParameterName(argumentName)));

    private static bool TryParseConstructedTypeName(string displayName, out IReadOnlyList<string> genericArgumentNames)
    {
        genericArgumentNames = [];
        var lessThanIndex = displayName.IndexOf('<');
        if (lessThanIndex <= 0 || !displayName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        genericArgumentNames = SplitConstructedTypeArguments(displayName[(lessThanIndex + 1)..^1]);
        return genericArgumentNames.Count > 0;
    }

    private static IReadOnlyList<string> SplitConstructedTypeArguments(string value)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        var finalArgument = value[start..].Trim();
        if (!string.IsNullOrEmpty(finalArgument))
        {
            arguments.Add(finalArgument);
        }

        return arguments;
    }

    private static bool IsLikelyTypeParameterName(string typeName) =>
        typeName.Length >= 1 &&
        typeName[0] == 'T' &&
        typeName.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string GetMethodKey(MethodSymbol method) =>
        $"{method.DeclaringTypeName ?? "<global>"}::{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}";

    private static string GetTypeIdentityKey(TypeSymbol type) =>
        type is NamedTypeSymbol namedType
            ? $"{namedType.Name}`{namedType.GenericArity}:{type.Name}"
            : type.Name;

    private static bool IsEnumerablePipelineMethod(MethodSymbol method) =>
        method.DeclaringTypeName is not null &&
        method.DeclaringTypeName.StartsWith("Enumerable", StringComparison.Ordinal) &&
        (method.Name == "Where" || method.Name == "Select");

    private static string FormatMethodDiagnostic(MethodSymbol method) =>
        $"{method.DeclaringTypeName}.{method.Name}({string.Join(", ", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}";

    private static string GetFieldKey(FieldSymbol field) =>
        $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}";

    private static string GetPropertyKey(PropertySymbol property) =>
        $"{property.DeclaringTypeName ?? "<global>"}::{property.Name}";

    private static void AddOrPreferRicherType(IDictionary<string, TypeSymbol> types, TypeSymbol candidate)
    {
        if (!types.TryGetValue(candidate.Name, out var existing))
        {
            types[candidate.Name] = candidate;
            return;
        }

        if (IsRicherType(candidate, existing))
        {
            types[candidate.Name] = candidate;
        }
    }

    private static bool IsRicherMethod(MethodSymbol candidate, MethodSymbol existing)
    {
        static int CountOpenGenericMarkers(TypeSymbol type) =>
            type.Name.Contains("<T", StringComparison.Ordinal) ||
            type.Name.Contains(", T", StringComparison.Ordinal) ||
            type.Name.EndsWith("<T>", StringComparison.Ordinal)
                ? 1
                : 0;

        static int Score(MethodSymbol method) =>
            method.Parameters.Sum(parameter => CountOpenGenericMarkers(parameter.Type)) +
            CountOpenGenericMarkers(method.ReturnType);

        return Score(candidate) < Score(existing);
    }

    private static bool IsRicherType(TypeSymbol candidate, TypeSymbol existing)
    {
        if (candidate is NamedTypeSymbol && existing is not NamedTypeSymbol)
        {
            return true;
        }

        if (candidate is not NamedTypeSymbol candidateNamed || existing is not NamedTypeSymbol existingNamed)
        {
            return false;
        }

        if (candidateNamed.GenericDefinition is not null && existingNamed.GenericDefinition is null)
        {
            return true;
        }

        var candidateScore =
            candidateNamed.Methods.Count +
            candidateNamed.Fields.Count +
            candidateNamed.Properties.Count +
            candidateNamed.InterfaceTypes.Count +
            (candidateNamed.BaseType is null ? 0 : 1) +
            (candidateNamed.GenericParameters?.Count ?? 0) +
            (candidateNamed.TypeArguments?.Count ?? 0);
        var existingScore =
            existingNamed.Methods.Count +
            existingNamed.Fields.Count +
            existingNamed.Properties.Count +
            existingNamed.InterfaceTypes.Count +
            (existingNamed.BaseType is null ? 0 : 1) +
            (existingNamed.GenericParameters?.Count ?? 0) +
            (existingNamed.TypeArguments?.Count ?? 0);

        return candidateScore > existingScore;
    }

    private static bool IsRuntimeConcreteType(NamedTypeSymbol type)
    {
        if (SemanticFacts.IsOpenGenericDefinition(type))
        {
            return false;
        }

        if (type.TypeArguments is null || type.TypeArguments.Count == 0)
        {
            return true;
        }

        return type.TypeArguments.All(argument => argument is not TypeParameterSymbol);
    }

    private static IEnumerable<NamedTypeSymbol> GetImplementedInterfaces(
        NamedTypeSymbol concreteType,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<NamedTypeSymbol>();

        void EnqueueInterface(TypeSymbol interfaceType)
        {
            if (namedTypeLookup.TryGetValue(interfaceType.Name, out var resolved) &&
                resolved.IsInterface &&
                seen.Add(resolved.Name))
            {
                pending.Enqueue(resolved);
            }
        }

        NamedTypeSymbol? current = concreteType;
        while (current is not null)
        {
            foreach (var interfaceType in current.InterfaceTypes)
            {
                EnqueueInterface(interfaceType);
            }

            if (current.BaseType is not null &&
                namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType))
            {
                current = baseType;
            }
            else
            {
                current = null;
            }
        }

        while (pending.Count > 0)
        {
            var interfaceType = pending.Dequeue();
            yield return interfaceType;

            foreach (var inheritedInterface in interfaceType.InterfaceTypes)
            {
                EnqueueInterface(inheritedInterface);
            }
        }
    }

    private static MethodSymbol? FindInterfaceImplementation(
        NamedTypeSymbol concreteType,
        MethodSymbol interfaceMethod,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        for (NamedTypeSymbol? current = concreteType; current is not null; current = current.BaseType is not null && namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType) ? baseType : null)
        {
            var match = current.Methods.FirstOrDefault(candidate =>
                !candidate.IsConstructor &&
                AreInterfaceImplementationCompatible(interfaceMethod, candidate, namedTypeLookup));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool AreInterfaceImplementationCompatible(
        MethodSymbol contractMethod,
        MethodSymbol implementationMethod,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        if (!StringComparer.Ordinal.Equals(contractMethod.Name, implementationMethod.Name) ||
            contractMethod.Parameters.Count != implementationMethod.Parameters.Count)
        {
            return false;
        }

        for (var index = 0; index < contractMethod.Parameters.Count; index++)
        {
            if (contractMethod.Parameters[index].Type.Name != implementationMethod.Parameters[index].Type.Name ||
                contractMethod.Parameters[index].PassingKind != implementationMethod.Parameters[index].PassingKind)
            {
                return false;
            }
        }

        if (contractMethod.ReturnType.Name == implementationMethod.ReturnType.Name)
        {
            return true;
        }

        return IsCompatibleReferenceType(
            implementationMethod.ReturnType.Name,
            contractMethod.ReturnType.Name,
            namedTypeLookup);
    }

    private static bool IsCompatibleReferenceType(
        string sourceTypeName,
        string targetTypeName,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        if (sourceTypeName == targetTypeName || targetTypeName == "Object")
        {
            return true;
        }

        if (!namedTypeLookup.TryGetValue(sourceTypeName, out var sourceType) ||
            !namedTypeLookup.TryGetValue(targetTypeName, out var targetType))
        {
            return false;
        }

        if (targetType.IsInterface)
        {
            return GetImplementedInterfaces(sourceType, namedTypeLookup).Any(candidate => candidate.Name == targetTypeName);
        }

        for (NamedTypeSymbol? current = sourceType; current is not null; current = current.BaseType is not null && namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType) ? baseType : null)
        {
            if (current.Name == targetTypeName)
            {
                return true;
            }
        }

        return false;
    }
}

public enum IlbSectionKind : uint
{
    StringTable = 1,
    BlobTable = 2,
    TypeTable = 3,
    FieldTable = 4,
    MethodTable = 5,
    ConstantTable = 6,
    CodeSection = 7,
    ExceptionTable = 8,
    EntryPoint = 9,
    InterfaceDispatchTable = 10
}

public sealed record IlbSectionDirectoryEntry(
    IlbSectionKind Kind,
    uint Offset,
    uint Size,
    uint ElementCount,
    uint Alignment,
    uint Flags);

public sealed record IlbImage(
    byte[] Bytes,
    IReadOnlyList<IlbSectionDirectoryEntry> Sections);

public sealed class IlbSerializer
{
    private const ushort MajorVersion = 1;
    private const ushort MinorVersion = 0;
    private const ushort HeaderSize = 64;
    private const uint SectionAlignment = 8;

    public IlbImage Serialize(
        BytecodeModule module,
        IEnumerable<MethodSymbol> methods,
        IEnumerable<FieldSymbol> fields,
        IEnumerable<TypeSymbol> types,
        MethodSymbol? entryPoint)
    {
        var methodList = methods.ToArray();
        var fieldList = fields.ToArray();
        var typeList = module.Types.Count > 0
            ? module.Types.ToArray()
            : CollectTypes(methodList, fieldList, types).ToArray();
        var stringTable = new IlbStringTableBuilder();
        var blobTable = new IlbBlobTableBuilder();

        foreach (var type in typeList)
        {
            if (type is not null)
            {
                stringTable.GetOrAdd(GetNamespace(type));
                stringTable.GetOrAdd(GetSimpleTypeName(type.Name));
            }
        }

        foreach (var field in fieldList)
        {
            stringTable.GetOrAdd(field.Name);
        }

        foreach (var method in methodList)
        {
            stringTable.GetOrAdd(method.Name);
            if (method.DllImport is not null)
            {
                stringTable.GetOrAdd(method.DllImport.LibraryName);
                stringTable.GetOrAdd(method.DllImport.EntryPoint);
                if (!string.IsNullOrEmpty(method.DllImport.StringFreeEntryPoint))
                {
                    stringTable.GetOrAdd(method.DllImport.StringFreeEntryPoint);
                }
            }

            blobTable.Add(BuildSignatureBlob(method, typeList));
        }

        var typeIds = typeList
            .Select((type, index) => (type, id: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.id, StringComparer.Ordinal);
        var fieldIds = fieldList
            .Select((field, index) => (field, id: (uint)(index + 1)))
            .ToDictionary(pair => GetFieldKey(pair.field), pair => pair.id, StringComparer.Ordinal);
        var methodIds = methodList
            .Select((method, index) => (method, id: (uint)(index + 1)))
            .ToDictionary(pair => GetMethodKey(pair.method), pair => pair.id, StringComparer.Ordinal);
        var functionStringIds = new Dictionary<uint, IReadOnlyList<uint>>();
        foreach (var function in module.Functions)
        {
            functionStringIds[function.FunctionId] = function.StringLiterals
                .Select(stringTable.GetOrAdd)
                .ToArray();
        }

        var codeInfo = BuildCodeSection(module.Functions, functionStringIds);
        var exceptionInfo = BuildExceptionTableSection(module.Functions);
        var stringsPayload = BuildStringTableSection(stringTable);
        var blobsPayload = BuildBlobTableSection(blobTable);
        var typesPayload = BuildTypeTableSection(typeList, stringTable, typeIds, fieldList, methodList);
        var fieldsPayload = BuildFieldTableSection(fieldList, typeIds, stringTable);
        var methodsPayload = BuildMethodTableSection(methodList, typeIds, stringTable, blobTable, codeInfo, exceptionInfo, methodIds);
        var interfaceDispatchPayload = BuildInterfaceDispatchTableSection(typeList, methodList, typeIds, methodIds);
        var codePayload = codeInfo.SectionBytes;
        var exceptionPayload = exceptionInfo.SectionBytes;
        var entryPayload = BuildEntryPointSection(entryPoint, methodIds);

        var sections = new List<(IlbSectionKind Kind, byte[] Payload, uint Count)>
        {
            (IlbSectionKind.StringTable, stringsPayload, (uint)stringTable.Count),
            (IlbSectionKind.BlobTable, blobsPayload, (uint)blobTable.Count),
            (IlbSectionKind.TypeTable, typesPayload, (uint)typeList.Length),
            (IlbSectionKind.FieldTable, fieldsPayload, (uint)fieldList.Length),
            (IlbSectionKind.MethodTable, methodsPayload, (uint)methodList.Length),
            (IlbSectionKind.CodeSection, codePayload, (uint)module.Functions.Count)
        };

        if (exceptionPayload.Length > 0)
        {
            sections.Add((IlbSectionKind.ExceptionTable, exceptionPayload, (uint)exceptionInfo.RowCount));
        }

        if (interfaceDispatchPayload.Length > 0)
        {
            sections.Add((IlbSectionKind.InterfaceDispatchTable, interfaceDispatchPayload, (uint)(interfaceDispatchPayload.Length / 16)));
        }

        if (entryPayload.Length > 0)
        {
            sections.Add((IlbSectionKind.EntryPoint, entryPayload, 1));
        }

        var sectionDirectoryOffset = HeaderSize;
        var sectionDirectorySize = sections.Count * 24;
        var payloadOffset = Align((uint)(HeaderSize + sectionDirectorySize), SectionAlignment);
        var directory = new List<IlbSectionDirectoryEntry>();
        var positionedPayloads = new List<(IlbSectionDirectoryEntry Section, byte[] Payload)>();

        foreach (var section in sections)
        {
            payloadOffset = Align(payloadOffset, SectionAlignment);
            var entry = new IlbSectionDirectoryEntry(
                section.Kind,
                payloadOffset,
                (uint)section.Payload.Length,
                section.Count,
                SectionAlignment,
                0);
            directory.Add(entry);
            positionedPayloads.Add((entry, section.Payload));
            payloadOffset += (uint)section.Payload.Length;
        }

        var fileSize = payloadOffset;
        using var stream = new MemoryStream((int)fileSize);
        using var writer = new BinaryWriter(stream);
        WriteHeader(writer, (uint)sections.Count, (uint)sectionDirectoryOffset, fileSize, entryPoint is null);
        stream.Position = sectionDirectoryOffset;
        foreach (var entry in directory)
        {
            writer.Write((uint)entry.Kind);
            writer.Write(entry.Offset);
            writer.Write(entry.Size);
            writer.Write(entry.ElementCount);
            writer.Write(entry.Alignment);
            writer.Write(entry.Flags);
        }

        foreach (var positioned in positionedPayloads)
        {
            stream.Position = positioned.Section.Offset;
            writer.Write(positioned.Payload);
        }

        var bytes = stream.ToArray();
        WriteChecksums(bytes);
        return new IlbImage(bytes, directory);
    }

    private static TypeSymbol[] CollectTypes(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> declaredTypes)
    {
        var declaredTypeList = declaredTypes.ToArray();
        var types = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        foreach (var type in declaredTypeList)
        {
            AddOrPreferRicherType(types, type);
        }

        foreach (var field in fields)
        {
            AddOrPreferRicherType(types, field.Type);
            if (!string.IsNullOrEmpty(field.DeclaringTypeName) &&
                SemanticFacts.ResolveTypeReference(field.DeclaringTypeName, declaredTypeList) is { } resolvedFieldDeclaringType)
            {
                AddOrPreferRicherType(types, resolvedFieldDeclaringType);
            }
        }

        foreach (var method in methods)
        {
            if (!string.IsNullOrEmpty(method.DeclaringTypeName) &&
                SemanticFacts.ResolveTypeReference(method.DeclaringTypeName, declaredTypeList) is { } resolvedMethodDeclaringType)
            {
                AddOrPreferRicherType(types, resolvedMethodDeclaringType);
            }

            if (method.ReturnType != TypeSymbol.Void)
            {
                AddOrPreferRicherType(types, method.ReturnType);
            }

            foreach (var parameter in method.Parameters)
            {
                AddOrPreferRicherType(types, parameter.Type);
            }
        }

        return types.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
    }

    private static void AddOrPreferRicherType(IDictionary<string, TypeSymbol> types, TypeSymbol candidate)
    {
        if (!types.TryGetValue(candidate.Name, out var existing))
        {
            types[candidate.Name] = candidate;
            return;
        }

        if (IsRicherType(candidate, existing))
        {
            types[candidate.Name] = candidate;
        }
    }

    private static bool IsRicherType(TypeSymbol candidate, TypeSymbol existing)
    {
        if (candidate is NamedTypeSymbol && existing is not NamedTypeSymbol)
        {
            return true;
        }

        if (candidate is not NamedTypeSymbol candidateNamed || existing is not NamedTypeSymbol existingNamed)
        {
            return false;
        }

        if (candidateNamed.GenericDefinition is not null && existingNamed.GenericDefinition is null)
        {
            return true;
        }

        var candidateScore =
            candidateNamed.Methods.Count +
            candidateNamed.Fields.Count +
            candidateNamed.Properties.Count +
            candidateNamed.InterfaceTypes.Count +
            (candidateNamed.BaseType is null ? 0 : 1) +
            (candidateNamed.GenericParameters?.Count ?? 0) +
            (candidateNamed.TypeArguments?.Count ?? 0);
        var existingScore =
            existingNamed.Methods.Count +
            existingNamed.Fields.Count +
            existingNamed.Properties.Count +
            existingNamed.InterfaceTypes.Count +
            (existingNamed.BaseType is null ? 0 : 1) +
            (existingNamed.GenericParameters?.Count ?? 0) +
            (existingNamed.TypeArguments?.Count ?? 0);

        return candidateScore > existingScore;
    }

    private static byte[] BuildStringTableSection(IlbStringTableBuilder strings)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)strings.Count);
        foreach (var value in strings.Values)
        {
            var utf8 = System.Text.Encoding.UTF8.GetBytes(value);
            writer.Write((uint)utf8.Length);
            writer.Write(utf8);
        }

        return stream.ToArray();
    }

    private static byte[] BuildBlobTableSection(IlbBlobTableBuilder blobs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)blobs.Count);
        foreach (var blob in blobs.Values)
        {
            writer.Write((uint)blob.Length);
            writer.Write(blob);
        }

        return stream.ToArray();
    }

    private static byte[] BuildTypeTableSection(
        IReadOnlyList<TypeSymbol> types,
        IlbStringTableBuilder strings,
        IReadOnlyDictionary<string, uint> typeIds,
        IReadOnlyList<FieldSymbol> fields,
        IReadOnlyList<MethodSymbol> methods)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var type in types)
        {
            writer.Write(strings.GetOrAdd(GetNamespace(type)));
            writer.Write(strings.GetOrAdd(GetSimpleTypeName(type.Name)));
            writer.Write(GetTypeKind(type));
            writer.Write(GetTypeFlags(type));
            writer.Write(GetBaseTypeId(type, typeIds));
            writer.Write(0u);
            var ownedFields = fields.Where(field => field.DeclaringTypeName == type.Name).ToArray();
            var ownedMethods = methods.Where(method => method.DeclaringTypeName == type.Name).ToArray();
            writer.Write(ownedFields.Length > 0 ? GetFieldId(ownedFields[0], fields) : 0u);
            writer.Write((uint)ownedFields.Length);
            writer.Write(ownedMethods.Length > 0 ? GetMethodId(ownedMethods[0], methods) : 0u);
            writer.Write((uint)ownedMethods.Length);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFieldTableSection(
        IReadOnlyList<FieldSymbol> fields,
        IReadOnlyDictionary<string, uint> typeIds,
        IlbStringTableBuilder strings)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var field in fields)
        {
            writer.Write(typeIds[field.DeclaringTypeName!]);
            writer.Write(strings.GetOrAdd(field.Name));
            writer.Write(typeIds[field.Type.Name]);
            writer.Write(GetFieldFlags(field));
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
        }

        return stream.ToArray();
    }

    private static byte[] BuildMethodTableSection(
        IReadOnlyList<MethodSymbol> methods,
        IReadOnlyDictionary<string, uint> typeIds,
        IlbStringTableBuilder strings,
        IlbBlobTableBuilder blobs,
        IlbCodeSectionInfo codeInfo,
        IlbExceptionSectionInfo exceptionInfo,
        IReadOnlyDictionary<string, uint> methodIds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var method in methods)
        {
            writer.Write(method.DeclaringTypeName is null ? 0u : typeIds[method.DeclaringTypeName]);
            writer.Write(strings.GetOrAdd(method.Name));
            writer.Write(blobs.GetId(BuildSignatureBlob(method, typeIds.Keys.Select(name => new TypeSymbol(name, true)).ToArray())));
            writer.Write(GetMethodFlags(method));
            var function = codeInfo.FunctionsById[methodIds[GetMethodKey(method)]];
            writer.Write(function.RegisterCount);
            writer.Write((ushort)method.Parameters.Count);
            var localCount = (ushort)Math.Max(function.RegisterCount - function.ArgumentCount - (method.ReturnType != TypeSymbol.Void ? 1 : 0), 0);
            writer.Write(localCount);
            writer.Write(method.ReturnType == TypeSymbol.Void ? 0u : typeIds[method.ReturnType.Name]);
            writer.Write(function.CodeOffset);
            writer.Write(function.CodeSize);
            if (exceptionInfo.FunctionsById.TryGetValue(function.FunctionId, out var functionExceptions))
            {
                writer.Write(functionExceptions.ExceptionStart);
                writer.Write(functionExceptions.ExceptionCount);
            }
            else
            {
                writer.Write(0u);
                writer.Write(0u);
            }
            writer.Write((uint)method.HostImportKind);
            writer.Write(method.DllImport is null ? 0u : strings.GetOrAdd(method.DllImport.LibraryName));
            writer.Write(method.DllImport is null ? 0u : strings.GetOrAdd(method.DllImport.EntryPoint));
            writer.Write(method.DllImport is null ? 0u : (uint)method.DllImport.CallingConvention);
            writer.Write(method.DllImport is null ? 0u : (uint)method.DllImport.StringReturnMarshalling);
            writer.Write(method.DllImport is null || string.IsNullOrEmpty(method.DllImport.StringFreeEntryPoint)
                ? 0u
                : strings.GetOrAdd(method.DllImport.StringFreeEntryPoint));
        }

        return stream.ToArray();
    }

    private static byte[] BuildEntryPointSection(MethodSymbol? entryPoint, IReadOnlyDictionary<string, uint> methodIds)
    {
        if (entryPoint is null)
        {
            return [];
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(methodIds[GetMethodKey(entryPoint)]);
        writer.Write(0u);
        return stream.ToArray();
    }

    private static byte[] BuildInterfaceDispatchTableSection(
        IReadOnlyList<TypeSymbol> types,
        IReadOnlyList<MethodSymbol> methods,
        IReadOnlyDictionary<string, uint> typeIds,
        IReadOnlyDictionary<string, uint> methodIds)
    {
        var rows = BuildInterfaceDispatchRows(types, methods, typeIds, methodIds).ToArray();
        if (rows.Length == 0)
        {
            return [];
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var row in rows)
        {
            writer.Write(row.OwnerTypeId);
            writer.Write(row.InterfaceTypeId);
            writer.Write(row.InterfaceMethodId);
            writer.Write(row.ImplementationMethodId);
        }

        return stream.ToArray();
    }

    private static IlbCodeSectionInfo BuildCodeSection(
        IReadOnlyList<BytecodeFunction> functions,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>> functionStringIds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var map = new Dictionary<uint, IlbCodeFunctionInfo>();
        foreach (var function in functions)
        {
            var offset = (uint)stream.Position;
            foreach (var instruction in function.Instructions)
            {
                var immediate = instruction.Immediate;
                if (instruction.ImmediateKind == InstructionImmediateKind.StringTableIndex &&
                    instruction.OpCode == OpCode.LdStr &&
                    instruction.Immediate > 0 &&
                    functionStringIds.TryGetValue(function.FunctionId, out var stringIds) &&
                    instruction.Immediate <= stringIds.Count)
                {
                    immediate = (int)stringIds[instruction.Immediate - 1];
                }

                writer.Write((byte)instruction.OpCode);
                writer.Write(instruction.Destination);
                writer.Write(instruction.Left);
                writer.Write(instruction.Right);
                writer.Write(immediate);
            }

            var size = (uint)stream.Position - offset;
            map[function.FunctionId] = new IlbCodeFunctionInfo(function.FunctionId, offset, size, function.RegisterCount, function.ArgumentCount);
        }

        return new IlbCodeSectionInfo(stream.ToArray(), map);
    }

    private static IlbExceptionSectionInfo BuildExceptionTableSection(IReadOnlyList<BytecodeFunction> functions)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var map = new Dictionary<uint, IlbFunctionExceptionInfo>();
        uint nextExceptionId = 1;

        foreach (var function in functions)
        {
            if (function.ExceptionHandlers.Count == 0)
            {
                continue;
            }

            var start = nextExceptionId;
            foreach (var handler in function.ExceptionHandlers)
            {
                writer.Write(nextExceptionId++);
                writer.Write((uint)handler.TryStart);
                writer.Write((uint)handler.TryEnd);
                writer.Write((uint)handler.HandlerStart);
                writer.Write((uint)handler.HandlerEnd);
                writer.Write((ushort)1);
                writer.Write((ushort)0);
                writer.Write(handler.CatchTypeId);
                writer.Write(handler.TargetRegister);
                writer.Write((ushort)0);
            }

            map[function.FunctionId] = new IlbFunctionExceptionInfo(function.FunctionId, start, (uint)function.ExceptionHandlers.Count);
        }

        return new IlbExceptionSectionInfo(stream.ToArray(), map, nextExceptionId - 1);
    }

    private static byte[] BuildSignatureBlob(MethodSymbol method, IReadOnlyList<TypeSymbol> types)
    {
        var typeIds = types
            .Select((type, index) => (type, id: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.id, StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)(method.IsStatic ? 1 : 2));
        writer.Write(method.ReturnType == TypeSymbol.Void ? 0u : typeIds[method.ReturnType.Name]);
        writer.Write((ushort)method.Parameters.Count);
        foreach (var parameter in method.Parameters)
        {
            writer.Write(typeIds[parameter.Type.Name]);
        }

        writer.Write((ushort)0);
        return stream.ToArray();
    }

    private static ushort GetTypeKind(TypeSymbol type) =>
        type.Name switch
        {
            "Boolean" or "Char" or "Integer" or "String" or "Nil" => 7,
            _ when type.Name.Contains('[') => 6,
            _ when type is NamedTypeSymbol { IsInterface: true } => 2,
            _ => 1
        };

    private static ushort GetTypeFlags(TypeSymbol type)
    {
        ushort flags = 0;
        if (type is NamedTypeSymbol { IsRecord: true })
        {
            flags |= 1 << 0;
        }

        if (type is NamedTypeSymbol { IsInterface: true })
        {
            flags |= 1 << 1;
        }

        if (type.IsReferenceType)
        {
            flags |= 1 << 6;
        }
        else
        {
            flags |= 1 << 5;
        }

        return flags;
    }

    private static ushort GetFieldFlags(FieldSymbol field)
    {
        ushort flags = 0;
        flags |= 1 << 0;
        if (field.IsStatic)
        {
            flags |= 1 << 4;
        }

        return flags;
    }

    private static uint GetMethodFlags(MethodSymbol method)
    {
        uint flags = 1u << 0;
        if (method.IsStatic)
        {
            flags |= 1u << 4;
        }

        if (method.IsVirtual)
        {
            flags |= 1u << 5;
        }

        if (method.IsOverride)
        {
            flags |= 1u << 6;
        }

        if (method.IsExtern)
        {
            flags |= 1u << 16;
        }

        if (method.Name == "Main")
        {
            flags |= 1u << 12;
        }

        flags |= 1u << 15;
        return flags;
    }

    private static uint GetFieldId(FieldSymbol field, IReadOnlyList<FieldSymbol> fields) =>
        (uint)(Array.IndexOf(fields.ToArray(), field) + 1);

    private static uint GetMethodId(MethodSymbol method, IReadOnlyList<MethodSymbol> methods) =>
        (uint)(Array.IndexOf(methods.ToArray(), method) + 1);

    private static uint GetBaseTypeId(TypeSymbol type, IReadOnlyDictionary<string, uint> typeIds)
    {
        if (type is not NamedTypeSymbol { BaseType: { } baseType })
        {
            return 0u;
        }

        return typeIds.TryGetValue(baseType.Name, out var baseTypeId)
            ? baseTypeId
            : 0u;
    }

    private static IEnumerable<IlbInterfaceDispatchRow> BuildInterfaceDispatchRows(
        IReadOnlyList<TypeSymbol> types,
        IReadOnlyList<MethodSymbol> methods,
        IReadOnlyDictionary<string, uint> typeIds,
        IReadOnlyDictionary<string, uint> methodIds)
    {
        var namedTypes = types.OfType<NamedTypeSymbol>().ToArray();
        var namedTypeLookup = namedTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
        var rows = new List<IlbInterfaceDispatchRow>();
        var seenRows = new HashSet<string>(StringComparer.Ordinal);

        foreach (var concreteType in namedTypes.Where(type => !type.IsInterface && IsRuntimeConcreteType(type)))
        {
            foreach (var interfaceType in GetImplementedInterfaces(concreteType, namedTypeLookup))
            {
                foreach (var interfaceMethod in interfaceType.Methods.Where(method => !method.IsConstructor))
                {
                    var implementationMethod = FindInterfaceImplementation(concreteType, interfaceMethod, namedTypeLookup);
                    if (implementationMethod is null ||
                        !typeIds.TryGetValue(concreteType.Name, out var ownerTypeId) ||
                        !typeIds.TryGetValue(interfaceType.Name, out var interfaceTypeId) ||
                        !methodIds.TryGetValue(GetMethodKey(interfaceMethod), out var interfaceMethodId) ||
                        !methodIds.TryGetValue(GetMethodKey(implementationMethod), out var implementationMethodId))
                    {
                        continue;
                    }

                    var rowKey = $"{ownerTypeId}:{interfaceTypeId}:{interfaceMethodId}:{implementationMethodId}";
                    if (!seenRows.Add(rowKey))
                    {
                        continue;
                    }

                    rows.Add(new IlbInterfaceDispatchRow(ownerTypeId, interfaceTypeId, interfaceMethodId, implementationMethodId));
                }
            }
        }

        return rows;
    }

    private static bool IsRuntimeConcreteType(NamedTypeSymbol type)
    {
        if (SemanticFacts.IsOpenGenericDefinition(type))
        {
            return false;
        }

        if (type.TypeArguments is null || type.TypeArguments.Count == 0)
        {
            return true;
        }

        return type.TypeArguments.All(argument => argument is not TypeParameterSymbol);
    }

    private static IEnumerable<NamedTypeSymbol> GetImplementedInterfaces(
        NamedTypeSymbol concreteType,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<NamedTypeSymbol>();

        void EnqueueInterface(TypeSymbol interfaceType)
        {
            if (namedTypeLookup.TryGetValue(interfaceType.Name, out var resolved) &&
                resolved.IsInterface &&
                seen.Add(resolved.Name))
            {
                pending.Enqueue(resolved);
            }
        }

        NamedTypeSymbol? current = concreteType;
        while (current is not null)
        {
            foreach (var interfaceType in current.InterfaceTypes)
            {
                EnqueueInterface(interfaceType);
            }

            current = current.BaseType is not null && namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType)
                ? baseType
                : null;
        }

        while (pending.Count > 0)
        {
            var interfaceType = pending.Dequeue();
            yield return interfaceType;

            foreach (var baseInterface in interfaceType.InterfaceTypes)
            {
                EnqueueInterface(baseInterface);
            }
        }
    }

    private static MethodSymbol? FindInterfaceImplementation(
        NamedTypeSymbol concreteType,
        MethodSymbol interfaceMethod,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        for (NamedTypeSymbol? current = concreteType; current is not null; current = current.BaseType is not null && namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType) ? baseType : null)
        {
            var match = current.Methods.FirstOrDefault(candidate =>
                !candidate.IsConstructor &&
                AreInterfaceImplementationCompatible(interfaceMethod, candidate, namedTypeLookup));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool AreInterfaceImplementationCompatible(
        MethodSymbol contractMethod,
        MethodSymbol implementationMethod,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        if (!StringComparer.Ordinal.Equals(contractMethod.Name, implementationMethod.Name) ||
            contractMethod.Parameters.Count != implementationMethod.Parameters.Count)
        {
            return false;
        }

        for (var index = 0; index < contractMethod.Parameters.Count; index++)
        {
            if (contractMethod.Parameters[index].Type.Name != implementationMethod.Parameters[index].Type.Name ||
                contractMethod.Parameters[index].PassingKind != implementationMethod.Parameters[index].PassingKind)
            {
                return false;
            }
        }

        if (contractMethod.ReturnType.Name == implementationMethod.ReturnType.Name)
        {
            return true;
        }

        return IsCompatibleReferenceType(implementationMethod.ReturnType.Name, contractMethod.ReturnType.Name, namedTypeLookup);
    }

    private static bool AreMethodSignaturesEquivalent(MethodSymbol left, MethodSymbol right)
    {
        if (!StringComparer.Ordinal.Equals(left.Name, right.Name) ||
            left.Parameters.Count != right.Parameters.Count ||
            left.ReturnType.Name != right.ReturnType.Name)
        {
            return false;
        }

        for (var index = 0; index < left.Parameters.Count; index++)
        {
            if (left.Parameters[index].Type.Name != right.Parameters[index].Type.Name ||
                left.Parameters[index].PassingKind != right.Parameters[index].PassingKind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCompatibleReferenceType(
        string sourceTypeName,
        string targetTypeName,
        IReadOnlyDictionary<string, NamedTypeSymbol> namedTypeLookup)
    {
        if (sourceTypeName == targetTypeName || targetTypeName == "Object")
        {
            return true;
        }

        if (!namedTypeLookup.TryGetValue(sourceTypeName, out var sourceType) ||
            !namedTypeLookup.TryGetValue(targetTypeName, out var targetType))
        {
            return false;
        }

        if (targetType.IsInterface)
        {
            if (sourceType.IsInterface)
            {
                return GetImplementedInterfaces(sourceType, namedTypeLookup).Any(candidate => candidate.Name == targetTypeName);
            }

            return GetImplementedInterfaces(sourceType, namedTypeLookup).Any(candidate => candidate.Name == targetTypeName);
        }

        for (NamedTypeSymbol? current = sourceType; current is not null; current = current.BaseType is not null && namedTypeLookup.TryGetValue(current.BaseType.Name, out var baseType) ? baseType : null)
        {
            if (current.Name == targetTypeName)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetMethodKey(MethodSymbol method) =>
        $"{method.DeclaringTypeName ?? "<global>"}::{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}";

    private static string GetFieldKey(FieldSymbol field) =>
        $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}";

    private static string GetNamespace(TypeSymbol type) => string.Empty;
    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator >= 0 ? typeName[(separator + 1)..] : typeName;
    }

    private static void WriteHeader(BinaryWriter writer, uint sectionCount, uint directoryOffset, uint fileSize, bool isLibrary)
    {
        writer.Write(System.Text.Encoding.ASCII.GetBytes("ILB1"));
        writer.Write(MajorVersion);
        writer.Write(MinorVersion);
        writer.Write(HeaderSize);
        ushort flags = 0;
        if (isLibrary)
        {
            flags |= 1 << 2;
        }

        flags |= 1 << 3;
        writer.Write(flags);
        writer.Write(sectionCount);
        writer.Write(directoryOffset);
        writer.Write(fileSize);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(Guid.Empty.ToByteArray());
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0UL);
    }

    private static void WriteChecksums(byte[] bytes)
    {
        WriteUInt32(bytes, 24, ComputeChecksum(bytes, 24));
        WriteUInt32(bytes, 28, ComputeChecksum(bytes, 28));
    }

    private static uint ComputeChecksum(byte[] bytes, int skipOffset)
    {
        uint checksum = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index >= skipOffset && index < skipOffset + 4)
            {
                continue;
            }

            checksum = unchecked((checksum * 16777619) ^ bytes[index]);
        }

        return checksum;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static uint Align(uint value, uint alignment) =>
        (value + alignment - 1) / alignment * alignment;

    private sealed class IlbStringTableBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<string> _values = [];

        public int Count => _values.Count;
        public IReadOnlyList<string> Values => _values;

        public uint GetOrAdd(string value)
        {
            if (_ids.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var id = (uint)(_values.Count + 1);
            _values.Add(value);
            _ids[value] = id;
            return id;
        }
    }

    private sealed class IlbBlobTableBuilder
    {
        private readonly Dictionary<string, uint> _ids = new(StringComparer.Ordinal);
        private readonly List<byte[]> _values = [];

        public int Count => _values.Count;
        public IReadOnlyList<byte[]> Values => _values;

        public uint Add(byte[] blob)
        {
            var key = Convert.ToBase64String(blob);
            if (_ids.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var id = (uint)(_values.Count + 1);
            _values.Add(blob);
            _ids[key] = id;
            return id;
        }

        public uint GetId(byte[] blob) => Add(blob);
    }

    private sealed record IlbCodeFunctionInfo(uint FunctionId, uint CodeOffset, uint CodeSize, ushort RegisterCount, ushort ArgumentCount);
    private sealed record IlbCodeSectionInfo(byte[] SectionBytes, IReadOnlyDictionary<uint, IlbCodeFunctionInfo> FunctionsById);
    private sealed record IlbFunctionExceptionInfo(uint FunctionId, uint ExceptionStart, uint ExceptionCount);
    private sealed record IlbExceptionSectionInfo(byte[] SectionBytes, IReadOnlyDictionary<uint, IlbFunctionExceptionInfo> FunctionsById, uint RowCount);
    private sealed record IlbInterfaceDispatchRow(uint OwnerTypeId, uint InterfaceTypeId, uint InterfaceMethodId, uint ImplementationMethodId);
}

public sealed class BytecodeEmitter
{
    public BytecodeFunction Emit(uint functionId, IrFunction function, MethodSymbol method)
    {
        var instructions = new List<Instruction>();
        var labelOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var pendingBranches = new List<(int Index, string Label)>();
        var stringIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var stringLiterals = new List<string>();
        var arrayShapes = new List<BytecodeArrayShapeInfo>();
        var exceptionHandlers = new List<BytecodeExceptionHandlerInfo>();
        var debugVmIpRanges = new List<BytecodeDebugVmIpRange>();
        var nextScratchRegister = function.Registers.Count;
        var irVmIp = 0;

        foreach (var block in function.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                var bytecodeStart = instructions.Count;
                switch (instruction.OpCode)
                {
                    case IrOpCode.LoadConstant:
                        if (instruction.Operand is string text)
                        {
                            if (!stringIds.TryGetValue(text, out var stringId))
                            {
                                stringId = stringIds.Count + 1;
                                stringIds[text] = stringId;
                                stringLiterals.Add(text);
                            }

                            instructions.Add(new Instruction(
                                OpCode.LdStr,
                                instruction.Destination?.Index ?? 0,
                                0,
                                0,
                                stringId,
                                InstructionImmediateKind.StringTableIndex));
                        }
                        else if (instruction.Operand is MethodSymbol methodOperand)
                        {
                            instructions.Add(new Instruction(
                                OpCode.LdI32,
                                instruction.Destination?.Index ?? 0,
                                0,
                                0,
                                unchecked((int)ResolveFunctionId(methodOperand)),
                                InstructionImmediateKind.InlineInt32));
                        }
                        else
                        {
                            instructions.Add(new Instruction(
                                OpCode.LdI32,
                                instruction.Destination?.Index ?? 0,
                                0,
                                0,
                                Convert.ToInt32(instruction.Operand),
                                InstructionImmediateKind.InlineInt32));
                        }
                        break;
                    case IrOpCode.Copy:
                        instructions.Add(new Instruction(
                            OpCode.Mov,
                            instruction.Destination?.Index ?? 0,
                            ((IrValue?)instruction.Operand)?.Index ?? 0,
                            0));
                        break;
                    case IrOpCode.Add:
                        instructions.Add(EmitBinaryInstruction(OpCode.AddI32, instruction));
                        break;
                    case IrOpCode.ShiftLeft:
                        instructions.Add(EmitBinaryInstruction(OpCode.ShlI32, instruction));
                        break;
                    case IrOpCode.ShiftRight:
                        instructions.Add(EmitBinaryInstruction(OpCode.ShrI32, instruction));
                        break;
                    case IrOpCode.BitwiseAnd:
                        instructions.Add(EmitBinaryInstruction(OpCode.AndI32, instruction));
                        break;
                    case IrOpCode.BitwiseOr:
                        instructions.Add(EmitBinaryInstruction(OpCode.OrI32, instruction));
                        break;
                    case IrOpCode.BitwiseNot:
                        instructions.Add(new Instruction(
                            OpCode.NotI32,
                            instruction.Destination?.Index ?? 0,
                            ((IrValue?)instruction.Operand)?.Index ?? 0,
                            0));
                        break;
                    case IrOpCode.Subtract:
                        instructions.Add(EmitBinaryInstruction(OpCode.SubI32, instruction));
                        break;
                    case IrOpCode.Multiply:
                        instructions.Add(EmitBinaryInstruction(OpCode.MulI32, instruction));
                        break;
                    case IrOpCode.Divide:
                        instructions.Add(EmitBinaryInstruction(OpCode.DivI32, instruction));
                        break;
                    case IrOpCode.Modulo:
                        instructions.Add(EmitBinaryInstruction(OpCode.ModI32, instruction));
                        break;
                    case IrOpCode.CompareEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqI32, instruction));
                        break;
                    case IrOpCode.CompareNotEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeI32, instruction));
                        break;
                    case IrOpCode.CompareEqualString:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqStr, instruction));
                        break;
                    case IrOpCode.CompareNotEqualString:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeStr, instruction));
                        break;
                    case IrOpCode.CompareEqualReference:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpEqRef, instruction));
                        break;
                    case IrOpCode.CompareNotEqualReference:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpNeRef, instruction));
                        break;
                    case IrOpCode.TypeIsReference:
                    {
                        var target = (IrTypeCheckTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.IsTypeRef,
                            instruction.Destination?.Index ?? 0,
                            target.Value.Index,
                            0,
                            ResolveTypeId(target.TypeName)));
                        break;
                    }
                    case IrOpCode.AsReference:
                    {
                        var target = (IrTypeCheckTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.AsTypeRef,
                            instruction.Destination?.Index ?? 0,
                            target.Value.Index,
                            0,
                            ResolveTypeId(target.TypeName)));
                        break;
                    }
                    case IrOpCode.ConcatString:
                        instructions.Add(EmitBinaryInstruction(OpCode.ConcatStr, instruction));
                        break;
                    case IrOpCode.ReplaceString:
                    {
                        var target = (IrStringReplaceTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.ReplaceStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.OldValue.Index,
                            target.NewValue.Index));
                        break;
                    }
                    case IrOpCode.InsertString:
                    {
                        var target = (IrStringInsertTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.InsertStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.Index.Index,
                            target.Value.Index));
                        break;
                    }
                    case IrOpCode.RemoveString:
                    {
                        var target = (IrStringRemoveTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.RemoveStr,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.Index.Index,
                            target.Length.Index));
                        break;
                    }
                    case IrOpCode.ToUpperString:
                        instructions.Add(new Instruction(
                            OpCode.ToUpperStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.ToLowerString:
                        instructions.Add(new Instruction(
                            OpCode.ToLowerStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimString:
                        instructions.Add(new Instruction(
                            OpCode.TrimStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimStartString:
                        instructions.Add(new Instruction(
                            OpCode.TrimStartStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TrimEndString:
                        instructions.Add(new Instruction(
                            OpCode.TrimEndStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.ParseStringToInteger:
                        instructions.Add(new Instruction(
                            OpCode.StrToI32,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.TryParseStringToInteger:
                    {
                        var target = (IrStringTryParseTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.TryStrToI32,
                            instruction.Destination!.Index,
                            target.Source.Index,
                            target.ParsedValue.Index));
                        break;
                    }
                    case IrOpCode.ConvertIntegerToString:
                        instructions.Add(new Instruction(
                            OpCode.I32ToStr,
                            instruction.Destination!.Index,
                            ((IrValue)instruction.Operand!).Index));
                        break;
                    case IrOpCode.StartsWithString:
                        instructions.Add(EmitBinaryInstruction(OpCode.StartsWithStr, instruction));
                        break;
                    case IrOpCode.EndsWithString:
                        instructions.Add(EmitBinaryInstruction(OpCode.EndsWithStr, instruction));
                        break;
                    case IrOpCode.ContainsString:
                        instructions.Add(EmitBinaryInstruction(OpCode.ContainsStr, instruction));
                        break;
                    case IrOpCode.IndexOfString:
                        instructions.Add(EmitBinaryInstruction(OpCode.IndexOfStr, instruction));
                        break;
                    case IrOpCode.LastIndexOfString:
                        instructions.Add(EmitBinaryInstruction(OpCode.LastIndexOfStr, instruction));
                        break;
                    case IrOpCode.CompareLess:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpLtI32, instruction));
                        break;
                    case IrOpCode.CompareLessOrEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpLeI32, instruction));
                        break;
                    case IrOpCode.CompareGreater:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpGtI32, instruction));
                        break;
                    case IrOpCode.CompareGreaterOrEqual:
                        instructions.Add(EmitBinaryInstruction(OpCode.CmpGeI32, instruction));
                        break;
                    case IrOpCode.NewObject:
                        instructions.Add(new Instruction(
                            OpCode.NewObj,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            ResolveTypeId((string)instruction.Operand!)));
                        break;
                    case IrOpCode.NewArray:
                        var newArrayOperand = (IrNewArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.NewArr,
                            instruction.Destination?.Index ?? 0,
                            newArrayOperand.LengthRegister.Index,
                            0,
                            ResolveTypeId(newArrayOperand.ElementTypeName)));
                        arrayShapes.Add(new BytecodeArrayShapeInfo(
                            instruction.Destination?.Index ?? 0,
                            newArrayOperand.Shape.Extents.Select(extent => extent.Index).ToArray()));
                        break;
                    case IrOpCode.Call:
                        var callTarget = (IrCallTarget)instruction.Operand!;
                        var firstArgumentRegister = callTarget.Receiver is null
                            ? EmitPackedArguments(instructions, callTarget.Arguments, ref nextScratchRegister)
                            : EmitPackedCallFrame(instructions, callTarget.Receiver, callTarget.Arguments, ref nextScratchRegister);
                        instructions.Add(new Instruction(
                            OpCode.Call,
                            instruction.Destination?.Index ?? 0,
                            firstArgumentRegister,
                            (ushort)(callTarget.Arguments.Count + (callTarget.Receiver is null ? 0 : 1)),
                            ResolveFunctionId(callTarget.Method)));
                        break;
                    case IrOpCode.CallVirtual:
                        var virtualCallTarget = (IrCallTarget)instruction.Operand!;
                        var callBaseRegister = EmitPackedCallFrame(
                            instructions,
                            virtualCallTarget.Receiver,
                            virtualCallTarget.Arguments,
                            ref nextScratchRegister);
                        instructions.Add(new Instruction(
                            OpCode.CallVirt,
                            instruction.Destination?.Index ?? 0,
                            callBaseRegister,
                            (ushort)virtualCallTarget.Arguments.Count,
                            ResolveFunctionId(virtualCallTarget.Method)));
                        break;
                    case IrOpCode.LoadStaticField:
                        var loadFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdSField,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            ResolveFieldId(loadFieldTarget.Field)));
                        break;
                    case IrOpCode.LoadField:
                        var loadInstanceFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdField,
                            instruction.Destination?.Index ?? 0,
                            loadInstanceFieldTarget.Receiver?.Index ?? 0,
                            0,
                            ResolveFieldId(loadInstanceFieldTarget.Field)));
                        break;
                    case IrOpCode.StoreField:
                        var storeInstanceFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StField,
                            storeInstanceFieldTarget.Receiver?.Index ?? 0,
                            instruction.Destination?.Index ?? 0,
                            0,
                            ResolveFieldId(storeInstanceFieldTarget.Field)));
                        break;
                    case IrOpCode.StoreStaticField:
                        var storeFieldTarget = (IrFieldTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StSField,
                            0,
                            instruction.Destination?.Index ?? 0,
                            0,
                            ResolveFieldId(storeFieldTarget.Field)));
                        break;
                    case IrOpCode.LoadElement:
                        var loadElementTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdElem,
                            instruction.Destination?.Index ?? 0,
                            loadElementTarget.Array.Index,
                            loadElementTarget.Index?.Index ?? 0));
                        break;
                    case IrOpCode.StoreElement:
                        var storeElementTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.StElem,
                            storeElementTarget.Array.Index,
                            storeElementTarget.Index?.Index ?? 0,
                            instruction.Destination?.Index ?? 0));
                        break;
                    case IrOpCode.LoadLength:
                        var lengthTarget = (IrArrayTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.LdLen,
                            instruction.Destination?.Index ?? 0,
                            lengthTarget.Array.Index,
                            0));
                        break;
                    case IrOpCode.SliceString:
                        var stringSliceTarget = (IrStringSliceTarget)instruction.Operand!;
                        instructions.Add(new Instruction(
                            OpCode.SliceStr,
                            instruction.Destination?.Index ?? 0,
                            stringSliceTarget.Source.Index,
                            stringSliceTarget.Start.Index,
                            stringSliceTarget.End.Index));
                        break;
                    case IrOpCode.Throw:
                        instructions.Add(new Instruction(
                            OpCode.Throw,
                            instruction.Destination?.Index ?? 0,
                            0,
                            0,
                            0));
                        break;
                    case IrOpCode.Rethrow:
                        instructions.Add(new Instruction(
                            OpCode.Rethrow,
                            0,
                            0,
                            0,
                            0));
                        break;
                    case IrOpCode.Branch:
                        pendingBranches.Add((instructions.Count, (string)instruction.Operand!));
                        instructions.Add(new Instruction(OpCode.Br));
                        break;
                    case IrOpCode.BranchIfFalse:
                        pendingBranches.Add((instructions.Count, (string)instruction.Operand!));
                        instructions.Add(new Instruction(
                            OpCode.BrFalse,
                            instruction.Destination?.Index ?? 0));
                        break;
                    case IrOpCode.Label:
                        labelOffsets[(string)instruction.Operand!] = instructions.Count;
                        break;
                    case IrOpCode.Return:
                        instructions.Add(new Instruction(OpCode.Ret));
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported IR opcode '{instruction.OpCode}'.");
                }

                var bytecodeEnd = instructions.Count == 0 ? bytecodeStart : Math.Max(instructions.Count - 1, bytecodeStart);
                debugVmIpRanges.Add(new BytecodeDebugVmIpRange(irVmIp, bytecodeStart, bytecodeEnd));
                irVmIp++;
            }
        }

        foreach (var pendingBranch in pendingBranches)
        {
            if (!labelOffsets.TryGetValue(pendingBranch.Label, out var targetOffset))
            {
                throw new InvalidOperationException($"Unknown branch label '{pendingBranch.Label}'.");
            }

            instructions[pendingBranch.Index] = instructions[pendingBranch.Index] with { Immediate = targetOffset };
        }

        foreach (var handler in function.ExceptionHandlers)
        {
            if (!labelOffsets.TryGetValue(handler.TryStartLabel, out var tryStart) ||
                !labelOffsets.TryGetValue(handler.TryEndLabel, out var tryEnd) ||
                !labelOffsets.TryGetValue(handler.HandlerStartLabel, out var handlerStart) ||
                !labelOffsets.TryGetValue(handler.HandlerEndLabel, out var handlerEnd))
            {
                throw new InvalidOperationException("Unknown exception handler label.");
            }

            var catchTypeId = handler.CatchTypeName is null
                ? 0u
                : unchecked((uint)ResolveTypeId(handler.CatchTypeName));

            exceptionHandlers.Add(new BytecodeExceptionHandlerInfo(
                (ushort)tryStart,
                (ushort)tryEnd,
                (ushort)handlerStart,
                (ushort)handlerEnd,
                handler.TargetRegister,
                catchTypeId));
        }

        return new BytecodeFunction(
            functionId,
            function.Name,
            (ushort)nextScratchRegister,
            (ushort)(method.Parameters.Count + (method.DeclaringTypeName is not null && !method.IsStatic ? 1 : 0)),
            method.HostImportKind,
            method.DllImport is null
                ? null
                : new BytecodeDllImportMetadata(
                    method.DllImport.LibraryName,
                    method.DllImport.EntryPoint,
                    method.DllImport.CallingConvention,
                    method.DllImport.StringReturnMarshalling,
                    method.DllImport.StringFreeEntryPoint),
            instructions,
            arrayShapes,
            exceptionHandlers,
            stringLiterals,
            debugVmIpRanges);
    }

    private static ushort EmitPackedArguments(List<Instruction> instructions, IReadOnlyList<IrValue> arguments, ref int nextScratchRegister)
    {
        if (arguments.Count == 0)
        {
            return 0;
        }

        if (AreContiguous(arguments))
        {
            return arguments[0].Index;
        }

        var firstRegister = (ushort)nextScratchRegister;
        for (var index = 0; index < arguments.Count; index++)
        {
            instructions.Add(new Instruction(
                OpCode.Mov,
                (ushort)(nextScratchRegister + index),
                arguments[index].Index,
                0,
                0));
        }

        nextScratchRegister += arguments.Count;
        return firstRegister;
    }

    private static ushort EmitPackedCallFrame(
        List<Instruction> instructions,
        IrValue? receiver,
        IReadOnlyList<IrValue> arguments,
        ref int nextScratchRegister)
    {
        if (receiver is null)
        {
            return EmitPackedArguments(instructions, arguments, ref nextScratchRegister);
        }

        if (arguments.Count == 0)
        {
            return receiver.Index;
        }

        if (AreContiguousCallFrame(receiver, arguments))
        {
            return receiver.Index;
        }

        var baseRegister = (ushort)nextScratchRegister;
        instructions.Add(new Instruction(
            OpCode.Mov,
            baseRegister,
            receiver.Index,
            0,
            0));
        nextScratchRegister++;

        for (var index = 0; index < arguments.Count; index++)
        {
            instructions.Add(new Instruction(
                OpCode.Mov,
                (ushort)(nextScratchRegister + index),
                arguments[index].Index,
                0,
                0));
        }

        nextScratchRegister += arguments.Count;
        return baseRegister;
    }

    private static bool AreContiguous(IReadOnlyList<IrValue> values)
    {
        if (values.Count == 0)
        {
            return false;
        }

        for (var index = 1; index < values.Count; index++)
        {
            if (values[index].Index != values[index - 1].Index + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreContiguousCallFrame(IrValue receiver, IReadOnlyList<IrValue> arguments) =>
        arguments.Count == 0
            ? true
            : receiver.Index + 1 == arguments[0].Index && AreContiguous(arguments);

    private static Instruction EmitBinaryInstruction(OpCode opCode, IrInstruction instruction)
    {
        var operands = ((IrValue Left, IrValue Right))instruction.Operand!;
        return new Instruction(opCode, instruction.Destination?.Index ?? 0, operands.Left.Index, operands.Right.Index, 0);
    }

    public BytecodeModule EmitModule(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> types, Lowerer lowerer)
    {
        var methodList = methods.ToArray();
        var fieldList = fields.ToArray();
        var loweredMethods = methodList
            .Select(method =>
            {
                try
                {
                    return (Method: method, Ir: lowerer.Lower(method));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to lower method '{method.DeclaringTypeName ?? "<global>"}.{method.Name}' with {method.Parameters.Count} parameter(s). {ex.Message}",
                        ex);
                }
            })
            .ToArray();
        var typeList = CollectReferencedTypesFromIr(loweredMethods, fieldList, types).ToArray();
        var functionEntries = methodList
            .Select((method, index) => (method, functionId: (uint)(index + 1)))
            .ToArray();
        var functionIds = functionEntries
            .ToDictionary(pair => GetMethodKey(pair.method), pair => pair.functionId, StringComparer.Ordinal);
        var fieldIds = fieldList
            .Select((field, index) => (field, fieldId: (uint)(index + 1)))
            .ToDictionary(pair => GetFieldKey(pair.field), pair => pair.fieldId, StringComparer.Ordinal);
        var typeIds = typeList
            .Select((type, index) => (type, typeId: (uint)(index + 1)))
            .ToDictionary(pair => pair.type.Name, pair => pair.typeId, StringComparer.Ordinal);

        var functions = new List<BytecodeFunction>();
        uint nextFunctionId = 1;

        foreach (var (method, ir) in loweredMethods)
        {
            functions.Add(Emit(nextFunctionId++, ir, method, functionEntries, functionIds, fieldIds, typeIds));
        }

        var moduleShapes = functions
            .SelectMany(function => function.ArrayShapes.Select(shape => new BytecodeModuleArrayShapeInfo(function.FunctionId, shape.ArrayRegister, shape.ExtentRegisters)))
            .ToArray();
        var moduleExceptionHandlers = functions
            .SelectMany(function => function.ExceptionHandlers.Select(handler => new BytecodeModuleExceptionHandlerInfo(
                function.FunctionId,
                handler.TryStart,
                handler.TryEnd,
                handler.HandlerStart,
                handler.HandlerEnd,
                handler.TargetRegister,
                handler.CatchTypeId)))
            .ToArray();

        return new BytecodeModule(functions, moduleShapes, moduleExceptionHandlers, typeList);
    }

    private static TypeSymbol[] CollectReferencedTypesFromIr(
        IEnumerable<(MethodSymbol Method, IrFunction Ir)> loweredMethods,
        IEnumerable<FieldSymbol> fields,
        IEnumerable<TypeSymbol> declaredTypes)
    {
        var typeList = CollectReferencedTypes(loweredMethods.Select(entry => entry.Method), fields, declaredTypes)
            .ToDictionary(type => type.Name, type => type, StringComparer.Ordinal);
        var declaredTypeList = declaredTypes.ToArray();

        void AddOrPreferRicher(TypeSymbol? candidate)
        {
            if (candidate is null)
            {
                return;
            }

            if (!typeList.TryGetValue(candidate.Name, out var existing))
            {
                typeList[candidate.Name] = candidate;
                return;
            }

            if (candidate is NamedTypeSymbol && existing is not NamedTypeSymbol)
            {
                typeList[candidate.Name] = candidate;
                return;
            }

            if (candidate is not NamedTypeSymbol candidateNamed || existing is not NamedTypeSymbol existingNamed)
            {
                return;
            }

            var candidateScore =
                candidateNamed.Methods.Count +
                candidateNamed.Fields.Count +
                candidateNamed.Properties.Count +
                candidateNamed.InterfaceTypes.Count +
                (candidateNamed.BaseType is null ? 0 : 1) +
                (candidateNamed.GenericParameters?.Count ?? 0) +
                (candidateNamed.TypeArguments?.Count ?? 0);
            var existingScore =
                existingNamed.Methods.Count +
                existingNamed.Fields.Count +
                existingNamed.Properties.Count +
                existingNamed.InterfaceTypes.Count +
                (existingNamed.BaseType is null ? 0 : 1) +
                (existingNamed.GenericParameters?.Count ?? 0) +
                (existingNamed.TypeArguments?.Count ?? 0);

            if (candidateScore > existingScore)
            {
                typeList[candidate.Name] = candidate;
            }
        }

        void AddByName(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            if (typeList.TryGetValue(typeName, out var existing))
            {
                AddOrPreferRicher(existing);
                return;
            }

            var resolvedType = ResolveTypeReferencePreservingClosedGenerics(typeName, [.. typeList.Values, .. declaredTypeList]);
            if (resolvedType is not null)
            {
                AddOrPreferRicher(resolvedType);
            }
        }

        void AddMethodSurface(MethodSymbol? method)
        {
            if (method is null)
            {
                return;
            }

            AddByName(method.DeclaringTypeName);
            if (method.ReturnType != TypeSymbol.Void)
            {
                AddOrPreferRicher(method.ReturnType);
            }

            foreach (var parameter in method.Parameters)
            {
                AddOrPreferRicher(parameter.Type);
            }
        }

        foreach (var (_, ir) in loweredMethods)
        {
            foreach (var instruction in ir.Blocks.SelectMany(block => block.Instructions))
            {
                switch (instruction.OpCode)
                {
                    case IrOpCode.NewObject:
                        AddByName(instruction.Operand as string);
                        break;
                    case IrOpCode.NewArray:
                        AddByName(((IrNewArrayTarget)instruction.Operand!).ElementTypeName);
                        break;
                    case IrOpCode.Call:
                    case IrOpCode.CallVirtual:
                    {
                        var callTarget = (IrCallTarget)instruction.Operand!;
                        AddMethodSurface(callTarget.Method);
                        if (callTarget.Receiver is not null)
                        {
                            AddOrPreferRicher(callTarget.Receiver.Type);
                        }

                        foreach (var argument in callTarget.Arguments)
                        {
                            AddOrPreferRicher(argument.Type);
                        }

                        break;
                    }
                    case IrOpCode.LoadField:
                    case IrOpCode.LoadStaticField:
                    case IrOpCode.StoreField:
                    case IrOpCode.StoreStaticField:
                    {
                        var fieldTarget = (IrFieldTarget)instruction.Operand!;
                        AddByName(fieldTarget.Field.DeclaringTypeName);
                        AddOrPreferRicher(fieldTarget.Field.Type);
                        if (fieldTarget.Receiver is not null)
                        {
                            AddOrPreferRicher(fieldTarget.Receiver.Type);
                        }

                        break;
                    }
                    case IrOpCode.LoadConstant when instruction.Operand is MethodSymbol methodOperand:
                        AddMethodSurface(methodOperand);
                        break;
                    case IrOpCode.TypeIsReference:
                    case IrOpCode.AsReference:
                    {
                        var typeCheckTarget = (IrTypeCheckTarget)instruction.Operand!;
                        AddByName(typeCheckTarget.TypeName);
                        AddOrPreferRicher(typeCheckTarget.Value.Type);
                        break;
                    }
                }
            }

            foreach (var handler in ir.ExceptionHandlers)
            {
                AddByName(handler.CatchTypeName);
            }
        }

        return typeList.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
    }

    private static TypeSymbol[] CollectReferencedTypes(IEnumerable<MethodSymbol> methods, IEnumerable<FieldSymbol> fields, IEnumerable<TypeSymbol> declaredTypes)
    {
        var declaredTypeList = declaredTypes.ToArray();
        var types = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);

        void AddOrPreferRicherType(TypeSymbol candidate)
        {
            if (!types.TryGetValue(candidate.Name, out var existing))
            {
                types[candidate.Name] = candidate;
                return;
            }

            if (candidate is NamedTypeSymbol && existing is not NamedTypeSymbol)
            {
                types[candidate.Name] = candidate;
                return;
            }

            if (candidate is not NamedTypeSymbol candidateNamed || existing is not NamedTypeSymbol existingNamed)
            {
                return;
            }

            if (candidateNamed.GenericDefinition is not null && existingNamed.GenericDefinition is null)
            {
                types[candidate.Name] = candidate;
                return;
            }

            var candidateScore =
                candidateNamed.Methods.Count +
                candidateNamed.Fields.Count +
                candidateNamed.Properties.Count +
                candidateNamed.InterfaceTypes.Count +
                (candidateNamed.BaseType is null ? 0 : 1) +
                (candidateNamed.GenericParameters?.Count ?? 0) +
                (candidateNamed.TypeArguments?.Count ?? 0);
            var existingScore =
                existingNamed.Methods.Count +
                existingNamed.Fields.Count +
                existingNamed.Properties.Count +
                existingNamed.InterfaceTypes.Count +
                (existingNamed.BaseType is null ? 0 : 1) +
                (existingNamed.GenericParameters?.Count ?? 0) +
                (existingNamed.TypeArguments?.Count ?? 0);

            if (candidateScore > existingScore)
            {
                types[candidate.Name] = candidate;
            }
        }

        foreach (var type in declaredTypeList)
        {
            AddOrPreferRicherType(type);
        }

        foreach (var field in fields)
        {
            AddOrPreferRicherType(field.Type);
            if (!string.IsNullOrEmpty(field.DeclaringTypeName) &&
                ResolveTypeReferencePreservingClosedGenerics(field.DeclaringTypeName, declaredTypeList) is { } resolvedFieldDeclaringType)
            {
                AddOrPreferRicherType(resolvedFieldDeclaringType);
            }
        }

        foreach (var method in methods)
        {
            if (!string.IsNullOrEmpty(method.DeclaringTypeName) &&
                ResolveTypeReferencePreservingClosedGenerics(method.DeclaringTypeName, declaredTypeList) is { } resolvedMethodDeclaringType)
            {
                AddOrPreferRicherType(resolvedMethodDeclaringType);
            }

            if (method.ReturnType != TypeSymbol.Void)
            {
                AddOrPreferRicherType(method.ReturnType);
            }

            foreach (var parameter in method.Parameters)
            {
                AddOrPreferRicherType(parameter.Type);
            }
        }

        return types.Values.OrderBy(type => type.Name, StringComparer.Ordinal).ToArray();
    }

    private static TypeSymbol? ResolveTypeReferencePreservingClosedGenerics(string typeName, IEnumerable<TypeSymbol> knownTypes)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var knownTypeArray = knownTypes.ToArray();
        if (!TryParseConstructedTypeReference(typeName, out var genericTypeName, out var genericArgumentNames))
        {
            return SemanticFacts.ResolveTypeReference(typeName, knownTypeArray);
        }

        var resolvedArguments = genericArgumentNames
            .Select(argumentName => ResolveTypeReferencePreservingClosedGenerics(argumentName, knownTypeArray))
            .ToArray();
        if (resolvedArguments.Any(argument => argument is null))
        {
            return SemanticFacts.ResolveTypeReference(typeName, knownTypeArray);
        }

        var simpleTypeName = genericTypeName.Contains('.')
            ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
            : genericTypeName;
        var definition = knownTypeArray
            .OfType<NamedTypeSymbol>()
            .FirstOrDefault(candidate =>
                candidate.Name == simpleTypeName &&
                candidate.GenericArity == resolvedArguments.Length &&
                candidate.GenericDefinition is null);
        if (definition is null)
        {
            return SemanticFacts.ResolveTypeReference(typeName, knownTypeArray);
        }

        return ConstructClosedGenericTypeLocal(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypeArray);
    }

    private static NamedTypeSymbol ConstructClosedGenericTypeLocal(
        NamedTypeSymbol definition,
        IReadOnlyList<TypeSymbol> typeArguments,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (definition.GenericParameters is null || definition.GenericParameters.Count != typeArguments.Count)
        {
            return definition;
        }

        var substitution = definition.GenericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, StringComparer.Ordinal);

        TypeSymbol Substitute(TypeSymbol type)
        {
            if (substitution.TryGetValue(type.Name, out var replacement))
            {
                return replacement;
            }

            if (type is NamedTypeSymbol namedType &&
                namedType.GenericDefinition is not null &&
                namedType.TypeArguments is { Count: > 0 })
            {
                var substitutedArguments = namedType.TypeArguments.Select(Substitute).ToArray();
                return ConstructClosedGenericTypeLocal(namedType.GenericDefinition, substitutedArguments, knownTypes);
            }

            if (TryParseConstructedTypeReference(type.Name, out var nestedGenericTypeName, out var nestedGenericArgumentNames))
            {
                var substitutedArguments = nestedGenericArgumentNames
                    .Select(argumentName =>
                    {
                        if (substitution.TryGetValue(argumentName, out var substitutedArgument))
                        {
                            return substitutedArgument;
                        }

                        return ResolveTypeReferencePreservingClosedGenerics(argumentName, knownTypes) ?? new TypeSymbol(argumentName, true);
                    })
                    .Select(Substitute)
                    .ToArray();
                var simpleNestedTypeName = nestedGenericTypeName.Contains('.')
                    ? nestedGenericTypeName[(nestedGenericTypeName.LastIndexOf('.') + 1)..]
                    : nestedGenericTypeName;
                var genericDefinition = knownTypes
                    .OfType<NamedTypeSymbol>()
                    .FirstOrDefault(candidate =>
                        candidate.Name == simpleNestedTypeName &&
                        candidate.GenericArity == substitutedArguments.Length &&
                        candidate.GenericDefinition is null);
                if (genericDefinition is not null)
                {
                    return ConstructClosedGenericTypeLocal(genericDefinition, substitutedArguments, knownTypes);
                }
            }

            return type;
        }

        var closedName = $"{definition.Name}<{string.Join(", ", typeArguments.Select(argument => argument.Name))}>";
        var fields = definition.Fields
            .Select(field => field with
            {
                Type = Substitute(field.Type),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var methods = definition.Methods
            .Select(method => method with
            {
                ReturnType = Substitute(method.ReturnType),
                Parameters = method.Parameters
                    .Select(parameter => parameter with
                    {
                        Type = Substitute(parameter.Type)
                    })
                    .ToArray(),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var properties = definition.Properties
            .Select(property => property with
            {
                Type = Substitute(property.Type),
                IndexParameter = property.IndexParameter is null
                    ? null
                    : property.IndexParameter with
                    {
                        Type = Substitute(property.IndexParameter.Type)
                    },
                GetterMethod = property.GetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => method.Name == property.GetterMethod.Name && method.Parameters.Count == property.GetterMethod.Parameters.Count),
                SetterMethod = property.SetterMethod is null
                    ? null
                    : methods.FirstOrDefault(method => method.Name == property.SetterMethod.Name && method.Parameters.Count == property.SetterMethod.Parameters.Count),
                ReadField = property.ReadField is null
                    ? null
                    : fields.FirstOrDefault(field => field.Name == property.ReadField.Name),
                WriteField = property.WriteField is null
                    ? null
                    : fields.FirstOrDefault(field => field.Name == property.WriteField.Name),
                DeclaringTypeName = closedName
            })
            .ToArray();

        return new NamedTypeSymbol(
            closedName,
            definition.IsReferenceType,
            definition.IsRecord,
            definition.IsInterface,
            definition.BaseType is null ? null : Substitute(definition.BaseType),
            definition.InterfaceTypes.Select(Substitute).ToArray(),
            methods,
            fields,
            definition.Constants,
            properties,
            definition.GenericArity,
            null,
            definition,
            typeArguments,
            definition.IsDelegate);
    }

    private static bool TryParseConstructedTypeReference(string displayName, out string genericTypeName, out IReadOnlyList<string> genericArgumentNames)
    {
        genericTypeName = string.Empty;
        genericArgumentNames = [];
        var lessThanIndex = displayName.IndexOf('<');
        if (lessThanIndex <= 0 || !displayName.EndsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        genericTypeName = displayName[..lessThanIndex].Trim();
        genericArgumentNames = SplitGenericArgumentNames(displayName[(lessThanIndex + 1)..^1]);
        return genericArgumentNames.Count > 0;
    }

    private static IReadOnlyList<string> SplitGenericArgumentNames(string value)
    {
        var arguments = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    arguments.Add(value[start..index].Trim());
                    start = index + 1;
                    break;
            }
        }

        var finalArgument = value[start..].Trim();
        if (!string.IsNullOrEmpty(finalArgument))
        {
            arguments.Add(finalArgument);
        }

        return arguments;
    }

    private BytecodeFunction Emit(uint functionId, IrFunction function, MethodSymbol method, IReadOnlyList<(MethodSymbol method, uint functionId)> functionEntries, IReadOnlyDictionary<string, uint> functionIds, IReadOnlyDictionary<string, uint> fieldIds, IReadOnlyDictionary<string, uint> typeIds)
    {
        _functionEntries = functionEntries;
        _functionIds = functionIds;
        _fieldIds = fieldIds;
        _typeIds = typeIds;
        return Emit(functionId, function, method);
    }

    private IReadOnlyList<(MethodSymbol method, uint functionId)> _functionEntries = [];
    private IReadOnlyDictionary<string, uint> _functionIds = new Dictionary<string, uint>();
    private IReadOnlyDictionary<string, uint> _fieldIds = new Dictionary<string, uint>();
    private IReadOnlyDictionary<string, uint> _typeIds = new Dictionary<string, uint>();

    private int ResolveFunctionId(MethodSymbol? method)
    {
        static bool IsRicherMethodLocal(MethodSymbol candidate, MethodSymbol existing)
        {
            static int CountOpenGenericMarkers(TypeSymbol type) =>
                type.Name.Contains("<T", StringComparison.Ordinal) ||
                type.Name.Contains(", T", StringComparison.Ordinal) ||
                type.Name.EndsWith("<T>", StringComparison.Ordinal)
                    ? 1
                    : 0;

            static int Score(MethodSymbol value) =>
                value.Parameters.Sum(parameter => CountOpenGenericMarkers(parameter.Type)) +
                CountOpenGenericMarkers(value.ReturnType);

            return Score(candidate) < Score(existing);
        }

        if (method is null)
        {
            return 0;
        }

        if (_functionIds.TryGetValue(GetMethodKey(method), out var functionId))
        {
            return (int)functionId;
        }

        var targetDeclaringType = GetSimpleDeclaringTypeName(method.DeclaringTypeName);
        var candidates = _functionEntries
            .Where(entry =>
                entry.method.Name == method.Name &&
                entry.method.Parameters.Count == method.Parameters.Count &&
                GetSimpleDeclaringTypeName(entry.method.DeclaringTypeName) == targetDeclaringType)
            .ToArray();

        if (candidates.Length == 0)
        {
            return 0;
        }

        var exactDeclaringTypeCandidates = candidates
            .Where(entry => string.Equals(entry.method.DeclaringTypeName, method.DeclaringTypeName, StringComparison.Ordinal))
            .ToArray();

        if (exactDeclaringTypeCandidates.Length > 0)
        {
            var bestExactCandidate = exactDeclaringTypeCandidates[0];
            for (var index = 1; index < exactDeclaringTypeCandidates.Length; index++)
            {
                if (IsRicherMethodLocal(exactDeclaringTypeCandidates[index].method, bestExactCandidate.method))
                {
                    bestExactCandidate = exactDeclaringTypeCandidates[index];
                }
            }

            return (int)bestExactCandidate.functionId;
        }

        if (!string.IsNullOrEmpty(method.DeclaringTypeName) && method.DeclaringTypeName.Contains('<', StringComparison.Ordinal))
        {
            return 0;
        }

        var bestCandidate = candidates[0];
        for (var index = 1; index < candidates.Length; index++)
        {
            if (IsRicherMethodLocal(candidates[index].method, bestCandidate.method))
            {
                bestCandidate = candidates[index];
            }
        }

        return (int)bestCandidate.functionId;
    }

    private int ResolveFieldId(FieldSymbol? field)
    {
        if (field is null)
        {
            return 0;
        }

        return _fieldIds.TryGetValue(GetFieldKey(field), out var fieldId) ? (int)fieldId : 0;
    }

    private int ResolveTypeId(string typeName)
        => _typeIds.TryGetValue(typeName, out var typeId) ? (int)typeId : 0;

    private static string GetMethodKey(MethodSymbol method) =>
        $"{method.DeclaringTypeName ?? "<global>"}::{method.Name}({string.Join(",", method.Parameters.Select(parameter => parameter.Type.Name))}):{method.ReturnType.Name}";

    private static string GetFieldKey(FieldSymbol field) =>
        $"{field.DeclaringTypeName ?? "<global>"}::{field.Name}";

    private static bool TryParseMethodKey(string key, out string declaringTypeName, out string methodName, out int parameterCount)
    {
        declaringTypeName = string.Empty;
        methodName = string.Empty;
        parameterCount = 0;

        var separatorIndex = key.IndexOf("::", StringComparison.Ordinal);
        var slashIndex = key.LastIndexOf('/');
        if (separatorIndex < 0 || slashIndex <= separatorIndex + 2)
        {
            return false;
        }

        declaringTypeName = key[..separatorIndex];
        methodName = key[(separatorIndex + 2)..slashIndex];
        return int.TryParse(key[(slashIndex + 1)..], out parameterCount);
    }

    private static string GetSimpleDeclaringTypeName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
        {
            return "<global>";
        }

        var lessThanIndex = displayName.IndexOf('<');
        return lessThanIndex >= 0
            ? displayName[..lessThanIndex]
            : displayName;
    }
}
