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

public sealed record DllImportMetadata(
    string LibraryName,
    string EntryPoint,
    NativeCallingConvention CallingConvention);

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
    bool IsDelegate = false) : TypeSymbol(Name, IsReferenceType);

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

public enum BoundReceiverKind
{
    None,
    Local,
    Self,
    Type,
    Expression
}

public sealed record BoundReceiver(
    BoundReceiverKind Kind,
    TypeSymbol Type,
    string? LocalName = null,
    TypeSymbol? TargetType = null,
    ExpressionSyntax? SourceExpression = null);

public enum BoundMemberReadKind
{
    Local,
    Field,
    Property,
    Constant
}

public sealed record BoundMemberRead(
    BoundMemberReadKind Kind,
    string DisplayName,
    TypeSymbol Type,
    BoundReceiver? Receiver = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? GetterMethod = null,
    FieldSymbol? ReadField = null,
    ConstantSymbol? Constant = null,
    ExpressionSyntax? SourceExpression = null);

public sealed record BoundLengthRead(
    string DisplayName,
    TypeSymbol TargetType,
    BoundReceiver? Receiver = null,
    ExpressionSyntax? SourceExpression = null);

public sealed record BoundSliceRead(
    string DisplayName,
    TypeSymbol TargetType,
    BoundReceiver? Receiver = null,
    ExpressionSyntax? TargetExpression = null,
    RangeExpressionSyntax? Range = null);

public sealed record BoundElementRead(
    string DisplayName,
    TypeSymbol ElementType,
    BoundReceiver? Receiver = null,
    PropertySymbol? IndexerProperty = null,
    MethodSymbol? GetterMethod = null,
    FieldSymbol? ReadField = null,
    TypeSymbol? IndexedType = null,
    ExpressionSyntax? TargetExpression = null);

public sealed record BoundElementWrite(
    string DisplayName,
    TypeSymbol ElementType,
    BoundReceiver? Receiver = null,
    PropertySymbol? IndexerProperty = null,
    MethodSymbol? SetterMethod = null,
    FieldSymbol? WriteField = null,
    TypeSymbol? IndexedType = null,
    ExpressionSyntax? TargetExpression = null);

public enum BoundWriteTargetKind
{
    Local,
    Field,
    Property,
    ElementAccess
}

public sealed record BoundWriteTarget(
    BoundWriteTargetKind Kind,
    string DisplayName,
    TypeSymbol Type,
    BoundReceiver? Receiver = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? SetterMethod = null,
    FieldSymbol? WriteField = null,
    ExpressionSyntax? SourceExpression = null);

public enum BoundCallKind
{
    Direct,
    Virtual,
    Intrinsic,
    Constructor
}

public sealed record BoundCall(
    BoundCallKind Kind,
    string DisplayName,
    MethodSymbol Method,
    TypeSymbol ReturnType,
    BoundReceiver? Receiver = null,
    IReadOnlyList<TypeSymbol>? ArgumentTypes = null,
    ExpressionSyntax? SourceExpression = null);

public sealed class Binder
{
    public BindingResult Bind(SyntaxTree syntaxTree)
    {
        var diagnostics = new DiagnosticBag();
        foreach (var diagnostic in syntaxTree.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        var declaredTypeShells = new List<TypeSymbol>();
        var topLevelConstants = new List<ConstantSymbol>();
        var globals = new List<GlobalVariableSymbol>();
        var hasTopLevelStatements = false;

        foreach (var member in syntaxTree.Root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax classDeclaration:
                    declaredTypeShells.Add(new NamedTypeSymbol(
                        classDeclaration.Identifier.Text,
                        true,
                        classDeclaration.ClassKeyword.Kind == SyntaxKind.RecordKeyword,
                        false,
                        null,
                        [],
                        [],
                        [],
                        [],
                        [],
                        classDeclaration.TypeParameters?.Parameters.Count ?? 0));
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    declaredTypeShells.Add(new NamedTypeSymbol(
                        interfaceDeclaration.Identifier.Text,
                        true,
                        false,
                        true,
                        null,
                        [],
                        [],
                        [],
                        [],
                        [],
                        interfaceDeclaration.TypeParameters?.Parameters.Count ?? 0));
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    declaredTypeShells.Add(new NamedTypeSymbol(
                        delegateDeclaration.Identifier.Text,
                        true,
                        false,
                        false,
                        null,
                        [],
                        [],
                        [],
                        [],
                        [],
                        delegateDeclaration.TypeParameters?.Parameters.Count ?? 0));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    declaredTypeShells.Add(new TypeSymbol(enumDeclaration.Identifier.Text, false));
                    break;
            }
        }

        var provisionalDeclaredTypes = new List<TypeSymbol>();
        foreach (var member in syntaxTree.Root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax classDeclaration:
                    provisionalDeclaredTypes.Add(BindClass(classDeclaration, declaredTypeShells));
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    provisionalDeclaredTypes.Add(BindInterface(interfaceDeclaration, declaredTypeShells));
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    provisionalDeclaredTypes.Add(BindDelegate(delegateDeclaration, declaredTypeShells));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    provisionalDeclaredTypes.Add(ResolveDeclaredType(enumDeclaration.Identifier.Text, declaredTypeShells, false));
                    break;
            }
        }

