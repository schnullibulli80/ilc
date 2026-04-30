namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public abstract record Symbol(string Name);

internal interface IIndexedSymbolList<TSymbol> : IReadOnlyList<TSymbol>
    where TSymbol : Symbol
{
    IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> ByName { get; }

    IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> ByDeclaringType { get; }
}

internal sealed class IndexedSymbolList<TSymbol>(
    IReadOnlyList<TSymbol> symbols,
    Func<TSymbol, string?> getDeclaringType) : IIndexedSymbolList<TSymbol>
    where TSymbol : Symbol
{
    private IReadOnlyDictionary<string, IReadOnlyList<TSymbol>>? byName;
    private IReadOnlyDictionary<string, IReadOnlyList<TSymbol>>? byDeclaringType;

    public IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> ByName =>
        byName ??= BuildLookup(symbols, symbol => symbol.Name);

    public IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> ByDeclaringType =>
        byDeclaringType ??= BuildLookup(symbols, getDeclaringType);

    public int Count => symbols.Count;

    public TSymbol this[int index] => symbols[index];

    public IEnumerator<TSymbol> GetEnumerator() => symbols.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    private static IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> BuildLookup(
        IEnumerable<TSymbol> symbols,
        Func<TSymbol, string?> getKey)
    {
        var lookup = new Dictionary<string, IReadOnlyList<TSymbol>>(StringComparer.Ordinal);
        foreach (var group in symbols
            .Select(symbol => (Symbol: symbol, Key: getKey(symbol)))
            .Where(item => !string.IsNullOrEmpty(item.Key))
            .GroupBy(item => item.Key!, item => item.Symbol, StringComparer.Ordinal))
        {
            lookup[group.Key] = group.ToArray();
        }

        return lookup;
    }
}

public static class SymbolLists
{
    public static IReadOnlyList<TypeSymbol> CreateTypes(IEnumerable<TypeSymbol> symbols)
    {
        var indexedTypes = new IndexedSymbolList<TypeSymbol>(symbols as IReadOnlyList<TypeSymbol> ?? symbols.ToArray(), _ => null);
        _ = indexedTypes.ByName;
        return indexedTypes;
    }

    public static IReadOnlyList<MethodSymbol> CreateMethods(IEnumerable<MethodSymbol> symbols) =>
        new IndexedSymbolList<MethodSymbol>(symbols as IReadOnlyList<MethodSymbol> ?? symbols.ToArray(), method => method.DeclaringTypeName);

    public static IReadOnlyList<FieldSymbol> CreateFields(IEnumerable<FieldSymbol> symbols) =>
        new IndexedSymbolList<FieldSymbol>(symbols as IReadOnlyList<FieldSymbol> ?? symbols.ToArray(), field => field.DeclaringTypeName);

    public static IReadOnlyList<PropertySymbol> CreateProperties(IEnumerable<PropertySymbol> symbols) =>
        new IndexedSymbolList<PropertySymbol>(symbols as IReadOnlyList<PropertySymbol> ?? symbols.ToArray(), property => property.DeclaringTypeName);

    public static IReadOnlyList<ConstantSymbol> CreateConstants(IEnumerable<ConstantSymbol> symbols) =>
        new IndexedSymbolList<ConstantSymbol>(symbols as IReadOnlyList<ConstantSymbol> ?? symbols.ToArray(), constant => constant.DeclaringTypeName);
}

public record TypeSymbol(string Name, bool IsReferenceType) : Symbol(Name)
{
    public static readonly TypeSymbol Unknown = new("<unknown>", false);
    public static readonly TypeSymbol Object = new("Object", true);
    public static readonly TypeSymbol Void = new("Void", false);
    public static readonly TypeSymbol Boolean = new("Boolean", false);
    public static readonly TypeSymbol Char = new("Char", false);
    public static readonly TypeSymbol Integer = new("Integer", false);
    public static readonly TypeSymbol UInt128 = new("UInt128", false);
    public static readonly TypeSymbol UInt256 = new("UInt256", false);
    public static readonly TypeSymbol UInt512 = new("UInt512", false);
    public static readonly TypeSymbol UInt1024 = new("UInt1024", false);
    public static readonly TypeSymbol UInt2048 = new("UInt2048", false);
    public static readonly TypeSymbol String = new("String", true);
    public static readonly TypeSymbol Nil = new("Nil", true);

