namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public abstract record Symbol(string Name);

public record TypeSymbol(string Name, bool IsReferenceType) : Symbol(Name)
{
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
    public IReadOnlyList<MethodSymbol> GetAllMethods() =>
        [
            .. Methods,
            .. Types.OfType<NamedTypeSymbol>()
                .Where(type => !SemanticFacts.IsOpenGenericDefinition(type))
                .SelectMany(type => type.Methods)
        ];

    public IReadOnlyList<FieldSymbol> GetAllFields() =>
        [
            .. Types.OfType<NamedTypeSymbol>()
                .Where(type => !SemanticFacts.IsOpenGenericDefinition(type))
                .SelectMany(type => type.Fields)
        ];

    public IReadOnlyList<PropertySymbol> GetAllProperties() =>
        [
            .. Types.OfType<NamedTypeSymbol>()
                .Where(type => !SemanticFacts.IsOpenGenericDefinition(type))
                .SelectMany(type => type.Properties)
        ];

    public IReadOnlyList<ConstantSymbol> GetAllConstants() =>
        [
            .. Constants,
            .. Types.OfType<NamedTypeSymbol>()
                .Where(type => !SemanticFacts.IsOpenGenericDefinition(type))
                .SelectMany(type => type.Constants)
        ];
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