        var declaredTypes = new List<TypeSymbol>();
        foreach (var member in syntaxTree.Root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax classDeclaration:
                    declaredTypes.Add(BindClass(classDeclaration, provisionalDeclaredTypes));
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    declaredTypes.Add(BindInterface(interfaceDeclaration, provisionalDeclaredTypes));
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    declaredTypes.Add(BindDelegate(delegateDeclaration, provisionalDeclaredTypes));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    declaredTypes.Add(ResolveDeclaredType(enumDeclaration.Identifier.Text, provisionalDeclaredTypes, false));
                    break;
            }
        }

        foreach (var constructedType in CollectConstructedGenericTypes(syntaxTree, declaredTypes))
        {
            if (declaredTypes.All(existing => existing.Name != constructedType.Name))
            {
                declaredTypes.Add(constructedType);
            }
        }

        AddConstructedGenericClosure(declaredTypes);

        var knownMethods = declaredTypes
            .OfType<NamedTypeSymbol>()
            .SelectMany(type => type.Methods)
            .ToArray();
        var knownFields = declaredTypes
            .OfType<NamedTypeSymbol>()
            .SelectMany(type => type.Fields)
            .ToArray();
        var knownConstants = declaredTypes
            .OfType<NamedTypeSymbol>()
            .SelectMany(type => type.Constants)
            .ToArray();
        var knownProperties = declaredTypes
            .OfType<NamedTypeSymbol>()
            .SelectMany(type => type.Properties)
            .ToArray();

        foreach (var member in syntaxTree.Root.Members)
        {
            var visibleConstants = knownConstants.Concat(topLevelConstants).ToArray();
            switch (member)
            {
                case EnumDeclarationSyntax enumDeclaration:
                    topLevelConstants.AddRange(BindEnumConstants(enumDeclaration, declaredTypes));
                    break;
                case TopLevelConstantDeclarationSyntax constantDeclaration:
                    foreach (var declarator in constantDeclaration.Declarators)
                    {
                        topLevelConstants.Add(new ConstantSymbol(
                            declarator.Identifier.Text,
                            declarator.TypeName is not null
                                ? BindType(declarator.TypeName, declaredTypes)
                                : SemanticFacts.InferExpressionType(
                                    declarator.Initializer,
                                    new Dictionary<string, TypeSymbol>(),
                                    knownMethods,
                                    knownFields,
                                    visibleConstants,
                                    knownProperties,
                                    null),
                            SemanticFacts.GetConstantValue(declarator.Initializer, new Dictionary<string, TypeSymbol>(), knownFields, visibleConstants, knownProperties, null),
                            null,
                            true));
                    }
                    break;
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        var type = declarator.TypeName is not null
                            ? BindType(declarator.TypeName, declaredTypes)
                            : SemanticFacts.InferExpressionType(
                                declarator.Initializer,
                                new Dictionary<string, TypeSymbol>(),
                                knownMethods,
                                knownFields,
                                visibleConstants,
                                knownProperties,
                                null);

                        globals.Add(new GlobalVariableSymbol(
                            declarator.Identifier.Text,
                            type,
                            declarator.Initializer is not null));
                    }
                    break;
                case TopLevelExpressionStatementSyntax:
                    hasTopLevelStatements = true;
                    break;
            }
        }

        if (globals.Count > 0)
        {
            hasTopLevelStatements = true;
        }

        var methods = new List<MethodSymbol>();
        if (hasTopLevelStatements || syntaxTree.Root.Members.Count == 0)
        {
            var syntheticMembers = syntaxTree.Root.Members
                .Where(member => member is TopLevelVariableDeclarationSyntax or TopLevelExpressionStatementSyntax)
                .ToArray();

            methods.Add(new MethodSymbol(
                "__TopLevelMain",
                TypeSymbol.Integer,
                [],
                null,
                true,
                null,
                false,
                true,
                syntheticMembers));
        }

        var lambdaArtifacts = CollectSyntheticLambdaArtifacts(syntaxTree.Root.Members, declaredTypes);
        foreach (var lambdaType in lambdaArtifacts.Types)
        {
            if (declaredTypes.All(existing => existing.Name != lambdaType.Name))
            {
                declaredTypes.Add(lambdaType);
            }
        }

        methods.AddRange(lambdaArtifacts.Methods);

        var allMethods = methods.Concat(knownMethods).ToArray();
        var allKnownConstants = knownConstants.Concat(topLevelConstants).ToArray();
        ValidateSemantics(syntaxTree.Root.Members, globals, topLevelConstants, declaredTypes, allMethods, knownFields, allKnownConstants, knownProperties, diagnostics);
        var explicitEntryPoints = knownMethods
            .Where(method => method.Name == "Main")
            .ToArray();

        MethodSymbol? entryPoint = null;
        if (explicitEntryPoints.Length == 1)
        {
            entryPoint = explicitEntryPoints[0];
        }
        else if (explicitEntryPoints.Length > 1)
        {
            diagnostics.Report(
                "ILC2000",
                "Multiple explicit Main methods were found. Exactly one valid entry point is required.",
                DiagnosticSeverity.Error,
                new TextSpan(0, 0));
        }
        else
        {
            entryPoint = methods.FirstOrDefault();
        }

        var symbol = new CompilationUnitSymbol(
            syntaxTree.Root.Namespace?.Name.ToDisplayString(),
            [.. TypeSymbol.BuiltInScalarTypes, .. declaredTypes],
            methods,
            topLevelConstants,
            globals,
            entryPoint);

        return new BindingResult(symbol, diagnostics);
    }

    private static void ValidateSemantics(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<GlobalVariableSymbol> globals,
        IReadOnlyList<ConstantSymbol> constants,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics)
    {
        var topLevelScope = globals.ToDictionary(global => global.Name, global => global.Type, StringComparer.Ordinal);

        foreach (var member in members)
        {
            switch (member)
            {
                case TopLevelConstantDeclarationSyntax constantDeclaration:
                    ValidateConstantDeclarators(constantDeclaration.Declarators, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                    break;
                case TopLevelExpressionStatementSyntax expressionStatement:
                    ValidateExpression(expressionStatement.Expression, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                    break;
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            ValidateExpression(declarator.Initializer, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                        }
                    }
                    break;
                case ClassDeclarationSyntax classDeclaration:
                    ValidateClassSemantics(classDeclaration, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, diagnostics);
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    ValidateInterfaceSemantics(interfaceDeclaration, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, diagnostics);
                    break;
                case DelegateDeclarationSyntax:
                    break;
                case EnumDeclarationSyntax:
                    break;
            }
        }
    }

    private static void ValidateClassSemantics(
        ClassDeclarationSyntax classDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics)
    {
        var declaredTypeName = classDeclaration.Identifier.Text;
        var typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
        if (classDeclaration.BaseType is not null)
        {
            var primaryType = SemanticFacts.ResolveTypeReference(classDeclaration.BaseType.ToDisplayString(), typeScope);
            if (primaryType is null)
            {
                diagnostics.Report(
                    "ILC2193",
                    $"Unknown inherited type '{classDeclaration.BaseType.ToDisplayString()}' for class '{declaredTypeName}'.",
                    DiagnosticSeverity.Error,
                    classDeclaration.BaseType.Parts[^1].Span);
            }
            else if (!IsReferenceClassOrInterfaceType(primaryType))
            {
                diagnostics.Report(
                    "ILC2194",
                    $"Inherited type '{primaryType.Name}' for class '{declaredTypeName}' must be a class, record, or interface type.",
                    DiagnosticSeverity.Error,
                    classDeclaration.BaseType.Parts[^1].Span);
            }
            else if (ResolveNamedType(primaryType, typeScope) is { IsInterface: false } namedPrimaryType &&
                CreatesTypeCycle(declaredTypeName, namedPrimaryType, typeScope))
            {
                diagnostics.Report(
                    "ILC2195",
                    $"Inheritance cycle detected for class '{declaredTypeName}'.",
                    DiagnosticSeverity.Error,
                    classDeclaration.Identifier.Span);
            }
        }

        foreach (var interfaceTypeName in classDeclaration.InterfaceTypes)
        {
            var interfaceType = SemanticFacts.ResolveTypeReference(interfaceTypeName.ToDisplayString(), typeScope);
            if (interfaceType is null)
            {
                diagnostics.Report(
                    "ILC2200",
                    $"Unknown interface '{interfaceTypeName.ToDisplayString()}' for class '{declaredTypeName}'.",
                    DiagnosticSeverity.Error,
                    interfaceTypeName.Parts[^1].Span);
                continue;
            }

            if (ResolveNamedType(interfaceType, typeScope) is not { IsInterface: true })
            {
                diagnostics.Report(
                    "ILC2201",
                    $"Implemented type '{interfaceType.Name}' for class '{declaredTypeName}' must be an interface.",
                    DiagnosticSeverity.Error,
                    interfaceTypeName.Parts[^1].Span);
            }
        }

        var (baseType, interfaceTypes) = ResolveClassInheritanceTargets(classDeclaration, typeScope);
        foreach (var interfaceType in interfaceTypes)
        {
            ValidateInterfaceImplementation(
                declaredTypeName,
                interfaceType,
                classDeclaration,
                typeScope,
                knownMethods,
                diagnostics);
        }

        var typeFields = FindFields(knownFields, classDeclaration.Identifier.Text);
        foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            ValidatePropertyDeclaration(property, classDeclaration.Identifier.Text, typeFields, knownTypes, diagnostics);
        }

        foreach (var constant in classDeclaration.Members.OfType<ConstantDeclarationSyntax>())
        {
            ValidateConstantDeclarators(constant.Declarators, new Dictionary<string, TypeSymbol>(StringComparer.Ordinal), knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
        }

        foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var locals = method.Parameters.ToDictionary(
                parameter => parameter.Identifier.Text,
                parameter => BindType(parameter.TypeName, typeScope),
                StringComparer.Ordinal);

            var boundMethod = FindMethod(knownMethods, classDeclaration.Identifier.Text, method.Identifier.Text, method.Parameters.Count);
            if (boundMethod is not null && !boundMethod.IsStatic)
            {
                locals["self"] = new TypeSymbol(classDeclaration.Identifier.Text, true);
            }

            ValidateMethodInheritanceModifiers(
                method,
                boundMethod,
                baseType,
                typeScope,
                knownMethods,
                declaredTypeName,
                diagnostics);

            if (method.Attributes.Any(attribute => IsDllImportAttribute(attribute)) && boundMethod is not null)
            {
                ValidateDllImportMethod(classDeclaration.Identifier.Text, method, boundMethod, diagnostics);
            }

            foreach (var field in typeFields.Where(field => field.IsStatic))
            {
                locals[field.Name] = field.Type;
            }

        if (method.ExpressionBody is not null)
        {
            if (boundMethod?.IsExtern == true)
            {
                diagnostics.Report(
                    "ILC2182",
                    $"Extern method '{classDeclaration.Identifier.Text}.{method.Identifier.Text}' must not declare a body.",
                    DiagnosticSeverity.Error,
                    method.Keyword.Span);
                continue;
            }

            if (boundMethod?.IsConstructor == true)
            {
                diagnostics.Report(
                    "ILC2114",
                    $"Constructor '{classDeclaration.Identifier.Text}' cannot declare an expression body.",
                    DiagnosticSeverity.Error,
                    method.Keyword.Span);
                continue;
            }

            ValidateExpression(method.ExpressionBody, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, boundMethod, diagnostics);
            continue;
        }

        if (boundMethod?.IsExtern == true)
        {
            if (method.Body is not null)
            {
                diagnostics.Report(
                    "ILC2182",
                    $"Extern method '{classDeclaration.Identifier.Text}.{method.Identifier.Text}' must not declare a body.",
                    DiagnosticSeverity.Error,
                    method.Keyword.Span);
            }
            else if (!method.Attributes.Any(attribute => IsDllImportAttribute(attribute)) && boundMethod.HostImportKind == HostImportKind.None)
            {
                diagnostics.Report(
                    "ILC2183",
                    $"Extern method '{classDeclaration.Identifier.Text}.{method.Identifier.Text}' does not map to a supported host service.",
                    DiagnosticSeverity.Error,
                    method.Keyword.Span);
            }

            continue;
        }

        if (method.Body is null)
        {
            continue;
        }

            ValidateStatements(
                method.Body.Statements,
                locals,
                typeScope,
                knownMethods,
                knownFields,
                knownConstants,
            knownProperties,
            boundMethod,
            false,
            false,
            diagnostics);
        }
    }

    private static void ValidateInterfaceSemantics(
        InterfaceDeclarationSyntax interfaceDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics)
    {
        var typeScope = knownTypes.Concat(BindTypeParameters(interfaceDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
        foreach (var baseInterfaceName in interfaceDeclaration.BaseInterfaces)
        {
            var resolvedInterface = SemanticFacts.ResolveTypeReference(baseInterfaceName.ToDisplayString(), typeScope);
            if (resolvedInterface is null)
            {
                diagnostics.Report(
                    "ILC2202",
                    $"Unknown base interface '{baseInterfaceName.ToDisplayString()}' for interface '{interfaceDeclaration.Identifier.Text}'.",
                    DiagnosticSeverity.Error,
                    baseInterfaceName.Parts[^1].Span);
                continue;
            }

            if (ResolveNamedType(resolvedInterface, typeScope) is not { IsInterface: true })
            {
                diagnostics.Report(
                    "ILC2203",
                    $"Base interface '{resolvedInterface.Name}' for interface '{interfaceDeclaration.Identifier.Text}' must itself be an interface.",
                    DiagnosticSeverity.Error,
                    baseInterfaceName.Parts[^1].Span);
            }
        }

        foreach (var member in interfaceDeclaration.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax:
                    diagnostics.Report(
                        "ILC2204",
                        $"Interface '{interfaceDeclaration.Identifier.Text}' cannot declare fields.",
                        DiagnosticSeverity.Error,
                        interfaceDeclaration.Identifier.Span);
                    break;
                case ConstantDeclarationSyntax:
                    diagnostics.Report(
                        "ILC2205",
                        $"Interface '{interfaceDeclaration.Identifier.Text}' cannot declare constants in the current bootstrap compiler.",
                        DiagnosticSeverity.Error,
                        interfaceDeclaration.Identifier.Span);
                    break;
                case MethodDeclarationSyntax method when method.Keyword.Kind == SyntaxKind.ConstructorKeyword:
                    diagnostics.Report(
                        "ILC2206",
                        $"Interface '{interfaceDeclaration.Identifier.Text}' cannot declare constructors.",
                        DiagnosticSeverity.Error,
                        method.Keyword.Span);
                    break;
                case MethodDeclarationSyntax method when method.Body is not null || method.ExpressionBody is not null:
                    diagnostics.Report(
                        "ILC2207",
                        $"Interface method '{interfaceDeclaration.Identifier.Text}.{method.Identifier.Text}' must not declare a body.",
                        DiagnosticSeverity.Error,
                        method.Keyword.Span);
                    break;
                case MethodDeclarationSyntax method when method.Attributes.Any(attribute => IsDllImportAttribute(attribute)):
                    diagnostics.Report(
                        "ILC2216",
                        $"Interface method '{interfaceDeclaration.Identifier.Text}.{method.Identifier.Text}' cannot declare DllImport metadata.",
                        DiagnosticSeverity.Error,
                        method.Keyword.Span);
                    break;
                case PropertyDeclarationSyntax property when
                    property.BeginKeyword is not null ||
                    property.GetterBody is not null ||
                    property.SetterBody is not null:
                    diagnostics.Report(
                        "ILC2208",
                        $"Interface property '{interfaceDeclaration.Identifier.Text}.{property.Identifier.Text}' must be declaration-only.",
                        DiagnosticSeverity.Error,
                        property.PropertyKeyword.Span);
                    break;
            }
        }
    }

    private static void ValidateConstantDeclarators(
        IReadOnlyList<ConstantDeclaratorSyntax> declarators,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        foreach (var declarator in declarators)
        {
            ValidateExpression(declarator.Initializer, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            if (!SemanticFacts.IsConstantExpression(declarator.Initializer, locals, knownFields, knownConstants, knownProperties, currentMethod))
            {
                diagnostics.Report(
                    "ILC2148",
                    $"Constant '{declarator.Identifier.Text}' must be initialized with a literal or constant value.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(declarator.Initializer, knownTypes));
                continue;
            }

            if (declarator.TypeName is not null)
            {
                var declaredType = BindType(declarator.TypeName, knownTypes);
                var initializerType = SemanticFacts.InferExpressionType(declarator.Initializer, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (declaredType != initializerType)
                {
                    diagnostics.Report(
                        "ILC2149",
                        $"Constant '{declarator.Identifier.Text}' declared as '{declaredType.Name}' but initialized with '{initializerType.Name}'.",
                        DiagnosticSeverity.Error,
                        declarator.Identifier.Span);
                }
            }
        }
    }

    private static void ValidatePropertyDeclaration(
        PropertyDeclarationSyntax property,
        string declaringTypeName,
        IReadOnlyList<FieldSymbol> typeFields,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        if (property.OpenBraceToken is not null)
        {
            return;
        }

        var propertyType = BindType(property.TypeName, knownTypes);
        var isStatic = property.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);
        var indexParameterType = property.IndexParameter is not null ? BindType(property.IndexParameter.TypeName, knownTypes) : null;
        if (property.ReadTarget is not null)
        {
            ValidatePropertyAccessorTarget(property.Identifier.Text, propertyType, indexParameterType, isStatic, property.ReadTarget, "read", declaringTypeName, typeFields, diagnostics);
        }

        if (property.WriteTarget is not null)
        {
            ValidatePropertyAccessorTarget(property.Identifier.Text, propertyType, indexParameterType, isStatic, property.WriteTarget, "write", declaringTypeName, typeFields, diagnostics);
        }
    }

    private static void ValidatePropertyAccessorTarget(
        string propertyName,
        TypeSymbol propertyType,
        TypeSymbol? indexParameterType,
        bool propertyIsStatic,
        QualifiedNameSyntax target,
        string accessorKind,
        string declaringTypeName,
        IReadOnlyList<FieldSymbol> typeFields,
        DiagnosticBag diagnostics)
    {
        var fieldName = target.Parts[^1].Text;
        var field = typeFields.FirstOrDefault(candidate => candidate.Name == fieldName);
        if (field is null)
        {
            diagnostics.Report(
                "ILC2117",
                $"Property '{propertyName}' references unknown {accessorKind} backing field '{target.ToDisplayString()}'.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return;
        }

        if (field.IsStatic != propertyIsStatic)
        {
            diagnostics.Report(
                "ILC2118",
                $"Property '{propertyName}' has a {accessorKind} backing field '{field.Name}' with incompatible static/instance semantics.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return;
        }

        if (indexParameterType is not null)
        {
            if (indexParameterType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2128",
                    $"Indexer '{propertyName}' must use Integer as its index parameter in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    target.Parts[^1].Span);
                return;
            }

            var expectedFieldType = new TypeSymbol($"{propertyType.Name}[]", true);
            if (field.Type != expectedFieldType)
            {
                diagnostics.Report(
                    "ILC2119",
                    $"Indexer '{propertyName}' type '{propertyType.Name}' does not match {accessorKind} backing field '{field.Name}' of type '{field.Type.Name}'.",
                    DiagnosticSeverity.Error,
                    target.Parts[^1].Span);
            }

            return;
        }

        if (field.Type != propertyType)
        {
            diagnostics.Report(
                "ILC2119",
                $"Property '{propertyName}' type '{propertyType.Name}' does not match {accessorKind} backing field '{field.Name}' of type '{field.Type.Name}'.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
        }
    }

    private static void ValidateStatements(
        IReadOnlyList<StatementSyntax> statements,
        Dictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        bool inExceptionHandler,
        bool inLoop,
        DiagnosticBag diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BlockStatementSyntax block:
                    ValidateStatements(block.Statements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    break;
                case LocalVariableDeclarationStatementSyntax localVariable:
                    foreach (var declarator in localVariable.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            ValidateExpressionForExpectedType(
                                declarator.Initializer,
                                declarator.TypeName is not null ? BindType(declarator.TypeName, knownTypes) : null,
                                locals,
                                knownTypes,
                                knownMethods,
                                knownFields,
                                knownConstants,
                                knownProperties,
                                currentMethod,
                                diagnostics);
                        }

                        locals[declarator.Identifier.Text] = declarator.TypeName is not null
                            ? BindType(declarator.TypeName, knownTypes)
                            : SemanticFacts.InferExpressionType(declarator.Initializer, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    }
                    break;
                case ReturnStatementSyntax returnStatement when returnStatement.Expression is not null:
                    ValidateExpression(returnStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case IncStatementSyntax incStatement:
                    ValidateIncDecStatement(incStatement.Keyword, incStatement.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case DecStatementSyntax decStatement:
                    ValidateIncDecStatement(decStatement.Keyword, decStatement.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case IncludeStatementSyntax includeStatement:
                    ValidateIncludeExcludeStatement(includeStatement.Keyword, includeStatement.Target, includeStatement.Value, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case ExcludeStatementSyntax excludeStatement:
                    ValidateIncludeExcludeStatement(excludeStatement.Keyword, excludeStatement.Target, excludeStatement.Value, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case RaiseStatementSyntax raiseStatement when raiseStatement.Expression is not null:
                    ValidateExpression(raiseStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case RaiseStatementSyntax raiseStatement when raiseStatement.Expression is null && !inExceptionHandler:
                    diagnostics.Report(
                        "ILC2133",
                        "Bare 'raise;' is only valid inside an except handler.",
                        DiagnosticSeverity.Error,
                        raiseStatement.Keyword.Span);
                    break;
                case ExpressionStatementSyntax expressionStatement:
                    ValidateExpression(expressionStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case BreakStatementSyntax breakStatement when !inLoop:
                    diagnostics.Report(
                        "ILC2143",
                        "'break' is only valid inside a loop.",
                        DiagnosticSeverity.Error,
                        breakStatement.BreakKeyword.Span);
                    break;
                case ContinueStatementSyntax continueStatement when !inLoop:
                    diagnostics.Report(
                        "ILC2144",
                        "'continue' is only valid inside a loop.",
                        DiagnosticSeverity.Error,
                        continueStatement.ContinueKeyword.Span);
                    break;
                case IfStatementSyntax ifStatement:
                    ValidateExpression(ifStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    ValidateStatements([ifStatement.ThenStatement], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    if (ifStatement.ElseStatement is not null)
                    {
                        ValidateStatements([ifStatement.ElseStatement], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    }
                    break;
                case WhileStatementSyntax whileStatement:
                    ValidateExpression(whileStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    ValidateStatements([whileStatement.Body], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics);
                    break;
                case RepeatStatementSyntax repeatStatement:
                    ValidateStatements(repeatStatement.Statements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics);
                    ValidateExpression(repeatStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case ForStatementSyntax forStatement:
                    var forLoopLocals = locals;
                    TypeSymbol? loopType = null;
                    if (forStatement.VarKeyword is not null)
                    {
                        forLoopLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                        {
                            [forStatement.Identifier.Text] = TypeSymbol.Integer
                        };
                    }
                    else if (!locals.TryGetValue(forStatement.Identifier.Text, out loopType))
                    {
                        diagnostics.Report(
                            "ILC2136",
                            $"Unknown for-loop variable '{forStatement.Identifier.Text}'.",
                            DiagnosticSeverity.Error,
                            forStatement.Identifier.Span);
                        break;
                    }

                    if (forStatement.VarKeyword is null && loopType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2137",
                            $"For-loop variable '{forStatement.Identifier.Text}' must be Integer, but was '{loopType!.Name}'.",
                            DiagnosticSeverity.Error,
                            forStatement.Identifier.Span);
                    }

                    ValidateExpression(forStatement.LowerBound, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    ValidateExpression(forStatement.UpperBound, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    if (forStatement.StepExpression is not null)
                    {
                        ValidateExpression(forStatement.StepExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }

                    if (SemanticFacts.InferExpressionType(forStatement.LowerBound, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2138",
                            $"For-loop lower bound for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.LowerBound, knownTypes));
                    }

                    if (SemanticFacts.InferExpressionType(forStatement.UpperBound, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2139",
                            $"For-loop upper bound for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.UpperBound, knownTypes));
                    }

                    if (forStatement.StepExpression is not null &&
                        SemanticFacts.InferExpressionType(forStatement.StepExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2189",
                            $"For-loop step for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.StepExpression, knownTypes));
                    }

                    ValidateStatements([forStatement.Body], forLoopLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics);
                    break;
                case ForeachStatementSyntax foreachStatement:
                    ValidateExpression(foreachStatement.Collection, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var collectionType = SemanticFacts.InferExpressionType(foreachStatement.Collection, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    TypeSymbol? elementType = null;
                    if (collectionType == TypeSymbol.String)
                    {
                        elementType = TypeSymbol.Char;
                    }
                    else if (SemanticFacts.IsArrayType(collectionType))
                    {
                        elementType = SemanticFacts.GetElementType(collectionType);
                    }
                    else if (SemanticFacts.IsSetType(collectionType))
                    {
                        elementType = SemanticFacts.GetSetElementType(collectionType);
                    }
                    else if (SemanticFacts.ResolveEnumerablePattern(collectionType, knownTypes) is { } enumerablePattern)
                    {
                        elementType = enumerablePattern.ElementType;
                    }

                    if (elementType is null)
                    {
                        diagnostics.Report(
                            "ILC2141",
                            $"Expression '{SemanticFacts.GetExpressionDisplayName(foreachStatement.Collection)}' is not enumerable in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(foreachStatement.Collection, knownTypes));
                        break;
                    }

                    var foreachLocals = locals;
                    TypeSymbol foreachType;
                    if (foreachStatement.VarKeyword is not null)
                    {
                        foreachType = elementType!;
                        foreachLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                        {
                            [foreachStatement.Identifier.Text] = foreachType
                        };
                    }
                    else if (!locals.TryGetValue(foreachStatement.Identifier.Text, out var resolvedForeachType))
                    {
                        diagnostics.Report(
                            "ILC2140",
                            $"Unknown foreach variable '{foreachStatement.Identifier.Text}'.",
                            DiagnosticSeverity.Error,
                            foreachStatement.Identifier.Span);
                        break;
                    }
                    else
                    {
                        foreachType = resolvedForeachType;
                    }

                    if (foreachType != elementType)
                    {
                        diagnostics.Report(
                            "ILC2142",
                            $"Foreach variable '{foreachStatement.Identifier.Text}' must be '{elementType.Name}', but was '{foreachType.Name}'.",
                            DiagnosticSeverity.Error,
                            foreachStatement.Identifier.Span);
                    }

                    ValidateStatements([foreachStatement.Body], foreachLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics);
                    break;
                case WithStatementSyntax withStatement:
                    ValidateExpression(withStatement.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var rewrittenWithBody = RewriteWithStatement(withStatement.Body, withStatement.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    ValidateStatements([rewrittenWithBody], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    break;
                case CaseStatementSyntax caseStatement:
                    ValidateExpression(caseStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var caseExpressionType = SemanticFacts.InferExpressionType(caseStatement.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (caseExpressionType != TypeSymbol.Integer && caseExpressionType != TypeSymbol.String && !SemanticFacts.IsEnumType(caseExpressionType))
                    {
                        diagnostics.Report(
                            "ILC2145",
                            $"Case expression '{SemanticFacts.GetExpressionDisplayName(caseStatement.Expression)}' must be Integer, String or Enum in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(caseStatement.Expression, knownTypes));
                    }

                    foreach (var clause in caseStatement.Clauses)
                    {
                        foreach (var label in clause.Labels)
                        {
                            ValidateExpression(label, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            ValidateCaseLabel(label, caseExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        }

                        if (clause.Guard is not null)
                        {
                            ValidateExpression(clause.Guard, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            var guardType = SemanticFacts.InferExpressionType(clause.Guard, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                            if (guardType != TypeSymbol.Boolean)
                            {
                                diagnostics.Report(
                                    "ILC2192",
                                    "Case clause guard must be Boolean.",
                                    DiagnosticSeverity.Error,
                                    GetExpressionDiagnosticSpan(clause.Guard, knownTypes));
                            }
                        }

                        ValidateStatements([clause.Body], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    }

                    ValidateStatements(caseStatement.ElseStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    break;
                case MatchStatementSyntax matchStatement:
                    ValidateExpression(matchStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var matchExpressionType = SemanticFacts.InferExpressionType(matchStatement.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (matchExpressionType != TypeSymbol.Integer &&
                        matchExpressionType != TypeSymbol.String &&
                        !SemanticFacts.IsEnumType(matchExpressionType) &&
                        !matchExpressionType.IsReferenceType)
                    {
                        diagnostics.Report(
                            "ILC2175",
                            $"Match expression '{SemanticFacts.GetExpressionDisplayName(matchStatement.Expression)}' must be Integer, String, Enum or reference-typed in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(matchStatement.Expression, knownTypes));
                    }

                    foreach (var arm in matchStatement.Arms)
                    {
                        var armLocals = locals;
                        if (arm.TypeName is not null)
                        {
                            var armType = SemanticFacts.ResolveTypeReference(arm.TypeName.ToDisplayString(), knownTypes);
                            if (armType is null)
                            {
                                diagnostics.Report(
                                    "ILC2131",
                                    $"Unknown type '{arm.TypeName.ToDisplayString()}' in match arm.",
                                    DiagnosticSeverity.Error,
                                    GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                            }
                            else if (!armType.IsReferenceType)
                            {
                                diagnostics.Report(
                                    "ILC2132",
                                    $"Match arm type '{arm.TypeName.ToDisplayString()}' must be a reference type.",
                                    DiagnosticSeverity.Error,
                                    GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                            }
                            else if (!SemanticFacts.IsCompatibleReferenceType(matchExpressionType, armType, knownTypes))
                            {
                                diagnostics.Report(
                                    "ILC2178",
                                    $"Typed match arm '{arm.TypeName.ToDisplayString()}' requires a compatible reference-typed match expression in the current bootstrap compiler.",
                                    DiagnosticSeverity.Error,
                                    GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                            }
                            else if (arm.Identifier is not null)
                            {
                                armLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                                {
                                    [arm.Identifier.Text] = armType
                                };
                            }
                        }
                        else if (!arm.IsWildcard)
                        {
                            foreach (var label in arm.Labels)
                            {
                                ValidateMatchLabel(label, matchExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            }
                        }

                        if (arm.Guard is not null)
                        {
                            ValidateExpression(arm.Guard, armLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            var guardType = SemanticFacts.InferExpressionType(arm.Guard, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                            if (guardType != TypeSymbol.Boolean)
                            {
                                diagnostics.Report(
                                    "ILC2179",
                                    "Match arm guard must be Boolean.",
                                    DiagnosticSeverity.Error,
                                    GetExpressionDiagnosticSpan(arm.Guard, knownTypes));
                            }
                        }

                        ValidateStatements([arm.Body], armLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    }

                    ValidateStatements(matchStatement.ElseStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    break;
                case TryStatementSyntax tryStatement:
                    ValidateStatements(tryStatement.TryStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    if (tryStatement.ExceptKeyword is not null)
                    {
                        foreach (var clause in tryStatement.ExceptionClauses)
                        {
                            var clauseType = SemanticFacts.ResolveTypeReference(clause.TypeName.ToDisplayString(), knownTypes);
                            if (clauseType is null)
                            {
                                diagnostics.Report(
                                    "ILC2131",
                                    $"Unknown type '{clause.TypeName.ToDisplayString()}' in exception handler.",
                                    DiagnosticSeverity.Error,
                                    GetReferenceDiagnosticSpan(clause.TypeName, knownTypes));
                                continue;
                            }

                            if (!clauseType.IsReferenceType)
                            {
                                diagnostics.Report(
                                    "ILC2135",
                                    $"Exception handler type '{clause.TypeName.ToDisplayString()}' must be a reference type.",
                                    DiagnosticSeverity.Error,
                                    GetReferenceDiagnosticSpan(clause.TypeName, knownTypes));
                                continue;
                            }

                            var clauseLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                            {
                                [clause.Identifier.Text] = clauseType
                            };
                            ValidateStatements([clause.Body], clauseLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, true, inLoop, diagnostics);
                        }

                        ValidateStatements(tryStatement.ExceptStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, true, inLoop, diagnostics);
                    }

                    if (tryStatement.FinallyKeyword is not null)
                    {
                        var containsReturnInProtectedRegions =
                            ContainsReturn(tryStatement.TryStatements) ||
                            tryStatement.ExceptionClauses.Any(clause => ContainsReturn([clause.Body])) ||
                            ContainsReturn(tryStatement.ExceptStatements);
                        if (containsReturnInProtectedRegions)
                        {
                            diagnostics.Report(
                                "ILC2134",
                                "Return inside try/finally is not supported yet.",
                                DiagnosticSeverity.Error,
                                tryStatement.FinallyKeyword.Span);
                        }

                        ValidateStatements(tryStatement.FinallyStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, false, inLoop, diagnostics);
                    }
                    break;
            }
        }
    }

    private static bool ContainsReturn(IReadOnlyList<StatementSyntax> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                    return true;
                case BlockStatementSyntax block when ContainsReturn(block.Statements):
                    return true;
                case IfStatementSyntax ifStatement when ContainsReturn([ifStatement.ThenStatement]) || (ifStatement.ElseStatement is not null && ContainsReturn([ifStatement.ElseStatement])):
                    return true;
                case WhileStatementSyntax whileStatement when ContainsReturn([whileStatement.Body]):
                    return true;
                case RepeatStatementSyntax repeatStatement when ContainsReturn(repeatStatement.Statements):
                    return true;
                case ForStatementSyntax forStatement when ContainsReturn([forStatement.Body]):
                    return true;
                case ForeachStatementSyntax foreachStatement when ContainsReturn([foreachStatement.Body]):
                    return true;
                case CaseStatementSyntax caseStatement when caseStatement.Clauses.Any(clause => ContainsReturn([clause.Body])) || ContainsReturn(caseStatement.ElseStatements):
                    return true;
                case MatchStatementSyntax matchStatement when matchStatement.Arms.Any(arm => ContainsReturn([arm.Body])) || ContainsReturn(matchStatement.ElseStatements):
                    return true;
                case TryStatementSyntax tryStatement when ContainsReturn(tryStatement.TryStatements) || ContainsReturn(tryStatement.ExceptStatements) || ContainsReturn(tryStatement.FinallyStatements):
                    return true;
            }
        }

        return false;
    }

    private static StatementSyntax RewriteWithStatement(
        StatementSyntax statement,
        ExpressionSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        statement switch
        {
            BlockStatementSyntax block => block with
            {
                Statements = block.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            ExpressionStatementSyntax expressionStatement => expressionStatement with
            {
                Expression = RewriteWithExpression(expressionStatement.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            ReturnStatementSyntax returnStatement when returnStatement.Expression is not null => returnStatement with
            {
                Expression = RewriteWithExpression(returnStatement.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            IncStatementSyntax incStatement => incStatement with
            {
                Target = RewriteWithExpression(incStatement.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            DecStatementSyntax decStatement => decStatement with
            {
                Target = RewriteWithExpression(decStatement.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            IncludeStatementSyntax includeStatement => includeStatement with
            {
                Target = RewriteWithExpression(includeStatement.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Value = RewriteWithExpression(includeStatement.Value, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            ExcludeStatementSyntax excludeStatement => excludeStatement with
            {
                Target = RewriteWithExpression(excludeStatement.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Value = RewriteWithExpression(excludeStatement.Value, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            RaiseStatementSyntax raiseStatement when raiseStatement.Expression is not null => raiseStatement with
            {
                Expression = RewriteWithExpression(raiseStatement.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            IfStatementSyntax ifStatement => ifStatement with
            {
                Condition = RewriteWithExpression(ifStatement.Condition, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                ThenStatement = RewriteWithStatement(ifStatement.ThenStatement, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                ElseStatement = ifStatement.ElseStatement is null
                    ? null
                    : RewriteWithStatement(ifStatement.ElseStatement, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            WhileStatementSyntax whileStatement => whileStatement with
            {
                Condition = RewriteWithExpression(whileStatement.Condition, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Body = RewriteWithStatement(whileStatement.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            RepeatStatementSyntax repeatStatement => repeatStatement with
            {
                Statements = repeatStatement.Statements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray(),
                Condition = RewriteWithExpression(repeatStatement.Condition, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            ForStatementSyntax forStatement => forStatement with
            {
                LowerBound = RewriteWithExpression(forStatement.LowerBound, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                UpperBound = RewriteWithExpression(forStatement.UpperBound, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                StepExpression = forStatement.StepExpression is null
                    ? null
                    : RewriteWithExpression(forStatement.StepExpression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Body = RewriteWithStatement(forStatement.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            ForeachStatementSyntax foreachStatement => foreachStatement with
            {
                Collection = RewriteWithExpression(foreachStatement.Collection, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Body = RewriteWithStatement(foreachStatement.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            CaseStatementSyntax caseStatement => caseStatement with
            {
                Expression = RewriteWithExpression(caseStatement.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Clauses = caseStatement.Clauses
                    .Select(clause => clause with
                    {
                        Labels = clause.Labels
                            .Select(label => RewriteWithExpression(label, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                            .ToArray(),
                        Guard = clause.Guard is null
                            ? null
                            : RewriteWithExpression(clause.Guard, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        Body = RewriteWithStatement(clause.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = caseStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            MatchStatementSyntax matchStatement => matchStatement with
            {
                Expression = RewriteWithExpression(matchStatement.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Arms = matchStatement.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels
                            .Select(label => RewriteWithExpression(label, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                            .ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        Body = RewriteWithStatement(arm.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray(),
                ElseStatements = matchStatement.ElseStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            LocalVariableDeclarationStatementSyntax localVariable => localVariable with
            {
                Declarators = localVariable.Declarators
                    .Select(declarator => declarator.Initializer is null
                        ? declarator
                        : declarator with
                        {
                            Initializer = RewriteWithExpression(declarator.Initializer, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                        })
                    .ToArray()
            },
            TryStatementSyntax tryStatement => tryStatement with
            {
                TryStatements = tryStatement.TryStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray(),
                ExceptionClauses = tryStatement.ExceptionClauses
                    .Select(clause => clause with
                    {
                        Body = RewriteWithStatement(clause.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray(),
                ExceptStatements = tryStatement.ExceptStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray(),
                FinallyStatements = tryStatement.FinallyStatements
                    .Select(nested => RewriteWithStatement(nested, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            WithStatementSyntax nestedWith => nestedWith with
            {
                Receiver = RewriteWithExpression(nestedWith.Receiver, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Body = RewriteWithStatement(nestedWith.Body, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            _ => statement
        };

    private static ExpressionSyntax RewriteWithExpression(
        ExpressionSyntax expression,
        ExpressionSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (expression is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            ShouldQualifyWithName(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return expression switch
        {
            AssignmentExpressionSyntax assignment => assignment with
            {
                Target = RewriteWithAssignmentTarget(assignment.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Expression = RewriteWithExpression(assignment.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            CompoundAssignmentExpressionSyntax assignment => assignment with
            {
                Target = RewriteWithAssignmentTarget(assignment.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Expression = RewriteWithExpression(assignment.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            UnaryExpressionSyntax unary => unary with
            {
                Operand = RewriteWithExpression(unary.Operand, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            MatchNotPatternSyntax notPattern => notPattern with
            {
                Pattern = RewriteWithExpression(notPattern.Pattern, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            MatchOrPatternSyntax orPattern => orPattern with
            {
                Patterns = orPattern.Patterns
                    .Select(pattern => RewriteWithExpression(pattern, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            MatchAndPatternSyntax andPattern => andPattern with
            {
                Patterns = andPattern.Patterns
                    .Select(pattern => (MatchRelationalPatternSyntax)RewriteWithExpression(pattern, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            MatchRelationalPatternSyntax relational => relational with
            {
                Operand = RewriteWithExpression(relational.Operand, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            BinaryExpressionSyntax binary => binary with
            {
                Left = RewriteWithExpression(binary.Left, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Right = RewriteWithExpression(binary.Right, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            MatchExpressionSyntax matchExpression => matchExpression with
            {
                Expression = RewriteWithExpression(matchExpression.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Arms = matchExpression.Arms
                    .Select(arm => arm with
                    {
                        Labels = arm.Labels
                            .Select(label => RewriteWithExpression(label, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                            .ToArray(),
                        Guard = arm.Guard is null
                            ? null
                            : RewriteWithExpression(arm.Guard, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                        Expression = RewriteWithExpression(arm.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray()
            },
            CallExpressionSyntax call => call with
            {
                Target = RewriteWithCallTarget(call.Target, call.Arguments.Count, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                Arguments = call.Arguments
                    .Select(argument => argument with
                    {
                        Expression = RewriteWithExpression(argument.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray()
            },
            MemberAccessExpressionSyntax memberAccess => memberAccess with
            {
                Receiver = RewriteWithExpression(memberAccess.Receiver, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            PostfixElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                Target = RewriteWithExpression(elementAccess.Target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
                IndexExpressions = elementAccess.IndexExpressions
                    .Select(index => RewriteWithExpression(index, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            ElementAccessExpressionSyntax elementAccess => elementAccess with
            {
                IndexExpressions = elementAccess.IndexExpressions
                    .Select(index => RewriteWithExpression(index, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            ArrayLengthExpressionSyntax arrayLength => arrayLength,
            AsExpressionSyntax asExpression => asExpression with
            {
                Expression = RewriteWithExpression(asExpression.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            TypeTestExpressionSyntax typeTest => typeTest with
            {
                Expression = RewriteWithExpression(typeTest.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            },
            NewExpressionSyntax newExpression => newExpression with
            {
                Arguments = newExpression.Arguments
                    .Select(argument => argument with
                    {
                        Expression = RewriteWithExpression(argument.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                    })
                    .ToArray()
            },
            NewArrayExpressionSyntax newArray => newArray with
            {
                LengthExpressions = newArray.LengthExpressions
                    .Select(length => RewriteWithExpression(length, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
                    .ToArray()
            },
            _ => expression
        };
    }

    private static ExpressionSyntax RewriteWithAssignmentTarget(
        ExpressionSyntax target,
        ExpressionSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        target is NameExpressionSyntax name &&
        name.Name.Parts.Count == 1 &&
        ShouldQualifyWithName(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
            ? QualifyWithReceiver(receiver, name.Name.Parts[0])
            : RewriteWithExpression(target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);

    private static ExpressionSyntax RewriteWithCallTarget(
        ExpressionSyntax target,
        int argumentCount,
        ExpressionSyntax receiver,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (target is NameExpressionSyntax name &&
            name.Name.Parts.Count == 1 &&
            SemanticFacts.ResolveInvocation(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) is null)
        {
            return QualifyWithReceiver(receiver, name.Name.Parts[0]);
        }

        return RewriteWithExpression(target, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    private static bool ShouldQualifyWithName(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        !locals.ContainsKey(name.ToDisplayString()) &&
        SemanticFacts.ResolveName(name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Kind == NameResolutionKind.Unknown;

    private static MemberAccessExpressionSyntax QualifyWithReceiver(ExpressionSyntax receiver, SyntaxToken memberName) =>
        new(
            receiver,
            new SyntaxToken(SyntaxKind.DotToken, ".", null, memberName.Span),
            memberName);

    private static void ValidateExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        switch (expression)
        {
            case NameExpressionSyntax name:
                ValidateNameReference(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                ValidateExpression(parenthesized.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case AssignmentExpressionSyntax assignment:
                ValidateAssignmentTarget(assignment.Target, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpressionForExpectedType(
                    assignment.Expression,
                    SemanticFacts.InferExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                    locals,
                    knownTypes,
                    knownMethods,
                    knownFields,
                    knownConstants,
                    knownProperties,
                    currentMethod,
                    diagnostics);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                ValidateAssignmentTarget(assignment.Target, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpression(assignment.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpression(assignment.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (assignment.OperatorToken.Kind == SyntaxKind.NullCoalescingAssignToken)
                {
                    var targetType = SemanticFacts.InferExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (!targetType.IsReferenceType)
                    {
                        diagnostics.Report(
                            "ILC2165",
                            $"Operator '??=' requires a reference-typed assignment target, but '{SemanticFacts.GetExpressionDisplayName(assignment.Target)}' has type '{targetType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Target, knownTypes));
                    }
                }
                else if (assignment.OperatorToken.Kind is SyntaxKind.ShlAssignToken or SyntaxKind.ShrAssignToken)
                {
                    var targetType = SemanticFacts.InferExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var valueType = SemanticFacts.InferExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (targetType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2169",
                            $"Left-hand side of '{assignment.OperatorToken.Text}' must be Integer, but got '{targetType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Target, knownTypes));
                    }
                    else if (valueType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2170",
                            $"Right-hand side of '{assignment.OperatorToken.Text}' must be Integer, but got '{valueType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Expression, knownTypes));
                    }
                }
                else if (assignment.OperatorToken.Kind == SyntaxKind.DivAssignToken)
                {
                    var targetType = SemanticFacts.InferExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var valueType = SemanticFacts.InferExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (targetType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2173",
                            $"Left-hand side of 'div=' must be Integer, but got '{targetType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Target, knownTypes));
                    }
                    else if (valueType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2174",
                            $"Right-hand side of 'div=' must be Integer, but got '{valueType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Expression, knownTypes));
                    }
                }
                else if (assignment.OperatorToken.Kind == SyntaxKind.ModAssignToken)
                {
                    var targetType = SemanticFacts.InferExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var valueType = SemanticFacts.InferExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (targetType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2171",
                            $"Left-hand side of 'mod=' must be Integer, but got '{targetType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Target, knownTypes));
                    }
                    else if (valueType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2172",
                            $"Right-hand side of 'mod=' must be Integer, but got '{valueType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(assignment.Expression, knownTypes));
                    }
                }
                break;
            case BinaryExpressionSyntax binary:
                ValidateExpression(binary.Left, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpression(binary.Right, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (binary.OperatorToken.Kind is SyntaxKind.InKeyword or SyntaxKind.NotInKeyword)
                {
                    ValidateSetMembership(binary, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }
                else if (binary.OperatorToken.Kind == SyntaxKind.NullCoalescingToken)
                {
                    var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (!leftType.IsReferenceType && leftType != TypeSymbol.Nil)
                    {
                        diagnostics.Report(
                            "ILC2166",
                            $"Left-hand side of '??' must be a reference type, but got '{leftType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Left, knownTypes));
                    }
                    else if (!rightType.IsReferenceType && rightType != TypeSymbol.Nil)
                    {
                        diagnostics.Report(
                            "ILC2167",
                            $"Right-hand side of '??' must be a reference type, but got '{rightType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Right, knownTypes));
                    }
                }
                else if (binary.OperatorToken.Kind is SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword)
                {
                    var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (leftType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2169",
                            $"Left-hand side of '{binary.OperatorToken.Text}' must be Integer, but got '{leftType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Left, knownTypes));
                    }
                    else if (rightType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2170",
                            $"Right-hand side of '{binary.OperatorToken.Text}' must be Integer, but got '{rightType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Right, knownTypes));
                    }
                }
                else if (binary.OperatorToken.Kind == SyntaxKind.DivKeyword)
                {
                    var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (leftType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2173",
                            $"Left-hand side of 'div' must be Integer, but got '{leftType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Left, knownTypes));
                    }
                    else if (rightType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2174",
                            $"Right-hand side of 'div' must be Integer, but got '{rightType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Right, knownTypes));
                    }
                }
                else if (binary.OperatorToken.Kind == SyntaxKind.ModKeyword)
                {
                    var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (leftType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2171",
                            $"Left-hand side of 'mod' must be Integer, but got '{leftType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Left, knownTypes));
                    }
                    else if (rightType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2172",
                            $"Right-hand side of 'mod' must be Integer, but got '{rightType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(binary.Right, knownTypes));
                    }
                }
                else if (binary.OperatorToken.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken)
                {
                    ValidateSetBinary(binary, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }
                break;
            case UnaryExpressionSyntax unary:
                ValidateExpression(unary.Operand, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (unary.OperatorToken.Kind == SyntaxKind.NotKeyword)
                {
                    var operandType = SemanticFacts.InferExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (operandType != TypeSymbol.Boolean && operandType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2168",
                            $"Operator 'not' requires a Boolean or Integer operand, but got '{operandType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(unary.Operand, knownTypes));
                    }
                }
                else if (unary.OperatorToken.Kind == SyntaxKind.MinusToken)
                {
                    var operandType = SemanticFacts.InferExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (operandType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2185",
                            $"Unary '-' requires an Integer operand, but got '{operandType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(unary.Operand, knownTypes));
                    }
                }
                else if (unary.OperatorToken.Kind == SyntaxKind.PlusToken)
                {
                    var operandType = SemanticFacts.InferExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (operandType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2186",
                            $"Unary '+' requires an Integer operand, but got '{operandType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(unary.Operand, knownTypes));
                    }
                }
                break;
            case MatchNotPatternSyntax notPattern:
                ValidateExpression(notPattern.Pattern, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    ValidateExpression(pattern, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                ValidateExpression(relational.Operand, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    ValidateExpression(pattern, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }
                break;
            case MatchExpressionSyntax matchExpression:
                ValidateExpression(matchExpression.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var matchedExpressionType = SemanticFacts.InferExpressionType(matchExpression.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (matchedExpressionType != TypeSymbol.Integer &&
                    matchedExpressionType != TypeSymbol.String &&
                    !SemanticFacts.IsEnumType(matchedExpressionType) &&
                    !matchedExpressionType.IsReferenceType)
                {
                    diagnostics.Report(
                        "ILC2175",
                        $"Match expression '{SemanticFacts.GetExpressionDisplayName(matchExpression.Expression)}' must be Integer, String, Enum or reference-typed in the current bootstrap compiler.",
                        DiagnosticSeverity.Error,
                        GetExpressionDiagnosticSpan(matchExpression.Expression, knownTypes));
                }

                TypeSymbol? resultType = null;
                var hasWildcardArm = false;
                foreach (var arm in matchExpression.Arms)
                {
                    var armLocals = locals;
                    if (arm.IsWildcard)
                    {
                        hasWildcardArm = true;
                    }
                    else if (arm.TypeName is not null)
                    {
                        var typedArmType = SemanticFacts.ResolveTypeReference(arm.TypeName.ToDisplayString(), knownTypes);
                        if (typedArmType is null)
                        {
                            diagnostics.Report(
                                "ILC2131",
                                $"Unknown type '{arm.TypeName.ToDisplayString()}' in match arm.",
                                DiagnosticSeverity.Error,
                                GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                        }
                        else if (!typedArmType.IsReferenceType)
                        {
                            diagnostics.Report(
                                "ILC2132",
                                $"Match arm type '{arm.TypeName.ToDisplayString()}' must be a reference type.",
                                DiagnosticSeverity.Error,
                                GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                        }
                        else if (!SemanticFacts.IsCompatibleReferenceType(matchedExpressionType, typedArmType, knownTypes))
                        {
                            diagnostics.Report(
                                "ILC2178",
                                $"Typed match arm '{arm.TypeName.ToDisplayString()}' requires a compatible reference-typed match expression in the current bootstrap compiler.",
                                DiagnosticSeverity.Error,
                                GetReferenceDiagnosticSpan(arm.TypeName, knownTypes));
                        }
                        else if (arm.Identifier is not null)
                        {
                            armLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                            {
                                [arm.Identifier.Text] = typedArmType
                            };
                        }
                    }
                    else
                    {
                        foreach (var label in arm.Labels)
                        {
                            ValidateMatchLabel(label, matchedExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        }
                    }

                    if (arm.Guard is not null)
                    {
                        ValidateExpression(arm.Guard, armLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var guardType = SemanticFacts.InferExpressionType(arm.Guard, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                        if (guardType != TypeSymbol.Boolean)
                        {
                            diagnostics.Report(
                                "ILC2179",
                                "Match arm guard must be Boolean.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(arm.Guard, knownTypes));
                        }
                    }

                    ValidateExpression(arm.Expression, armLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var armType = SemanticFacts.InferExpressionType(arm.Expression, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (resultType is null)
                    {
                        resultType = armType;
                    }
                    else if (armType != resultType)
                    {
                        diagnostics.Report(
                            "ILC2177",
                            $"Match arm result '{SemanticFacts.GetExpressionDisplayName(arm.Expression)}' must be of type '{resultType.Name}', but got '{armType.Name}'.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(arm.Expression, knownTypes));
                    }
                }

                if (!hasWildcardArm)
                {
                    diagnostics.Report(
                        "ILC2176",
                        "Match expressions must contain a wildcard arm '_' in the current bootstrap compiler.",
                        DiagnosticSeverity.Error,
                        matchExpression.MatchKeyword.Span);
                }

                break;
            case SetLiteralExpressionSyntax setLiteral:
                ValidateSetLiteral(setLiteral, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case RangeExpressionSyntax range:
                ValidateExpression(range.Start, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpression(range.End, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case AsExpressionSyntax asExpression:
                ValidateExpression(asExpression.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var asTargetType = SemanticFacts.ResolveTypeReference(asExpression.TypeName.ToDisplayString(), knownTypes);
                if (asTargetType is null)
                {
                    diagnostics.Report(
                        "ILC2131",
                        $"Unknown type '{asExpression.TypeName.ToDisplayString()}' in type cast.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(asExpression.TypeName, knownTypes));
                }
                else if (!asTargetType.IsReferenceType)
                {
                    diagnostics.Report(
                        "ILC2132",
                        $"Type cast target '{asExpression.TypeName.ToDisplayString()}' must be a reference type.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(asExpression.TypeName, knownTypes));
                }
                break;
            case TypeTestExpressionSyntax typeTest:
                ValidateExpression(typeTest.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (SemanticFacts.ResolveTypeReference(typeTest.TypeName.ToDisplayString(), knownTypes) is null)
                {
                    diagnostics.Report(
                        "ILC2131",
                        $"Unknown type '{typeTest.TypeName.ToDisplayString()}' in type test.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(typeTest.TypeName, knownTypes));
                }
                break;
            case ElementAccessExpressionSyntax elementAccess:
                ValidateNameReference(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                ValidateExpression(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                ValidateExpression(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Property is not null && memberResolution.Property.IsGetterPrivate && memberResolution.Property.DeclaringTypeName != currentMethod?.DeclaringTypeName)
                {
                    diagnostics.Report(
                        "ILC2121",
                        $"Property getter '{memberResolution.DisplayName}' is not accessible in the current context.",
                        DiagnosticSeverity.Error,
                        memberAccess.MemberName.Span);
                    break;
                }

                if (memberResolution.Type is not null)
                {
                    if (memberResolution.Method is not null)
                    {
                        diagnostics.Report(
                            "ILC2106",
                            $"Member reference '{memberResolution.DisplayName}' cannot be used as a value expression in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            memberAccess.MemberName.Span);
                    }

                    break;
                }

                diagnostics.Report(
                    "ILC2102",
                    $"Unknown name '{memberResolution.DisplayName}'.",
                    DiagnosticSeverity.Error,
                    memberAccess.MemberName.Span);
                break;
            case ArrayLengthExpressionSyntax arrayLength:
                ValidateNameReference(arrayLength.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var lengthTargetType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(arrayLength.Target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (!SemanticFacts.HasLengthProperty(lengthTargetType))
                {
                    diagnostics.Report(
                        "ILC2124",
                        $"Expression '{arrayLength.Target.ToDisplayString()}' has no Length in the current bootstrap compiler.",
                        DiagnosticSeverity.Error,
                        arrayLength.Target.Parts[^1].Span);
                }
                break;
            case NewArrayExpressionSyntax newArrayExpression:
                foreach (var lengthExpression in newArrayExpression.LengthExpressions)
                {
                    ValidateExpression(lengthExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                break;
            case CallExpressionSyntax call:
                var invocation = SemanticFacts.ResolveInvocation(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (TryReportInvalidMethodAccess(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics))
                {
                }
                else if (invocation is null)
                {
                    diagnostics.Report(
                        "ILC2103",
                        $"Unknown call target '{SemanticFacts.GetExpressionDisplayName(call.Target)}' with arity {call.Arguments.Count}.",
                        DiagnosticSeverity.Error,
                        GetExpressionDiagnosticSpan(call.Target, knownTypes));
                }

                for (var argumentIndex = 0; argumentIndex < call.Arguments.Count; argumentIndex++)
                {
                    var argument = call.Arguments[argumentIndex];
                    var parameter = invocation?.Method.Parameters.ElementAtOrDefault(argumentIndex);
                    if (parameter is not null && (parameter.PassingKind == ParameterPassingKind.Out || parameter.PassingKind == ParameterPassingKind.Ref))
                    {
                        ValidateAssignmentTarget(argument.Expression, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }
                    else
                    {
                        ValidateExpressionForExpectedType(
                            argument.Expression,
                            parameter?.Type,
                            locals,
                            knownTypes,
                            knownMethods,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            currentMethod,
                            diagnostics);
                    }
                }

                if (invocation?.Method.Name == "TryParse" &&
                    invocation.Method.DeclaringTypeName == TypeSymbol.Integer.Name &&
                    invocation.Method.IsStatic &&
                    call.Arguments.Count == 2)
                {
                    var parseInputType = SemanticFacts.InferExpressionType(call.Arguments[0].Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (parseInputType != TypeSymbol.String)
                    {
                        diagnostics.Report(
                            "ILC2163",
                            "Integer.TryParse expects a String as its first argument.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(call.Arguments[0].Expression, knownTypes));
                    }

                    var parseTargetType = SemanticFacts.InferExpressionType(call.Arguments[1].Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (parseTargetType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2164",
                            "Integer.TryParse expects an Integer assignment target as its second argument.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(call.Arguments[1].Expression, knownTypes));
                    }
                }
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    ValidateExpression(argument.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                if (SemanticFacts.ResolveTypeReference(newExpression.TypeName.ToDisplayString(), knownTypes) is null)
                {
                    diagnostics.Report(
                        "ILC2115",
                        $"Unknown constructed type '{newExpression.TypeName.ToDisplayString()}'.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(newExpression.TypeName, knownTypes));
                    break;
                }

                if (SemanticFacts.ResolveConstructor(newExpression.TypeName, newExpression.Arguments.Count, knownTypes, knownMethods) is null &&
                    HasDeclaredConstructors(newExpression.TypeName, knownTypes, knownMethods))
                {
                    diagnostics.Report(
                        "ILC2116",
                        $"No constructor for '{newExpression.TypeName.ToDisplayString()}' matches arity {newExpression.Arguments.Count}.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(newExpression.TypeName, knownTypes));
                }

                break;
            case LambdaExpressionSyntax lambda:
                diagnostics.Report(
                    "ILC2219",
                    "Lambda expressions require a delegate target type in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(lambda, knownTypes));
                break;
        }
    }

    private static void ValidateExpressionForExpectedType(
        ExpressionSyntax expression,
        TypeSymbol? expectedType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (TryValidateDelegateMethodGroupConversion(
                expression,
                expectedType,
                locals,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                diagnostics))
        {
            return;
        }

        if (TryValidateDelegateLambdaConversion(
                expression,
                expectedType,
                locals,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                currentMethod,
                diagnostics))
        {
            return;
        }

        ValidateExpression(expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
    }

    private static bool TryValidateDelegateMethodGroupConversion(
        ExpressionSyntax expression,
        TypeSymbol? expectedType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (expectedType is null ||
            ResolveNamedType(expectedType, knownTypes) is not { IsDelegate: true } delegateType)
        {
            return false;
        }

        var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
        if (invokeMethod is null)
        {
            return false;
        }

        MethodSymbol? methodGroup = null;
        TextSpan diagnosticSpan;

        switch (expression)
        {
            case NameExpressionSyntax name:
            {
                var resolution = SemanticFacts.ResolveName(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (resolution.Kind != NameResolutionKind.MethodGroup || resolution.Method is null)
                {
                    return false;
                }

                methodGroup = resolution.Method;
                diagnosticSpan = GetReferenceDiagnosticSpan(name.Name, knownTypes);
                break;
            }
            case MemberAccessExpressionSyntax memberAccess:
            {
                ValidateExpression(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Method is null)
                {
                    return false;
                }

                methodGroup = memberResolution.Method;
                diagnosticSpan = memberAccess.MemberName.Span;
                break;
            }
            default:
                return false;
        }

        if (AreDelegateMethodSignaturesCompatible(invokeMethod, methodGroup, knownTypes))
        {
            return true;
        }

        diagnostics.Report(
            "ILC2218",
            $"Method group '{methodGroup.Name}' is not compatible with delegate '{delegateType.Name}'.",
            DiagnosticSeverity.Error,
            diagnosticSpan);
        return true;
    }

    private static void ValidateSetLiteral(
        SetLiteralExpressionSyntax setLiteral,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (setLiteral.Elements.Count == 0)
        {
            diagnostics.Report(
                "ILC2151",
                "Empty set literals are not supported in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                setLiteral.OpenBracketToken.Span);
            return;
        }

        TypeSymbol? expectedElementType = null;
        foreach (var element in setLiteral.Elements)
        {
            ValidateSetLiteralElement(element, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics, ref expectedElementType);
        }
    }

    private static void ValidateSetLiteralElement(
        ExpressionSyntax element,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics,
        ref TypeSymbol? expectedElementType)
    {
        if (element is RangeExpressionSyntax range)
        {
            ValidateExpression(range.Start, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            ValidateExpression(range.End, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            ValidateSetLiteralEnumValue(range.Start, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics, ref expectedElementType);
            ValidateSetLiteralEnumValue(range.End, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics, ref expectedElementType);
            return;
        }

        ValidateExpression(element, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        ValidateSetLiteralEnumValue(element, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics, ref expectedElementType);
    }

    private static void ValidateSetLiteralEnumValue(
        ExpressionSyntax element,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics,
        ref TypeSymbol? expectedElementType)
    {
        if (!SemanticFacts.IsConstantExpression(element, locals, knownFields, knownConstants, knownProperties, currentMethod))
        {
            diagnostics.Report(
                "ILC2152",
                "Set literal elements must be literal or constant enum values in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(element, knownTypes));
            return;
        }

        var elementType = SemanticFacts.InferExpressionType(element, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (!SemanticFacts.IsEnumType(elementType))
        {
            diagnostics.Report(
                "ILC2153",
                $"Set literal element '{SemanticFacts.GetExpressionDisplayName(element)}' must be an enum value.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(element, knownTypes));
            return;
        }

        expectedElementType ??= elementType;
        if (elementType != expectedElementType)
        {
            diagnostics.Report(
                "ILC2154",
                $"Set literal element '{SemanticFacts.GetExpressionDisplayName(element)}' must be of enum type '{expectedElementType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(element, knownTypes));
        }
    }

    private static void ValidateSetMembership(
        BinaryExpressionSyntax binary,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var setElementType = SemanticFacts.GetSetElementType(rightType);
        if (setElementType is null)
        {
            diagnostics.Report(
                "ILC2155",
                $"Right-hand side of 'in' must be a set type, but got '{rightType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary.Right, knownTypes));
            return;
        }

        if (leftType != setElementType)
        {
            diagnostics.Report(
                "ILC2157",
                $"Left-hand side of 'in' must be of enum type '{setElementType.Name}', but got '{leftType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary.Left, knownTypes));
        }
    }

    private static void ValidateSetBinary(
        BinaryExpressionSyntax binary,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var leftType = SemanticFacts.InferExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var rightType = SemanticFacts.InferExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (!SemanticFacts.IsSetType(leftType) || !SemanticFacts.IsSetType(rightType))
        {
            return;
        }

        if (leftType != rightType)
        {
            diagnostics.Report(
                "ILC2158",
                $"Set operands for '{binary.OperatorToken.Text}' must have the same element type.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary, knownTypes));
        }
    }

    private static void ValidateCaseLabel(
        ExpressionSyntax label,
        TypeSymbol caseExpressionType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (label is RangeExpressionSyntax range)
        {
            if (caseExpressionType == TypeSymbol.String)
            {
                diagnostics.Report(
                    "ILC2159",
                    "Case ranges are only supported for Integer and Enum expressions in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(label, knownTypes));
                return;
            }

            ValidateCaseLabel(range.Start, caseExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            ValidateCaseLabel(range.End, caseExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (!SemanticFacts.IsConstantExpression(label, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes))
        {
            diagnostics.Report(
                "ILC2147",
                $"Case label '{SemanticFacts.GetExpressionDisplayName(label)}' must be a literal or constant value in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(label, knownTypes));
            return;
        }

        var labelType = SemanticFacts.InferExpressionType(label, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (labelType != caseExpressionType)
        {
            diagnostics.Report(
                "ILC2146",
                $"Case label '{SemanticFacts.GetExpressionDisplayName(label)}' must be of type '{caseExpressionType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(label, knownTypes));
        }
    }

    private static void ValidateMatchLabel(
        ExpressionSyntax label,
        TypeSymbol matchExpressionType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        ValidateExpression(label, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        if (label is MatchNotPatternSyntax notPattern)
        {
            ValidateMatchLabel(notPattern.Pattern, matchExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (label is MatchOrPatternSyntax orPattern)
        {
            foreach (var pattern in orPattern.Patterns)
            {
                ValidateMatchLabel(pattern, matchExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            }

            return;
        }

        if (label is MatchAndPatternSyntax andPattern)
        {
            foreach (var pattern in andPattern.Patterns)
            {
                ValidateMatchLabel(pattern, matchExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            }

            return;
        }

        if (label is MatchRelationalPatternSyntax relational)
        {
            if (matchExpressionType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2180",
                    $"Relational match pattern '{relational.OperatorToken.Text}{SemanticFacts.GetExpressionDisplayName(relational.Operand)}' requires an Integer match expression.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(relational, knownTypes));
                return;
            }

            var operandType = SemanticFacts.InferExpressionType(relational.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            if (operandType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2181",
                    $"Relational match pattern operand '{SemanticFacts.GetExpressionDisplayName(relational.Operand)}' must be Integer.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(relational.Operand, knownTypes));
            }

            return;
        }

        ValidateCaseLabel(label, matchExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
    }

    private static void ValidateNameReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (TryReportInvalidFieldAccess(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (property is not null && property.IsGetterPrivate && property.DeclaringTypeName != currentMethod?.DeclaringTypeName)
        {
            diagnostics.Report(
                "ILC2121",
                $"Property getter '{name.ToDisplayString()}' is not accessible in the current context.",
                DiagnosticSeverity.Error,
                name.Parts[^1].Span);
            return;
        }

        var resolution = SemanticFacts.ResolveName(name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        switch (resolution.Kind)
        {
            case NameResolutionKind.LocalOrGlobal:
                return;
            case NameResolutionKind.Field:
                return;
            case NameResolutionKind.Constant:
                return;
            case NameResolutionKind.Type:
                diagnostics.Report(
                    "ILC2105",
                    $"Type reference '{name.ToDisplayString()}' cannot be used as a value expression in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(name, knownTypes));
                return;
            case NameResolutionKind.MethodGroup:
                diagnostics.Report(
                    "ILC2106",
                    $"Member reference '{name.ToDisplayString()}' cannot be used as a value expression in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(name, knownTypes));
                return;
        }

        diagnostics.Report(
            "ILC2102",
            $"Unknown name '{name.ToDisplayString()}'.",
            DiagnosticSeverity.Error,
            GetReferenceDiagnosticSpan(name, knownTypes));
    }

    private static void ValidateAssignmentTarget(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (target is ElementAccessExpressionSyntax elementAccess)
        {
            ValidateNameReference(elementAccess.Target, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            foreach (var indexExpression in elementAccess.IndexExpressions)
            {
                ValidateExpression(indexExpression, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            }

            var indexedTargetType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(elementAccess.Target), locals, [], knownFields, knownConstants, knownProperties, currentMethod);
            var resolvedIndexer = SemanticFacts.ResolveIndexerReference(elementAccess.Target, locals, knownFields, knownConstants, knownProperties, currentMethod);
            if (SemanticFacts.IsSliceAccess(elementAccess.IndexExpressions))
            {
                diagnostics.Report(
                    "ILC2162",
                    $"Slice assignment '{elementAccess.Target.ToDisplayString()}[...] := ...' is not supported in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    elementAccess.OpenBracketToken.Span);
                return;
            }

            if (resolvedIndexer is null && indexedTargetType == TypeSymbol.String)
            {
                diagnostics.Report(
                    "ILC2127",
                    $"String '{elementAccess.Target.ToDisplayString()}' is immutable and cannot be assigned through an index.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(elementAccess.Target, []));
                return;
            }

            ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is PostfixElementAccessExpressionSyntax postfixElementAccess)
        {
            ValidateExpression(postfixElementAccess.Target, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            foreach (var indexExpression in postfixElementAccess.IndexExpressions)
            {
                ValidateExpression(indexExpression, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            }

            var indexedTargetType = SemanticFacts.InferExpressionType(postfixElementAccess.Target, locals, [], knownFields, knownConstants, knownProperties, currentMethod);
            var resolvedIndexer = SemanticFacts.ResolveIndexerReference(postfixElementAccess.Target, locals, [], knownFields, knownConstants, knownProperties, currentMethod);
            if (SemanticFacts.IsSliceAccess(postfixElementAccess.IndexExpressions))
            {
                diagnostics.Report(
                    "ILC2162",
                    $"Slice assignment '{GetExpressionDisplayName(postfixElementAccess.Target)}[...] := ...' is not supported in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    postfixElementAccess.OpenBracketToken.Span);
                return;
            }

            if (resolvedIndexer is null && indexedTargetType == TypeSymbol.String)
            {
                diagnostics.Report(
                    "ILC2127",
                    $"String '{GetExpressionDisplayName(postfixElementAccess.Target)}' is immutable and cannot be assigned through an index.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(postfixElementAccess.Target, []));
                return;
            }

            ValidateArrayAccess(postfixElementAccess.Target, postfixElementAccess.IndexExpressions, postfixElementAccess.OpenBracketToken.Span, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is not NameExpressionSyntax nameTarget)
        {
            if (target is MemberAccessExpressionSyntax memberTarget)
            {
                ValidateExpression(memberTarget.Receiver, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberTarget, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Property is not null)
                {
                    if (memberResolution.Property.WriteField is null && memberResolution.Property.SetterMethod is null)
                    {
                        diagnostics.Report(
                            "ILC2120",
                            $"Property '{memberResolution.DisplayName}' is read-only and cannot be assigned to.",
                            DiagnosticSeverity.Error,
                            memberTarget.MemberName.Span);
                        return;
                    }

                    if (memberResolution.Property.IsInitOnly &&
                        !(currentMethod?.IsConstructor == true && currentMethod.DeclaringTypeName == memberResolution.Property.DeclaringTypeName))
                    {
                        diagnostics.Report(
                            "ILC2123",
                            $"Init-only property '{memberResolution.DisplayName}' can only be assigned in a constructor of '{memberResolution.Property.DeclaringTypeName}'.",
                            DiagnosticSeverity.Error,
                            memberTarget.MemberName.Span);
                        return;
                    }

                    if (memberResolution.Property.IsSetterPrivate && memberResolution.Property.DeclaringTypeName != currentMethod?.DeclaringTypeName)
                    {
                        diagnostics.Report(
                            "ILC2122",
                            $"Property setter '{memberResolution.DisplayName}' is not accessible in the current context.",
                            DiagnosticSeverity.Error,
                            memberTarget.MemberName.Span);
                        return;
                    }

                    return;
                }

                if (memberResolution.Field is not null)
                {
                    return;
                }
            }

            diagnostics.Report(
                "ILC2101",
                $"Assignment target '{GetExpressionDisplayName(target)}' is not assignable in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(target, knownTypes));
            return;
        }

        var targetName = nameTarget.Name;
        if (TryReportInvalidFieldAccess(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (property is not null && property.WriteField is null && property.SetterMethod is null)
        {
            diagnostics.Report(
                "ILC2120",
                $"Property '{targetName.ToDisplayString()}' is read-only and cannot be assigned to.",
                DiagnosticSeverity.Error,
                targetName.Parts[^1].Span);
            return;
        }

        if (property is not null && property.IsInitOnly &&
            !(currentMethod?.IsConstructor == true && currentMethod.DeclaringTypeName == property.DeclaringTypeName))
        {
            diagnostics.Report(
                "ILC2123",
                $"Init-only property '{targetName.ToDisplayString()}' can only be assigned in a constructor of '{property.DeclaringTypeName}'.",
                DiagnosticSeverity.Error,
                targetName.Parts[^1].Span);
            return;
        }

        if (property is not null && property.IsSetterPrivate && property.DeclaringTypeName != currentMethod?.DeclaringTypeName)
        {
            diagnostics.Report(
                "ILC2122",
                $"Property setter '{targetName.ToDisplayString()}' is not accessible in the current context.",
                DiagnosticSeverity.Error,
                targetName.Parts[^1].Span);
            return;
        }

        if (targetName.Parts.Count == 1)
        {
            var name = targetName.ToDisplayString();
            var simpleResolution = SemanticFacts.ResolveName(targetName, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod);
            if (simpleResolution.Kind == NameResolutionKind.Constant)
            {
                diagnostics.Report(
                    "ILC2150",
                    $"Constant '{targetName.ToDisplayString()}' is read-only and cannot be assigned to.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(targetName, knownTypes));
                return;
            }

            if (!locals.ContainsKey(name) &&
                simpleResolution.Kind is not NameResolutionKind.Field)
            {
                diagnostics.Report(
                    "ILC2100",
                    $"Unknown assignment target '{name}'.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(targetName, knownTypes));
            }

            return;
        }

        var resolution = SemanticFacts.ResolveName(targetName, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod);
        if (resolution.Kind == NameResolutionKind.Constant)
        {
            diagnostics.Report(
                "ILC2150",
                $"Constant '{targetName.ToDisplayString()}' is read-only and cannot be assigned to.",
                DiagnosticSeverity.Error,
                GetReferenceDiagnosticSpan(targetName, knownTypes));
            return;
        }
        if (resolution.Kind == NameResolutionKind.Field)
        {
            return;
        }

        diagnostics.Report(
            "ILC2101",
            $"Assignment target '{targetName.ToDisplayString()}' is not assignable in the current bootstrap compiler.",
            DiagnosticSeverity.Error,
            GetReferenceDiagnosticSpan(targetName, knownTypes));
    }

    private static void ValidateArrayAccess(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TextSpan indexSpan,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (SemanticFacts.IsSliceAccess(indexExpressions))
        {
            ValidateSliceAccess(target, indexExpressions[0], locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is MemberAccessExpressionSyntax memberAccess &&
            SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property is { IsIndexer: true })
        {
            var memberIndexer = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property!;
            if (indexExpressions.Count != 1)
            {
                diagnostics.Report(
                    "ILC2129",
                    $"Expression '{GetExpressionDisplayName(target)}' expects 1 index, but {indexExpressions.Count} were provided.",
                    DiagnosticSeverity.Error,
                    indexSpan);
                return;
            }

            foreach (var indexExpression in indexExpressions)
            {
                var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (indexType != memberIndexer.IndexParameter?.Type)
                {
                    diagnostics.Report(
                        "ILC2126",
                        $"Array index for '{GetExpressionDisplayName(target)}' must be '{memberIndexer.IndexParameter?.Type?.Name ?? "Integer"}', but was '{indexType.Name}'.",
                        DiagnosticSeverity.Error,
                        indexSpan);
                }
            }

            return;
        }

        var indexedType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            if (indexExpressions.Count != 1)
            {
                diagnostics.Report(
                    "ILC2129",
                    $"Expression '{GetExpressionDisplayName(target)}' expects 1 index, but {indexExpressions.Count} were provided.",
                    DiagnosticSeverity.Error,
                    indexSpan);
                return;
            }

            foreach (var indexExpression in indexExpressions)
            {
                var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (indexType != indexer.IndexParameter?.Type)
                {
                    diagnostics.Report(
                        "ILC2126",
                        $"Array index for '{GetExpressionDisplayName(target)}' must be '{indexer.IndexParameter?.Type?.Name ?? "Integer"}', but was '{indexType.Name}'.",
                        DiagnosticSeverity.Error,
                        indexSpan);
                }
            }

            return;
        }

        if (!SemanticFacts.IsIndexableType(indexedType))
        {
            diagnostics.Report(
                "ILC2125",
                $"Expression '{GetExpressionDisplayName(target)}' of type '{indexedType.Name}' is not indexable in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(target, []));
        }

        var expectedRank = SemanticFacts.GetArrayRank(indexedType);
        if (expectedRank > 0 && expectedRank != indexExpressions.Count)
        {
            diagnostics.Report(
                "ILC2129",
                $"Expression '{GetExpressionDisplayName(target)}' expects {expectedRank} index{(expectedRank == 1 ? string.Empty : "es")}, but {indexExpressions.Count} were provided.",
                DiagnosticSeverity.Error,
                indexSpan);
        }

        foreach (var indexExpression in indexExpressions)
        {
            var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            if (indexType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2126",
                    $"Array index for '{GetExpressionDisplayName(target)}' must be Integer, but was '{indexType.Name}'.",
                    DiagnosticSeverity.Error,
                    indexSpan);
            }
        }
    }

    private static void ValidateIncDecStatement(
        SyntaxToken keyword,
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        ValidateAssignmentTarget(target, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        ValidateExpression(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);

        var targetType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (targetType != TypeSymbol.Integer)
        {
            diagnostics.Report(
                keyword.Kind == SyntaxKind.IncKeyword ? "ILC2187" : "ILC2188",
                $"Statement '{keyword.Text}' requires an Integer target, but got '{targetType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(target, knownTypes));
        }
    }

    private static void ValidateIncludeExcludeStatement(
        SyntaxToken keyword,
        ExpressionSyntax target,
        ExpressionSyntax value,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        ValidateAssignmentTarget(target, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        ValidateExpression(target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        ValidateExpression(value, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);

        var targetType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var elementType = SemanticFacts.GetSetElementType(targetType);
        if (elementType is null)
        {
            diagnostics.Report(
                "ILC2190",
                $"Target of '{keyword.Text}' must be a set type, but got '{targetType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(target, knownTypes));
            return;
        }

        var valueType = SemanticFacts.InferExpressionType(value, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (valueType != elementType)
        {
            diagnostics.Report(
                "ILC2191",
                $"Value of '{keyword.Text}' must be of enum type '{elementType.Name}', but got '{valueType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(value, knownTypes));
        }
    }

    private static void ValidateArrayAccess(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TextSpan indexSpan,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (SemanticFacts.IsSliceAccess(indexExpressions))
        {
            ValidateSliceAccess(new NameExpressionSyntax(target), indexExpressions[0], locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            if (indexExpressions.Count != 1)
            {
                diagnostics.Report(
                    "ILC2129",
                    $"Expression '{target.ToDisplayString()}' expects 1 index, but {indexExpressions.Count} were provided.",
                    DiagnosticSeverity.Error,
                    indexSpan);
                return;
            }

            var resolvedIndexType = SemanticFacts.InferExpressionType(indexExpressions[0], locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            if (resolvedIndexType != indexer.IndexParameter?.Type)
            {
                diagnostics.Report(
                    "ILC2126",
                    $"Array index for '{target.ToDisplayString()}' must be '{indexer.IndexParameter?.Type?.Name ?? "Integer"}', but was '{resolvedIndexType.Name}'.",
                    DiagnosticSeverity.Error,
                    indexSpan);
            }

            return;
        }

        var indexedType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (!SemanticFacts.IsIndexableType(indexedType))
        {
            diagnostics.Report(
                "ILC2125",
                $"Expression '{target.ToDisplayString()}' of type '{indexedType.Name}' is not indexable in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetReferenceDiagnosticSpan(target, []));
        }

        var expectedRank = SemanticFacts.GetArrayRank(indexedType);
        if (expectedRank > 0 && expectedRank != indexExpressions.Count)
        {
            diagnostics.Report(
                "ILC2129",
                $"Expression '{target.ToDisplayString()}' expects {expectedRank} index{(expectedRank == 1 ? string.Empty : "es")}, but {indexExpressions.Count} were provided.",
                DiagnosticSeverity.Error,
                indexSpan);
        }

        foreach (var indexExpression in indexExpressions)
        {
            var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            if (indexType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2126",
                    $"Array index for '{target.ToDisplayString()}' must be Integer, but was '{indexType.Name}'.",
                    DiagnosticSeverity.Error,
                    indexSpan);
            }
        }
    }

    private static void ValidateSliceAccess(
        ExpressionSyntax target,
        ExpressionSyntax sliceExpression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var targetType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (targetType != TypeSymbol.String &&
            (!SemanticFacts.IsArrayType(targetType) ||
             SemanticFacts.GetArrayRank(targetType) != 1 ||
             SemanticFacts.ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) is not null))
        {
            diagnostics.Report(
                "ILC2161",
                $"Slices are only supported on one-dimensional arrays in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(sliceExpression, []));
            return;
        }

        if (sliceExpression is not RangeExpressionSyntax range)
        {
            return;
        }

        var startType = SemanticFacts.InferExpressionType(range.Start, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var endType = SemanticFacts.InferExpressionType(range.End, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (startType != TypeSymbol.Integer || endType != TypeSymbol.Integer)
        {
            diagnostics.Report(
                "ILC2126",
                $"Array slice indices for '{GetExpressionDisplayName(target)}' must be Integer.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(sliceExpression, []));
        }
    }

    private static bool TryReportInvalidFieldAccess(
        QualifiedNameSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        var receiverType = target.Parts.Count >= 2 && target.Parts[0].Text != "self"
            ? SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : null;
        if (receiverType is not null)
        {
            var field = knownFields.FirstOrDefault(candidate =>
                candidate.DeclaringTypeName == receiverType.Name &&
                candidate.Name == target.Parts[^1].Text);
            if (field is not null && field.IsStatic)
            {
                diagnostics.Report(
                    "ILC2113",
                    $"Static field '{target.ToDisplayString()}' cannot be accessed through an instance receiver.",
                    DiagnosticSeverity.Error,
                    target.Parts[^1].Span);
                return true;
            }

            return false;
        }

        return TryReportInvalidFieldAccess(target, knownFields, currentMethod, diagnostics);
    }

    private static bool TryReportInvalidFieldAccess(
        QualifiedNameSyntax target,
        IReadOnlyList<FieldSymbol> knownFields,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var candidate = SemanticFacts.ResolveFieldIgnoringAccess(target, knownFields, currentMethod);
        if (candidate is null)
        {
            return false;
        }

        var qualifier = target.Parts.Count > 1 ? target.Parts[^2].Text : null;
        if (target.Parts.Count > 1 && qualifier == "self" && candidate.IsStatic)
        {
            diagnostics.Report(
                "ILC2111",
                $"Static field '{target.ToDisplayString()}' cannot be accessed through self.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count > 1 && qualifier is not null && qualifier != "self" && !candidate.IsStatic)
        {
            diagnostics.Report(
                "ILC2110",
                $"Instance field '{target.ToDisplayString()}' cannot be accessed through a type qualifier.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count == 1 && !candidate.IsStatic && (currentMethod is null || currentMethod.IsStatic))
        {
            diagnostics.Report(
                "ILC2112",
                $"Instance field '{target.ToDisplayString()}' requires an object receiver.",
                DiagnosticSeverity.Error,
                target.Parts[0].Span);
            return true;
        }

        return false;
    }

    private static bool TryReportInvalidMethodAccess(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        return target switch
        {
            NameExpressionSyntax name => TryReportInvalidMethodAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics),
            MemberAccessExpressionSyntax memberAccess => TryReportInvalidMethodAccess(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics),
            _ => false
        };
    }

    private static bool TryReportInvalidMethodAccess(
        MemberAccessExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var candidate = SemanticFacts.ResolveInvocationIgnoringAccess(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (target.Receiver is NameExpressionSyntax receiverName)
        {
            var receiverDisplayName = receiverName.Name.ToDisplayString();
            if (candidate is not null && receiverDisplayName == "self" && candidate.Method.IsStatic)
            {
                diagnostics.Report(
                    "ILC2108",
                    $"Static method '{SemanticFacts.GetExpressionDisplayName(target)}' cannot be called through self.",
                    DiagnosticSeverity.Error,
                    target.MemberName.Span);
                return true;
            }

            if (SemanticFacts.ResolveTypeReference(receiverDisplayName, knownTypes) is { } receiverType)
            {
                var instanceMethod = knownMethods.FirstOrDefault(method =>
                    method.DeclaringTypeName == receiverType.Name &&
                    method.Name == target.MemberName.Text &&
                    SemanticFacts.SupportsArgumentCount(method, argumentCount) &&
                    !method.IsStatic);
                if (instanceMethod is not null)
                {
                    diagnostics.Report(
                        "ILC2107",
                        $"Instance method '{SemanticFacts.GetExpressionDisplayName(target)}' cannot be called through a type qualifier.",
                        DiagnosticSeverity.Error,
                        target.MemberName.Span);
                    return true;
                }
            }
        }

        if (candidate is null)
        {
            return false;
        }

        return false;
    }

    private static bool TryReportInvalidMethodAccess(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var candidate = SemanticFacts.ResolveInvocationIgnoringAccess(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (candidate is null)
        {
            return false;
        }

        if (candidate.IsVirtual &&
            SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is not null)
        {
            return false;
        }

        var qualifier = target.Parts.Count > 1 ? target.Parts[^2].Text : null;
        if (target.Parts.Count > 1 && qualifier == "self" && candidate.Method.IsStatic)
        {
            diagnostics.Report(
                "ILC2108",
                $"Static method '{target.ToDisplayString()}' cannot be called through self.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count > 1 && qualifier is not null && qualifier != "self" && !candidate.Method.IsStatic)
        {
            diagnostics.Report(
                "ILC2107",
                $"Instance method '{target.ToDisplayString()}' cannot be called through a type qualifier.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count == 1 && !candidate.Method.IsStatic && (currentMethod is null || currentMethod.IsStatic))
        {
            diagnostics.Report(
                "ILC2109",
                $"Instance method '{target.ToDisplayString()}' requires an object receiver.",
                DiagnosticSeverity.Error,
                target.Parts[0].Span);
            return true;
        }

        return false;
    }

    private static bool TryReportUnsupportedInterfacePropertyAccess(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var receiverType = SemanticFacts.TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (receiverType is null ||
            SemanticFacts.ResolveTypeReference(receiverType.Name, knownTypes) is not NamedTypeSymbol { IsInterface: true } interfaceType)
        {
            return false;
        }

        if (SemanticFacts.ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is null)
        {
            return false;
        }

        diagnostics.Report(
            "ILC2211",
            $"Interface property access through receiver type '{interfaceType.Name}' is not yet supported in the current bootstrap compiler.",
            DiagnosticSeverity.Error,
            name.Parts[^1].Span);
        return true;
    }

    private static bool TryReportUnsupportedInterfacePropertyAccess(
        MemberAccessExpressionSyntax memberAccess,
        MemberResolution memberResolution,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (memberResolution.Property is null)
        {
            return false;
        }

        var receiverType = SemanticFacts.InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (SemanticFacts.ResolveTypeReference(receiverType.Name, knownTypes) is not NamedTypeSymbol { IsInterface: true } interfaceType)
        {
            return false;
        }

        diagnostics.Report(
            "ILC2211",
            $"Interface property access through receiver type '{interfaceType.Name}' is not yet supported in the current bootstrap compiler.",
            DiagnosticSeverity.Error,
            memberAccess.MemberName.Span);
        return true;
    }

    private static TextSpan GetReferenceDiagnosticSpan(
        QualifiedNameSyntax name,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (name.Parts.Count == 0)
        {
            return new TextSpan(0, 0);
        }

        if (name.Parts.Count == 1)
        {
            return name.Parts[0].Span;
        }

        if (knownTypes.Any(type => type.Name == name.Parts[0].Text))
        {
            return name.Parts.Count > 1 ? name.Parts[1].Span : name.Parts[0].Span;
        }

        return name.Parts[0].Span;
    }

    private static TextSpan GetExpressionDiagnosticSpan(
        ExpressionSyntax expression,
        IReadOnlyList<TypeSymbol> knownTypes) =>
        expression switch
        {
            NameExpressionSyntax name => GetReferenceDiagnosticSpan(name.Name, knownTypes),
            MemberAccessExpressionSyntax member => member.MemberName.Span,
            ElementAccessExpressionSyntax element => GetReferenceDiagnosticSpan(element.Target, knownTypes),
            PostfixElementAccessExpressionSyntax element => GetExpressionDiagnosticSpan(element.Target, knownTypes),
            CallExpressionSyntax call => GetExpressionDiagnosticSpan(call.Target, knownTypes),
            LambdaExpressionSyntax lambda => lambda.SignatureKeyword.Span,
            ParenthesizedExpressionSyntax parenthesized => parenthesized.OpenParenToken.Span,
            MatchNotPatternSyntax notPattern => notPattern.NotKeyword.Span,
            MatchOrPatternSyntax orPattern => orPattern.Patterns.Count > 0
                ? GetExpressionDiagnosticSpan(orPattern.Patterns[0], knownTypes)
                : new TextSpan(0, 0),
            MatchAndPatternSyntax andPattern => andPattern.Patterns.Count > 0
                ? andPattern.Patterns[0].OperatorToken.Span
                : new TextSpan(0, 0),
            MatchRelationalPatternSyntax relational => relational.OperatorToken.Span,
            MatchExpressionSyntax matchExpression => matchExpression.MatchKeyword.Span,
            _ => new TextSpan(0, 0)
        };

    private static string GetExpressionDisplayName(ExpressionSyntax expression) =>
        expression switch
        {
            LambdaExpressionSyntax lambda => $"{lambda.SignatureKeyword.Text}(...) => ...",
            _ => SemanticFacts.GetExpressionDisplayName(expression)
        };

    private static MethodSymbol? FindMethod(
        IEnumerable<MethodSymbol> knownMethods,
        string? declaringTypeName,
        string name,
        int parameterCount) =>
        knownMethods.FirstOrDefault(method =>
            method.DeclaringTypeName == declaringTypeName &&
            method.Name == name &&
            method.Parameters.Count == parameterCount);

    private static IReadOnlyList<FieldSymbol> FindFields(
        IEnumerable<FieldSymbol> knownFields,
        string? declaringTypeName) =>
        knownFields
            .Where(field => field.DeclaringTypeName == declaringTypeName)
            .ToArray();

    private static IReadOnlyList<FieldSymbol> FindInstanceFields(
        IEnumerable<FieldSymbol> knownFields,
        string? declaringTypeName) =>
        knownFields
            .Where(field => field.DeclaringTypeName == declaringTypeName && !field.IsStatic)
            .ToArray();

    private static IReadOnlyList<ConstantSymbol> FindConstants(
        IEnumerable<ConstantSymbol> knownConstants,
        string? declaringTypeName) =>
        knownConstants
            .Where(constant => constant.DeclaringTypeName == declaringTypeName)
            .ToArray();

    private static bool HasDeclaredConstructors(
        QualifiedNameSyntax typeName,
        IEnumerable<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var resolvedType = SemanticFacts.ResolveTypeReference(typeName.ToDisplayString(), knownTypes);
        return resolvedType is not null && knownMethods.Any(method => method.DeclaringTypeName == resolvedType.Name && method.IsConstructor);
    }

    private static bool IsReferenceClassOrInterfaceType(TypeSymbol type) =>
        type == TypeSymbol.Object ||
        (type.IsReferenceType && type is NamedTypeSymbol {
            IsRecord: false or true,
            IsInterface: false or true
        });

    private static NamedTypeSymbol? ResolveNamedType(TypeSymbol type, IEnumerable<TypeSymbol> knownTypes) =>
        knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(candidate => candidate.Name == type.Name) ??
        (SemanticFacts.ResolveTypeReference(type.Name, knownTypes) as NamedTypeSymbol) ??
        (type as NamedTypeSymbol);

    private static bool CreatesTypeCycle(string declaredTypeName, TypeSymbol baseType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = ResolveNamedType(baseType, knownTypes);
        while (current is not null && visited.Add(current.Name))
        {
            if (current.Name == declaredTypeName)
            {
                return true;
            }

            current = ResolveNamedType(current.BaseType ?? TypeSymbol.Object, knownTypes);
        }

        return false;
    }

    private static IEnumerable<NamedTypeSymbol> GetTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var current = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current is not null && visited.Add(current.Name))
        {
            yield return current;
            current = ResolveNamedType(current.BaseType ?? TypeSymbol.Object, knownTypes);
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetInterfaceHierarchy(TypeSymbol interfaceType, IEnumerable<TypeSymbol> knownTypes)
    {
        var pending = new Queue<NamedTypeSymbol>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (ResolveNamedType(interfaceType, knownTypes) is { IsInterface: true } rootInterface)
        {
            pending.Enqueue(rootInterface);
        }

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Name))
            {
                continue;
            }

            yield return current;
            foreach (var inheritedInterface in current.InterfaceTypes)
            {
                if (ResolveNamedType(inheritedInterface, knownTypes) is { IsInterface: true } nextInterface)
                {
                    pending.Enqueue(nextInterface);
                }
            }
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetReceiverTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var resolvedType = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        if (resolvedType is null)
        {
            yield break;
        }

        if (resolvedType.IsInterface)
        {
            foreach (var interfaceType in GetInterfaceHierarchy(resolvedType, knownTypes))
            {
                yield return interfaceType;
            }

            yield break;
        }

        foreach (var candidate in GetTypeHierarchy(resolvedType, knownTypes))
        {
            yield return candidate;
        }
    }

    private static void ValidateInterfaceImplementation(
        string declaringTypeName,
        TypeSymbol interfaceType,
        ClassDeclarationSyntax classDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        DiagnosticBag diagnostics)
    {
        foreach (var inheritedInterface in GetInterfaceHierarchy(interfaceType, knownTypes))
        {
            foreach (var interfaceMethod in inheritedInterface.Methods.Where(method =>
                         !method.IsStatic &&
                         !method.IsConstructor))
            {
                var implementation = GetTypeHierarchy(new TypeSymbol(declaringTypeName, true), knownTypes)
                    .SelectMany(type => type.Methods
                        .Where(candidate =>
                            candidate.Name == interfaceMethod.Name &&
                            !candidate.IsStatic)
                        .Concat(knownMethods.Where(candidate =>
                            candidate.DeclaringTypeName == type.Name &&
                            candidate.Name == interfaceMethod.Name &&
                            !candidate.IsStatic)))
                    .FirstOrDefault(candidate => AreInterfaceMethodSignaturesCompatible(interfaceMethod, candidate, knownTypes));

                if (implementation is null)
                {
                    var hierarchy = GetTypeHierarchy(new TypeSymbol(declaringTypeName, true), knownTypes).ToArray();
                    var candidates = hierarchy
                        .SelectMany(type => type.Methods
                            .Where(candidate =>
                                candidate.Name == interfaceMethod.Name &&
                                !candidate.IsStatic)
                            .Concat(knownMethods.Where(candidate =>
                                candidate.DeclaringTypeName == type.Name &&
                                candidate.Name == interfaceMethod.Name &&
                                !candidate.IsStatic)))
                        .Select(candidate => $"{candidate.DeclaringTypeName}.{candidate.Name}({string.Join(", ", candidate.Parameters.Select(parameter => $"{parameter.PassingKind}:{parameter.Type.Name}"))}):{candidate.ReturnType.Name}")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    diagnostics.Report(
                        "ILC2209",
                        $"Class '{declaringTypeName}' does not implement interface method '{inheritedInterface.Name}.{interfaceMethod.Name}'. Expected=({string.Join(", ", interfaceMethod.Parameters.Select(parameter => $"{parameter.PassingKind}:{parameter.Type.Name}"))}):{interfaceMethod.ReturnType.Name}; Hierarchy=[{string.Join(", ", hierarchy.Select(type => type.Name))}]; Candidates=[{string.Join(" | ", candidates)}]",
                        DiagnosticSeverity.Error,
                        classDeclaration.Identifier.Span);
                }
            }
        }
    }

    private static void ValidateMethodInheritanceModifiers(
        MethodDeclarationSyntax methodDeclaration,
        MethodSymbol? boundMethod,
        TypeSymbol? baseType,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        string declaringTypeName,
        DiagnosticBag diagnostics)
    {
        if (boundMethod is null)
        {
            return;
        }

        if (boundMethod.IsVirtual && boundMethod.IsStatic)
        {
            diagnostics.Report(
                "ILC2196",
                $"Method '{declaringTypeName}.{boundMethod.Name}' cannot be static and virtual.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
        }

        if (boundMethod.IsOverride && boundMethod.IsVirtual)
        {
            diagnostics.Report(
                "ILC2197",
                $"Method '{declaringTypeName}.{boundMethod.Name}' cannot be marked both virtual and override.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
        }

        if (boundMethod.IsStatic && boundMethod.IsOverride)
        {
            diagnostics.Report(
                "ILC2196",
                $"Method '{declaringTypeName}.{boundMethod.Name}' cannot be static and override.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
        }

        if (!boundMethod.IsOverride || baseType is null)
        {
            return;
        }

        var overriddenMethod = FindOverridableBaseMethod(baseType, boundMethod, knownTypes, knownMethods);
        var baseMethodByName = overriddenMethod ?? FindBaseMethodByName(baseType, boundMethod, knownTypes, knownMethods);
        if (baseMethodByName is null)
        {
            diagnostics.Report(
                "ILC2198",
                $"Method '{declaringTypeName}.{boundMethod.Name}' is marked override but no matching virtual method exists in base type hierarchy.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);

            return;
        }

        if (!(baseMethodByName.IsVirtual || baseMethodByName.IsOverride))
        {
            diagnostics.Report(
                "ILC2198",
                $"Method '{declaringTypeName}.{boundMethod.Name}' is marked override but base method '{baseMethodByName.DeclaringTypeName}.{baseMethodByName.Name}' is not virtual.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
            return;
        }

        if (overriddenMethod is null)
        {
            diagnostics.Report(
                "ILC2199",
                $"Override method '{declaringTypeName}.{boundMethod.Name}' signature does not match overridden method '{baseMethodByName.DeclaringTypeName}.{baseMethodByName.Name}'.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
            return;
        }

        if (!AreMethodSignaturesEquivalent(overriddenMethod, boundMethod))
        {
            diagnostics.Report(
                "ILC2199",
                $"Override method '{declaringTypeName}.{boundMethod.Name}' signature does not match overridden method '{overriddenMethod.DeclaringTypeName}.{overriddenMethod.Name}'.",
                DiagnosticSeverity.Error,
                methodDeclaration.Keyword.Span);
        }
    }

    private static MethodSymbol? FindOverridableBaseMethod(
        TypeSymbol baseType,
        MethodSymbol method,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        foreach (var baseTypeEntry in GetTypeHierarchy(baseType, knownTypes))
        {
            var candidate = FindMethod(
                knownMethods,
                baseTypeEntry.Name,
                method.Name,
                method.Parameters.Count,
                method.Parameters);

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static MethodSymbol? FindBaseMethodByName(
        TypeSymbol baseType,
        MethodSymbol method,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        foreach (var baseTypeEntry in GetTypeHierarchy(baseType, knownTypes))
        {
            var candidate = knownMethods.FirstOrDefault(knownMethod =>
                knownMethod.DeclaringTypeName == baseTypeEntry.Name &&
                knownMethod.Name == method.Name);

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static MethodSymbol? FindMethod(
        IEnumerable<MethodSymbol> knownMethods,
        string? declaringTypeName,
        string name,
        int parameterCount,
        IReadOnlyList<ParameterSymbol> parameterTypes)
    {
        return knownMethods.FirstOrDefault(candidate =>
            candidate.DeclaringTypeName == declaringTypeName &&
            candidate.Name == name &&
            candidate.Parameters.Count == parameterCount &&
            candidate.Parameters.Select(parameter => parameter.Type).SequenceEqual(parameterTypes.Select(parameter => parameter.Type)));
    }

    private static bool AreMethodSignaturesEquivalent(MethodSymbol left, MethodSymbol right)
    {
        if (left.ReturnType != right.ReturnType || left.Parameters.Count != right.Parameters.Count)
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < left.Parameters.Count; parameterIndex++)
        {
            var leftParameter = left.Parameters[parameterIndex];
            var rightParameter = right.Parameters[parameterIndex];
            if (leftParameter.Type != rightParameter.Type || leftParameter.PassingKind != rightParameter.PassingKind)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryValidateDelegateLambdaConversion(
        ExpressionSyntax expression,
        TypeSymbol? expectedType,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        if (expectedType is null ||
            ResolveNamedType(expectedType, knownTypes) is not { IsDelegate: true } delegateType)
        {
            diagnostics.Report(
                "ILC2219",
                "Lambda expressions require a delegate target type in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(lambda, knownTypes));
            return true;
        }

        var invokeMethod = delegateType.Methods.FirstOrDefault(method => method.Name == "Invoke" && !method.IsStatic);
        if (invokeMethod is null)
        {
            diagnostics.Report(
                "ILC2220",
                $"Delegate type '{delegateType.Name}' does not expose a callable Invoke signature.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(lambda, knownTypes));
            return true;
        }

        if (lambda.Parameters.Count != invokeMethod.Parameters.Count)
        {
            diagnostics.Report(
                "ILC2221",
                $"Lambda parameter count {lambda.Parameters.Count} is not compatible with delegate '{delegateType.Name}' parameter count {invokeMethod.Parameters.Count}.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(lambda, knownTypes));
            return true;
        }

        var lambdaLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal);
        for (var parameterIndex = 0; parameterIndex < lambda.Parameters.Count; parameterIndex++)
        {
            var parameter = lambda.Parameters[parameterIndex];
            var delegateParameter = invokeMethod.Parameters[parameterIndex];
            var parameterType = BindType(parameter.TypeName, knownTypes);
            if (parameterType != delegateParameter.Type)
            {
                diagnostics.Report(
                    "ILC2222",
                    $"Lambda parameter '{parameter.Identifier.Text}' must have type '{delegateParameter.Type.Name}' to match delegate '{delegateType.Name}', but was '{parameterType.Name}'.",
                    DiagnosticSeverity.Error,
                    parameter.TypeName.Parts[0].Span);
                return true;
            }

            lambdaLocals[parameter.Identifier.Text] = parameterType;
        }

        if (lambda.SignatureKeyword.Kind == SyntaxKind.FunctionKeyword)
        {
            if (lambda.ReturnType is null)
            {
                diagnostics.Report(
                    "ILC2223",
                    "Function lambdas must declare an explicit return type in the current bootstrap compiler.",
                    DiagnosticSeverity.Error,
                    lambda.SignatureKeyword.Span);
                return true;
            }

            var declaredReturnType = BindType(lambda.ReturnType, knownTypes);
            if (declaredReturnType != invokeMethod.ReturnType)
            {
                diagnostics.Report(
                    "ILC2224",
                    $"Lambda return type '{declaredReturnType.Name}' is not compatible with delegate '{delegateType.Name}' return type '{invokeMethod.ReturnType.Name}'.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(lambda.ReturnType, knownTypes));
                return true;
            }
        }
        else if (invokeMethod.ReturnType != TypeSymbol.Void)
        {
            diagnostics.Report(
                "ILC2225",
                $"Procedure lambda is not compatible with non-void delegate '{delegateType.Name}'.",
                DiagnosticSeverity.Error,
                lambda.SignatureKeyword.Span);
            return true;
        }

        ValidateExpression(lambda.Body, lambdaLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
        var bodyType = SemanticFacts.InferExpressionType(lambda.Body, lambdaLocals, knownMethods, knownFields, knownConstants, knownProperties, null, knownTypes);
        if (invokeMethod.ReturnType != TypeSymbol.Void && bodyType != invokeMethod.ReturnType)
        {
            diagnostics.Report(
                "ILC2226",
                $"Lambda body type '{bodyType.Name}' is not compatible with delegate '{delegateType.Name}' return type '{invokeMethod.ReturnType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(lambda.Body, knownTypes));
        }

        return true;
    }

    private static bool AreInterfaceMethodSignaturesCompatible(MethodSymbol contractMethod, MethodSymbol implementationMethod, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (contractMethod.Parameters.Count != implementationMethod.Parameters.Count)
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < contractMethod.Parameters.Count; parameterIndex++)
        {
            var contractParameter = contractMethod.Parameters[parameterIndex];
            var implementationParameter = implementationMethod.Parameters[parameterIndex];
            if (contractParameter.Type != implementationParameter.Type || contractParameter.PassingKind != implementationParameter.PassingKind)
            {
                return false;
            }
        }

        if (contractMethod.ReturnType == implementationMethod.ReturnType)
        {
            return true;
        }

        if (contractMethod.ReturnType is NamedTypeSymbol { GenericArity: > 0, GenericDefinition: null } openContractReturn &&
            implementationMethod.ReturnType is NamedTypeSymbol implementationReturn &&
            implementationReturn.GenericDefinition?.Name == openContractReturn.Name &&
            implementationReturn.GenericDefinition.GenericArity == openContractReturn.GenericArity)
        {
            return true;
        }

        return SemanticFacts.IsCompatibleReferenceType(implementationMethod.ReturnType, contractMethod.ReturnType, knownTypes);
    }

    private static bool AreDelegateMethodSignaturesCompatible(MethodSymbol delegateInvokeMethod, MethodSymbol targetMethod, IReadOnlyList<TypeSymbol> knownTypes) =>
        AreInterfaceMethodSignaturesCompatible(delegateInvokeMethod, targetMethod, knownTypes);

    private static TypeSymbol BindType(QualifiedNameSyntax? typeName, IEnumerable<TypeSymbol>? knownTypes = null)
    {
        if (typeName is null)
        {
            return TypeSymbol.Integer;
        }

        var resolvedType = SemanticFacts.ResolveTypeReference(typeName.ToDisplayString(), knownTypes ?? []);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        return SemanticFacts.TryResolveBuiltInType(typeName.ToDisplayString())
            ?? new TypeSymbol(typeName.ToDisplayString(), true);
    }

    private static TypeSymbol ResolveDeclaredType(string typeName, IEnumerable<TypeSymbol> knownTypes, bool isReferenceType) =>
        SemanticFacts.ResolveTypeReference(typeName, knownTypes) ?? new TypeSymbol(typeName, isReferenceType);

    private static (TypeSymbol? BaseType, IReadOnlyList<TypeSymbol> InterfaceTypes) ResolveClassInheritanceTargets(ClassDeclarationSyntax classDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var interfaceTypes = new List<TypeSymbol>();
        TypeSymbol? baseType = null;

        if (classDeclaration.BaseType is not null)
        {
            var primaryType = BindType(classDeclaration.BaseType, knownTypes);
            if (ResolveNamedType(primaryType, knownTypes) is { IsInterface: true })
            {
                interfaceTypes.Add(primaryType);
            }
            else
            {
                baseType = primaryType;
            }
        }

        foreach (var interfaceTypeName in classDeclaration.InterfaceTypes)
        {
            interfaceTypes.Add(BindType(interfaceTypeName, knownTypes));
        }

        return (baseType, interfaceTypes);
    }

    private static NamedTypeSymbol BindClass(ClassDeclarationSyntax classDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var typeParameters = BindTypeParameters(classDeclaration.TypeParameters);
        var typeScope = knownTypes.Concat(typeParameters).ToArray();
        var (baseType, interfaceTypes) = ResolveClassInheritanceTargets(classDeclaration, typeScope);
        var constants = classDeclaration.Members
            .OfType<ConstantDeclarationSyntax>()
            .SelectMany(constant => BindConstants(constant, classDeclaration.Identifier.Text, typeScope))
            .ToArray();
        var declaredFields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => BindFields(field, classDeclaration.Identifier.Text, typeScope))
            .ToArray();
        var autoPropertyFields = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(property => property.OpenBraceToken is not null)
            .Select(property => BindAutoPropertyBackingField(property, classDeclaration.Identifier.Text, typeScope))
            .ToArray();
        var fields = declaredFields
            .Concat(autoPropertyFields)
            .ToArray();
        var declaredMethods = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => BindMethod(method, classDeclaration.Identifier.Text, typeScope))
            .ToArray();
        var properties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(property => BindProperty(property, classDeclaration.Identifier.Text, fields, typeScope))
            .ToArray();
        var propertyAccessorMethods = properties
            .SelectMany(property => new[] { property.GetterMethod, property.SetterMethod })
            .Where(method => method is not null)
            .Cast<MethodSymbol>()
            .ToArray();
        var methods = declaredMethods
            .Concat(propertyAccessorMethods)
            .ToArray();

        return new NamedTypeSymbol(
            classDeclaration.Identifier.Text,
            true,
            classDeclaration.ClassKeyword.Kind == SyntaxKind.RecordKeyword,
            false,
            baseType,
            interfaceTypes,
            methods,
            fields,
            constants,
            properties,
            typeParameters.Count,
            typeParameters);
    }

    private static NamedTypeSymbol BindInterface(InterfaceDeclarationSyntax interfaceDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var typeParameters = BindTypeParameters(interfaceDeclaration.TypeParameters);
        var typeScope = knownTypes.Concat(typeParameters).ToArray();
        var interfaceTypes = interfaceDeclaration.BaseInterfaces
            .Select(typeName => BindType(typeName, typeScope))
            .ToArray();
        var methods = interfaceDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => BindMethod(method, interfaceDeclaration.Identifier.Text, typeScope))
            .ToArray();
        var properties = interfaceDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(property => BindProperty(property, interfaceDeclaration.Identifier.Text, [], typeScope))
            .ToArray();
        var propertyAccessorMethods = properties
            .SelectMany(property => new[] { property.GetterMethod, property.SetterMethod })
            .Where(method => method is not null)
            .Cast<MethodSymbol>()
            .ToArray();

        return new NamedTypeSymbol(
            interfaceDeclaration.Identifier.Text,
            true,
            false,
            true,
            null,
            interfaceTypes,
            methods.Concat(propertyAccessorMethods).ToArray(),
            [],
            [],
            properties,
            typeParameters.Count,
            typeParameters);
    }

    private static NamedTypeSymbol BindDelegate(DelegateDeclarationSyntax delegateDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var typeParameters = BindTypeParameters(delegateDeclaration.TypeParameters);
        var typeScope = knownTypes.Concat(typeParameters).ToArray();
        var invokeReturnType = delegateDeclaration.ReturnType is null
            ? TypeSymbol.Void
            : BindType(delegateDeclaration.ReturnType, typeScope);
        var delegateTypeName = delegateDeclaration.Identifier.Text;
        var invokeParameters = delegateDeclaration.Parameters
            .Select(parameter => new ParameterSymbol(
                parameter.Identifier.Text,
                BindType(parameter.TypeName, typeScope),
                BindParameterPassingKind(parameter.ModifierKeyword)))
            .ToArray();
        var targetField = new FieldSymbol("TargetObjectValue", TypeSymbol.Object, delegateTypeName, false, null);
        var methodIdField = new FieldSymbol("TargetFunctionIdValue", TypeSymbol.Integer, delegateTypeName, false, null);
        var constructor = new MethodSymbol(
            Name: ".ctor",
            ReturnType: TypeSymbol.Void,
            Parameters:
            [
                new ParameterSymbol("target", TypeSymbol.Object),
                new ParameterSymbol("methodId", TypeSymbol.Integer)
            ],
            DeclaringTypeName: delegateTypeName,
            IsStatic: false,
            Declaration: null,
            IsConstructor: true,
            IsSynthetic: true,
            SyntheticMembers: null,
            IsExtern: false,
            IsVirtual: false,
            IsOverride: false,
            HostImportKind: HostImportKind.DelegateBind);
        var invokeMethod = new MethodSymbol(
            Name: "Invoke",
            ReturnType: invokeReturnType,
            Parameters: invokeParameters,
            DeclaringTypeName: delegateTypeName,
            IsStatic: false,
            Declaration: null,
            IsConstructor: false,
            IsSynthetic: true,
            SyntheticMembers: null,
            IsExtern: false,
            IsVirtual: false,
            IsOverride: false,
            HostImportKind: HostImportKind.DelegateInvoke);

        return new NamedTypeSymbol(
            delegateTypeName,
            true,
            false,
            false,
            TypeSymbol.Object,
            [],
            [constructor, invokeMethod],
            [targetField, methodIdField],
            [],
            [],
            typeParameters.Count,
            typeParameters,
            null,
            null,
            true);
    }

    private static IReadOnlyList<TypeParameterSymbol> BindTypeParameters(TypeParameterListSyntax? typeParameters) =>
        typeParameters?.Parameters.Select(parameter => new TypeParameterSymbol(parameter.Text)).ToArray()
        ?? [];

    private static IReadOnlyList<NamedTypeSymbol> CollectConstructedGenericTypes(SyntaxTree syntaxTree, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var genericTypeNames = new HashSet<string>(StringComparer.Ordinal);
        CollectConstructedGenericTypeNames(syntaxTree.Root, genericTypeNames);
        return genericTypeNames
            .Select(typeName => SemanticFacts.ResolveTypeReference(typeName, knownTypes))
            .OfType<NamedTypeSymbol>()
            .Where(type => type.GenericDefinition is not null)
            .ToArray();
    }

    private static void AddConstructedGenericClosure(List<TypeSymbol> declaredTypes)
    {
        var index = 0;
        while (index < declaredTypes.Count)
        {
            if (declaredTypes[index] is NamedTypeSymbol namedType)
            {
                foreach (var referencedType in CollectReferencedConstructedTypes(namedType))
                {
                    if (declaredTypes.All(existing => existing.Name != referencedType.Name))
                    {
                        declaredTypes.Add(referencedType);
                    }
                }
            }

            index++;
        }
    }

    private static IEnumerable<NamedTypeSymbol> CollectReferencedConstructedTypes(NamedTypeSymbol type)
    {
        if (type.BaseType is NamedTypeSymbol { GenericDefinition: not null } baseType)
        {
            yield return baseType;
        }

        foreach (var interfaceType in type.InterfaceTypes.OfType<NamedTypeSymbol>().Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return interfaceType;
        }

        foreach (var fieldType in type.Fields.Select(field => field.Type).OfType<NamedTypeSymbol>().Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return fieldType;
        }

        foreach (var propertyType in type.Properties.Select(property => property.Type).OfType<NamedTypeSymbol>().Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return propertyType;
        }

        foreach (var indexParameterType in type.Properties
                     .Select(property => property.IndexParameter?.Type)
                     .OfType<NamedTypeSymbol>()
                     .Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return indexParameterType;
        }

        foreach (var methodType in type.Methods.Select(method => method.ReturnType).OfType<NamedTypeSymbol>().Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return methodType;
        }

        foreach (var parameterType in type.Methods
                     .SelectMany(method => method.Parameters.Select(parameter => parameter.Type))
                     .OfType<NamedTypeSymbol>()
                     .Where(candidate => candidate.GenericDefinition is not null))
        {
            yield return parameterType;
        }
    }

    private static void CollectConstructedGenericTypeNames(object? value, HashSet<string> genericTypeNames)
    {
        if (value is null or string or SyntaxToken)
        {
            return;
        }

        if (value is QualifiedNameSyntax qualifiedName)
        {
            var displayName = qualifiedName.ToDisplayString();
            if (displayName.Contains('<'))
            {
                genericTypeNames.Add(displayName);
            }
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectConstructedGenericTypeNames(item, genericTypeNames);
            }

            return;
        }

        var valueType = value.GetType();
        foreach (var property in valueType.GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            CollectConstructedGenericTypeNames(property.GetValue(value), genericTypeNames);
        }
    }

    private static IReadOnlyList<FieldSymbol> BindFields(FieldDeclarationSyntax fieldDeclaration, string declaringTypeName, IReadOnlyList<TypeSymbol> knownTypes) =>
        fieldDeclaration.Declarators
            .Select(declarator => new FieldSymbol(
                declarator.Identifier.Text,
                declarator.TypeName is not null ? BindType(declarator.TypeName, knownTypes) : TypeSymbol.Integer,
                declaringTypeName,
                fieldDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
                fieldDeclaration))
            .ToArray();

    private static IReadOnlyList<ConstantSymbol> BindConstants(ConstantDeclarationSyntax constantDeclaration, string declaringTypeName, IReadOnlyList<TypeSymbol> knownTypes) =>
        constantDeclaration.Declarators
            .Select(declarator => new ConstantSymbol(
                declarator.Identifier.Text,
                declarator.TypeName is not null ? BindType(declarator.TypeName, knownTypes) : InferLiteralOrNamedConstantType(declarator.Initializer),
                InferLiteralOrNamedConstantValue(declarator.Initializer),
                declaringTypeName,
                true))
            .ToArray();

    private static IReadOnlyList<ConstantSymbol> BindEnumConstants(EnumDeclarationSyntax enumDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var enumType = ResolveDeclaredType(enumDeclaration.Identifier.Text, knownTypes, false);
        var constants = new List<ConstantSymbol>();
        var nextValue = 0;
        foreach (var member in enumDeclaration.Members)
        {
            var value = member.ValueToken is not null && SemanticFacts.TryGetInt32LiteralValue(member.ValueToken, out var explicitValue)
                ? explicitValue
                : nextValue;
            constants.Add(new ConstantSymbol(
                member.Identifier.Text,
                enumType,
                value,
                enumDeclaration.Identifier.Text,
                true));
            nextValue = value + 1;
        }

        return constants;
    }

    private static TypeSymbol InferLiteralOrNamedConstantType(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal => literal.LiteralToken.Kind switch
            {
                SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword => TypeSymbol.Boolean,
                SyntaxKind.StringToken => TypeSymbol.String,
                SyntaxKind.NilKeyword => TypeSymbol.Nil,
                _ => TypeSymbol.Integer
            },
            _ => TypeSymbol.Integer
        };

    private static object? InferLiteralOrNamedConstantValue(ExpressionSyntax expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal when literal.LiteralToken.Kind == SyntaxKind.TrueKeyword => 1,
            LiteralExpressionSyntax literal when literal.LiteralToken.Kind == SyntaxKind.FalseKeyword => 0,
            LiteralExpressionSyntax literal => SemanticFacts.GetLiteralValue(literal.LiteralToken) ?? 0,
            _ => null
        };

    private static MethodSymbol BindMethod(MethodDeclarationSyntax methodDeclaration, string? declaringTypeName = null, IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var returnType = methodDeclaration.ReturnType is null
            ? TypeSymbol.Void
            : BindType(methodDeclaration.ReturnType, knownTypes);

        var parameters = methodDeclaration.Parameters
            .Select(parameter => new ParameterSymbol(
                parameter.Identifier.Text,
                BindType(parameter.TypeName, knownTypes),
                BindParameterPassingKind(parameter.ModifierKeyword)))
            .ToArray();

        return new MethodSymbol(
            Name: methodDeclaration.Identifier.Text,
            ReturnType: returnType,
            Parameters: parameters,
            DeclaringTypeName: declaringTypeName,
            IsStatic: methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
            Declaration: methodDeclaration,
            IsConstructor: methodDeclaration.Keyword.Kind == SyntaxKind.ConstructorKeyword,
            IsSynthetic: false,
            SyntheticMembers: null,
            IsExtern: methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.ExternKeyword),
            IsVirtual: methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.VirtualKeyword),
            IsOverride: methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.OverrideKeyword),
            HostImportKind: ResolveHostImportKind(
                methodDeclaration.Identifier.Text,
                returnType,
                parameters,
                declaringTypeName,
                methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
                methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.ExternKeyword)),
            DllImport: BindDllImportMetadata(methodDeclaration));
    }

    private sealed record SyntheticLambdaArtifact(
        LambdaExpressionSyntax Lambda,
        NamedTypeSymbol? ClosureType,
        MethodSymbol Method);

    private sealed record SyntheticLambdaArtifacts(
        IReadOnlyList<NamedTypeSymbol> Types,
        IReadOnlyList<MethodSymbol> Methods);

    private static SyntheticLambdaArtifacts CollectSyntheticLambdaArtifacts(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var types = new List<NamedTypeSymbol>();
        var methods = new List<MethodSymbol>();
        var nextLambdaId = 0;

        foreach (var classDeclaration in members.OfType<ClassDeclarationSyntax>())
        {
            var typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                foreach (var artifact in CollectLambdaArtifacts(methodDeclaration, classDeclaration.Identifier.Text, typeScope, ref nextLambdaId))
                {
                    if (artifact.ClosureType is not null)
                    {
                        types.Add(artifact.ClosureType);
                    }

                    if (artifact.Method.IsStatic)
                    {
                        methods.Add(artifact.Method);
                    }
                }
            }
        }

        return new SyntheticLambdaArtifacts(types, methods);
    }

    private static IReadOnlyList<SyntheticLambdaArtifact> CollectLambdaArtifacts(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        ref int nextLambdaId)
    {
        var outerLocals = CollectMethodLambdaCaptureScope(methodDeclaration, knownTypes);
        var lambdas = new List<LambdaExpressionSyntax>();
        CollectLambdaExpressions(methodDeclaration.ExpressionBody, lambdas);
        CollectLambdaExpressions(methodDeclaration.Body, lambdas);

        var artifacts = new List<SyntheticLambdaArtifact>();
        foreach (var lambda in lambdas)
        {
            var captures = CollectLambdaCaptures(lambda, outerLocals);
            if (captures.Count == 0)
            {
                artifacts.Add(new SyntheticLambdaArtifact(
                    lambda,
                    null,
                    BindSyntheticLambdaMethod(lambda, declaringTypeName, ++nextLambdaId, knownTypes)));
                continue;
            }

            var closureTypeName = $"__LambdaClosure_{++nextLambdaId}";
            var closureFields = captures
                .Select(capture => new FieldSymbol(capture.Key, capture.Value, closureTypeName, false, null))
                .ToArray();
            var closureMethod = BindSyntheticLambdaMethod(lambda, closureTypeName, nextLambdaId, knownTypes, isStatic: false);
            var closureType = new NamedTypeSymbol(
                closureTypeName,
                true,
                false,
                false,
                TypeSymbol.Object,
                [],
                [closureMethod],
                closureFields,
                [],
                [],
                0,
                []);
            artifacts.Add(new SyntheticLambdaArtifact(lambda, closureType, closureMethod));
        }

        return artifacts;
    }

    private static MethodSymbol BindSyntheticLambdaMethod(
        LambdaExpressionSyntax lambda,
        string? declaringTypeName,
        int ordinal,
        IReadOnlyList<TypeSymbol> knownTypes,
        bool isStatic = true)
    {
        var identifier = new SyntaxToken(
            SyntaxKind.IdentifierToken,
            $"__lambda_{ordinal}",
            $"__lambda_{ordinal}",
            lambda.SignatureKeyword.Span);
        var declaration = new MethodDeclarationSyntax(
            Attributes: [],
            Modifiers: isStatic ? [new SyntaxToken(SyntaxKind.StaticKeyword, "static", null, lambda.SignatureKeyword.Span)] : [],
            Keyword: lambda.SignatureKeyword,
            Identifier: identifier,
            OpenParenToken: lambda.OpenParenToken,
            Parameters: lambda.Parameters,
            CloseParenToken: lambda.CloseParenToken,
            ColonToken: lambda.ColonToken,
            ReturnType: lambda.ReturnType,
            ArrowToken: lambda.ArrowToken,
            ExpressionBody: lambda.Body,
            Body: null,
            TerminatorToken: lambda.ArrowToken);
        var boundMethod = BindMethod(declaration, declaringTypeName, knownTypes);
        return boundMethod with
        {
            IsSynthetic = true,
            LambdaSource = lambda
        };
    }

    private static Dictionary<string, TypeSymbol> CollectMethodLambdaCaptureScope(
        MethodDeclarationSyntax methodDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var locals = methodDeclaration.Parameters.ToDictionary(
            parameter => parameter.Identifier.Text,
            parameter => BindType(parameter.TypeName, knownTypes),
            StringComparer.Ordinal);

        if (methodDeclaration.Body is not null)
        {
            CollectDeclaredLocals(methodDeclaration.Body, locals, knownTypes);
        }

        return locals;
    }

    private static void CollectDeclaredLocals(
        object? value,
        Dictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (value is null or string or SyntaxToken or LambdaExpressionSyntax)
        {
            return;
        }

        if (value is LocalVariableDeclarationStatementSyntax localDeclaration)
        {
            foreach (var declarator in localDeclaration.Declarators)
            {
                locals[declarator.Identifier.Text] = declarator.TypeName is not null
                    ? BindType(declarator.TypeName, knownTypes)
                    : TypeSymbol.Integer;
            }
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectDeclaredLocals(item, locals, knownTypes);
            }

            return;
        }

        var valueType = value.GetType();
        foreach (var property in valueType.GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            CollectDeclaredLocals(property.GetValue(value), locals, knownTypes);
        }
    }

    private static Dictionary<string, TypeSymbol> CollectLambdaCaptures(
        LambdaExpressionSyntax lambda,
        IReadOnlyDictionary<string, TypeSymbol> outerLocals)
    {
        var lambdaParameters = lambda.Parameters
            .Select(parameter => parameter.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);
        var referencedNames = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencedNames(lambda.Body, referencedNames);

        return referencedNames
            .Where(name => !lambdaParameters.Contains(name) && outerLocals.ContainsKey(name))
            .ToDictionary(name => name, name => outerLocals[name], StringComparer.Ordinal);
    }

    private static void CollectReferencedNames(object? value, HashSet<string> names)
    {
        if (value is null or string or SyntaxToken or LambdaExpressionSyntax)
        {
            return;
        }

        if (value is NameExpressionSyntax nameExpression && nameExpression.Name.Parts.Count == 1)
        {
            names.Add(nameExpression.Name.Parts[0].Text);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectReferencedNames(item, names);
            }

            return;
        }

        var valueType = value.GetType();
        foreach (var property in valueType.GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            CollectReferencedNames(property.GetValue(value), names);
        }
    }

    private static void CollectLambdaExpressions(object? value, List<LambdaExpressionSyntax> lambdas)
    {
        if (value is null or string or SyntaxToken)
        {
            return;
        }

        if (value is LambdaExpressionSyntax lambda)
        {
            lambdas.Add(lambda);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectLambdaExpressions(item, lambdas);
            }

            return;
        }

        var valueType = value.GetType();
        foreach (var property in valueType.GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            CollectLambdaExpressions(property.GetValue(value), lambdas);
        }
    }

    private static void ValidateDllImportMethod(
        string declaringTypeName,
        MethodDeclarationSyntax declaration,
        MethodSymbol method,
        DiagnosticBag diagnostics)
    {
        if (!method.IsExtern)
        {
            diagnostics.Report(
                "ILC2212",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must be declared extern.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (!method.IsStatic)
        {
            diagnostics.Report(
                "ILC2213",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must be declared static.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (method.IsConstructor)
        {
            diagnostics.Report(
                "ILC2214",
                $"DllImport constructor '{declaringTypeName}' is not supported.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }

        if (method.DllImport is null)
        {
            diagnostics.Report(
                "ILC2215",
                $"DllImport method '{declaringTypeName}.{declaration.Identifier.Text}' must declare a string library name.",
                DiagnosticSeverity.Error,
                declaration.Keyword.Span);
        }
    }

    private static DllImportMetadata? BindDllImportMetadata(MethodDeclarationSyntax methodDeclaration)
    {
        var dllImportAttribute = methodDeclaration.Attributes.FirstOrDefault(attribute => IsDllImportAttribute(attribute));
        if (dllImportAttribute is null)
        {
            return null;
        }

        var libraryName = dllImportAttribute.Arguments
            .FirstOrDefault(argument => argument.ModifierKeyword is null)?.Expression switch
        {
            LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.StringToken, LiteralToken.Value: string value } => value,
            _ => null
        };

        var entryPoint = methodDeclaration.Identifier.Text;
        var callingConvention = NativeCallingConvention.Cdecl;

        foreach (var argument in dllImportAttribute.Arguments.Select(argument => argument.Expression).OfType<AssignmentExpressionSyntax>())
        {
            var targetName = argument.Target switch
            {
                NameExpressionSyntax { Name.Parts.Count: > 0 } name => name.Name.Parts[^1].Text,
                MemberAccessExpressionSyntax memberAccess => memberAccess.MemberName.Text,
                _ => null
            };

            if (string.Equals(targetName, "EntryPoint", StringComparison.Ordinal))
            {
                if (argument.Expression is LiteralExpressionSyntax { LiteralToken.Kind: SyntaxKind.StringToken, LiteralToken.Value: string value })
                {
                    entryPoint = value;
                }
            }
            else if (string.Equals(targetName, "CallingConvention", StringComparison.Ordinal))
            {
                switch (argument.Expression)
                {
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "StdCall", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "StdCall" }:
                        callingConvention = NativeCallingConvention.StdCall;
                        break;
                    case NameExpressionSyntax { Name.Parts.Count: > 0 } name when string.Equals(name.Name.Parts[^1].Text, "Cdecl", StringComparison.Ordinal):
                    case MemberAccessExpressionSyntax { MemberName.Text: "Cdecl" }:
                        callingConvention = NativeCallingConvention.Cdecl;
                        break;
                }
            }
        }

        return libraryName is null
            ? null
            : new DllImportMetadata(libraryName, entryPoint, callingConvention);
    }

    private static bool IsDllImportAttribute(AttributeSyntax attribute) =>
        string.Equals(attribute.Name.Parts[^1].Text, "DllImport", StringComparison.Ordinal);

    private static HostImportKind ResolveHostImportKind(
        string methodName,
        TypeSymbol returnType,
        IReadOnlyList<ParameterSymbol> parameters,
        string? declaringTypeName,
        bool isStatic,
        bool isExtern)
    {
        if (!isExtern)
        {
            return HostImportKind.None;
        }

        if (declaringTypeName == "Console" && isStatic)
        {
            if (methodName == "Write" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.ConsoleWrite;
            }

            if (methodName == "WriteLine" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.ConsoleWriteLine;
            }

            if (methodName == "ReadLine" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ConsoleReadLine;
            }
        }

        if (declaringTypeName == "Environment" && isStatic)
        {
            if (methodName == "GetCommandLineArgsCore" &&
                returnType.Name == $"{TypeSymbol.String.Name}[]" &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetCommandLineArgs;
            }

            if (methodName == "GetCurrentDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetCurrentDirectory;
            }

            if (methodName == "GetUserNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetUserName;
            }

            if (methodName == "GetMachineNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetMachineName;
            }

            if (methodName == "GetHomeDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetHomeDirectory;
            }

            if (methodName == "GetTempDirectoryCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.EnvironmentGetTempDirectory;
            }

            if (methodName == "GetEnvironmentVariableCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.EnvironmentGetEnvironmentVariable;
            }

            if (methodName == "SetEnvironmentVariableCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.EnvironmentSetEnvironmentVariable;
            }
        }

        if (declaringTypeName == "Clock" && isStatic)
        {
            if (methodName == "GetMonotonicMillisecondsTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetMonotonicMillisecondsText;
            }

            if (methodName == "GetWallMillisecondsTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetWallMillisecondsText;
            }

            if (methodName == "GetWallDateTimeTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 0)
            {
                return HostImportKind.ClockGetWallDateTimeText;
            }
        }

        if (declaringTypeName == "Exception" && isStatic)
        {
            if (methodName == "GetCurrentStackTraceCore" &&
                returnType.Name == $"{TypeSymbol.String.Name}[]" &&
                parameters.Count == 0)
            {
                return HostImportKind.ExceptionGetCurrentStackTrace;
            }
        }

        if (declaringTypeName == "File" && isStatic)
        {
            if (methodName == "ExistsCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.FileExists;
            }

            if (methodName == "ReadAllTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.FileReadAllText;
            }

            if (methodName == "WriteAllTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.FileWriteAllText;
            }

            if (methodName == "AppendAllTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.FileAppendAllText;
            }
        }

        if (declaringTypeName == "Path" && isStatic)
        {
            if (methodName == "CombineCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.PathCombine;
            }

            if (methodName == "GetFileNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetFileName;
            }

            if (methodName == "GetDirectoryNameCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetDirectoryName;
            }

            if (methodName == "GetExtensionCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.PathGetExtension;
            }
        }

        if (declaringTypeName == "TcpClient" && isStatic)
        {
            if (methodName == "ConnectCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.String &&
                parameters[1].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpConnect;
            }

            if (methodName == "ReadLineCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpReadLine;
            }

            if (methodName == "WriteLineCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.Integer &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.TcpWriteLine;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.TcpClose;
            }
        }

        if (declaringTypeName == "HttpClient" && isStatic)
        {
            if (methodName == "GetStringCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.HttpGetString;
            }
        }

        if (declaringTypeName == "WebSocketClient" && isStatic)
        {
            if (methodName == "ConnectCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.String)
            {
                return HostImportKind.WebSocketConnect;
            }

            if (methodName == "ReceiveTextCore" &&
                returnType == TypeSymbol.String &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.WebSocketReceiveText;
            }

            if (methodName == "SendTextCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 2 &&
                parameters[0].Type == TypeSymbol.Integer &&
                parameters[1].Type == TypeSymbol.String)
            {
                return HostImportKind.WebSocketSendText;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.WebSocketClose;
            }
        }

        if (declaringTypeName == "Thread" && isStatic)
        {
            if (methodName == "SleepCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadSleep;
            }

            if (methodName == "GetCurrentManagedIdCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 0)
            {
                return HostImportKind.ThreadGetCurrentManagedId;
            }

            if (methodName == "StartCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 1 &&
                parameters[0].Type.Name == "IRunnable")
            {
                return HostImportKind.ThreadStartRunnable;
            }

            if (methodName == "JoinCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadJoin;
            }

            if (methodName == "IsAliveCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.ThreadIsAlive;
            }
        }

        if (declaringTypeName == "Mutex" && isStatic)
        {
            if (methodName == "CreateCore" &&
                returnType == TypeSymbol.Integer &&
                parameters.Count == 0)
            {
                return HostImportKind.MutexCreate;
            }

            if (methodName == "WaitOneCore" &&
                returnType == TypeSymbol.Boolean &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexWaitOne;
            }

            if (methodName == "ReleaseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexRelease;
            }

            if (methodName == "CloseCore" &&
                returnType == TypeSymbol.Void &&
                parameters.Count == 1 &&
                parameters[0].Type == TypeSymbol.Integer)
            {
                return HostImportKind.MutexClose;
            }
        }

        return HostImportKind.None;
    }

    private static PropertySymbol BindProperty(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName, IReadOnlyList<FieldSymbol> fields, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var declaringType = knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(type => type.Name == declaringTypeName);
        var synthesizeDeclarationOnlyAccessorMethods = declaringType?.IsInterface == true;
        var isStatic = propertyDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);
        var isGetterPrivate = propertyDeclaration.GetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.GetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isSetterPrivate = propertyDeclaration.SetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.SetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isInitOnly = propertyDeclaration.InitKeyword is not null;
        var autoPropertyField = propertyDeclaration.OpenBraceToken is not null
            ? fields.FirstOrDefault(field => field.Name == $"__auto_{propertyDeclaration.Identifier.Text}" && field.DeclaringTypeName == declaringTypeName)
            : null;
        var readField = propertyDeclaration.BeginKeyword is not null
            ? null
            : propertyDeclaration.OpenBraceToken is not null
            ? autoPropertyField
            : BindPropertyFieldReference(propertyDeclaration.ReadTarget!, declaringTypeName, fields);
        var writeField = propertyDeclaration.BeginKeyword is not null
            ? null
            : propertyDeclaration.OpenBraceToken is not null
            ? (propertyDeclaration.SetKeyword is null && propertyDeclaration.InitKeyword is null ? null : autoPropertyField)
            : propertyDeclaration.WriteTarget is null
                ? null
                : BindPropertyFieldReference(propertyDeclaration.WriteTarget, declaringTypeName, fields);
        var getterMethod = propertyDeclaration.GetKeyword is null && propertyDeclaration.GetterBody is null
            ? null
            : propertyDeclaration.GetterBody is not null || synthesizeDeclarationOnlyAccessorMethods
                ? BindMethod(CreateGetterAccessorDeclaration(propertyDeclaration), declaringTypeName, knownTypes)
                : null;
        var setterMethod = propertyDeclaration.SetKeyword is null && propertyDeclaration.InitKeyword is null && propertyDeclaration.SetterBody is null
            ? null
            : propertyDeclaration.SetterBody is not null || synthesizeDeclarationOnlyAccessorMethods
                ? BindMethod(CreateSetterAccessorDeclaration(propertyDeclaration), declaringTypeName, knownTypes)
                : null;
        var indexParameter = propertyDeclaration.IndexParameter is null
            ? null
            : new ParameterSymbol(
                propertyDeclaration.IndexParameter.Identifier.Text,
                BindType(propertyDeclaration.IndexParameter.TypeName, knownTypes),
                BindParameterPassingKind(propertyDeclaration.IndexParameter.ModifierKeyword));

        return new PropertySymbol(
            propertyDeclaration.Identifier.Text,
            BindType(propertyDeclaration.TypeName, knownTypes),
            readField,
            writeField,
            indexParameter,
            getterMethod,
            setterMethod,
            isGetterPrivate,
            isSetterPrivate,
            isInitOnly,
            declaringTypeName,
            isStatic,
            propertyDeclaration.IndexParameter is not null,
            propertyDeclaration);
    }

    private static ParameterPassingKind BindParameterPassingKind(SyntaxToken? modifierKeyword) =>
        modifierKeyword?.Kind switch
        {
            SyntaxKind.OutKeyword => ParameterPassingKind.Out,
            SyntaxKind.RefKeyword => ParameterPassingKind.Ref,
            SyntaxKind.InKeyword => ParameterPassingKind.In,
            SyntaxKind.ParamsKeyword => ParameterPassingKind.Params,
            _ => ParameterPassingKind.Value
        };

    private static FieldSymbol BindAutoPropertyBackingField(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName, IReadOnlyList<TypeSymbol> knownTypes) =>
        new(
            $"__auto_{propertyDeclaration.Identifier.Text}",
            BindType(propertyDeclaration.TypeName, knownTypes),
            declaringTypeName,
            propertyDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
            null);

    private static MethodDeclarationSyntax CreateGetterAccessorDeclaration(PropertyDeclarationSyntax propertyDeclaration) =>
        new(
            [],
            propertyDeclaration.Modifiers,
            new SyntaxToken(SyntaxKind.FunctionKeyword, "function", null, propertyDeclaration.GetterKeyword?.Span ?? propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.IdentifierToken, $"get_{propertyDeclaration.Identifier.Text}", null, propertyDeclaration.Identifier.Span),
            propertyDeclaration.IndexParameter is null ? null : new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, propertyDeclaration.IndexParameter.Identifier.Span),
            propertyDeclaration.IndexParameter is null ? [] : [propertyDeclaration.IndexParameter],
            propertyDeclaration.IndexParameter is null ? null : new SyntaxToken(SyntaxKind.CloseParenToken, ")", null, propertyDeclaration.IndexParameter.Identifier.Span),
            propertyDeclaration.ColonToken,
            propertyDeclaration.TypeName,
            null,
            null,
            propertyDeclaration.GetterBody,
            propertyDeclaration.GetterBody?.SemicolonToken ?? propertyDeclaration.SemicolonToken);

    private static MethodDeclarationSyntax CreateSetterAccessorDeclaration(PropertyDeclarationSyntax propertyDeclaration)
    {
        var setterParameter = propertyDeclaration.SetterParameter
            ?? new SyntaxToken(SyntaxKind.IdentifierToken, "value", null, propertyDeclaration.Identifier.Span);
        var parameters = new List<ParameterSyntax>();
        if (propertyDeclaration.IndexParameter is not null)
        {
            parameters.Add(propertyDeclaration.IndexParameter);
        }

        parameters.Add(new ParameterSyntax(
            null,
            setterParameter,
            new SyntaxToken(SyntaxKind.ColonToken, ":", null, setterParameter.Span),
            propertyDeclaration.TypeName));

        return new MethodDeclarationSyntax(
            [],
            propertyDeclaration.Modifiers,
            new SyntaxToken(SyntaxKind.MethodKeyword, "method", null, propertyDeclaration.SetterKeyword?.Span ?? propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.IdentifierToken, $"set_{propertyDeclaration.Identifier.Text}", null, propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, setterParameter.Span),
            parameters,
            new SyntaxToken(SyntaxKind.CloseParenToken, ")", null, setterParameter.Span),
            null,
            null,
            null,
            null,
            propertyDeclaration.SetterBody,
            propertyDeclaration.SetterBody?.SemicolonToken ?? propertyDeclaration.SemicolonToken);
    }

    private static FieldSymbol BindPropertyFieldReference(QualifiedNameSyntax target, string declaringTypeName, IReadOnlyList<FieldSymbol> fields)
    {
        var fieldName = target.Parts[^1].Text;
        var resolvedField = fields.FirstOrDefault(field =>
            field.DeclaringTypeName == declaringTypeName &&
            field.Name == fieldName);

        return resolvedField ?? new FieldSymbol(fieldName, TypeSymbol.Integer, declaringTypeName, false, null);
    }
}

public static class SemanticFacts
{
    private static NamedTypeSymbol? ResolveNamedType(TypeSymbol type, IEnumerable<TypeSymbol> knownTypes) =>
        knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(candidate => candidate.Name == type.Name) ??
        (ResolveTypeReference(type.Name, knownTypes) as NamedTypeSymbol) ??
        (type as NamedTypeSymbol);

    private static IEnumerable<NamedTypeSymbol> GetTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var current = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (current is not null && visited.Add(current.Name))
        {
            yield return current;
            current = ResolveNamedType(current.BaseType ?? TypeSymbol.Object, knownTypes);
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetInterfaceHierarchy(TypeSymbol interfaceType, IEnumerable<TypeSymbol> knownTypes)
    {
        var pending = new Queue<NamedTypeSymbol>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (ResolveNamedType(interfaceType, knownTypes) is { IsInterface: true } rootInterface)
        {
            pending.Enqueue(rootInterface);
        }

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current.Name))
            {
                continue;
            }

            yield return current;
            foreach (var inheritedInterface in current.InterfaceTypes)
            {
                if (ResolveNamedType(inheritedInterface, knownTypes) is { IsInterface: true } nextInterface)
                {
                    pending.Enqueue(nextInterface);
                }
            }
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetReceiverTypeHierarchy(TypeSymbol? type, IEnumerable<TypeSymbol> knownTypes)
    {
        var resolvedType = ResolveNamedType(type ?? TypeSymbol.Object, knownTypes);
        if (resolvedType is null)
        {
            yield break;
        }

        if (resolvedType.IsInterface)
        {
            foreach (var interfaceType in GetInterfaceHierarchy(resolvedType, knownTypes))
            {
                yield return interfaceType;
            }

            yield break;
        }

        foreach (var candidate in GetTypeHierarchy(resolvedType, knownTypes))
        {
            yield return candidate;
        }
    }

    public static TypeSymbol InferExpressionType(
        ExpressionSyntax? expression,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol>? knownFields,
        IEnumerable<ConstantSymbol>? knownConstants,
        IEnumerable<PropertySymbol>? knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (expression is null)
        {
            return TypeSymbol.Integer;
        }

        return expression switch
        {
            LiteralExpressionSyntax literal => InferLiteralType(literal),
            SetLiteralExpressionSyntax setLiteral => InferSetLiteralType(setLiteral, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            RangeExpressionSyntax range => InferExpressionType(range.Start, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            ParenthesizedExpressionSyntax parenthesized => InferExpressionType(parenthesized.Expression, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MatchAndPatternSyntax andPattern => andPattern.Patterns.Count > 0
                ? InferExpressionType(andPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes)
                : TypeSymbol.Integer,
            MatchNotPatternSyntax notPattern => InferExpressionType(notPattern.Pattern, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MatchOrPatternSyntax orPattern => orPattern.Patterns.Count > 0
                ? InferExpressionType(orPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes)
                : TypeSymbol.Integer,
            MatchRelationalPatternSyntax relational => InferExpressionType(relational.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            NewExpressionSyntax newExpression => ResolveTypeReferenceInGenericContext(newExpression.TypeName.ToDisplayString(), currentMethod, knownTypes ?? [])
                ?? new TypeSymbol(newExpression.TypeName.ToDisplayString(), true),
            NewArrayExpressionSyntax newArray => new TypeSymbol(
                $"{newArray.ElementTypeName.ToDisplayString()}{GetArrayTypeSuffix(newArray.LengthExpressions)}",
                true),
            ArrayLengthExpressionSyntax => TypeSymbol.Integer,
            ElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            PostfixElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes).Type
                ?? TypeSymbol.Integer,
            NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType) => localType,
            NameExpressionSyntax name => ResolveName(name.Name, localTypes, knownTypes ?? [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            AssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var assignmentType) => assignmentType,
            AssignmentExpressionSyntax => TypeSymbol.Integer,
            CompoundAssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var compoundAssignmentType) => compoundAssignmentType,
            CompoundAssignmentExpressionSyntax => TypeSymbol.Integer,
            UnaryExpressionSyntax unary => unary.OperatorToken.Kind == SyntaxKind.NotKeyword
                ? InferExpressionType(unary.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, knownTypes) == TypeSymbol.Boolean
                    ? TypeSymbol.Boolean
                    : TypeSymbol.Integer
                : TypeSymbol.Integer,
            BinaryExpressionSyntax binary => binary.OperatorToken.Kind is SyntaxKind.InKeyword or SyntaxKind.NotInKeyword
                ? TypeSymbol.Boolean
                : IsComparisonOperator(binary.OperatorToken.Kind)
                    ? TypeSymbol.Boolean
                    : InferBinaryExpressionType(binary, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            AsExpressionSyntax asExpression => ResolveTypeReferenceInGenericContext(asExpression.TypeName.ToDisplayString(), currentMethod, knownTypes ?? [])
                ?? new TypeSymbol(asExpression.TypeName.ToDisplayString(), true),
            TypeTestExpressionSyntax => TypeSymbol.Boolean,
            CallExpressionSyntax call => call.Target is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Receiver is NameExpressionSyntax receiverName &&
                knownMethods.FirstOrDefault(method =>
                    method.DeclaringTypeName == receiverName.Name.ToDisplayString() &&
                    method.Name == memberAccess.MemberName.Text &&
                    method.Parameters.Count == call.Arguments.Count &&
                    method.IsStatic) is { } staticMethod
                    ? staticMethod.ReturnType
                    : ResolveInvocation(call.Target, call.Arguments.Count, localTypes, knownTypes ?? [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod)?.Method.ReturnType ?? TypeSymbol.Integer,
            LambdaExpressionSyntax lambda => lambda.SignatureKeyword.Kind == SyntaxKind.FunctionKeyword && lambda.ReturnType is not null
                ? ResolveTypeReferenceInGenericContext(lambda.ReturnType.ToDisplayString(), currentMethod, knownTypes ?? [])
                    ?? new TypeSymbol(lambda.ReturnType.ToDisplayString(), true)
                : TypeSymbol.Void,
            MatchExpressionSyntax matchExpression => matchExpression.Arms.Count > 0
                ? InferExpressionType(
                    matchExpression.Arms[0].Expression,
                    matchExpression.Arms[0].TypeName is not null &&
                    matchExpression.Arms[0].Identifier is not null &&
                    ResolveTypeReference(matchExpression.Arms[0].TypeName!.ToDisplayString(), knownTypes ?? []) is { } matchArmType
                        ? new Dictionary<string, TypeSymbol>(localTypes, StringComparer.Ordinal)
                        {
                            [matchExpression.Arms[0].Identifier!.Text] = matchArmType
                        }
                        : localTypes,
                    knownMethods,
                    knownFields ?? [],
                    knownConstants ?? [],
                    knownProperties ?? [],
                    currentMethod,
                    knownTypes)
                : TypeSymbol.Integer,
            _ => TypeSymbol.Integer
        };
    }

    private static bool TryGetAssignmentTargetType(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        out TypeSymbol type)
    {
        switch (target)
        {
            case NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType):
                type = localType;
                return true;
            case NameExpressionSyntax name:
                type = ResolvePropertyReference(name.Name, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                    ?? ResolveConstantReference(name.Name, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                    ?? ResolveFieldReference(name.Name, knownFields, currentMethod)?.Type
                    ?? TypeSymbol.Integer;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                type = ResolveMemberAccess(memberAccess, localTypes, [], knownFields, knownConstants, knownProperties, currentMethod).Type
                    ?? TypeSymbol.Integer;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownFields, knownConstants, knownProperties, currentMethod);
                return true;
            case PostfixElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, [], knownFields, knownConstants, knownProperties, currentMethod);
                return true;
            default:
                type = TypeSymbol.Integer;
                return false;
        }
    }

    private static TypeSymbol GetIndexedElementType(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var arrayType = localTypes.TryGetValue(target.ToDisplayString(), out var localType)
            ? localType
            : ResolvePropertyReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Type
                ?? ResolveConstantReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Type
                ?? ResolveFieldReference(target, knownFields, currentMethod)?.Type
                ?? new TypeSymbol("Integer[]", true);

        var indexer = ResolveIndexerReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            return indexer.Type;
        }

        if (IsSliceAccess(indexExpressions))
        {
            return arrayType;
        }

        if (arrayType == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(arrayType))
        {
            return TypeSymbol.Integer;
        }

        var elementTypeName = GetArrayElementTypeName(arrayType.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

    private static TypeSymbol GetIndexedElementType(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target is MemberAccessExpressionSyntax memberAccess &&
            ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property is { IsIndexer: true } directIndexer)
        {
            return directIndexer.Type;
        }

        var targetType = InferExpressionType(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var indexer = ResolveIndexerReference(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (indexer is not null)
        {
            return indexer.Type;
        }

        if (IsSliceAccess(indexExpressions))
        {
            return targetType;
        }

        if (targetType == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(targetType))
        {
            return TypeSymbol.Integer;
        }

        var elementTypeName = GetArrayElementTypeName(targetType.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

    public static InvocationResolution? ResolveInvocation(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var directResolution = target switch
        {
            NameExpressionSyntax name => ResolveInvocation(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            _ => null
        };
        if (directResolution is not null)
        {
            return directResolution;
        }

        return TryResolveDelegateInvocation(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    public static InvocationResolution? ResolveInvocationIgnoringAccess(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var directResolution = target switch
        {
            NameExpressionSyntax name => ResolveInvocationIgnoringAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, ignoreAccess: true),
            _ => null
        };
        if (directResolution is not null)
        {
            return directResolution;
        }

        return TryResolveDelegateInvocation(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
    }

    private static InvocationResolution? TryResolveDelegateInvocation(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var targetType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (ResolveNamedType(targetType, knownTypes) is not NamedTypeSymbol { IsDelegate: true } delegateType)
        {
            return null;
        }

        var invokeMethod = delegateType.Methods.FirstOrDefault(method =>
            method.Name == "Invoke" &&
            !method.IsStatic &&
            SupportsArgumentCount(method, argumentCount));
        return invokeMethod is null ? null : new InvocationResolution(invokeMethod, delegateType, true);
    }

    public static MemberResolution ResolveMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var displayName = $"{GetExpressionDisplayName(memberAccess.Receiver)}.{memberAccess.MemberName.Text}";
        if (memberAccess.MemberName.Text == "Length" && HasLengthProperty(receiverType))
        {
            return new MemberResolution(displayName, TypeSymbol.Integer);
        }

        var typeHierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes ?? []);
        var property = typeHierarchy
            .SelectMany(knownType => knownType.Properties
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownProperties.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (property is not null)
        {
            return new MemberResolution(displayName, property.Type, property.ReadField, property, null);
        }

        var field = typeHierarchy
            .SelectMany(knownType => knownType.Fields
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownFields.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (field is not null)
        {
            return new MemberResolution(displayName, field.Type, field);
        }

        var constant = typeHierarchy
            .SelectMany(knownType => knownConstants.Where(candidate =>
                candidate.IsStatic &&
                candidate.DeclaringTypeName == knownType.Name &&
                candidate.Name == memberAccess.MemberName.Text))
            .FirstOrDefault();
        if (constant is not null)
        {
            return new MemberResolution(displayName, constant.Type, null, null, null, constant);
        }

        var method = typeHierarchy
            .SelectMany(knownType => knownType.Methods
                .Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.Name == memberAccess.MemberName.Text)
                .Concat(knownMethods.Where(candidate =>
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text)))
            .FirstOrDefault();
        if (method is not null)
        {
            return new MemberResolution(displayName, method.ReturnType, Method: method);
        }

        return new MemberResolution(displayName);
    }

    public static string GetExpressionDisplayName(ExpressionSyntax expression) =>
        expression switch
        {
            NameExpressionSyntax name => name.Name.ToDisplayString(),
            ParenthesizedExpressionSyntax parenthesized => $"({GetExpressionDisplayName(parenthesized.Expression)})",
            RangeExpressionSyntax range => $"{GetExpressionDisplayName(range.Start)}..{GetExpressionDisplayName(range.End)}",
            ElementAccessExpressionSyntax element => $"{element.Target.ToDisplayString()}[...]",
            PostfixElementAccessExpressionSyntax element => $"{GetExpressionDisplayName(element.Target)}[...]",
            ArrayLengthExpressionSyntax length => $"{length.Target.ToDisplayString()}.Length",
            MemberAccessExpressionSyntax member => $"{GetExpressionDisplayName(member.Receiver)}.{member.MemberName.Text}",
            AsExpressionSyntax asExpression => $"{GetExpressionDisplayName(asExpression.Expression)} as {asExpression.TypeName.ToDisplayString()}",
            TypeTestExpressionSyntax typeTest => $"{GetExpressionDisplayName(typeTest.Expression)} is {typeTest.TypeName.ToDisplayString()}",
            CallExpressionSyntax call => $"{GetExpressionDisplayName(call.Target)}(...)",
            MatchExpressionSyntax => "match",
            MatchNotPatternSyntax notPattern => $"not {GetExpressionDisplayName(notPattern.Pattern)}",
            MatchOrPatternSyntax orPattern => string.Join(" or ", orPattern.Patterns.Select(GetExpressionDisplayName)),
            MatchAndPatternSyntax andPattern => string.Join(" and ", andPattern.Patterns.Select(GetExpressionDisplayName)),
            MatchRelationalPatternSyntax relational => $"{relational.OperatorToken.Text}{GetExpressionDisplayName(relational.Operand)}",
            _ => expression.Kind.ToString()
        };

    private static InvocationResolution? ResolveMemberInvocation(
        MemberAccessExpressionSyntax memberAccess,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        bool ignoreAccess = false)
    {
        QualifiedNameSyntax? receiverTypeName = memberAccess.Receiver switch
        {
            NameExpressionSyntax receiverName => receiverName.Name,
            MemberAccessExpressionSyntax nestedReceiver => TryFlattenQualifiedTarget(nestedReceiver),
            _ => null
        };

        if (receiverTypeName is not null &&
            ResolveTypeReference(receiverTypeName.ToDisplayString(), knownTypes) is { } targetType)
        {
            if (TryResolveTypeIntrinsic(targetType, memberAccess.MemberName.Text, argumentCount) is { } typeIntrinsic)
            {
                return new InvocationResolution(typeIntrinsic);
            }

            var staticMethod = knownMethods.FirstOrDefault(candidate =>
                candidate.DeclaringTypeName == targetType.Name &&
                candidate.Name == memberAccess.MemberName.Text &&
                SupportsArgumentCount(candidate, argumentCount) &&
                candidate.IsStatic);
            if (staticMethod is not null)
            {
                return new InvocationResolution(staticMethod);
            }

            return null;
        }

        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (TryResolveIntrinsic(receiverType, memberAccess.MemberName.Text, argumentCount) is { } intrinsic)
        {
            return new InvocationResolution(intrinsic, receiverType, true);
        }

        var method = GetReceiverTypeHierarchy(receiverType, knownTypes)
            .SelectMany(knownType => knownType.Methods
                .Where(candidate =>
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    (!ignoreAccess || !candidate.IsStatic))
                .Concat(knownMethods.Where(candidate =>
                    candidate.DeclaringTypeName == knownType.Name &&
                    candidate.Name == memberAccess.MemberName.Text &&
                    SupportsArgumentCount(candidate, argumentCount) &&
                    (!ignoreAccess || !candidate.IsStatic))))
            .FirstOrDefault(candidate => !ignoreAccess ? !candidate.IsStatic : true);
        if (method is null)
        {
            return null;
        }

        return new InvocationResolution(
            method,
            receiverType,
            method.IsVirtual ||
            method.IsOverride ||
            ResolveTypeReference(receiverType.Name, knownTypes) is NamedTypeSymbol { IsInterface: true });
    }

    private static MethodSymbol? TryResolveIntrinsic(TypeSymbol receiverType, string name, int argumentCount)
    {
        if (receiverType == TypeSymbol.String)
        {
            return TryResolveStringIntrinsic(name, argumentCount);
        }

        if (receiverType == TypeSymbol.Integer && argumentCount == 0 && name == "ToString")
        {
            return new MethodSymbol("ToString", TypeSymbol.String, [], TypeSymbol.Integer.Name, false, null, false, true);
        }

        return null;
    }

    private static MethodSymbol? TryResolveTypeIntrinsic(TypeSymbol targetType, string name, int argumentCount)
    {
        if (targetType == TypeSymbol.Integer && name == "Parse" && argumentCount == 1)
        {
            return new MethodSymbol(
                "Parse",
                TypeSymbol.Integer,
                [new ParameterSymbol("value", TypeSymbol.String)],
                TypeSymbol.Integer.Name,
                true,
                null,
                false,
                true);
        }

        if (targetType == TypeSymbol.Integer && name == "TryParse" && argumentCount == 2)
        {
            return new MethodSymbol(
                "TryParse",
                TypeSymbol.Boolean,
                [new ParameterSymbol("value", TypeSymbol.String), new ParameterSymbol("result", TypeSymbol.Integer, ParameterPassingKind.Out)],
                TypeSymbol.Integer.Name,
                true,
                null,
                false,
                true);
        }

        return null;
    }

    private static MethodSymbol? TryResolveStringIntrinsic(string name, int argumentCount)
    {
        if (argumentCount == 0)
        {
            return name switch
            {
                "ToUpper" => new MethodSymbol("ToUpper", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "ToLower" => new MethodSymbol("ToLower", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "Trim" => new MethodSymbol("Trim", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "TrimStart" => new MethodSymbol("TrimStart", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                "TrimEnd" => new MethodSymbol("TrimEnd", TypeSymbol.String, [], TypeSymbol.String.Name, false, null, false, true),
                _ => null
            };
        }

        if (name == "Substring" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Substring",
                TypeSymbol.String,
                [new ParameterSymbol("start", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Replace" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Replace",
                TypeSymbol.String,
                [new ParameterSymbol("oldValue", TypeSymbol.String), new ParameterSymbol("newValue", TypeSymbol.String)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Insert" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Insert",
                TypeSymbol.String,
                [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("value", TypeSymbol.String)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (name == "Remove" && argumentCount == 2)
        {
            return new MethodSymbol(
                "Remove",
                TypeSymbol.String,
                [new ParameterSymbol("index", TypeSymbol.Integer), new ParameterSymbol("length", TypeSymbol.Integer)],
                TypeSymbol.String.Name,
                false,
                null,
                false,
                true);
        }

        if (argumentCount != 1)
        {
            return null;
        }

        return name switch
        {
            "StartsWith" => new MethodSymbol("StartsWith", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "EndsWith" => new MethodSymbol("EndsWith", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "Contains" => new MethodSymbol("Contains", TypeSymbol.Boolean, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "IndexOf" => new MethodSymbol("IndexOf", TypeSymbol.Integer, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            "LastIndexOf" => new MethodSymbol("LastIndexOf", TypeSymbol.Integer, [new ParameterSymbol("value", TypeSymbol.String)], TypeSymbol.String.Name, false, null, false, true),
            _ => null
        };
    }

    public static bool IsArrayType(TypeSymbol type)
    {
        var openBracketIndex = type.Name.LastIndexOf('[');
        var rankSpecifier = openBracketIndex > 0 && type.Name.EndsWith("]", StringComparison.Ordinal)
            ? type.Name[(openBracketIndex + 1)..^1]
            : null;
        return openBracketIndex > 0 &&
            type.Name.EndsWith("]", StringComparison.Ordinal) &&
            rankSpecifier is not null &&
            (rankSpecifier.Length == 0 || rankSpecifier.All(character => char.IsDigit(character) || character == ','));
    }

    public static bool IsIndexableType(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
    }

    public static bool IsSliceAccess(IReadOnlyList<ExpressionSyntax> indexExpressions) =>
        indexExpressions.Count == 1 && indexExpressions[0] is RangeExpressionSyntax;

    private static bool IsEnumerableInterface(NamedTypeSymbol type) =>
        type.IsInterface &&
        (type.Name == "IEnumerable" || type.Name.StartsWith("IEnumerable<", StringComparison.Ordinal));

    public static bool IsSetType(TypeSymbol type) =>
        type.Name.StartsWith("set of ", StringComparison.Ordinal);

    public static TypeSymbol? GetSetElementType(TypeSymbol type)
    {
        if (!IsSetType(type))
        {
            return null;
        }

        var elementTypeName = type.Name["set of ".Length..];
        return ResolveTypeReference(elementTypeName, []) ?? new TypeSymbol(elementTypeName, false);
    }

    public static TypeSymbol CreateSetType(TypeSymbol elementType) =>
        new($"set of {elementType.Name}", false);

    public static bool HasLengthProperty(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
    }

    public static bool IsEnumType(TypeSymbol type)
    {
        return !type.IsReferenceType &&
            type != TypeSymbol.Void &&
            type != TypeSymbol.Boolean &&
            type != TypeSymbol.Char &&
            !IsBuiltInIntegerType(type);
    }

    public static bool IsBuiltInType(TypeSymbol type) =>
        TypeSymbol.BuiltInTypes.Any(candidate => candidate == type);

    public static bool IsBuiltInIntegerType(TypeSymbol type) =>
        type == TypeSymbol.Integer ||
        type == TypeSymbol.UInt128 ||
        type == TypeSymbol.UInt256 ||
        type == TypeSymbol.UInt512 ||
        type == TypeSymbol.UInt1024 ||
        type == TypeSymbol.UInt2048;

    public static int? GetIntegerBitWidth(TypeSymbol type) =>
        type.Name switch
        {
            "Integer" => 32,
            "UInt128" => 128,
            "UInt256" => 256,
            "UInt512" => 512,
            "UInt1024" => 1024,
            "UInt2048" => 2048,
            _ => null
        };

    public static TypeSymbol? GetElementType(TypeSymbol type)
    {
        if (type == TypeSymbol.String)
        {
            return TypeSymbol.Char;
        }

        if (!IsArrayType(type))
        {
            return null;
        }

        var elementTypeName = GetArrayElementTypeName(type.Name);
        return TryResolveBuiltInType(elementTypeName) ?? new TypeSymbol(elementTypeName, true);
    }

    public static EnumerablePatternResolution? ResolveEnumerablePattern(TypeSymbol collectionType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        NamedTypeSymbol? TryBuildPattern(NamedTypeSymbol enumerableType)
        {
            return IsEnumerableInterface(enumerableType) ? enumerableType : null;
        }

        EnumerablePatternResolution? CreatePattern(NamedTypeSymbol enumerableType)
        {
            var getEnumeratorMethod = enumerableType.Methods.FirstOrDefault(method =>
                method.Name == "GetEnumerator" &&
                method.Parameters.Count == 0 &&
                !method.IsConstructor);
            if (getEnumeratorMethod is null)
            {
                return null;
            }

            var enumeratorType = ResolveNamedType(getEnumeratorMethod.ReturnType, knownTypes) ?? getEnumeratorMethod.ReturnType;
            var resolvedEnumeratorType = ResolveNamedType(enumeratorType, knownTypes);
            if (resolvedEnumeratorType is null)
            {
                return null;
            }

            var moveNextMethod = GetReceiverTypeHierarchy(resolvedEnumeratorType, knownTypes)
                .SelectMany(type => type.Methods)
                .FirstOrDefault(method =>
                    method.Name == "MoveNext" &&
                    method.Parameters.Count == 0 &&
                    method.ReturnType == TypeSymbol.Boolean);
            var currentProperty = GetReceiverTypeHierarchy(resolvedEnumeratorType, knownTypes)
                .SelectMany(type => type.Properties)
                .FirstOrDefault(property =>
                    property.Name == "Current" &&
                    property.GetterMethod is not null &&
                    property.IndexParameter is null);
            if (moveNextMethod is null || currentProperty?.GetterMethod is null)
            {
                return null;
            }

            return new EnumerablePatternResolution(
                currentProperty.Type,
                enumeratorType,
                getEnumeratorMethod,
                moveNextMethod,
                currentProperty.GetterMethod);
        }

        var resolvedCollectionType = ResolveNamedType(collectionType, knownTypes);
        if (resolvedCollectionType is null)
        {
            return null;
        }

        foreach (var candidate in GetReceiverTypeHierarchy(resolvedCollectionType, knownTypes))
        {
            if (TryBuildPattern(candidate) is { } directEnumerable &&
                CreatePattern(directEnumerable) is { } directPattern)
            {
                return directPattern;
            }

            foreach (var interfaceType in candidate.InterfaceTypes)
            {
                foreach (var inheritedInterface in GetInterfaceHierarchy(interfaceType, knownTypes))
                {
                    if (TryBuildPattern(inheritedInterface) is { } enumerableInterface &&
                        CreatePattern(enumerableInterface) is { } pattern)
                    {
                        return pattern;
                    }
                }
            }
        }

        return null;
    }

    public static int GetArrayRank(TypeSymbol type)
    {
        if (!IsArrayType(type))
        {
            return 0;
        }

        var dimensions = GetArrayDimensions(type);
        return dimensions.Count == 0 ? 1 : dimensions.Count;
    }

    public static IReadOnlyList<int?> GetArrayDimensions(TypeSymbol type)
    {
        if (!IsArrayType(type))
        {
            return [];
        }

        var openBracketIndex = type.Name.LastIndexOf('[');
        var content = type.Name[(openBracketIndex + 1)..^1];
        if (content.Length == 0)
        {
            return [null];
        }

        return content.Split(',', StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? (int?)value : null)
            .ToArray();
    }

    private static string GetArrayElementTypeName(string arrayTypeName)
    {
        var openBracketIndex = arrayTypeName.LastIndexOf('[');
        return openBracketIndex > 0 ? arrayTypeName[..openBracketIndex] : arrayTypeName;
    }

    private static string GetArrayTypeSuffix(IReadOnlyList<ExpressionSyntax> lengthExpressions)
    {
        var literalTexts = lengthExpressions.Select(GetArrayLengthLiteralText).ToArray();
        if (literalTexts.All(text => text is not null))
        {
            return $"[{string.Join(",", literalTexts!)}]";
        }

        return lengthExpressions.Count == 1
            ? "[]"
            : $"[{new string(',', lengthExpressions.Count - 1)}]";
    }

    private static string? GetArrayLengthLiteralText(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && literal.LiteralToken.Kind == SyntaxKind.NumberToken
            ? literal.LiteralToken.Text
            : null;
    }

    public static PropertySymbol? ResolveIndexerReference(
        QualifiedNameSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target.Parts.Count >= 2)
        {
            var staticDeclaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            var staticIndexer = knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                property.IsStatic &&
                property.DeclaringTypeName == staticDeclaringTypeName &&
                property.Name == target.Parts[^1].Text);
            if (staticIndexer is not null)
            {
                return staticIndexer;
            }
        }

        if (target.Parts.Count == 1 &&
            (locals.TryGetValue(target.Parts[0].Text, out var localTargetType) && IsIndexableType(localTargetType) ||
             currentMethod?.DeclaringTypeName is not null &&
             ResolveFieldReference(target, knownFields, currentMethod) is { Type: var fieldType } && IsIndexableType(fieldType) ||
             ResolvePropertyReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { Type: var propertyType } && IsIndexableType(propertyType) ||
             ResolveConstantReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is { Type: var constantType } && IsIndexableType(constantType)))
        {
            return null;
        }

        var valueType = target.Parts.Count == 1
            ? TryResolveValueReferenceType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : null;
        if (valueType is not null)
        {
            var valueHierarchyIndexer = GetReceiverTypeHierarchy(valueType, knownTypes ?? []).ToArray()
                .SelectMany(type => type.Properties.Where(property => property.IsIndexer && !property.IsStatic))
                .FirstOrDefault();
            if (valueHierarchyIndexer is not null)
            {
                return valueHierarchyIndexer;
            }

            var valueIndexer = knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                GetReceiverTypeHierarchy(valueType, knownTypes ?? [])
                    .Any(type => type.Name == property.DeclaringTypeName));
            if (valueIndexer is not null)
            {
                return valueIndexer;
            }
        }

        TypeSymbol? receiverType = null;
        if (target.Parts.Count == 1 && locals.TryGetValue(target.Parts[0].Text, out var localReceiverType))
        {
            receiverType = localReceiverType;
        }
        else if (target.Parts.Count >= 2 && target.Parts[0].Text != "self" && locals.TryGetValue(target.Parts[0].Text, out var qualifiedReceiverType))
        {
            receiverType = qualifiedReceiverType;
        }
        else if (target.Parts.Count == 1 && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            receiverType = new TypeSymbol(currentMethod.DeclaringTypeName, true);
        }
        else if (target.Parts.Count >= 1 && target.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            receiverType = new TypeSymbol(currentMethod.DeclaringTypeName, true);
        }

        if (receiverType is null)
        {
            return null;
        }

        var receiverHierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes ?? []).ToArray();
        var hierarchyIndexer = receiverHierarchy
            .SelectMany(type => type.Properties.Where(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text)))
            .FirstOrDefault();
        if (hierarchyIndexer is not null)
        {
            return hierarchyIndexer;
        }

        return knownProperties.FirstOrDefault(property =>
            property.IsIndexer &&
            !property.IsStatic &&
            receiverHierarchy.Any(type => type.Name == property.DeclaringTypeName) &&
            (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text));
    }

    public static PropertySymbol? ResolveIndexerReference(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (target is NameExpressionSyntax nameTarget)
        {
            var resolvedIndexer = ResolveIndexerReference(nameTarget.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            if (resolvedIndexer is not null)
            {
                return resolvedIndexer;
            }
        }

        var targetType = InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (targetType == TypeSymbol.String || IsArrayType(targetType) || IsSetType(targetType))
        {
            return null;
        }

        return target switch
        {
            NameExpressionSyntax => null,
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Property is { IsIndexer: true } property
                ? property
                : GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                    .SelectMany(type => type.Properties.Where(candidate => candidate.IsIndexer && !candidate.IsStatic))
                    .FirstOrDefault()
                    ?? knownProperties.FirstOrDefault(candidate =>
                        candidate.IsIndexer &&
                        !candidate.IsStatic &&
                        GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                            .Any(type => type.Name == candidate.DeclaringTypeName)),
            _ => GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                    .SelectMany(type => type.Properties.Where(property => property.IsIndexer && !property.IsStatic))
                    .FirstOrDefault()
                ?? knownProperties.FirstOrDefault(property =>
                    property.IsIndexer &&
                    !property.IsStatic &&
                    GetReceiverTypeHierarchy(targetType, knownTypes ?? [])
                        .Any(type => type.Name == property.DeclaringTypeName))
        };
    }

    public static MethodSymbol? ResolveConstructor(
        QualifiedNameSyntax typeName,
        int argumentCount,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var resolvedType = ResolveTypeReference(typeName.ToDisplayString(), knownTypes);
        if (resolvedType is null)
        {
            return null;
        }

        if (resolvedType is NamedTypeSymbol namedType)
        {
            return namedType.Methods.FirstOrDefault(method =>
                method.IsConstructor &&
                method.DeclaringTypeName == resolvedType.Name &&
                method.Parameters.Count == argumentCount);
        }

        return knownMethods.FirstOrDefault(method =>
            method.IsConstructor &&
            method.DeclaringTypeName == resolvedType.Name &&
            method.Parameters.Count == argumentCount);
    }

    public static InvocationResolution? ResolveInvocation(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            if (TryResolveIntrinsic(valueReceiverType, target.Parts[^1].Text, argumentCount) is { } intrinsic)
            {
                return new InvocationResolution(intrinsic, valueReceiverType, true);
            }

            var instanceMethod = GetTypeHierarchy(valueReceiverType, knownTypes)
                .SelectMany(knownType => knownMethods.Where(method =>
                    method.DeclaringTypeName == knownType.Name &&
                    method.Name == target.Parts[^1].Text &&
                    SupportsArgumentCount(method, argumentCount) &&
                    !method.IsStatic))
                .FirstOrDefault();
            if (instanceMethod is not null)
            {
                return new InvocationResolution(
                    instanceMethod,
                    valueReceiverType,
                    instanceMethod.IsVirtual ||
                    instanceMethod.IsOverride ||
                    ResolveTypeReference(valueReceiverType.Name, knownTypes) is NamedTypeSymbol { IsInterface: true });
            }
        }

        if (target.Parts.Count >= 2)
        {
            var declaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            if (ResolveTypeReference(declaringTypeName, knownTypes) is { } targetType &&
                TryResolveTypeIntrinsic(targetType, target.Parts[^1].Text, argumentCount) is { } typeIntrinsic)
            {
                return new InvocationResolution(typeIntrinsic);
            }
        }

        var staticMethod = ResolveMethod(target.ToDisplayString(), argumentCount, knownMethods, currentMethod);
        return staticMethod is null ? null : new InvocationResolution(staticMethod);
    }

    public static MethodSymbol? ResolveMethod(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod)
    {
        var candidates = ResolveMethodCandidates(name, argumentCount, knownMethods);
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            return candidates.FirstOrDefault(method => method.IsStatic);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName && (method.IsStatic || !currentMethod.IsStatic));
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }
        }

        return candidates.FirstOrDefault(method => method.IsStatic) ?? candidates.FirstOrDefault();
    }

    internal static MethodSymbol? ResolveMethodIgnoringAccess(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod)
    {
        var candidates = ResolveMethodCandidates(name, argumentCount, knownMethods);
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            return candidates.FirstOrDefault();
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName);
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }
        }

        return candidates.FirstOrDefault();
    }

    internal static InvocationResolution? ResolveInvocationIgnoringAccess(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var method = GetReceiverTypeHierarchy(valueReceiverType, knownTypes)
                .SelectMany(knownType => knownType.Methods
                    .Where(candidate =>
                        candidate.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(candidate, argumentCount))
                    .Concat(knownMethods.Where(candidate =>
                        candidate.DeclaringTypeName == knownType.Name &&
                        candidate.Name == target.Parts[^1].Text &&
                        SupportsArgumentCount(candidate, argumentCount))))
                .FirstOrDefault();
            if (method is not null)
            {
                return new InvocationResolution(method, valueReceiverType, !method.IsStatic);
            }
        }

        if (target.Parts.Count >= 2)
        {
            var declaringTypeName = string.Join(".", target.Parts.Take(target.Parts.Count - 1).Select(part => part.Text));
            if (ResolveTypeReference(declaringTypeName, knownTypes) is { } targetType &&
                TryResolveTypeIntrinsic(targetType, target.Parts[^1].Text, argumentCount) is { } typeIntrinsic)
            {
                return new InvocationResolution(typeIntrinsic);
            }
        }

        var staticMethod = ResolveMethodIgnoringAccess(target.ToDisplayString(), argumentCount, knownMethods, currentMethod);
        return staticMethod is null ? null : new InvocationResolution(staticMethod);
    }

    private static MethodSymbol[] ResolveMethodCandidates(
        string name,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        return knownMethods
            .Where(method =>
                method.Name == qualifiedTarget.MethodName &&
                SupportsArgumentCount(method, argumentCount) &&
                (qualifiedTarget.DeclaringTypeName is null || method.DeclaringTypeName == qualifiedTarget.DeclaringTypeName))
            .ToArray();
    }

    public static bool SupportsArgumentCount(MethodSymbol method, int argumentCount)
    {
        if (method.Parameters.Count > 0 &&
            method.Parameters[^1].PassingKind == ParameterPassingKind.Params)
        {
            return argumentCount >= method.Parameters.Count - 1;
        }

        return method.Parameters.Count == argumentCount;
    }

    public static NameResolution ResolveName(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var displayName = name.ToDisplayString();
        if (name.Parts.Count == 1 && locals.TryGetValue(displayName, out var localType))
        {
            return new NameResolution(NameResolutionKind.LocalOrGlobal, displayName, localType);
        }

        var receiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (receiverType is not null)
        {
            var hierarchy = GetReceiverTypeHierarchy(receiverType, knownTypes);
            var instanceField = hierarchy
                .SelectMany(receiver => knownFields.Where(field =>
                    field.DeclaringTypeName == receiver.Name &&
                    field.Name == name.Parts[^1].Text &&
                    !field.IsStatic))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceField.Type, null, instanceField);
            }

            var instanceProperty = hierarchy
                .SelectMany(receiver => knownProperties.Where(property =>
                    property.DeclaringTypeName == receiver.Name &&
                    property.Name == name.Parts[^1].Text &&
                    !property.IsStatic))
                .FirstOrDefault();
            if (instanceProperty is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceProperty.Type, null, instanceProperty.ReadField);
            }

            var instanceConstant = hierarchy
                .SelectMany(receiver => knownConstants.Where(constant =>
                    constant.IsStatic &&
                    constant.DeclaringTypeName == receiver.Name &&
                    constant.Name == name.Parts[^1].Text))
                .FirstOrDefault();
            if (instanceConstant is not null)
            {
                return new NameResolution(NameResolutionKind.Constant, displayName, instanceConstant.Type, null, null, instanceConstant);
            }

            var instanceMethodGroup = hierarchy
                .SelectMany(receiver => knownMethods.Where(method =>
                    method.DeclaringTypeName == receiver.Name &&
                    method.Name == name.Parts[^1].Text &&
                    !method.IsStatic))
                .FirstOrDefault();
            if (instanceMethodGroup is not null)
            {
                return new NameResolution(NameResolutionKind.MethodGroup, displayName, instanceMethodGroup.ReturnType, instanceMethodGroup);
            }
        }

        var propertyCandidate = ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (propertyCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Field, displayName, propertyCandidate.Type, null, propertyCandidate.ReadField);
        }

        var constantCandidate = ResolveConstantReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (constantCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Constant, displayName, constantCandidate.Type, null, null, constantCandidate);
        }

        var fieldCandidate = ResolveFieldReference(name, knownFields, currentMethod);
        if (fieldCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Field, displayName, fieldCandidate.Type, null, fieldCandidate);
        }

        var typeCandidate = ResolveTypeReference(displayName, knownTypes);
        if (typeCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Type, displayName, typeCandidate);
        }

        var methodCandidate = ResolveMethodGroup(displayName, knownMethods, currentMethod);
        if (methodCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.MethodGroup, displayName, methodCandidate.ReturnType, methodCandidate);
        }

        return new NameResolution(NameResolutionKind.Unknown, displayName);
    }

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
                    InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
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
        MethodSymbol? currentMethod)
    {
        var invocation = ResolveInvocation(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (invocation?.Method is not null)
        {
            return CreateBoundCall(call, invocation, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        }

        var qualifiedTarget = TryFlattenQualifiedTarget(call.Target);
        if (qualifiedTarget is null)
        {
            return null;
        }

        invocation = ResolveInvocation(qualifiedTarget, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (invocation?.Method is not null)
        {
            return CreateBoundCall(call, invocation, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
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
        MethodSymbol? currentMethod)
    {
        var isExplicitInvokeMemberAccess =
            call.Target is MemberAccessExpressionSyntax { MemberName.Text: "Invoke" };
        var isDirectDelegateInvoke =
            !isExplicitInvokeMemberAccess &&
            ResolveTypeReference(invocation.Method.DeclaringTypeName, knownTypes) is NamedTypeSymbol { IsDelegate: true } &&
            invocation.Method.Name == "Invoke" &&
            !invocation.Method.IsStatic;

        var receiver = invocation.Method.IsStatic
            ? null
            : isDirectDelegateInvoke
                ? BindReceiver(call.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
                : call.Target switch
            {
                MemberAccessExpressionSyntax memberAccess => BindReceiver(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
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

        var kind = invocation.Method.IsConstructor
            ? BoundCallKind.Constructor
            : invocation.Method.IsSynthetic && invocation.Method.HostImportKind != HostImportKind.None
                ? BoundCallKind.Intrinsic
                : invocation.IsVirtual
                    ? BoundCallKind.Virtual
                    : BoundCallKind.Direct;

        var argumentTypes = call.Arguments
            .Select(argument => InferExpressionType(argument.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod))
            .ToArray();

        return new BoundCall(
            kind,
            GetExpressionDisplayName(call.Target),
            invocation.Method,
            invocation.Method.ReturnType,
            receiver,
            argumentTypes,
            call);
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

    private static TypeSymbol InferLiteralType(LiteralExpressionSyntax literal) =>
        literal.LiteralToken.Kind switch
        {
            SyntaxKind.TrueKeyword => TypeSymbol.Boolean,
            SyntaxKind.FalseKeyword => TypeSymbol.Boolean,
            SyntaxKind.StringToken => TypeSymbol.String,
            SyntaxKind.NilKeyword => TypeSymbol.Nil,
            _ => TypeSymbol.Integer
        };

    private static TypeSymbol InferSetLiteralType(
        SetLiteralExpressionSyntax setLiteral,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (setLiteral.Elements.Count == 0)
        {
            return new TypeSymbol("set", false);
        }

        var elementType = InferExpressionType(setLiteral.Elements[0], localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        return IsEnumType(elementType)
            ? CreateSetType(elementType)
            : TypeSymbol.Integer;
    }

    private static TypeSymbol InferBinaryExpressionType(
        BinaryExpressionSyntax binary,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var leftType = InferExpressionType(binary.Left, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var rightType = InferExpressionType(binary.Right, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (binary.OperatorToken.Kind == SyntaxKind.PlusToken &&
            leftType == TypeSymbol.String &&
            rightType == TypeSymbol.String)
        {
            return TypeSymbol.String;
        }

        if (binary.OperatorToken.Kind == SyntaxKind.NullCoalescingToken)
        {
            if (leftType.IsReferenceType && leftType != TypeSymbol.Nil)
            {
                return leftType;
            }

            if (rightType.IsReferenceType && rightType != TypeSymbol.Nil)
            {
                return rightType;
            }

            return leftType;
        }

        if (binary.OperatorToken.Kind is SyntaxKind.ShlKeyword or SyntaxKind.ShrKeyword)
        {
            return IsBuiltInIntegerType(leftType) ? leftType : TypeSymbol.Integer;
        }

        if (IsSetType(leftType) && IsSetType(rightType))
        {
            return leftType;
        }

        return TypeSymbol.Integer;
    }

    private static bool IsComparisonOperator(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsToken
            or SyntaxKind.NotEqualsToken
            or SyntaxKind.LessToken
            or SyntaxKind.LessOrEqualsToken
            or SyntaxKind.GreaterToken
            or SyntaxKind.GreaterOrEqualsToken;

    private static (string? DeclaringTypeName, string MethodName) ParseQualifiedMethodTarget(string name)
    {
        var lastSeparator = name.LastIndexOf('.');
        if (lastSeparator < 0)
        {
            return (null, name);
        }

        var methodName = name[(lastSeparator + 1)..];
        var qualifier = name[..lastSeparator];
        var typeNameSeparator = qualifier.LastIndexOf('.');
        var declaringTypeName = typeNameSeparator >= 0
            ? qualifier[(typeNameSeparator + 1)..]
            : qualifier;

        return (declaringTypeName, methodName);
    }

    public static TypeSymbol? ResolveTypeReference(string displayName, IEnumerable<TypeSymbol> knownTypes)
    {
        if (displayName.StartsWith("set of ", StringComparison.Ordinal))
        {
            var elementType = ResolveTypeReference(displayName["set of ".Length..], knownTypes);
            return elementType is not null ? CreateSetType(elementType) : null;
        }

        if (TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
        {
            var resolvedArguments = genericArgumentNames
                .Select(argumentName => ResolveTypeReference(argumentName, knownTypes))
                .ToArray();
            if (resolvedArguments.Any(argument => argument is null))
            {
                return null;
            }

            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(type => type.Name == simpleTypeName && type.GenericArity == resolvedArguments.Length)
                .OrderByDescending(GetGenericDefinitionRichness)
                .FirstOrDefault();
            if (definition is null)
            {
                return null;
            }

            return ConstructClosedGenericType(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypes);
        }

        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return TryResolveBuiltInType(typeName)
            ?? knownTypes.FirstOrDefault(type => type.Name == typeName);
    }

    public static TypeSymbol? ResolveTypeReferenceInGenericContext(
        string displayName,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        var resolvedType = ResolveTypeReference(displayName, knownTypes);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        if (currentMethod?.DeclaringTypeName is null)
        {
            return null;
        }

        if (ResolveTypeReference(currentMethod.DeclaringTypeName, knownTypes) is not NamedTypeSymbol currentDeclaringType ||
            currentDeclaringType.GenericDefinition?.GenericParameters is not { Count: > 0 } genericParameters ||
            currentDeclaringType.TypeArguments is not { Count: > 0 } typeArguments)
        {
            return null;
        }

        var substitution = genericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument, StringComparer.Ordinal);

        return ResolveTypeReferenceWithSubstitution(displayName, substitution, knownTypes);
    }

    public static TypeSymbol? TryResolveBuiltInType(string displayName)
    {
        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return TypeSymbol.BuiltInTypes.FirstOrDefault(type => type.Name == typeName);
    }

    public static bool IsCompatibleReferenceType(TypeSymbol sourceType, TypeSymbol targetType, IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        if (sourceType == TypeSymbol.Nil)
        {
            return targetType.IsReferenceType;
        }

        if (!targetType.IsReferenceType || !sourceType.IsReferenceType)
        {
            return false;
        }

        if (targetType.Name == TypeSymbol.Object.Name || sourceType.Name == targetType.Name)
        {
            return true;
        }

        var resolvedTypes = knownTypes ?? [];
        var resolvedSourceType = ResolveNamedType(sourceType, resolvedTypes);
        var resolvedTargetType = ResolveNamedType(targetType, resolvedTypes);
        if (resolvedSourceType is null || resolvedTargetType is null)
        {
            return false;
        }

        if (resolvedTargetType.IsInterface)
        {
            if (resolvedSourceType.IsInterface)
            {
                return GetInterfaceHierarchy(resolvedSourceType, resolvedTypes).Any(candidate => candidate.Name == resolvedTargetType.Name);
            }

            foreach (var candidateType in GetTypeHierarchy(resolvedSourceType, resolvedTypes))
            {
                if (candidateType.Name == resolvedTargetType.Name)
                {
                    return true;
                }

                foreach (var implementedInterface in candidateType.InterfaceTypes)
                {
                    if (GetInterfaceHierarchy(implementedInterface, resolvedTypes).Any(candidate => candidate.Name == resolvedTargetType.Name))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return GetTypeHierarchy(resolvedSourceType, resolvedTypes).Any(candidate => candidate.Name == resolvedTargetType.Name);
    }

    public static bool IsOpenGenericDefinition(TypeSymbol type) =>
        type is NamedTypeSymbol namedType &&
        namedType.GenericArity > 0 &&
        namedType.GenericDefinition is null &&
        (namedType.TypeArguments is null || namedType.TypeArguments.Count == 0);

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

    private static TypeSymbol? ResolveTypeReferenceWithSubstitution(
        string displayName,
        IReadOnlyDictionary<string, TypeSymbol> substitution,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (substitution.TryGetValue(displayName, out var exactReplacement))
        {
            return exactReplacement;
        }

        if (displayName.StartsWith("set of ", StringComparison.Ordinal))
        {
            var elementType = ResolveTypeReferenceWithSubstitution(displayName["set of ".Length..], substitution, knownTypes);
            return elementType is not null ? CreateSetType(elementType) : null;
        }

        if (displayName.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = ResolveTypeReferenceWithSubstitution(displayName[..^2], substitution, knownTypes);
            return elementType is not null ? new TypeSymbol($"{elementType.Name}[]", true) : null;
        }

        if (TryParseConstructedTypeReference(displayName, out var genericTypeName, out var genericArgumentNames))
        {
            var resolvedArguments = genericArgumentNames
                .Select(argumentName => ResolveTypeReferenceWithSubstitution(argumentName, substitution, knownTypes))
                .ToArray();
            if (resolvedArguments.Any(argument => argument is null))
            {
                return null;
            }

            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = knownTypes
                .OfType<NamedTypeSymbol>()
                .FirstOrDefault(type => type.Name == simpleTypeName && type.GenericArity == resolvedArguments.Length);
            if (definition is null)
            {
                return null;
            }

            return ConstructClosedGenericType(definition, resolvedArguments!.Cast<TypeSymbol>().ToArray(), knownTypes);
        }

        return ResolveTypeReference(displayName, knownTypes);
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

    private static NamedTypeSymbol ConstructClosedGenericType(
        NamedTypeSymbol definition,
        IReadOnlyList<TypeSymbol> typeArguments,
        IEnumerable<TypeSymbol> knownTypes)
    {
        definition = knownTypes
            .OfType<NamedTypeSymbol>()
            .Where(candidate => candidate.Name == definition.Name && candidate.GenericArity == definition.GenericArity)
            .OrderByDescending(GetGenericDefinitionRichness)
            .FirstOrDefault() ?? definition;

        if (definition.GenericParameters is null || definition.GenericParameters.Count != typeArguments.Count)
        {
            return definition;
        }

        var substitution = definition.GenericParameters
            .Zip(typeArguments, (parameter, argument) => (parameter.Name, argument))
            .ToDictionary(entry => entry.Name, entry => entry.argument);

        var closedName = $"{definition.Name}<{string.Join(", ", typeArguments.Select(argument => argument.Name))}>";
        var fields = definition.Fields
            .Select(field => field with
            {
                Type = SubstituteGenericType(field.Type, substitution, knownTypes),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var methods = definition.Methods
            .Select(method => method with
            {
                ReturnType = SubstituteGenericType(method.ReturnType, substitution, knownTypes),
                Parameters = method.Parameters
                    .Select(parameter => parameter with
                    {
                        Type = SubstituteGenericType(parameter.Type, substitution, knownTypes)
                    })
                    .ToArray(),
                DeclaringTypeName = closedName
            })
            .ToArray();
        var properties = definition.Properties
            .Select(property => property with
            {
                Type = SubstituteGenericType(property.Type, substitution, knownTypes),
                IndexParameter = property.IndexParameter is null
                    ? null
                    : property.IndexParameter with
                    {
                        Type = SubstituteGenericType(property.IndexParameter.Type, substitution, knownTypes)
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
            definition.BaseType is null ? null : SubstituteGenericType(definition.BaseType, substitution, knownTypes),
            definition.InterfaceTypes.Select(type => SubstituteGenericType(type, substitution, knownTypes)).ToArray(),
            methods,
            fields,
            definition.Constants,
            properties,
            definition.GenericArity,
            null,
            definition,
            typeArguments);
    }

    private static TypeSymbol SubstituteGenericType(
        TypeSymbol type,
        IReadOnlyDictionary<string, TypeSymbol> substitution,
        IEnumerable<TypeSymbol> knownTypes)
    {
        if (substitution.TryGetValue(type.Name, out var replacement))
        {
            return replacement;
        }

        if (type.Name.StartsWith("set of ", StringComparison.Ordinal))
        {
            var elementType = SubstituteGenericType(
                ResolveTypeReference(type.Name["set of ".Length..], knownTypes) ?? new TypeSymbol(type.Name["set of ".Length..], true),
                substitution,
                knownTypes);
            return CreateSetType(elementType);
        }

        if (type.Name.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementTypeName = type.Name[..^2];
            var elementType = SubstituteGenericType(
                ResolveTypeReference(elementTypeName, knownTypes) ?? new TypeSymbol(elementTypeName, true),
                substitution,
                knownTypes);
            return new TypeSymbol($"{elementType.Name}[]", true);
        }

        if (type is NamedTypeSymbol namedType && namedType.GenericDefinition is not null && namedType.TypeArguments is not null)
        {
            var substitutedArguments = namedType.TypeArguments
                .Select(argument => SubstituteGenericType(argument, substitution, knownTypes))
                .ToArray();
            return ConstructClosedGenericType(namedType.GenericDefinition, substitutedArguments, knownTypes);
        }

        if (TryParseConstructedTypeReference(type.Name, out var genericTypeName, out var genericArgumentNames))
        {
            var substitutedArguments = genericArgumentNames
                .Select(argumentName => SubstituteGenericType(
                    ResolveTypeReference(argumentName, knownTypes) ?? new TypeSymbol(argumentName, true),
                    substitution,
                    knownTypes))
                .ToArray();
            var simpleTypeName = genericTypeName.Contains('.')
                ? genericTypeName[(genericTypeName.LastIndexOf('.') + 1)..]
                : genericTypeName;
            var definition = knownTypes
                .OfType<NamedTypeSymbol>()
                .Where(candidate => candidate.Name == simpleTypeName && candidate.GenericArity == substitutedArguments.Length)
                .OrderByDescending(GetGenericDefinitionRichness)
                .FirstOrDefault();
            if (definition is not null)
            {
                return ConstructClosedGenericType(definition, substitutedArguments, knownTypes);
            }
        }

        return type;
    }

    private static int GetGenericDefinitionRichness(NamedTypeSymbol type) =>
        (type.GenericParameters?.Count ?? 0) * 100 +
        type.Methods.Count * 10 +
        type.Properties.Count * 10 +
        type.InterfaceTypes.Count +
        (type.BaseType is null ? 0 : 1);

    private static FieldSymbol? ResolveFieldReference(
        QualifiedNameSyntax name,
        IEnumerable<FieldSymbol> knownFields,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var fields = knownFields.ToArray();
        var locals = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        if (currentMethod?.DeclaringTypeName is not null)
        {
            foreach (var parameter in currentMethod.Parameters)
            {
                locals[parameter.Name] = parameter.Type;
            }

            if (!currentMethod.IsStatic)
            {
                locals["self"] = new TypeSymbol(currentMethod.DeclaringTypeName, true);
            }
        }

        var valueReceiverType = TryResolveValueReceiverType(name, locals, fields, [], [], currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceField = GetTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => fields.Where(field =>
                    !field.IsStatic &&
                    field.DeclaringTypeName == knownType.Name &&
                    field.Name == name.Parts[^1].Text))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return instanceField;
            }
        }

        var displayName = name.ToDisplayString();
        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            return fields.FirstOrDefault(field =>
                !field.IsStatic &&
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == name.Parts[1].Text);
        }

        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var fieldName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return fields.FirstOrDefault(field => field.IsStatic && field.DeclaringTypeName == declaringTypeName && field.Name == fieldName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeField = fields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName &&
                (field.IsStatic || !currentMethod.IsStatic));
            if (sameTypeField is not null)
            {
                return sameTypeField;
            }
        }

        return null;
    }

    internal static FieldSymbol? ResolveFieldIgnoringAccess(
        QualifiedNameSyntax name,
        IEnumerable<FieldSymbol> knownFields,
        MethodSymbol? currentMethod)
    {
        var displayName = name.ToDisplayString();
        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null)
        {
            return knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == name.Parts[1].Text);
        }

        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var fieldName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return knownFields.FirstOrDefault(field => field.DeclaringTypeName == declaringTypeName && field.Name == fieldName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            return knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName);
        }

        return null;
    }

    public static PropertySymbol? ResolvePropertyReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var properties = knownProperties.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, properties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            return GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Properties
                    .Where(property =>
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)
                    .Concat(properties.Where(property =>
                        property.DeclaringTypeName == knownType.Name &&
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)))
                .FirstOrDefault();
        }

        if (name.Parts.Count >= 2 && name.Parts[0].Text == "self" && currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic)
        {
            return properties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == name.Parts[^1].Text &&
                !property.IsStatic);
        }

        var displayName = name.ToDisplayString();
        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var propertyName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return properties.FirstOrDefault(property =>
                property.IsStatic &&
                property.DeclaringTypeName == declaringTypeName &&
                property.Name == propertyName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            return properties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == displayName &&
                (property.IsStatic || !currentMethod.IsStatic));
        }

        return null;
    }

    public static ConstantSymbol? ResolveConstantReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var constants = knownConstants.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceConstant = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => constants.Where(constant =>
                    constant.DeclaringTypeName == knownType.Name &&
                    constant.Name == name.Parts[^1].Text &&
                    constant.IsStatic))
                .FirstOrDefault();
            if (instanceConstant is not null)
            {
                return instanceConstant;
            }
        }

        var displayName = name.ToDisplayString();
        var lastSeparator = displayName.LastIndexOf('.');
        if (lastSeparator >= 0)
        {
            var constantName = displayName[(lastSeparator + 1)..];
            var qualifier = displayName[..lastSeparator];
            var typeNameSeparator = qualifier.LastIndexOf('.');
            var declaringTypeName = typeNameSeparator >= 0
                ? qualifier[(typeNameSeparator + 1)..]
                : qualifier;

            return constants.FirstOrDefault(constant =>
                constant.IsStatic &&
                constant.DeclaringTypeName == declaringTypeName &&
                constant.Name == constantName);
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeConstant = constants.FirstOrDefault(constant =>
                constant.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                constant.Name == displayName &&
                constant.IsStatic);
            if (sameTypeConstant is not null)
            {
                return sameTypeConstant;
            }
        }

        return constants.FirstOrDefault(constant =>
            constant.DeclaringTypeName is null &&
            constant.Name == displayName);
    }

    public static bool IsConstantExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null) =>
        expression is LiteralExpressionSyntax ||
        expression is NameExpressionSyntax name && ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is not null ||
        expression is MemberAccessExpressionSyntax member && ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Constant is not null;

    public static bool TryGetInt32LiteralValue(SyntaxToken token, out int value)
    {
        switch (token.Value)
        {
            case int parsedInt:
                value = parsedInt;
                return true;
            case NumericLiteralValue numericLiteral when numericLiteral.Int32Value is int literalInt:
                value = literalInt;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public static object? GetLiteralValue(SyntaxToken token) =>
        token.Kind switch
        {
            SyntaxKind.TrueKeyword => 1,
            SyntaxKind.FalseKeyword => 0,
            SyntaxKind.NumberToken when token.Value is NumericLiteralValue numericLiteral => numericLiteral.Int32Value,
            _ => token.Value
        };

    public static object? GetConstantValue(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null) =>
        expression switch
        {
            LiteralExpressionSyntax literal => GetLiteralValue(literal.LiteralToken) ?? 0,
            NameExpressionSyntax name => ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)?.Value,
            MemberAccessExpressionSyntax member => ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Constant?.Value,
            ParenthesizedExpressionSyntax parenthesized => GetConstantValue(parenthesized.Expression, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.NotKeyword => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is int operand
                ? InferExpressionType(unary.Operand, locals, [], knownFields, knownConstants, knownProperties, currentMethod) == TypeSymbol.Boolean
                    ? operand == 0 ? 1 : 0
                    : ~operand
                : null,
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.MinusToken => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod) is int negatedOperand
                ? -negatedOperand
                : null,
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.PlusToken => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod) is int positiveOperand
                ? positiveOperand
                : null,
            _ => null
        };

    public static TypeSymbol? TryResolveValueReceiverType(
        QualifiedNameSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var qualifier = GetQualifier(memberAccess);
        if (qualifier is null)
        {
            return currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic
                ? new TypeSymbol(currentMethod.DeclaringTypeName, true)
                : null;
        }

        return TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
    }

    public static TypeSymbol? TryResolveValueReferenceType(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol>? knownTypes = null)
    {
        var displayName = name.ToDisplayString();
        if (locals.TryGetValue(displayName, out var localType))
        {
            return localType;
        }

        if (name.Parts.Count == 1 &&
            currentMethod?.DeclaringTypeName is not null &&
            (displayName == "self" || !locals.ContainsKey(displayName)))
        {
            var sameTypeProperty = knownProperties.FirstOrDefault(property =>
                property.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                property.Name == displayName &&
                (property.IsStatic || !currentMethod.IsStatic));
            if (sameTypeProperty is not null)
            {
                return sameTypeProperty.Type;
            }

            var sameTypeField = knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                field.Name == displayName &&
                (field.IsStatic || !currentMethod.IsStatic));
            if (sameTypeField is not null)
            {
                return sameTypeField.Type;
            }

            var sameTypeConstant = knownConstants.FirstOrDefault(constant =>
                constant.DeclaringTypeName == currentMethod.DeclaringTypeName &&
                constant.Name == displayName &&
                constant.IsStatic);
            if (sameTypeConstant is not null)
            {
                return sameTypeConstant.Type;
            }

            if (displayName == "self" && !currentMethod.IsStatic)
            {
                return new TypeSymbol(currentMethod.DeclaringTypeName, true);
            }
        }

        if (name.Parts.Count < 2)
        {
            return null;
        }

        var qualifier = GetQualifier(name);
        if (qualifier is null)
        {
            return null;
        }

        var valueReceiverType = TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (valueReceiverType is not null)
        {
            var instanceProperty = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Properties
                    .Where(property =>
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)
                    .Concat(knownProperties.Where(property =>
                        property.DeclaringTypeName == knownType.Name &&
                        property.Name == name.Parts[^1].Text &&
                        !property.IsStatic)))
                .FirstOrDefault();
            if (instanceProperty is not null)
            {
                return instanceProperty.Type;
            }

            var instanceField = GetReceiverTypeHierarchy(valueReceiverType, knownTypes ?? [])
                .SelectMany(knownType => knownType.Fields
                    .Where(field =>
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)
                    .Concat(knownFields.Where(field =>
                        field.DeclaringTypeName == knownType.Name &&
                        field.Name == name.Parts[^1].Text &&
                        !field.IsStatic)))
                .FirstOrDefault();
            if (instanceField is not null)
            {
                return instanceField.Type;
            }
        }

        var qualifierTypeName = qualifier.ToDisplayString();
        var declaringTypeName = qualifierTypeName.Contains('.')
            ? qualifierTypeName[(qualifierTypeName.LastIndexOf('.') + 1)..]
            : qualifierTypeName;

        var staticProperty = knownProperties.FirstOrDefault(property =>
            property.IsStatic &&
            property.DeclaringTypeName == declaringTypeName &&
            property.Name == name.Parts[^1].Text);
        if (staticProperty is not null)
        {
            return staticProperty.Type;
        }

        var staticField = knownFields.FirstOrDefault(field =>
            field.IsStatic &&
            field.DeclaringTypeName == declaringTypeName &&
            field.Name == name.Parts[^1].Text);
        return staticField?.Type;
    }

    private static QualifiedNameSyntax? GetQualifier(QualifiedNameSyntax name) =>
        name.Parts.Count > 1
            ? new QualifiedNameSyntax(name.Parts.Take(name.Parts.Count - 1).ToArray())
            : null;

    private static MethodSymbol? ResolveMethodGroup(
        string name,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod)
    {
        var qualifiedTarget = ParseQualifiedMethodTarget(name);
        var candidates = knownMethods
            .Where(method =>
                method.Name == qualifiedTarget.MethodName &&
                (qualifiedTarget.DeclaringTypeName is null || method.DeclaringTypeName == qualifiedTarget.DeclaringTypeName))
            .ToArray();

        if (qualifiedTarget.DeclaringTypeName is not null)
        {
            return candidates.FirstOrDefault();
        }

        if (currentMethod?.DeclaringTypeName is not null)
        {
            var sameTypeCandidate = candidates.FirstOrDefault(method => method.DeclaringTypeName == currentMethod.DeclaringTypeName);
            if (sameTypeCandidate is not null)
            {
                return sameTypeCandidate;
            }
        }

        return candidates.FirstOrDefault();
    }
}

public sealed record BindingResult(
    CompilationUnitSymbol Compilation,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