    public static IReadOnlyList<TypeSymbol> BuiltInTypes { get; } =
    [
        Object,
        Void,
        Boolean,
        Char,
        Integer,
        UInt128,
        UInt256,
        UInt512,
        UInt1024,
        UInt2048,
        String,
        Nil
    ];

    public static IReadOnlyList<TypeSymbol> BuiltInScalarTypes { get; } =
    [
        Boolean,
        Char,
        Integer,
        UInt128,
        UInt256,
        UInt512,
        UInt1024,
        UInt2048,
        String,
        Nil
    ];
}

public sealed record TypeParameterSymbol(string Name) : TypeSymbol(Name, true);

public enum ParameterPassingKind
{
    Value,
    Out,
    Ref,
    In,
    Params
}

public enum HostImportKind
{
    None,
    ConsoleWrite,
    ConsoleWriteLine,
    ConsoleReadLine,
    EnvironmentGetCommandLineArgs,
    EnvironmentGetCurrentDirectory,
    EnvironmentGetEnvironmentVariable,
    EnvironmentSetEnvironmentVariable,
    EnvironmentGetUserName,
    EnvironmentGetMachineName,
    EnvironmentGetHomeDirectory,
    EnvironmentGetTempDirectory,
    ClockGetMonotonicMillisecondsText,
    ClockGetWallMillisecondsText,
    ClockGetWallDateTimeText,
    ExceptionGetCurrentStackTrace,
    FileExists,
    FileReadAllText,
    FileWriteAllText,
    FileAppendAllText,
    PathCombine,
    PathGetFileName,
    PathGetDirectoryName,
    PathGetExtension,
    TcpConnect,
    TcpReadLine,
    TcpWriteLine,
    TcpClose,
    HttpGetString,
    WebSocketConnect,
    WebSocketReceiveText,
    WebSocketSendText,
    WebSocketClose,
    ThreadSleep,
    ThreadGetCurrentManagedId,
    MutexCreate,
    MutexWaitOne,
    MutexRelease,
    MutexClose,
    ThreadStartRunnable,
    ThreadJoin,
    ThreadIsAlive,
    DelegateBind,
    DelegateInvoke
}

public enum NativeCallingConvention
{
    Cdecl,
    StdCall
}

public enum NativeStringReturnMarshalling
{
    None,
    Utf8Owned
}

public sealed record DllImportMetadata(
    string LibraryName,
    string EntryPoint,
    NativeCallingConvention CallingConvention,
    NativeStringReturnMarshalling StringReturnMarshalling = NativeStringReturnMarshalling.None,
    string? StringFreeEntryPoint = null);

public sealed record ParameterSymbol(string Name, TypeSymbol Type, ParameterPassingKind PassingKind = ParameterPassingKind.Value) : Symbol(Name);

public sealed record FieldSymbol(
    string Name,
    TypeSymbol Type,
    string? DeclaringTypeName = null,
    bool IsStatic = false,
    FieldDeclarationSyntax? Declaration = null) : Symbol(Name);

public sealed record ConstantSymbol(
    string Name,
    TypeSymbol Type,
    object? Value,
    string? DeclaringTypeName = null,
    bool IsStatic = false) : Symbol(Name);

public sealed record PropertySymbol(
    string Name,
    TypeSymbol Type,
    FieldSymbol? ReadField,
    FieldSymbol? WriteField,
    ParameterSymbol? IndexParameter = null,
    MethodSymbol? GetterMethod = null,
    MethodSymbol? SetterMethod = null,
    bool IsGetterPrivate = false,
    bool IsSetterPrivate = false,
    bool IsInitOnly = false,
    string? DeclaringTypeName = null,
    bool IsStatic = false,
    bool IsIndexer = false,
    PropertyDeclarationSyntax? Declaration = null) : Symbol(Name);

