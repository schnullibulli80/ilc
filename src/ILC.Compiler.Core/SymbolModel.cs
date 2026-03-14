namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public abstract record Symbol(string Name);

public record TypeSymbol(string Name, bool IsReferenceType) : Symbol(Name)
{
    public static readonly TypeSymbol Void = new("Void", false);
    public static readonly TypeSymbol Boolean = new("Boolean", false);
    public static readonly TypeSymbol Char = new("Char", false);
    public static readonly TypeSymbol Integer = new("Integer", false);
    public static readonly TypeSymbol String = new("String", true);
}

public sealed record ParameterSymbol(string Name, TypeSymbol Type) : Symbol(Name);

public sealed record FieldSymbol(
    string Name,
    TypeSymbol Type,
    string? DeclaringTypeName = null,
    bool IsStatic = false,
    FieldDeclarationSyntax? Declaration = null) : Symbol(Name);

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
    IReadOnlyList<MemberSyntax>? SyntheticMembers = null) : Symbol(Name);

public sealed record CompilationUnitSymbol(
    string? Namespace,
    IReadOnlyList<TypeSymbol> Types,
    IReadOnlyList<MethodSymbol> Methods,
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
}

public sealed record GlobalVariableSymbol(
    string Name,
    TypeSymbol Type,
    bool HasInitializer) : Symbol(Name);

public sealed record NamedTypeSymbol(
    string Name,
    bool IsReferenceType,
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<FieldSymbol> Fields,
    IReadOnlyList<PropertySymbol> Properties) : TypeSymbol(Name, IsReferenceType);

public enum NameResolutionKind
{
    Unknown,
    LocalOrGlobal,
    Type,
    MethodGroup,
    Field
}

public sealed record NameResolution(
    NameResolutionKind Kind,
    string DisplayName,
    TypeSymbol? Type = null,
    MethodSymbol? Method = null,
    FieldSymbol? Field = null);

public sealed record InvocationResolution(MethodSymbol Method, TypeSymbol? ReceiverType = null, bool IsVirtual = false);

public sealed record MemberResolution(
    string DisplayName,
    TypeSymbol? Type = null,
    FieldSymbol? Field = null,
    PropertySymbol? Property = null,
    MethodSymbol? Method = null);

