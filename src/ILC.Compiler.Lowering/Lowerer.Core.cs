namespace ILC.Compiler.Lowering;

using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Collections;
using System.Reflection;

public sealed partial class Lowerer
{
    private sealed class DebugVariableBuilder(string name, TypeSymbol type, ushort registerIndex, int vmIpStart, IrDebugVariableKind kind)
    {
        public string Name { get; } = name;
        public TypeSymbol Type { get; } = type;
        public ushort RegisterIndex { get; } = registerIndex;
        public int VmIpStart { get; } = vmIpStart;
        public int? VmIpEnd { get; private set; }
        public IrDebugVariableKind Kind { get; } = kind;

        public IrDebugVariable ToSymbol(int defaultVmIpEnd) =>
            new(Name, Type, RegisterIndex, VmIpStart, VmIpEnd ?? defaultVmIpEnd, Kind);
    }

    private readonly IReadOnlyList<MethodSymbol> _knownMethods;
    private readonly IReadOnlyList<FieldSymbol> _knownFields;
    private readonly IReadOnlyList<ConstantSymbol> _knownConstants;
    private readonly IReadOnlyList<PropertySymbol> _knownProperties;
    private readonly IReadOnlyList<TypeSymbol> _knownTypes;
    private readonly IReadOnlyList<MethodSymbol> _knownLambdaMethods;
    private readonly Dictionary<LambdaExpressionSyntax, MethodSymbol> _lambdaMethodBySyntax = new(ReferenceComparer<LambdaExpressionSyntax>.Instance);
    private readonly Dictionary<MethodLookupKey, MethodSymbol[]> _methodsByDeclaringTypeName;
    private static readonly Dictionary<Type, PropertyInfo[]> SyntaxNodePropertyCache = new();
    private static readonly object SyntaxNodePropertyCacheLock = new();
    private readonly LoweringProfiler? _profiler;
    private readonly Stack<(string BreakLabel, string ContinueLabel)> _loopLabels = new();
    private readonly Dictionary<string, TypeSymbol?> _typeReferenceCache = new(StringComparer.Ordinal);
    private Dictionary<string, IrValue>? _localTypeCacheSource;
    private Dictionary<string, TypeSymbol>? _localTypeCache;
    private readonly Dictionary<object, TextSpan?> _syntaxSpanCache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodSymbol, CallParameterShape> _callParameterShapeCache = new(ReferenceEqualityComparer.Instance);
    private int _localTypeCacheCount = -1;
    private int _labelCounter;

    public Lowerer(IEnumerable<MethodSymbol>? knownMethods = null, IEnumerable<FieldSymbol>? knownFields = null, IEnumerable<TypeSymbol>? knownTypes = null, IEnumerable<PropertySymbol>? knownProperties = null, IEnumerable<ConstantSymbol>? knownConstants = null, LoweringProfiler? profiler = null)
    {
        _knownMethods = knownMethods as IReadOnlyList<MethodSymbol> ?? knownMethods?.ToArray() ?? [];
        _knownFields = knownFields as IReadOnlyList<FieldSymbol> ?? knownFields?.ToArray() ?? [];
        _knownConstants = knownConstants as IReadOnlyList<ConstantSymbol> ?? knownConstants?.ToArray() ?? [];
        _knownTypes = knownTypes?.ToArray() ?? [];
        _knownProperties = knownProperties as IReadOnlyList<PropertySymbol> ?? knownProperties?.ToArray() ?? [];
        _knownLambdaMethods = _knownMethods.Where(method => method.LambdaSource is not null).ToArray();
        foreach (var lambdaMethod in _knownLambdaMethods)
        {
            _lambdaMethodBySyntax.TryAdd(lambdaMethod.LambdaSource!, lambdaMethod);
        }

        _methodsByDeclaringTypeName = _knownMethods
            .Where(method => !string.IsNullOrWhiteSpace(method.DeclaringTypeName))
            .GroupBy(method => new MethodLookupKey(method.DeclaringTypeName!, method.Name, method.IsStatic))
            .ToDictionary(group => group.Key, group => group.ToArray());

        _profiler = profiler;
    }