public sealed record MethodSymbol(
    string Name,
    TypeSymbol ReturnType,
    IReadOnlyList<ParameterSymbol> Parameters,
    string? DeclaringTypeName = null,
    bool IsStatic = false,
    MethodDeclarationSyntax? Declaration = null,
    bool IsConstructor = false,
    bool IsSynthetic = false,
    IReadOnlyList<MemberSyntax>? SyntheticMembers = null,
    bool IsExtern = false,
    bool IsVirtual = false,
    bool IsOverride = false,
    HostImportKind HostImportKind = HostImportKind.None,
    DllImportMetadata? DllImport = null,
    LambdaExpressionSyntax? LambdaSource = null) : Symbol(Name);

public sealed record EnumerablePatternResolution(
    TypeSymbol ElementType,
    TypeSymbol EnumeratorType,
    MethodSymbol GetEnumeratorMethod,
    MethodSymbol MoveNextMethod,
    MethodSymbol CurrentGetterMethod);

public sealed record CompilationUnitSymbol(
    string? Namespace,
    IReadOnlyList<TypeSymbol> Types,
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<ConstantSymbol> Constants,
    IReadOnlyList<GlobalVariableSymbol> Globals,
    MethodSymbol? EntryPoint)
{
    private IReadOnlyList<MethodSymbol>? allMethods;
    private IReadOnlyList<FieldSymbol>? allFields;
    private IReadOnlyList<PropertySymbol>? allProperties;
    private IReadOnlyList<ConstantSymbol>? allConstants;
    private IReadOnlyDictionary<string, IReadOnlyList<MethodSymbol>>? methodsByName;
    private IReadOnlyDictionary<string, IReadOnlyList<MethodSymbol>>? methodsByDeclaringType;
    private IReadOnlyDictionary<string, IReadOnlyList<FieldSymbol>>? fieldsByDeclaringType;
    private IReadOnlyDictionary<string, IReadOnlyList<PropertySymbol>>? propertiesByDeclaringType;
    private IReadOnlyDictionary<string, IReadOnlyList<ConstantSymbol>>? constantsByDeclaringType;

    public IReadOnlyList<MethodSymbol> GetAllMethods() =>
        allMethods ??= new IndexedSymbolList<MethodSymbol>([
            .. Methods,
            .. GetConcreteNamedTypes().SelectMany(type => type.Methods)
        ], method => method.DeclaringTypeName);

    public IReadOnlyList<FieldSymbol> GetAllFields() =>
        allFields ??= new IndexedSymbolList<FieldSymbol>([
            .. GetConcreteNamedTypes().SelectMany(type => type.Fields)
        ], field => field.DeclaringTypeName);

    public IReadOnlyList<PropertySymbol> GetAllProperties() =>
        allProperties ??= new IndexedSymbolList<PropertySymbol>([
            .. GetConcreteNamedTypes().SelectMany(type => type.Properties)
        ], property => property.DeclaringTypeName);

    public IReadOnlyList<ConstantSymbol> GetAllConstants() =>
        allConstants ??= new IndexedSymbolList<ConstantSymbol>([
            .. Constants,
            .. GetConcreteNamedTypes().SelectMany(type => type.Constants)
        ], constant => constant.DeclaringTypeName);

    public IReadOnlyDictionary<string, IReadOnlyList<MethodSymbol>> GetMethodsByName() =>
        methodsByName ??= GetAllMethods() is IIndexedSymbolList<MethodSymbol> indexedMethods
            ? indexedMethods.ByName
            : BuildLookup(GetAllMethods(), method => method.Name);

    public IReadOnlyDictionary<string, IReadOnlyList<MethodSymbol>> GetMethodsByDeclaringType() =>
        methodsByDeclaringType ??= GetAllMethods() is IIndexedSymbolList<MethodSymbol> indexedMethods
            ? indexedMethods.ByDeclaringType
            : BuildLookup(GetAllMethods(), method => method.DeclaringTypeName);

    public IReadOnlyDictionary<string, IReadOnlyList<FieldSymbol>> GetFieldsByDeclaringType() =>
        fieldsByDeclaringType ??= GetAllFields() is IIndexedSymbolList<FieldSymbol> indexedFields
            ? indexedFields.ByDeclaringType
            : BuildLookup(GetAllFields(), field => field.DeclaringTypeName);

    public IReadOnlyDictionary<string, IReadOnlyList<PropertySymbol>> GetPropertiesByDeclaringType() =>
        propertiesByDeclaringType ??= GetAllProperties() is IIndexedSymbolList<PropertySymbol> indexedProperties
            ? indexedProperties.ByDeclaringType
            : BuildLookup(GetAllProperties(), property => property.DeclaringTypeName);

    public IReadOnlyDictionary<string, IReadOnlyList<ConstantSymbol>> GetConstantsByDeclaringType() =>
        constantsByDeclaringType ??= GetAllConstants() is IIndexedSymbolList<ConstantSymbol> indexedConstants
            ? indexedConstants.ByDeclaringType
            : BuildLookup(GetAllConstants(), constant => constant.DeclaringTypeName);

    private IEnumerable<NamedTypeSymbol> GetConcreteNamedTypes() =>
        Types.OfType<NamedTypeSymbol>()
            .Where(type => !SemanticFacts.IsOpenGenericDefinition(type));

    private static IReadOnlyDictionary<string, IReadOnlyList<TSymbol>> BuildLookup<TSymbol>(
        IEnumerable<TSymbol> symbols,
        Func<TSymbol, string?> getKey)
    {
        var lookup = new Dictionary<string, IReadOnlyList<TSymbol>>(StringComparer.Ordinal);
        foreach (var group in symbols
            .Select(symbol => (Symbol: symbol, Key: getKey(symbol)))
            .Where(item => !string.IsNullOrEmpty(item.Key))
            .GroupBy(item => item.Key!, item => item.Symbol, StringComparer.Ordinal))
        {
            lookup[group.Key] = group.ToArray();
        }

        return lookup;
    }
}