public sealed class Binder
{
    public BindingResult Bind(SyntaxTree syntaxTree)
    {
        var diagnostics = new DiagnosticBag();
        foreach (var diagnostic in syntaxTree.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        var declaredTypes = new List<TypeSymbol>();
        var globals = new List<GlobalVariableSymbol>();
        var hasTopLevelStatements = false;

        foreach (var member in syntaxTree.Root.Members)
        {
            if (member is ClassDeclarationSyntax classDeclaration)
            {
                declaredTypes.Add(BindClass(classDeclaration));
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
        var knownProperties = declaredTypes
            .OfType<NamedTypeSymbol>()
            .SelectMany(type => type.Properties)
            .ToArray();

        foreach (var member in syntaxTree.Root.Members)
        {
            switch (member)
            {
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        var type = declarator.TypeName is not null
                            ? BindType(declarator.TypeName)
                            : SemanticFacts.InferExpressionType(
                                declarator.Initializer,
                                new Dictionary<string, TypeSymbol>(),
                                knownMethods,
                                knownFields,
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
        ValidateSemantics(syntaxTree.Root.Members, globals, declaredTypes, knownMethods, knownFields, knownProperties, diagnostics);
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
            [TypeSymbol.Boolean, TypeSymbol.Char, TypeSymbol.Integer, TypeSymbol.String, .. declaredTypes],
            methods,
            globals,
            entryPoint);

        return new BindingResult(symbol, diagnostics);
    }

    private static void ValidateSemantics(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<GlobalVariableSymbol> globals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics)
    {
        var topLevelScope = globals.ToDictionary(global => global.Name, global => global.Type, StringComparer.Ordinal);

        foreach (var member in members)
        {
            switch (member)
            {
                case TopLevelExpressionStatementSyntax expressionStatement:
                    ValidateExpression(expressionStatement.Expression, topLevelScope, knownTypes, knownMethods, knownFields, knownProperties, null, diagnostics);
                    break;
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            ValidateExpression(declarator.Initializer, topLevelScope, knownTypes, knownMethods, knownFields, knownProperties, null, diagnostics);
                        }
                    }
                    break;
                case ClassDeclarationSyntax classDeclaration:
                    ValidateClassSemantics(classDeclaration, knownTypes, knownMethods, knownFields, knownProperties, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateClassSemantics(
        ClassDeclarationSyntax classDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics)
    {
        var typeFields = FindFields(knownFields, classDeclaration.Identifier.Text);
        foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
        {
            ValidatePropertyDeclaration(property, classDeclaration.Identifier.Text, typeFields, diagnostics);
        }

        foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var locals = method.Parameters.ToDictionary(
                parameter => parameter.Identifier.Text,
                parameter => BindType(parameter.TypeName),
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
            if (boundMethod?.IsConstructor == true)
            {
                diagnostics.Report(
                    "ILC2114",
                    $"Constructor '{classDeclaration.Identifier.Text}' cannot declare an expression body.",
                    DiagnosticSeverity.Error,
                    method.Keyword.Span);
                continue;
            }

            ValidateExpression(method.ExpressionBody, locals, knownTypes, knownMethods, knownFields, knownProperties, boundMethod, diagnostics);
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
                knownProperties,
                boundMethod,
                diagnostics);
        }
    }

    private static void ValidatePropertyDeclaration(
        PropertyDeclarationSyntax property,
        string declaringTypeName,
        IReadOnlyList<FieldSymbol> typeFields,
        DiagnosticBag diagnostics)
    {
        if (property.OpenBraceToken is not null)
        {
            return;
        }

        var propertyType = BindType(property.TypeName);
        var isStatic = property.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);
        var indexParameterType = property.IndexParameter is not null ? BindType(property.IndexParameter.TypeName) : null;
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
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case BlockStatementSyntax block:
                    ValidateStatements(block.Statements, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    break;
                case LocalVariableDeclarationStatementSyntax localVariable:
                    foreach (var declarator in localVariable.Declarators)
                    {
                        if (declarator.Initializer is not null)
                        {
                            ValidateExpression(declarator.Initializer, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                        }

                        locals[declarator.Identifier.Text] = declarator.TypeName is not null
                            ? BindType(declarator.TypeName)
                            : SemanticFacts.InferExpressionType(declarator.Initializer, locals, knownMethods, knownFields, knownProperties, currentMethod);
                    }
                    break;
                case ReturnStatementSyntax returnStatement when returnStatement.Expression is not null:
                    ValidateExpression(returnStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    break;
                case ExpressionStatementSyntax expressionStatement:
                    ValidateExpression(expressionStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    break;
                case IfStatementSyntax ifStatement:
                    ValidateExpression(ifStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    ValidateStatements([ifStatement.ThenStatement], locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    if (ifStatement.ElseStatement is not null)
                    {
                        ValidateStatements([ifStatement.ElseStatement], locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    }
                    break;
                case WhileStatementSyntax whileStatement:
                    ValidateExpression(whileStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    ValidateStatements([whileStatement.Body], locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                    break;
            }
        }
    }

    private static void ValidateExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        switch (expression)
        {
            case NameExpressionSyntax name:
                ValidateNameReference(name.Name, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                break;
            case AssignmentExpressionSyntax assignment:
                ValidateAssignmentTarget(assignment.Target, locals, knownTypes, knownFields, knownProperties, currentMethod, diagnostics);
                ValidateExpression(assignment.Expression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                break;
            case BinaryExpressionSyntax binary:
                ValidateExpression(binary.Left, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                ValidateExpression(binary.Right, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                break;
            case ElementAccessExpressionSyntax elementAccess:
                ValidateNameReference(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                ValidateExpression(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                ValidateExpression(memberAccess.Receiver, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownProperties, currentMethod);
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
                ValidateNameReference(arrayLength.Target, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                var lengthTargetType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(arrayLength.Target), locals, knownMethods, knownFields, knownProperties, currentMethod);
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
                    ValidateExpression(lengthExpression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                }

                break;
            case CallExpressionSyntax call:
                if (TryReportInvalidMethodAccess(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics))
                {
                }
                else if (SemanticFacts.ResolveInvocation(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod) is null)
                {
                    diagnostics.Report(
                        "ILC2103",
                        $"Unknown call target '{SemanticFacts.GetExpressionDisplayName(call.Target)}' with arity {call.Arguments.Count}.",
                        DiagnosticSeverity.Error,
                        GetExpressionDiagnosticSpan(call.Target, knownTypes));
                }

                foreach (var argument in call.Arguments)
                {
                    ValidateExpression(argument.Expression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
                }
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    ValidateExpression(argument.Expression, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics);
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

    private static void ValidateNameReference(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (TryReportInvalidFieldAccess(name, locals, knownFields, knownProperties, currentMethod, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(name, locals, knownFields, knownProperties, currentMethod);
        if (property is not null && property.IsGetterPrivate && property.DeclaringTypeName != currentMethod?.DeclaringTypeName)
        {
            diagnostics.Report(
                "ILC2121",
                $"Property getter '{name.ToDisplayString()}' is not accessible in the current context.",
                DiagnosticSeverity.Error,
                name.Parts[^1].Span);
            return;
        }

        var resolution = SemanticFacts.ResolveName(name, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod);
        switch (resolution.Kind)
        {
            case NameResolutionKind.LocalOrGlobal:
                return;
            case NameResolutionKind.Field:
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
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (target is ElementAccessExpressionSyntax elementAccess)
        {
            ValidateNameReference(elementAccess.Target, locals, knownTypes, [], knownFields, knownProperties, currentMethod, diagnostics);
            foreach (var indexExpression in elementAccess.IndexExpressions)
            {
                ValidateExpression(indexExpression, locals, knownTypes, [], knownFields, knownProperties, currentMethod, diagnostics);
            }

            var indexedTargetType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(elementAccess.Target), locals, [], knownFields, knownProperties, currentMethod);
            if (indexedTargetType == TypeSymbol.String)
            {
                diagnostics.Report(
                    "ILC2127",
                    $"String '{elementAccess.Target.ToDisplayString()}' is immutable and cannot be assigned through an index.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(elementAccess.Target, []));
                return;
            }

            ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, [], knownFields, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is PostfixElementAccessExpressionSyntax postfixElementAccess)
        {
            ValidateExpression(postfixElementAccess.Target, locals, knownTypes, [], knownFields, knownProperties, currentMethod, diagnostics);
            foreach (var indexExpression in postfixElementAccess.IndexExpressions)
            {
                ValidateExpression(indexExpression, locals, knownTypes, [], knownFields, knownProperties, currentMethod, diagnostics);
            }

            var indexedTargetType = SemanticFacts.InferExpressionType(postfixElementAccess.Target, locals, [], knownFields, knownProperties, currentMethod);
            if (indexedTargetType == TypeSymbol.String)
            {
                diagnostics.Report(
                    "ILC2127",
                    $"String '{GetExpressionDisplayName(postfixElementAccess.Target)}' is immutable and cannot be assigned through an index.",
                    DiagnosticSeverity.Error,
                    GetExpressionDiagnosticSpan(postfixElementAccess.Target, []));
                return;
            }

            ValidateArrayAccess(postfixElementAccess.Target, postfixElementAccess.IndexExpressions, postfixElementAccess.OpenBracketToken.Span, locals, [], knownFields, knownProperties, currentMethod, diagnostics);
            return;
        }

        if (target is not NameExpressionSyntax nameTarget)
        {
            if (target is MemberAccessExpressionSyntax memberTarget)
            {
                ValidateExpression(memberTarget.Receiver, locals, knownTypes, [], knownFields, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberTarget, locals, [], knownFields, knownProperties, currentMethod);
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
        if (TryReportInvalidFieldAccess(targetName, locals, knownFields, knownProperties, currentMethod, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(targetName, locals, knownFields, knownProperties, currentMethod);
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
            if (!locals.ContainsKey(name) &&
                SemanticFacts.ResolveName(targetName, locals, knownTypes, [], knownFields, knownProperties, currentMethod).Kind != NameResolutionKind.Field)
            {
                diagnostics.Report(
                    "ILC2100",
                    $"Unknown assignment target '{name}'.",
                    DiagnosticSeverity.Error,
                    GetReferenceDiagnosticSpan(targetName, knownTypes));
            }

            return;
        }

        var resolution = SemanticFacts.ResolveName(targetName, locals, knownTypes, [], knownFields, knownProperties, currentMethod);
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
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (target is MemberAccessExpressionSyntax memberAccess &&
            SemanticFacts.ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownProperties, currentMethod).Property is { IsIndexer: true })
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
                var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownProperties, currentMethod);
                if (indexType != TypeSymbol.Integer)
                {
                    diagnostics.Report(
                        "ILC2126",
                        $"Array index for '{GetExpressionDisplayName(target)}' must be Integer, but was '{indexType.Name}'.",
                        DiagnosticSeverity.Error,
                        indexSpan);
                }
            }

            return;
        }

        var indexedType = SemanticFacts.InferExpressionType(target, locals, knownMethods, knownFields, knownProperties, currentMethod);
        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownMethods, knownFields, knownProperties, currentMethod);
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
                var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownProperties, currentMethod);
                if (indexType != TypeSymbol.Integer)
                {
                    diagnostics.Report(
                        "ILC2126",
                        $"Array index for '{GetExpressionDisplayName(target)}' must be Integer, but was '{indexType.Name}'.",
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
            var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownProperties, currentMethod);
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

    private static void ValidateArrayAccess(
        QualifiedNameSyntax target,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        TextSpan indexSpan,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var indexer = SemanticFacts.ResolveIndexerReference(target, locals, knownFields, knownProperties, currentMethod);
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

            var resolvedIndexType = SemanticFacts.InferExpressionType(indexExpressions[0], locals, knownMethods, knownFields, knownProperties, currentMethod);
            if (resolvedIndexType != TypeSymbol.Integer)
            {
                diagnostics.Report(
                    "ILC2126",
                    $"Array index for '{target.ToDisplayString()}' must be Integer, but was '{resolvedIndexType.Name}'.",
                    DiagnosticSeverity.Error,
                    indexSpan);
            }

            return;
        }

        var indexedType = SemanticFacts.InferExpressionType(new NameExpressionSyntax(target), locals, knownMethods, knownFields, knownProperties, currentMethod);
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
            var indexType = SemanticFacts.InferExpressionType(indexExpression, locals, knownMethods, knownFields, knownProperties, currentMethod);
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

    private static bool TryReportInvalidFieldAccess(
        QualifiedNameSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var receiverType = target.Parts.Count >= 2 && target.Parts[0].Text != "self"
            ? SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownProperties, currentMethod)
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
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        return target switch
        {
            NameExpressionSyntax name => TryReportInvalidMethodAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod, diagnostics),
            MemberAccessExpressionSyntax memberAccess => false,
            _ => false
        };
    }

    private static bool TryReportInvalidMethodAccess(
        QualifiedNameSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var candidate = SemanticFacts.ResolveInvocationIgnoringAccess(target, argumentCount, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod);
        if (candidate is null)
        {
            return false;
        }

        if (candidate.IsVirtual &&
            SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownProperties, currentMethod) is not null)
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

    private static bool HasDeclaredConstructors(
        QualifiedNameSyntax typeName,
        IEnumerable<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var resolvedType = SemanticFacts.ResolveTypeReference(typeName.ToDisplayString(), knownTypes);
        return resolvedType is not null && knownMethods.Any(method => method.DeclaringTypeName == resolvedType.Name && method.IsConstructor);
    }

    private static TypeSymbol BindType(QualifiedNameSyntax? typeName)
    {
        if (typeName is null)
        {
            return TypeSymbol.Integer;
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

    private static NamedTypeSymbol BindClass(ClassDeclarationSyntax classDeclaration)
    {
        var declaredFields = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => BindFields(field, classDeclaration.Identifier.Text))
            .ToArray();
        var autoPropertyFields = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Where(property => property.OpenBraceToken is not null)
            .Select(property => BindAutoPropertyBackingField(property, classDeclaration.Identifier.Text))
            .ToArray();
        var fields = declaredFields
            .Concat(autoPropertyFields)
            .ToArray();
        var declaredMethods = classDeclaration.Members
            .OfType<MethodDeclarationSyntax>()
            .Select(method => BindMethod(method, classDeclaration.Identifier.Text))
            .ToArray();
        var properties = classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Select(property => BindProperty(property, classDeclaration.Identifier.Text, fields))
            .ToArray();
        var propertyAccessorMethods = properties
            .SelectMany(property => new[] { property.GetterMethod, property.SetterMethod })
            .Where(method => method is not null)
            .Cast<MethodSymbol>()
            .ToArray();
        var methods = declaredMethods
            .Concat(propertyAccessorMethods)
            .ToArray();

        return new NamedTypeSymbol(classDeclaration.Identifier.Text, true, methods, fields, properties);
    }

    private static IReadOnlyList<FieldSymbol> BindFields(FieldDeclarationSyntax fieldDeclaration, string declaringTypeName) =>
        fieldDeclaration.Declarators
            .Select(declarator => new FieldSymbol(
                declarator.Identifier.Text,
                declarator.TypeName is not null ? BindType(declarator.TypeName) : TypeSymbol.Integer,
                declaringTypeName,
                fieldDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
                fieldDeclaration))
            .ToArray();

    private static MethodSymbol BindMethod(MethodDeclarationSyntax methodDeclaration, string? declaringTypeName = null)
    {
        var returnType = methodDeclaration.ReturnType is null
            ? TypeSymbol.Void
            : BindType(methodDeclaration.ReturnType);

        var parameters = methodDeclaration.Parameters
            .Select(parameter => new ParameterSymbol(parameter.Identifier.Text, BindType(parameter.TypeName)))
            .ToArray();

        return new MethodSymbol(
            methodDeclaration.Identifier.Text,
            returnType,
            parameters,
            declaringTypeName,
            methodDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
            methodDeclaration,
            methodDeclaration.Keyword.Kind == SyntaxKind.ConstructorKeyword);
    }

    private static PropertySymbol BindProperty(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName, IReadOnlyList<FieldSymbol> fields)
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
            : BindMethod(CreateGetterAccessorDeclaration(propertyDeclaration), declaringTypeName);
        var setterMethod = propertyDeclaration.SetterBody is null
            ? null
            : BindMethod(CreateSetterAccessorDeclaration(propertyDeclaration), declaringTypeName);
        var indexParameter = propertyDeclaration.IndexParameter is null
            ? null
            : new ParameterSymbol(propertyDeclaration.IndexParameter.Identifier.Text, BindType(propertyDeclaration.IndexParameter.TypeName));

        return new PropertySymbol(
            propertyDeclaration.Identifier.Text,
            BindType(propertyDeclaration.TypeName),
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

    private static FieldSymbol BindAutoPropertyBackingField(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName) =>
        new(
            $"__auto_{propertyDeclaration.Identifier.Text}",
            BindType(propertyDeclaration.TypeName),
            declaringTypeName,
            propertyDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword),
            null);

    private static MethodDeclarationSyntax CreateGetterAccessorDeclaration(PropertyDeclarationSyntax propertyDeclaration) =>
        new(
            propertyDeclaration.Modifiers,
            new SyntaxToken(SyntaxKind.FunctionKeyword, "function", null, propertyDeclaration.GetterKeyword?.Span ?? propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.IdentifierToken, $"get_{propertyDeclaration.Identifier.Text}", null, propertyDeclaration.Identifier.Span),
            null,
            [],
            null,
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
        return new MethodDeclarationSyntax(
            propertyDeclaration.Modifiers,
            new SyntaxToken(SyntaxKind.MethodKeyword, "method", null, propertyDeclaration.SetterKeyword?.Span ?? propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.IdentifierToken, $"set_{propertyDeclaration.Identifier.Text}", null, propertyDeclaration.Identifier.Span),
            new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, setterParameter.Span),
            [
                new ParameterSyntax(
                    setterParameter,
                    new SyntaxToken(SyntaxKind.ColonToken, ":", null, setterParameter.Span),
                    propertyDeclaration.TypeName)
            ],
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
            NewExpressionSyntax newExpression => new TypeSymbol(newExpression.TypeName.ToDisplayString(), true),
            NewArrayExpressionSyntax newArray => new TypeSymbol(
                $"{newArray.ElementTypeName.ToDisplayString()}{GetArrayTypeSuffix(newArray.LengthExpressions)}",
                true),
            ArrayLengthExpressionSyntax => TypeSymbol.Integer,
            ElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, localTypes, knownFields ?? [], knownProperties ?? [], currentMethod),
            PostfixElementAccessExpressionSyntax elementAccess => GetIndexedElementType(elementAccess.Target, localTypes, knownMethods, knownFields ?? [], knownProperties ?? [], currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            NameExpressionSyntax name when localTypes.TryGetValue(name.Name.ToDisplayString(), out var localType) => localType,
            NameExpressionSyntax name => ResolveName(name.Name, localTypes, [], knownMethods, knownFields ?? [], knownProperties ?? [], currentMethod).Type
                ?? TypeSymbol.Integer,
            AssignmentExpressionSyntax assignment when TryGetAssignmentTargetType(assignment.Target, localTypes, knownFields ?? [], knownProperties ?? [], currentMethod, out var assignmentType) => assignmentType,
            AssignmentExpressionSyntax => TypeSymbol.Integer,
            BinaryExpressionSyntax binary => IsComparisonOperator(binary.OperatorToken.Kind) ? TypeSymbol.Boolean : TypeSymbol.Integer,
            CallExpressionSyntax call => ResolveInvocation(call.Target, call.Arguments.Count, localTypes, [], knownMethods, knownFields ?? [], knownProperties ?? [], currentMethod)?.Method.ReturnType ?? TypeSymbol.Integer,
            _ => TypeSymbol.Integer
        };
    }

    private static bool TryGetAssignmentTargetType(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
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
                type = ResolvePropertyReference(name.Name, localTypes, knownFields, knownProperties, currentMethod)?.Type
                    ?? ResolveFieldReference(name.Name, knownFields, currentMethod)?.Type
                    ?? TypeSymbol.Integer;
                return true;
            case MemberAccessExpressionSyntax memberAccess:
                type = ResolveMemberAccess(memberAccess, localTypes, [], knownFields, knownProperties, currentMethod).Type
                    ?? TypeSymbol.Integer;
                return true;
            case ElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, localTypes, knownFields, knownProperties, currentMethod);
                return true;
            case PostfixElementAccessExpressionSyntax elementAccess:
                type = GetIndexedElementType(elementAccess.Target, localTypes, [], knownFields, knownProperties, currentMethod);
                return true;
            default:
                type = TypeSymbol.Integer;
                return false;
        }
    }

    private static TypeSymbol GetIndexedElementType(
        QualifiedNameSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var arrayType = localTypes.TryGetValue(target.ToDisplayString(), out var localType)
            ? localType
            : ResolvePropertyReference(target, localTypes, knownFields, knownProperties, currentMethod)?.Type
                ?? ResolveFieldReference(target, knownFields, currentMethod)?.Type
                ?? new TypeSymbol("Integer[]", true);

        var indexer = ResolveIndexerReference(target, localTypes, knownFields, knownProperties, currentMethod);
        if (indexer is not null)
        {
            return indexer.Type;
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
        IReadOnlyDictionary<string, TypeSymbol> localTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (target is MemberAccessExpressionSyntax memberAccess &&
            ResolveMemberAccess(memberAccess, localTypes, knownMethods, knownFields, knownProperties, currentMethod).Property is { IsIndexer: true } directIndexer)
        {
            return directIndexer.Type;
        }

        var targetType = InferExpressionType(target, localTypes, knownMethods, knownFields, knownProperties, currentMethod);
        var indexer = ResolveIndexerReference(target, localTypes, knownMethods, knownFields, knownProperties, currentMethod);
        if (indexer is not null)
        {
            return indexer.Type;
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

    internal static InvocationResolution? ResolveInvocation(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        return target switch
        {
            NameExpressionSyntax name => ResolveInvocation(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownMethods, knownFields, knownProperties, currentMethod),
            _ => null
        };
    }

    internal static InvocationResolution? ResolveInvocationIgnoringAccess(
        ExpressionSyntax target,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        return target switch
        {
            NameExpressionSyntax name => ResolveInvocationIgnoringAccess(name.Name, argumentCount, locals, knownTypes, knownMethods, knownFields, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberInvocation(memberAccess, argumentCount, locals, knownMethods, knownFields, knownProperties, currentMethod, ignoreAccess: true),
            _ => null
        };
    }

    internal static MemberResolution ResolveMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownProperties, currentMethod);
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

    internal static string GetExpressionDisplayName(ExpressionSyntax expression) =>
        expression switch
        {
            NameExpressionSyntax name => name.Name.ToDisplayString(),
            ElementAccessExpressionSyntax element => $"{element.Target.ToDisplayString()}[...]",
            PostfixElementAccessExpressionSyntax element => $"{GetExpressionDisplayName(element.Target)}[...]",
            ArrayLengthExpressionSyntax length => $"{length.Target.ToDisplayString()}.Length",
            MemberAccessExpressionSyntax member => $"{GetExpressionDisplayName(member.Receiver)}.{member.MemberName.Text}",
            CallExpressionSyntax call => $"{GetExpressionDisplayName(call.Target)}(...)",
            _ => expression.Kind.ToString()
        };

    private static InvocationResolution? ResolveMemberInvocation(
        MemberAccessExpressionSyntax memberAccess,
        int argumentCount,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        bool ignoreAccess = false)
    {
        var receiverType = InferExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownProperties, currentMethod);
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

    public static bool HasLengthProperty(TypeSymbol type)
    {
        return type == TypeSymbol.String || IsArrayType(type);
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
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        if (target.Parts.Count == 1 &&
            (locals.TryGetValue(target.Parts[0].Text, out var localTargetType) && IsIndexableType(localTargetType) ||
             currentMethod?.DeclaringTypeName is not null &&
             ResolveFieldReference(target, knownFields, currentMethod) is { Type: var fieldType } && IsIndexableType(fieldType) ||
             ResolvePropertyReference(target, locals, knownFields, knownProperties, currentMethod) is { Type: var propertyType } && IsIndexableType(propertyType)))
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
            (target.Parts.Count == 1 || property.Name == target.Parts[^1].Text) &&
            property.IndexParameter?.Type == TypeSymbol.Integer);
    }

    public static PropertySymbol? ResolveIndexerReference(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<MethodSymbol> knownMethods,
        IEnumerable<FieldSymbol> knownFields,
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        return target switch
        {
            NameExpressionSyntax name => ResolveIndexerReference(name.Name, locals, knownFields, knownProperties, currentMethod),
            MemberAccessExpressionSyntax memberAccess => ResolveMemberAccess(memberAccess, locals, knownMethods, knownFields, knownProperties, currentMethod).Property is { IsIndexer: true } property
                ? property
                : knownProperties.FirstOrDefault(candidate =>
                    candidate.IsIndexer &&
                    !candidate.IsStatic &&
                    candidate.DeclaringTypeName == InferExpressionType(target, locals, knownMethods, knownFields, knownProperties, currentMethod).Name &&
                    candidate.IndexParameter?.Type == TypeSymbol.Integer),
            _ => knownProperties.FirstOrDefault(property =>
                property.IsIndexer &&
                !property.IsStatic &&
                property.DeclaringTypeName == InferExpressionType(target, locals, knownMethods, knownFields, knownProperties, currentMethod).Name &&
                property.IndexParameter?.Type == TypeSymbol.Integer)
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
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownProperties, currentMethod);
        if (valueReceiverType is not null)
        {
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
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var valueReceiverType = TryResolveValueReceiverType(target, locals, knownFields, knownProperties, currentMethod);
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
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var displayName = name.ToDisplayString();
        if (name.Parts.Count == 1 && locals.TryGetValue(displayName, out var localType))
        {
            return new NameResolution(NameResolutionKind.LocalOrGlobal, displayName, localType);
        }

        var receiverType = TryResolveValueReceiverType(name, locals, knownFields, knownProperties, currentMethod);
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

            var instanceMethodGroup = knownMethods.FirstOrDefault(method =>
                method.DeclaringTypeName == receiverType.Name &&
                method.Name == name.Parts[^1].Text &&
                !method.IsStatic);
            if (instanceMethodGroup is not null)
            {
                return new NameResolution(NameResolutionKind.MethodGroup, displayName, instanceMethodGroup.ReturnType, instanceMethodGroup);
            }
        }

        var propertyCandidate = ResolvePropertyReference(name, locals, knownFields, knownProperties, currentMethod);
        if (propertyCandidate is not null)
        {
            return new NameResolution(NameResolutionKind.Field, displayName, propertyCandidate.Type, null, propertyCandidate.ReadField);
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
            SyntaxKind.NilKeyword => TypeSymbol.Void,
            _ => TypeSymbol.Integer
        };

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

    internal static TypeSymbol? ResolveTypeReference(string displayName, IEnumerable<TypeSymbol> knownTypes)
    {
        var typeName = displayName.Contains('.')
            ? displayName[(displayName.LastIndexOf('.') + 1)..]
            : displayName;

        return knownTypes.FirstOrDefault(type => type.Name == typeName);
    }

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

        var valueReceiverType = TryResolveValueReceiverType(name, locals, fields, [], currentMethod);
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
        IEnumerable<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        var properties = knownProperties.ToArray();
        var valueReceiverType = TryResolveValueReceiverType(name, locals, knownFields, properties, currentMethod);
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

    internal static TypeSymbol? TryResolveValueReceiverType(
        QualifiedNameSyntax memberAccess,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
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

        return TryResolveValueReferenceType(qualifier, locals, knownFields, knownProperties, currentMethod);
    }

    internal static TypeSymbol? TryResolveValueReferenceType(
        QualifiedNameSyntax name,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IEnumerable<FieldSymbol> knownFields,
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

        var valueReceiverType = TryResolveValueReferenceType(qualifier, locals, knownFields, knownProperties, currentMethod);
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