    private readonly record struct MethodLookupKey(string DeclaringTypeName, string Name, bool IsStatic);
    private readonly record struct CallParameterShape(bool HasByRef, bool HasParams, int FixedParameterCount);

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => ReferenceEqualityComparer.Instance.GetHashCode(obj);
    }

    private static PropertyInfo[] GetCachedSyntaxNodeProperties(Type type)
    {
        lock (SyntaxNodePropertyCacheLock)
        {
            if (SyntaxNodePropertyCache.TryGetValue(type, out var properties))
            {
                return properties;
            }

            properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            SyntaxNodePropertyCache.Add(type, properties);
            return properties;
        }
    }

    public IrFunction Lower(MethodSymbol method)
    {
        using var methodProfile = ProfileMethod(method);
        using (Profile("NormalizeMethodForLowering"))
        {
            method = NormalizeMethodForLowering(method);
        }

        InvalidateLocalTypeCache();
        var registers = new List<IrValue>();
        var registerByName = new Dictionary<string, IrValue>(StringComparer.Ordinal);
        var localTypes = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        var arrayShapesByName = new Dictionary<string, IReadOnlyList<IrValue>>(StringComparer.Ordinal);
        var exceptionHandlers = new List<IrExceptionHandler>();
        var debugVariables = new List<DebugVariableBuilder>();
        var debugSourceMaps = new List<IrDebugSourceMap>();
        _labelCounter = 0;
        _loopLabels.Clear();

        ushort nextIndex = 0;
        if (method.DeclaringTypeName is not null && !method.IsStatic)
        {
            var selfType = new TypeSymbol(method.DeclaringTypeName, true);
            var selfRegister = new IrValue($"r{nextIndex}", selfType, nextIndex);
            registers.Add(selfRegister);
            SetRegisterLocalType(registerByName, "self", selfRegister);
            localTypes["self"] = selfType;
            debugVariables.Add(new DebugVariableBuilder("self", selfType, selfRegister.Index, 0, IrDebugVariableKind.Self));
            nextIndex++;
        }

        for (ushort parameterIndex = 0; parameterIndex < method.Parameters.Count; parameterIndex++, nextIndex++)
        {
            var parameter = method.Parameters[parameterIndex];
            var register = new IrValue($"r{nextIndex}", parameter.Type, nextIndex);
            registers.Add(register);
            SetRegisterLocalType(registerByName, parameter.Name, register);
            localTypes[parameter.Name] = parameter.Type;
            debugVariables.Add(new DebugVariableBuilder(parameter.Name, parameter.Type, register.Index, 0, IrDebugVariableKind.Parameter));
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
                using var syntheticBodyProfile = Profile("LowerSyntheticTopLevelBody");
                LowerSyntheticTopLevelBody(method, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps);
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
            using var bodyProfile = Profile("LowerMethodBody");
            LowerMethodBody(method.Declaration, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, false, method);
        }

        using var finalizeProfile = Profile("FinalizeIrFunction");
        var entryBlock = new IrBasicBlock("entry", instructions);
        var arrayShapes = instructions
            .Where(instruction => instruction.OpCode == IrOpCode.NewArray && instruction.Operand is IrNewArrayTarget)
            .Select(instruction => ((IrNewArrayTarget)instruction.Operand!).Shape)
            .ToArray();
        var lastVmIp = Math.Max(instructions.Count - 1, 0);
        return new IrFunction(
            method.Name,
            method.ReturnType,
            registers,
            [entryBlock],
            arrayShapes,
            exceptionHandlers,
            debugVariables.Select(variable => variable.ToSymbol(lastVmIp)).ToArray(),
            debugSourceMaps.ToArray());
    }

    private Dictionary<string, TypeSymbol> GetLocalTypes(Dictionary<string, IrValue> registerByName)
    {
        if (ReferenceEquals(_localTypeCacheSource, registerByName) &&
            _localTypeCacheCount == registerByName.Count &&
            _localTypeCache is not null)
        {
            return _localTypeCache;
        }

        _localTypeCacheSource = registerByName;
        _localTypeCacheCount = registerByName.Count;
        _localTypeCache = registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);
        return _localTypeCache;
    }

    private static Dictionary<string, TypeSymbol> GetLocalTypes(IReadOnlyDictionary<string, IrValue> registerByName) =>
        registerByName.ToDictionary(pair => pair.Key, pair => pair.Value.Type, StringComparer.Ordinal);

    private LoweringProfiler.ProfileScope Profile(string name) =>
        _profiler?.Enter(name) ?? default;

    private LoweringProfiler.ProfileScope ProfileMethod(MethodSymbol method) =>
        _profiler?.EnterMethod(method) ?? default;

    private T Profiled<T>(string name, Func<T> action)
    {
        using var profile = Profile(name);
        return action();
    }

    private TypeSymbol? ResolveKnownTypeReference(string displayName)
    {
        if (!_typeReferenceCache.TryGetValue(displayName, out var resolvedType))
        {
            resolvedType = SemanticFacts.ResolveTypeReference(displayName, _knownTypes);
            _typeReferenceCache.Add(displayName, resolvedType);
        }

        return resolvedType;
    }

    private void InvalidateLocalTypeCache()
    {
        _localTypeCacheSource = null;
        _localTypeCache = null;
        _localTypeCacheCount = -1;
    }

    private void SetRegisterLocalType(Dictionary<string, IrValue> registerByName, string name, IrValue register)
    {
        registerByName[name] = register;
        if (ReferenceEquals(_localTypeCacheSource, registerByName) && _localTypeCache is not null)
        {
            _localTypeCache[name] = register.Type;
            _localTypeCacheCount = registerByName.Count;
        }
    }

    private void RemoveRegisterLocalType(Dictionary<string, IrValue> registerByName, string name)
    {
        registerByName.Remove(name);
        if (ReferenceEquals(_localTypeCacheSource, registerByName) && _localTypeCache is not null)
        {
            _localTypeCache.Remove(name);
            _localTypeCacheCount = registerByName.Count;
        }
    }

    private MethodSymbol NormalizeMethodForLowering(MethodSymbol method)
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

        var openGenericMarkerCount = CountOpenGenericMarkers(method.ReturnType) +
            method.Parameters.Sum(parameter => CountOpenGenericMarkers(parameter.Type));
        if (openGenericMarkerCount == 0)
        {
            return method;
        }

        if (ResolveKnownTypeReference(method.DeclaringTypeName) is not NamedTypeSymbol declaringType)
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

    private void LowerMethodBody(
        MethodDeclarationSyntax declaration,
        Dictionary<string, IrValue> registerByName,
        Dictionary<string, TypeSymbol> localTypes,
        Dictionary<string, IReadOnlyList<IrValue>> arrayShapesByName,
        List<IrValue> registers,
        List<IrInstruction> instructions,
        IrValue? returnRegister,
        List<IrExceptionHandler> exceptionHandlers,
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps,
        bool inExceptionHandler = false,
        MethodSymbol? currentMethod = null)
    {
        if (declaration.ExpressionBody is not null)
        {
            var vmIpStart = instructions.Count;
            LowerReturnExpression(declaration.ExpressionBody, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, currentMethod);
            instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
            AddDebugSourceMap(debugSourceMaps, declaration.ExpressionBody, vmIpStart, instructions.Count - 1);
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
            hasTerminated = LowerStatement(statement, registerByName, localTypes, arrayShapesByName, registers, instructions, returnRegister, exceptionHandlers, debugVariables, debugSourceMaps, inExceptionHandler, currentMethod);
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
        List<IrExceptionHandler> exceptionHandlers,
        List<DebugVariableBuilder> debugVariables,
        List<IrDebugSourceMap> debugSourceMaps)
    {
        foreach (var member in method.SyntheticMembers ?? [])
        {
            var vmIpStart = instructions.Count;
            switch (member)
            {
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        var localType = BindSyntheticTopLevelType(declarator, localTypes, method);
                        var localRegister = new IrValue($"r{registers.Count}", localType, (ushort)registers.Count);
                        registers.Add(localRegister);
                        SetRegisterLocalType(registerByName, declarator.Identifier.Text, localRegister);
                        localTypes[declarator.Identifier.Text] = localType;
                        debugVariables.Add(new DebugVariableBuilder(declarator.Identifier.Text, localType, localRegister.Index, instructions.Count, IrDebugVariableKind.Local));

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

            if (member is not TopLevelConstantDeclarationSyntax)
            {
                AddDebugSourceMap(debugSourceMaps, member, vmIpStart, instructions.Count - 1);
            }
        }

        if (returnRegister is not null)
        {
            instructions.Add(new IrInstruction(IrOpCode.LoadConstant, returnRegister, 0));
        }

        instructions.Add(new IrInstruction(IrOpCode.Return, null, returnRegister));
    }
}