public sealed record GlobalVariableSymbol(
    string Name,
    TypeSymbol Type,
    bool HasInitializer) : Symbol(Name);

public sealed record NamedTypeSymbol(
    string Name,
    bool IsReferenceType,
    bool IsRecord,
    bool IsInterface,
    TypeSymbol? BaseType,
    IReadOnlyList<TypeSymbol> InterfaceTypes,
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<FieldSymbol> Fields,
    IReadOnlyList<ConstantSymbol> Constants,
    IReadOnlyList<PropertySymbol> Properties,
    int GenericArity = 0,
    IReadOnlyList<TypeParameterSymbol>? GenericParameters = null,
    NamedTypeSymbol? GenericDefinition = null,
    IReadOnlyList<TypeSymbol>? TypeArguments = null,
    bool IsDelegate = false,
    ProjectorExpressionSyntax? ProjectorSource = null) : TypeSymbol(Name, IsReferenceType);

public enum NameResolutionKind
{
    Unknown,
    LocalOrGlobal,
    Type,
    MethodGroup,
    Field,
    Constant
}

public sealed record NameResolution(
    NameResolutionKind Kind,
    string DisplayName,
    TypeSymbol? Type = null,
    MethodSymbol? Method = null,
    FieldSymbol? Field = null,
    ConstantSymbol? Constant = null);

public sealed record InvocationResolution(MethodSymbol Method, TypeSymbol? ReceiverType = null, bool IsVirtual = false);

public sealed record MemberResolution(
    string DisplayName,
    TypeSymbol? Type = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? Method = null,
    ConstantSymbol? Constant = null);
