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
    public static readonly TypeSymbol String = new("String", true);
    public static readonly TypeSymbol Nil = new("Nil", true);
}

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
    FileExists,
    FileReadAllText,
    FileWriteAllText,
    FileAppendAllText,
    PathCombine,
    PathGetFileName,
    PathGetDirectoryName,
    PathGetExtension
}

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
    HostImportKind HostImportKind = HostImportKind.None) : Symbol(Name);

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
            .. Types.OfType<NamedTypeSymbol>().SelectMany(type => type.Methods)
        ];

    public IReadOnlyList<FieldSymbol> GetAllFields() =>
        [
            .. Types.OfType<NamedTypeSymbol>().SelectMany(type => type.Fields)
        ];

    public IReadOnlyList<PropertySymbol> GetAllProperties() =>
        [
            .. Types.OfType<NamedTypeSymbol>().SelectMany(type => type.Properties)
        ];

    public IReadOnlyList<ConstantSymbol> GetAllConstants() =>
        [
            .. Constants,
            .. Types.OfType<NamedTypeSymbol>().SelectMany(type => type.Constants)
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
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<FieldSymbol> Fields,
    IReadOnlyList<ConstantSymbol> Constants,
    IReadOnlyList<PropertySymbol> Properties) : TypeSymbol(Name, IsReferenceType);

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
                    declaredTypeShells.Add(new TypeSymbol(classDeclaration.Identifier.Text, true));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    declaredTypeShells.Add(new TypeSymbol(enumDeclaration.Identifier.Text, false));
                    break;
            }
        }

        var declaredTypes = new List<TypeSymbol>();
        foreach (var member in syntaxTree.Root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax classDeclaration:
                    declaredTypes.Add(BindClass(classDeclaration, declaredTypeShells));
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    declaredTypes.Add(ResolveDeclaredType(enumDeclaration.Identifier.Text, declaredTypeShells, false));
                    break;
            }
        }

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

        var allMethods = methods.Concat(knownMethods).ToArray();
        var allKnownConstants = knownConstants.Concat(topLevelConstants).ToArray();
        ValidateSemantics(syntaxTree.Root.Members, globals, topLevelConstants, declaredTypes, knownMethods, knownFields, allKnownConstants, knownProperties, diagnostics);
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
            [TypeSymbol.Boolean, TypeSymbol.Char, TypeSymbol.Integer, TypeSymbol.String, TypeSymbol.Nil, .. declaredTypes],
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
                parameter => BindType(parameter.TypeName, knownTypes),
                StringComparer.Ordinal);

            var boundMethod = FindMethod(knownMethods, classDeclaration.Identifier.Text, method.Identifier.Text, method.Parameters.Count);
            if (boundMethod is not null && !boundMethod.IsStatic)
            {
                locals["self"] = new TypeSymbol(classDeclaration.Identifier.Text, true);
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
            else if (boundMethod.HostImportKind == HostImportKind.None)
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
            knownTypes,
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
                            ValidateExpression(declarator.Initializer, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
                    var collectionType = SemanticFacts.InferExpressionType(foreachStatement.Collection, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    TypeSymbol? elementType = null;
                    if (collectionType == TypeSymbol.String)
                    {
                        elementType = TypeSymbol.Char;
                    }
                    else if (SemanticFacts.IsArrayType(collectionType))
                    {
                        elementType = SemanticFacts.GetElementType(collectionType);
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

                        ValidateStatements([clause.Body], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    }

                    ValidateStatements(caseStatement.ElseStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics);
                    break;
                case MatchStatementSyntax matchStatement:
                    ValidateExpression(matchStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var matchExpressionType = SemanticFacts.InferExpressionType(matchStatement.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    if (matchExpressionType != TypeSymbol.Integer && matchExpressionType != TypeSymbol.String && !SemanticFacts.IsEnumType(matchExpressionType))
                    {
                        diagnostics.Report(
                            "ILC2175",
                            $"Match expression '{SemanticFacts.GetExpressionDisplayName(matchStatement.Expression)}' must be Integer, String or Enum in the current bootstrap compiler.",
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
                            else if (!SemanticFacts.IsCompatibleReferenceType(matchExpressionType, armType))
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
                ValidateExpression(assignment.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
                if (binary.OperatorToken.Kind == SyntaxKind.InKeyword)
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
                var matchedExpressionType = SemanticFacts.InferExpressionType(matchExpression.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                if (matchedExpressionType != TypeSymbol.Integer && matchedExpressionType != TypeSymbol.String && !SemanticFacts.IsEnumType(matchedExpressionType))
                {
                    diagnostics.Report(
                        "ILC2175",
                        $"Match expression '{SemanticFacts.GetExpressionDisplayName(matchExpression.Expression)}' must be Integer, String or Enum in the current bootstrap compiler.",
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
                        else if (!SemanticFacts.IsCompatibleReferenceType(matchedExpressionType, typedArmType))
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

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                ValidateExpression(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                ValidateExpression(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
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
                var lengthTargetType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(arrayLength.Target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
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
                        ValidateExpression(argument.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
        }
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

        if (!SemanticFacts.IsConstantExpression(binary.Left, locals, knownFields, knownConstants, knownProperties, currentMethod))
        {
            diagnostics.Report(
                "ILC2156",
                "Left-hand side of 'in' must be a literal or constant enum value in the current bootstrap compiler.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary.Left, knownTypes));
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

        if (!SemanticFacts.IsConstantExpression(label, locals, knownFields, knownConstants, knownProperties, currentMethod))
        {
            diagnostics.Report(
                "ILC2147",
                "Case labels must be literal or constant values in the current bootstrap compiler.",
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
        if (TryReportInvalidFieldAccess(name, locals, knownFields, knownConstants, knownProperties, currentMethod, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
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

            ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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

            ValidateArrayAccess(postfixElementAccess.Target, postfixElementAccess.IndexExpressions, postfixElementAccess.OpenBracketToken.Span, locals, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is not NameExpressionSyntax nameTarget)
        {
            if (target is MemberAccessExpressionSyntax memberTarget)
            {
                ValidateExpression(memberTarget.Receiver, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberTarget, locals, [], knownFields, knownConstants, knownProperties, currentMethod);
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
        if (TryReportInvalidFieldAccess(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod);
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
            SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Property is { IsIndexer: true })
        {
            var memberIndexer = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Property!;
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

        var indexedType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
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
                $"Expression '{GetExpressionDisplayName(target)}' is not indexable in the current bootstrap compiler.",
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

    private static void ValidateArrayAccess(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TextSpan indexSpan,
        IReadOnlyDictionary<string, TypeSymbol> locals,
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

        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod);
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

        var indexedType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (!SemanticFacts.IsIndexableType(indexedType))
        {
            diagnostics.Report(
                "ILC2125",
                $"Expression '{target.ToDisplayString()}' is not indexable in the current bootstrap compiler.",
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
        DiagnosticBag diagnostics)
    {
        var receiverType = target.Parts.Count >= 2 && target.Parts[0].Text != "self"
            ? SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod)
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
                    method.Parameters.Count == argumentCount &&
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
            SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod) is not null)
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

        return typeName.ToDisplayString() switch
        {
            "Boolean" => TypeSymbol.Boolean,
            "Char" => TypeSymbol.Char,
            "String" => TypeSymbol.String,
            "Integer" => TypeSymbol.Integer,
            _ => new TypeSymbol(typeName.ToDisplayString(), true)
        };
    }

    private static TypeSymbol ResolveDeclaredType(string typeName, IEnumerable<TypeSymbol> knownTypes, bool isReferenceType) =>
        SemanticFacts.ResolveTypeReference(typeName, knownTypes) ?? new TypeSymbol(typeName, isReferenceType);

    private static NamedTypeSymbol BindClass(ClassDeclarationSyntax classDeclaration, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var constants = classDeclaration.Members
            .OfType<ConstantDeclarationSyntax>()
            .SelectMany(constant => BindConstants(constant, classDeclaration.Identifier.Text, knownTypes))
            .ToArray();
        var declaredFields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => BindFields(field, classDeclaration.Identifier.Text, knownTypes))
            .ToArray();
        var autoPropertyFields = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(property => property.OpenBraceToken is not null)
            .Select(property => BindAutoPropertyBackingField(property, classDeclaration.Identifier.Text, knownTypes))
            .ToArray();
        var fields = declaredFields
            .Concat(autoPropertyFields)
            .ToArray();
        var declaredMethods = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => BindMethod(method, classDeclaration.Identifier.Text, knownTypes))
            .ToArray();
        var properties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(property => BindProperty(property, classDeclaration.Identifier.Text, fields, knownTypes))
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
            methods,
            fields,
            constants,
            properties);
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
            var value = member.ValueToken?.Value is int explicitValue ? explicitValue : nextValue;
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
            LiteralExpressionSyntax literal => literal.LiteralToken.Value ?? 0,
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
            methodDeclaration.Identifier.Text,
            returnType,
            parameters,
            declaringTypeName,
            methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
            methodDeclaration,
            methodDeclaration.Keyword.Kind == SyntaxKind.ConstructorKeyword,
            false,
            null,
            methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.ExternKeyword),
            ResolveHostImportKind(
                methodDeclaration.Identifier.Text,
                returnType,
                parameters,
                declaringTypeName,
                methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
                methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.ExternKeyword)));
    }

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

        return HostImportKind.None;
    }

    private static PropertySymbol BindProperty(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName, IReadOnlyList<FieldSymbol> fields, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var isStatic = propertyDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);
        var isGetterPrivate = propertyDeclaration.GetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.GetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isSetterPrivate = propertyDeclaration.SetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.SetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isInitOnly = propertyDeclaration.InitKeyword is not null;
        var readField = propertyDeclaration.BeginKeyword is not null
            ? null
            : propertyDeclaration.OpenBraceToken is not null
            ? fields.First(field => field.Name == $"__auto_{propertyDeclaration.Identifier.Text}" && field.DeclaringTypeName == declaringTypeName)
            : BindPropertyFieldReference(propertyDeclaration.ReadTarget!, declaringTypeName, fields);
        var writeField = propertyDeclaration.BeginKeyword is not null
            ? null
            : propertyDeclaration.OpenBraceToken is not null
            ? (propertyDeclaration.SetKeyword is null && propertyDeclaration.InitKeyword is null ? null : readField)
            : propertyDeclaration.WriteTarget is null
                ? null
                : BindPropertyFieldReference(propertyDeclaration.WriteTarget, declaringTypeName, fields);
        var getterMethod = propertyDeclaration.GetterBody is null
            ? null
            : BindMethod(CreateGetterAccessorDeclaration(propertyDeclaration), declaringTypeName, knownTypes);
        var setterMethod = propertyDeclaration.SetterBody is null
            ? null
            : BindMethod(CreateSetterAccessorDeclaration(propertyDeclaration), declaringTypeName, knownTypes);
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
            propertyDeclaration.GetterBody!.SemicolonToken);

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
            propertyDeclaration.SetterBody!.SemicolonToken);
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
    public static TypeSymbol InferExpressionType(
        ExpressionSyntax? expression,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol>? knownFields,
        IEnumerable<ConstantSymbol>? knownConstants,
        IEnumerable<PropertySymbol>? knownProperties,
        MethodSymbol? currentMethod)
    {
        if (expression is null)
        {
            return TypeSymbol.Integer;
        }

        return expression switch
        {
            LiteralExpressionSyntax literal => InferLiteralType(literal),
            SetLiteralExpressionSyntax setLiteral => InferSetLiteralType(setLiteral, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            RangeExpressionSyntax range => InferExpressionType(range.Start, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            ParenthesizedExpressionSyntax parenthesized => InferExpressionType(parenthesized.Expression, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            MatchAndPatternSyntax andPattern => andPattern.Patterns.Count > 0
                ? InferExpressionType(andPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod)
                : TypeSymbol.Integer,
            MatchNotPatternSyntax notPattern => InferExpressionType(notPattern.Pattern, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            MatchOrPatternSyntax orPattern => orPattern.Patterns.Count > 0
                ? InferExpressionType(orPattern.Patterns[0], localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod)
                : TypeSymbol.Integer,
            MatchRelationalPatternSyntax relational => InferExpressionType(relational.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            NewExpressionSyntax newExpression => new TypeSymbol(newExpression.TypeName.ToDisplayString(), true),
            NewArrayExpressionSyntax newArray => new TypeSymbol(
                $"{newArray.ElementTypeName.ToDisplayString()}{GetArrayTypeSuffix(newArray.LengthExpressions)}",
                true),
            ArrayLengthExpressionSyntax => TypeSymbol.Integer,
            ElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            PostfixElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, elementAccess.IndexExpressions, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType) => localType,
            NameExpressionSyntax name => ResolveName(name.Name, localTypes, [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            AssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var assignmentType) => assignmentType,
            AssignmentExpressionSyntax => TypeSymbol.Integer,
            CompoundAssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod, out var compoundAssignmentType) => compoundAssignmentType,
            CompoundAssignmentExpressionSyntax => TypeSymbol.Integer,
            UnaryExpressionSyntax unary => unary.OperatorToken.Kind == SyntaxKind.NotKeyword
                ? InferExpressionType(unary.Operand, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod) == TypeSymbol.Boolean
                    ? TypeSymbol.Boolean
                    : TypeSymbol.Integer
                : TypeSymbol.Integer,
            BinaryExpressionSyntax binary => binary.OperatorToken.Kind == SyntaxKind.InKeyword
                ? TypeSymbol.Boolean
                : IsComparisonOperator(binary.OperatorToken.Kind)
                    ? TypeSymbol.Boolean
                    : InferBinaryExpressionType(binary, localTypes, knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod),
            AsExpressionSyntax asExpression => ResolveTypeReference(asExpression.TypeName.ToDisplayString(), [])
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
                    : ResolveInvocation(call.Target, call.Arguments.Count, localTypes, [], knownMethods, knownFields ?? [], knownConstants ?? [], knownProperties ?? [], currentMethod)?.Method.ReturnType ?? TypeSymbol.Integer,
            MatchExpressionSyntax matchExpression => matchExpression.Arms.Count > 0
                ? InferExpressionType(
                    matchExpression.Arms[0].Expression,
                    matchExpression.Arms[0].TypeName is not null &&
                    matchExpression.Arms[0].Identifier is not null &&
                    ResolveTypeReference(matchExpression.Arms[0].TypeName!.ToDisplayString(), []) is { } matchArmType
                        ? new Dictionary<string, TypeSymbol>(localTypes, StringComparer.Ordinal)
                        {
                            [matchExpression.Arms[0].Identifier!.Text] = matchArmType
                        }
                        : localTypes,
                    knownMethods,
                    knownFields ?? [],
                    knownConstants ?? [],
                    knownProperties ?? [],
                    currentMethod)
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
        MethodSymbol? currentMethod)
    {
        var arrayType = localTypes.TryGetValue(target.ToDisplayString(), out var localType)
            ? localType
            : ResolvePropertyReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                ?? ResolveConstantReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod)?.Type
                ?? ResolveFieldReference(target, knownFields, currentMethod)?.Type
                ?? new TypeSymbol("Integer[]", true);

        var indexer = ResolveIndexerReference(target, localTypes, knownFields, knownConstants, knownProperties, currentMethod);
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

        return IsArrayType(arrayType)
            ? GetArrayElementTypeName(arrayType.Name) switch
            {
                "Boolean" => TypeSymbol.Boolean,
                "Char" => TypeSymbol.Char,
                "String" => TypeSymbol.String,
                "Integer" => TypeSymbol.Integer,
                var other => new TypeSymbol(other, true)
            }
            : TypeSymbol.Integer;
    }

    private static TypeSymbol GetIndexedElementType(
        ExpressionSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (target is MemberAccessExpressionSyntax memberAccess &&
            ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Property is { IsIndexer: true } directIndexer)
        {
            return directIndexer.Type;
        }

        var targetType = InferExpressionType(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var indexer = ResolveIndexerReference(target, localTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
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

        return IsArrayType(targetType)
            ? GetArrayElementTypeName(targetType.Name) switch
            {
                "Boolean" => TypeSymbol.Boolean,
                "Char" => TypeSymbol.Char,
                "String" => TypeSymbol.String,
                "Integer" => TypeSymbol.Integer,
                var other => new TypeSymbol(other, true)
            }
            : TypeSymbol.Integer;
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
        return target switch
        {
            NameExpressionSyntax name => ResolveInvocation(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            _ => null
        };
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
        return target switch
        {
            NameExpressionSyntax name => ResolveInvocationIgnoringAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, ignoreAccess: true),
            _ => null
        };
    }

    public static MemberResolution ResolveMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        var displayName = $"{GetExpressionDisplayName(memberAccess.Receiver)}.{memberAccess.MemberName.Text}";
        if (memberAccess.MemberName.Text == "Length" && HasLengthProperty(receiverType))
        {
            return new MemberResolution(displayName, TypeSymbol.Integer);
        }

        var property = knownProperties.FirstOrDefault(candidate =>
            !candidate.IsStatic &&
            candidate.DeclaringTypeName == receiverType.Name &&
            candidate.Name == memberAccess.MemberName.Text);
        if (property is not null)
        {
            return new MemberResolution(displayName, property.Type, property.ReadField, property, null);
        }

        var field = knownFields.FirstOrDefault(candidate =>
            !candidate.IsStatic &&
            candidate.DeclaringTypeName == receiverType.Name &&
            candidate.Name == memberAccess.MemberName.Text);
        if (field is not null)
        {
            return new MemberResolution(displayName, field.Type, field);
        }

        var constant = knownConstants.FirstOrDefault(candidate =>
            candidate.IsStatic &&
            candidate.DeclaringTypeName == receiverType.Name &&
            candidate.Name == memberAccess.MemberName.Text);
        if (constant is not null)
        {
            return new MemberResolution(displayName, constant.Type, null, null, null, constant);
        }

        var method = knownMethods.FirstOrDefault(candidate =>
            !candidate.IsStatic &&
            candidate.DeclaringTypeName == receiverType.Name &&
            candidate.Name == memberAccess.MemberName.Text);
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
        if (memberAccess.Receiver is NameExpressionSyntax receiverName &&
            ResolveTypeReference(receiverName.Name.ToDisplayString(), knownTypes) is { } targetType)
        {
            if (TryResolveTypeIntrinsic(targetType, memberAccess.MemberName.Text, argumentCount) is { } typeIntrinsic)
            {
                return new InvocationResolution(typeIntrinsic);
            }

            var staticMethod = knownMethods.FirstOrDefault(candidate =>
                candidate.DeclaringTypeName == targetType.Name &&
                candidate.Name == memberAccess.MemberName.Text &&
                candidate.Parameters.Count == argumentCount &&
                candidate.IsStatic);
            if (staticMethod is not null)
            {
                return new InvocationResolution(staticMethod);
            }

            return null;
        }

        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
        if (TryResolveIntrinsic(receiverType, memberAccess.MemberName.Text, argumentCount) is { } intrinsic)
        {
            return new InvocationResolution(intrinsic, receiverType, true);
        }

        var method = knownMethods.FirstOrDefault(candidate =>
            candidate.DeclaringTypeName == receiverType.Name &&
            candidate.Name == memberAccess.MemberName.Text &&
            candidate.Parameters.Count == argumentCount);
        if (method is null || (!ignoreAccess && method.IsStatic))
        {
            return null;
        }

        return new InvocationResolution(method, receiverType, !method.IsStatic);
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
        return openBracketIndex > 0 &&
            type.Name.EndsWith("]", StringComparison.Ordinal) &&
            type.Name[(openBracketIndex + 1)..^1].All(character => char.IsDigit(character) || character == ',');
    }

    public static bool IsIndexableType(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
    }

    public static bool IsSliceAccess(IReadOnlyList<ExpressionSyntax> indexExpressions) =>
        indexExpressions.Count == 1 && indexExpressions[0] is RangeExpressionSyntax;

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
            type != TypeSymbol.Integer;
    }

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

        return GetArrayElementTypeName(type.Name) switch
        {
            "Void" => TypeSymbol.Void,
            "Boolean" => TypeSymbol.Boolean,
            "Char" => TypeSymbol.Char,
            "Integer" => TypeSymbol.Integer,
            "String" => TypeSymbol.String,
            _ => new TypeSymbol(GetArrayElementTypeName(type.Name), true)
        };
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
        MethodSymbol? currentMethod)
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
             ResolvePropertyReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod) is { Type: var propertyType } && IsIndexableType(propertyType) ||
             ResolveConstantReference(target, locals, knownFields, knownConstants, knownProperties, currentMethod) is { Type: var constantType } && IsIndexableType(constantType)))
        {
            return null;
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

        return knownProperties.FirstOrDefault(property =>
            property.IsIndexer &&
            !property.IsStatic &&
            property.DeclaringTypeName == receiverType.Name &&
            (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text));
    }

    public static PropertySymbol? ResolveIndexerReference(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        return target switch
        {
            NameExpressionSyntax name => ResolveIndexerReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Property is { IsIndexer: true } property
                ? property
                : knownProperties.FirstOrDefault(candidate =>
                    candidate.IsIndexer &&
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Name),
            _ => knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                property.DeclaringTypeName == InferExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Name)
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
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (valueReceiverType is not null)
        {
            if (TryResolveIntrinsic(valueReceiverType, target.Parts[^1].Text, argumentCount) is { } intrinsic)
            {
                return new InvocationResolution(intrinsic, valueReceiverType, true);
            }

            var instanceMethod = knownMethods.FirstOrDefault(method =>
                method.DeclaringTypeName == valueReceiverType.Name &&
                method.Name == target.Parts[^1].Text &&
                method.Parameters.Count == argumentCount &&
                !method.IsStatic);
            if (instanceMethod is not null)
            {
                return new InvocationResolution(instanceMethod, valueReceiverType, true);
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
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (valueReceiverType is not null)
        {
            var method = knownMethods.FirstOrDefault(candidate =>
                candidate.DeclaringTypeName == valueReceiverType.Name &&
                candidate.Name == target.Parts[^1].Text &&
                candidate.Parameters.Count == argumentCount);
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
                method.Parameters.Count == argumentCount &&
                (qualifiedTarget.DeclaringTypeName is null || method.DeclaringTypeName == qualifiedTarget.DeclaringTypeName))
            .ToArray();
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

        var receiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (receiverType is not null)
        {
            var instanceField = knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == receiverType.Name &&
                field.Name == name.Parts[^1].Text &&
                !field.IsStatic);
            if (instanceField is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceField.Type, null, instanceField);
            }

            var instanceProperty = knownProperties.FirstOrDefault(property =>
                property.DeclaringTypeName == receiverType.Name &&
                property.Name == name.Parts[^1].Text &&
                !property.IsStatic);
            if (instanceProperty is not null)
            {
                return new NameResolution(NameResolutionKind.Field, displayName, instanceProperty.Type, null, instanceProperty.ReadField);
            }

            var instanceConstant = knownConstants.FirstOrDefault(constant =>
                constant.IsStatic &&
                constant.DeclaringTypeName == receiverType.Name &&
                constant.Name == name.Parts[^1].Text);
            if (instanceConstant is not null)
            {
                return new NameResolution(NameResolutionKind.Constant, displayName, instanceConstant.Type, null, null, instanceConstant);
            }

            var instanceMethodGroup = knownMethods.FirstOrDefault(method =>
                method.DeclaringTypeName == receiverType.Name &&
                method.Name == name.Parts[^1].Text &&
                !method.IsStatic);
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
            return TypeSymbol.Integer;
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

        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return typeName switch
        {
            "Object" => TypeSymbol.Object,
            "Void" => TypeSymbol.Void,
            "Boolean" => TypeSymbol.Boolean,
            "Char" => TypeSymbol.Char,
            "Integer" => TypeSymbol.Integer,
            "String" => TypeSymbol.String,
            "Nil" => TypeSymbol.Nil,
            _ => knownTypes.FirstOrDefault(type => type.Name == typeName)
        };
    }

    public static bool IsCompatibleReferenceType(TypeSymbol sourceType, TypeSymbol targetType) =>
        targetType.IsReferenceType &&
        sourceType.IsReferenceType &&
        (targetType.Name == TypeSymbol.Object.Name || sourceType.Name == targetType.Name);

    private static FieldSymbol? ResolveFieldReference(
        QualifiedNameSyntax name,
        IEnumerable<FieldSymbol> knownFields,
        MethodSymbol? currentMethod)
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

        var valueReceiverType = TryResolveValueReceiverType(name, locals, fields, [], [], currentMethod);
        if (valueReceiverType is not null)
        {
            var instanceField = fields.FirstOrDefault(field =>
                !field.IsStatic &&
                field.DeclaringTypeName == valueReceiverType.Name &&
                field.Name == name.Parts[^1].Text);
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
        MethodSymbol? currentMethod)
    {
        var properties = knownProperties.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, properties, currentMethod);
        if (valueReceiverType is not null)
        {
            return properties.FirstOrDefault(property =>
                property.DeclaringTypeName == valueReceiverType.Name &&
                property.Name == name.Parts[^1].Text &&
                !property.IsStatic);
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
        MethodSymbol? currentMethod)
    {
        var constants = knownConstants.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (valueReceiverType is not null)
        {
            var instanceConstant = constants.FirstOrDefault(constant =>
                constant.DeclaringTypeName == valueReceiverType.Name &&
                constant.Name == name.Parts[^1].Text &&
                constant.IsStatic);
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
        MethodSymbol? currentMethod) =>
        expression is LiteralExpressionSyntax ||
        expression is NameExpressionSyntax name && ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod) is not null ||
        expression is MemberAccessExpressionSyntax member && ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod).Constant is not null;

    public static object? GetConstantValue(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        expression switch
        {
            LiteralExpressionSyntax literal => literal.LiteralToken.Value ?? (literal.LiteralToken.Kind == SyntaxKind.TrueKeyword ? 1 : literal.LiteralToken.Kind == SyntaxKind.FalseKeyword ? 0 : 0),
            NameExpressionSyntax name => ResolveConstantReference(name.Name, locals, knownFields, knownConstants, knownProperties, currentMethod)?.Value,
            MemberAccessExpressionSyntax member => ResolveMemberAccess(member, locals, [], knownFields, knownConstants, knownProperties, currentMethod).Constant?.Value,
            ParenthesizedExpressionSyntax parenthesized => GetConstantValue(parenthesized.Expression, locals, knownFields, knownConstants, knownProperties, currentMethod),
            UnaryExpressionSyntax unary when unary.OperatorToken.Kind == SyntaxKind.NotKeyword => GetConstantValue(unary.Operand, locals, knownFields, knownConstants, knownProperties, currentMethod) is int operand
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
        MethodSymbol? currentMethod)
    {
        var qualifier = GetQualifier(memberAccess);
        if (qualifier is null)
        {
            return currentMethod?.DeclaringTypeName is not null && !currentMethod.IsStatic
                ? new TypeSymbol(currentMethod.DeclaringTypeName, true)
                : null;
        }

        return TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod);
    }

    public static TypeSymbol? TryResolveValueReferenceType(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<ConstantSymbol> knownConstants,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
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

        var valueReceiverType = TryResolveValueReferenceType(qualifier, locals, knownFields, knownConstants, knownProperties, currentMethod);
        if (valueReceiverType is not null)
        {
            var instanceProperty = knownProperties.FirstOrDefault(property =>
                property.DeclaringTypeName == valueReceiverType.Name &&
                property.Name == name.Parts[^1].Text &&
                !property.IsStatic);
            if (instanceProperty is not null)
            {
                return instanceProperty.Type;
            }

            var instanceField = knownFields.FirstOrDefault(field =>
                field.DeclaringTypeName == valueReceiverType.Name &&
                field.Name == name.Parts[^1].Text &&
                !field.IsStatic);
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
