namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public sealed partial class Binder
{
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
                ValidateDllImportMethod(classDeclaration.Identifier.Text, method, boundMethod, typeScope, diagnostics);
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
                            : SemanticFacts.InferExpressionType(
                                declarator.Initializer,
                                locals,
                                knownMethods,
                                knownFields,
                                knownConstants,
                                knownProperties,
                                currentMethod,
                                knownTypes);
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
            ProjectorExpressionSyntax projector => projector with
            {
                Members = projector.Members
                    .Select(member => member with
                    {
                        Expression = RewriteWithExpression(member.Expression, receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod)
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
            case QueryExpressionSyntax query:
                ValidateExpression(query.SourceExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var querySourceType = SemanticFacts.InferExpressionType(query.SourceExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                var enumerablePattern = SemanticFacts.ResolveEnumerablePattern(querySourceType, knownTypes);
                if (enumerablePattern is null)
                {
                    diagnostics.Report(
                        "ILC2141",
                        $"Expression '{SemanticFacts.GetExpressionDisplayName(query.SourceExpression)}' is not enumerable in the current bootstrap compiler.",
                        DiagnosticSeverity.Error,
                        GetExpressionDiagnosticSpan(query.SourceExpression, knownTypes));
                    break;
                }

                var queryLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                {
                    [query.Identifier.Text] = enumerablePattern.ElementType
                };

                if (query.JoinSourceExpression is not null && query.JoinIdentifier is not null)
                {
                    ValidateExpression(query.JoinSourceExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var joinSourceType = SemanticFacts.InferExpressionType(query.JoinSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var joinEnumerablePattern = SemanticFacts.ResolveEnumerablePattern(joinSourceType, knownTypes);
                    if (joinEnumerablePattern is null)
                    {
                        diagnostics.Report(
                            "ILC2141",
                            $"Expression '{SemanticFacts.GetExpressionDisplayName(query.JoinSourceExpression)}' is not enumerable in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.JoinSourceExpression, knownTypes));
                        break;
                    }

                    queryLocals[query.JoinIdentifier.Text] = joinEnumerablePattern.ElementType;
                    if (query.JoinIntoIdentifier is not null)
                    {
                        queryLocals[query.JoinIntoIdentifier.Text] =
                            SemanticFacts.ResolveTypeReference($"IEnumerable<{joinEnumerablePattern.ElementType.Name}>", knownTypes)
                            ?? new TypeSymbol($"IEnumerable<{joinEnumerablePattern.ElementType.Name}>", true);
                    }
                    if (query.JoinLeftExpression is not null)
                    {
                        ValidateExpression(query.JoinLeftExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }

                    if (query.JoinRightExpression is not null)
                    {
                        ValidateExpression(query.JoinRightExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }

                    if (query.JoinLeftExpression is not null && query.JoinRightExpression is not null)
                    {
                        var joinLeftType = SemanticFacts.InferExpressionType(query.JoinLeftExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        var joinRightType = SemanticFacts.InferExpressionType(query.JoinRightExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        if (joinLeftType.Name != joinRightType.Name)
                        {
                            diagnostics.Report(
                                "ILC2231",
                                "Query join-clause key expressions must have the same type.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(query.JoinLeftExpression, knownTypes));
                        }
                        else if (joinLeftType != TypeSymbol.Integer && joinLeftType != TypeSymbol.String)
                        {
                            diagnostics.Report(
                                "ILC2232",
                                "Query join-clause keys must be Integer or String in the current bootstrap compiler.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(query.JoinLeftExpression, knownTypes));
                        }
                    }
                }

                if (query.SecondSourceExpression is not null && query.SecondIdentifier is not null)
                {
                    ValidateExpression(query.SecondSourceExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var secondSourceType = SemanticFacts.InferExpressionType(query.SecondSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var secondEnumerablePattern = SemanticFacts.ResolveEnumerablePattern(secondSourceType, knownTypes);
                    if (secondEnumerablePattern is null)
                    {
                        diagnostics.Report(
                            "ILC2141",
                            $"Expression '{SemanticFacts.GetExpressionDisplayName(query.SecondSourceExpression)}' is not enumerable in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.SecondSourceExpression, knownTypes));
                        break;
                    }

                    queryLocals[query.SecondIdentifier.Text] = secondEnumerablePattern.ElementType;
                }

                if (query.LetExpression is not null && query.LetIdentifier is not null)
                {
                    ValidateExpression(query.LetExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var letType = SemanticFacts.InferExpressionType(query.LetExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    queryLocals[query.LetIdentifier.Text] = letType;
                }

                if (query.PredicateExpression is not null)
                {
                    ValidateExpression(query.PredicateExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var predicateType = SemanticFacts.InferExpressionType(query.PredicateExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (predicateType != TypeSymbol.Boolean)
                    {
                        diagnostics.Report(
                            "ILC2227",
                            "Query where-clause must be Boolean.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.PredicateExpression, knownTypes));
                        }
                }

                if (query.OrderByExpression is not null)
                {
                    ValidateExpression(query.OrderByExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var orderByType = SemanticFacts.InferExpressionType(query.OrderByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (orderByType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2230",
                            "Query orderby-clause must be Integer in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.OrderByExpression, knownTypes));
                    }
                }

                if (query.ThenByExpression is not null)
                {
                    ValidateExpression(query.ThenByExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var thenByType = SemanticFacts.InferExpressionType(query.ThenByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (thenByType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2230",
                            "Query orderby-clause must be Integer in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.ThenByExpression, knownTypes));
                    }
                }

                if (query.GroupExpression is not null && query.GroupByExpression is not null)
                {
                    ValidateExpression(query.GroupExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    ValidateExpression(query.GroupByExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var groupKeyType = SemanticFacts.InferExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (groupKeyType != TypeSymbol.Integer && groupKeyType != TypeSymbol.String)
                    {
                        diagnostics.Report(
                            "ILC2233",
                            "Query group-by keys must be Integer or String in the current bootstrap compiler.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.GroupByExpression, knownTypes));
                    }
                }
                else
                {
                    ValidateExpression(query.SelectExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }
                if (query.IntoIdentifier is not null && query.ContinuationSelectExpression is not null)
                {
                    var continuationRangeType =
                        query.GroupExpression is not null && query.GroupByExpression is not null
                            ? SemanticFacts.ResolveTypeReference(
                                $"Grouping<{SemanticFacts.InferExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}, {SemanticFacts.InferExpressionType(query.GroupExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}>",
                                knownTypes)
                                ?? new TypeSymbol(
                                    $"Grouping<{SemanticFacts.InferExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}, {SemanticFacts.InferExpressionType(query.GroupExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}>",
                                    true)
                            : SemanticFacts.InferExpressionType(query.SelectExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var continuationLocals = new Dictionary<string, TypeSymbol>(locals, StringComparer.Ordinal)
                    {
                        [query.IntoIdentifier.Text] = continuationRangeType
                    };

                    if (query.ContinuationLetExpression is not null && query.ContinuationLetIdentifier is not null)
                    {
                        ValidateExpression(query.ContinuationLetExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationLetType = SemanticFacts.InferExpressionType(query.ContinuationLetExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        continuationLocals[query.ContinuationLetIdentifier.Text] = continuationLetType;
                    }

                    if (query.ContinuationPredicateExpression is not null)
                    {
                        ValidateExpression(query.ContinuationPredicateExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationPredicateType = SemanticFacts.InferExpressionType(query.ContinuationPredicateExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        if (continuationPredicateType != TypeSymbol.Boolean)
                        {
                            diagnostics.Report(
                                "ILC2227",
                                "Query where-clause must be Boolean.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(query.ContinuationPredicateExpression, knownTypes));
                        }
                    }

                    if (query.ContinuationOrderByExpression is not null)
                    {
                        ValidateExpression(query.ContinuationOrderByExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationOrderByType = SemanticFacts.InferExpressionType(query.ContinuationOrderByExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        if (continuationOrderByType != TypeSymbol.Integer)
                        {
                            diagnostics.Report(
                                "ILC2230",
                                "Query orderby-clause must be Integer in the current bootstrap compiler.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(query.ContinuationOrderByExpression, knownTypes));
                        }
                    }

                    if (query.ContinuationThenByExpression is not null)
                    {
                        ValidateExpression(query.ContinuationThenByExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationThenByType = SemanticFacts.InferExpressionType(query.ContinuationThenByExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        if (continuationThenByType != TypeSymbol.Integer)
                        {
                            diagnostics.Report(
                                "ILC2230",
                                "Query orderby-clause must be Integer in the current bootstrap compiler.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(query.ContinuationThenByExpression, knownTypes));
                        }
                    }

                    ValidateExpression(query.ContinuationSelectExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                if (query.TakeExpression is not null)
                {
                    ValidateExpression(query.TakeExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var takeType = SemanticFacts.InferExpressionType(query.TakeExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (takeType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2228",
                            "Query take-clause must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.TakeExpression, knownTypes));
                    }
                }

                if (query.SkipExpression is not null)
                {
                    ValidateExpression(query.SkipExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var skipType = SemanticFacts.InferExpressionType(query.SkipExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (skipType != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2229",
                            "Query skip-clause must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(query.SkipExpression, knownTypes));
                    }
                }
                break;
            case NewExpressionSyntax newExpression:
                var resolvedConstructedType = SemanticFacts.ResolveTypeReference(newExpression.TypeName.ToDisplayString(), knownTypes);
                if (resolvedConstructedType is null)
                {
                    diagnostics.Report(
                        "ILC2115",
                        $"Unknown constructed type '{newExpression.TypeName.ToDisplayString()}'.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(newExpression.TypeName, knownTypes));
                    break;
                }

                var constructor = SemanticFacts.ResolveConstructor(newExpression.TypeName, newExpression.Arguments.Count, knownTypes, knownMethods);
                if (constructor is null &&
                    HasDeclaredConstructors(newExpression.TypeName, knownTypes, knownMethods))
                {
                    diagnostics.Report(
                        "ILC2116",
                        $"No constructor for '{newExpression.TypeName.ToDisplayString()}' matches arity {newExpression.Arguments.Count}.",
                        DiagnosticSeverity.Error,
                        GetReferenceDiagnosticSpan(newExpression.TypeName, knownTypes));
                }

                if (constructor is not null)
                {
                    for (var argumentIndex = 0; argumentIndex < newExpression.Arguments.Count && argumentIndex < constructor.Parameters.Count; argumentIndex++)
                    {
                        ValidateExpressionForExpectedType(
                            newExpression.Arguments[argumentIndex].Expression,
                            constructor.Parameters[argumentIndex].Type,
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
                else
                {
                    foreach (var argument in newExpression.Arguments)
                    {
                        ValidateExpression(argument.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }
                }

                break;
            case ProjectorExpressionSyntax projector:
                foreach (var member in projector.Members)
                {
                    ValidateExpression(member.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
        if (expectedType is null)
        {
            return;
        }

        var actualType = SemanticFacts.InferExpressionType(expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (!IsAssignableTo(actualType, expectedType, knownTypes))
        {
            diagnostics.Report(
                "ILC2240",
                $"Cannot assign expression of type '{actualType.Name}' to target type '{expectedType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(expression, knownTypes));
        }
    }

    private static bool IsAssignableTo(TypeSymbol sourceType, TypeSymbol targetType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (sourceType == targetType || sourceType.Name == targetType.Name)
        {
            return true;
        }

        if (sourceType is TypeParameterSymbol || targetType is TypeParameterSymbol)
        {
            return true;
        }

        if (sourceType == TypeSymbol.Integer && targetType.Name.EndsWith("[]", StringComparison.Ordinal))
        {
            return true;
        }

        if (IsCompatibleArrayAssignment(sourceType.Name, targetType.Name))
        {
            return true;
        }

        if (IsOpenGenericAssignment(sourceType, targetType))
        {
            return true;
        }

        if (IsGenericInterfaceAssignment(sourceType, targetType, knownTypes))
        {
            return true;
        }

        if ((sourceType == TypeSymbol.Boolean && targetType == TypeSymbol.Integer) ||
            (sourceType == TypeSymbol.Integer && targetType == TypeSymbol.Boolean))
        {
            return true;
        }

        if (targetType.IsReferenceType)
        {
            return SemanticFacts.IsCompatibleReferenceType(sourceType, targetType, knownTypes);
        }

        return false;
    }

    private static bool IsGenericInterfaceAssignment(TypeSymbol sourceType, TypeSymbol targetType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var targetGenericStart = targetType.Name.IndexOf('<', StringComparison.Ordinal);
        if (targetGenericStart <= 0)
        {
            return false;
        }

        var targetDefinitionName = targetType.Name[..targetGenericStart];
        var sourceCandidates = knownTypes
            .OfType<NamedTypeSymbol>()
            .Where(type => type.Name == sourceType.Name || type.Name.StartsWith(sourceType.Name + "<", StringComparison.Ordinal))
            .ToArray();
        if (SemanticFacts.ResolveTypeReference(sourceType.Name, knownTypes) is NamedTypeSymbol resolvedSource &&
            !sourceCandidates.Contains(resolvedSource))
        {
            sourceCandidates = [.. sourceCandidates, resolvedSource];
        }

        return sourceCandidates.Any(sourceNamedType => sourceNamedType.InterfaceTypes.Any(interfaceType =>
        {
            var interfaceGenericStart = interfaceType.Name.IndexOf('<', StringComparison.Ordinal);
            return interfaceGenericStart > 0 && interfaceType.Name[..interfaceGenericStart] == targetDefinitionName;
        }));
    }

    private static bool IsCompatibleArrayAssignment(string sourceName, string targetName)
    {
        if (!sourceName.EndsWith(']') || !targetName.EndsWith(']'))
        {
            return false;
        }

        var sourceBracket = sourceName.IndexOf('[', StringComparison.Ordinal);
        var targetBracket = targetName.IndexOf('[', StringComparison.Ordinal);
        if (sourceBracket <= 0 || targetBracket <= 0 || sourceName[..sourceBracket] != targetName[..targetBracket])
        {
            return false;
        }

        var sourceRank = sourceName[sourceBracket..].Count(ch => ch == ',') + 1;
        var targetRank = targetName[targetBracket..].Count(ch => ch == ',') + 1;
        return sourceRank == targetRank;
    }

    private static bool IsOpenGenericAssignment(TypeSymbol sourceType, TypeSymbol targetType)
    {
        var sourceGenericStart = sourceType.Name.IndexOf('<', StringComparison.Ordinal);
        var targetGenericStart = targetType.Name.IndexOf('<', StringComparison.Ordinal);
        if (sourceGenericStart > 0 && targetGenericStart > 0 &&
            sourceType.Name[..sourceGenericStart] == targetType.Name[..targetGenericStart])
        {
            return true;
        }

        if (targetType.Name == "IEnumerable" && sourceType.IsReferenceType)
        {
            return true;
        }

        if (targetType.Name.Contains('<', StringComparison.Ordinal) && sourceType.Name == targetType.Name[..targetType.Name.IndexOf('<', StringComparison.Ordinal)])
        {
            return true;
        }

        if (targetGenericStart > 0 && sourceType.Name == targetType.Name[..targetGenericStart])
        {
            return true;
        }

        if (sourceGenericStart > 0 && targetType.Name == sourceType.Name[..sourceGenericStart])
        {
            return true;
        }

        return false;
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
            LiteralExpressionSyntax literal => literal.LiteralToken.Span,
            NameExpressionSyntax name => GetReferenceDiagnosticSpan(name.Name, knownTypes),
            NewExpressionSyntax newExpression => GetReferenceDiagnosticSpan(newExpression.TypeName, knownTypes),
            NewArrayExpressionSyntax newArray => GetReferenceDiagnosticSpan(newArray.ElementTypeName, knownTypes),
            MemberAccessExpressionSyntax member => member.MemberName.Span,
            ElementAccessExpressionSyntax element => GetReferenceDiagnosticSpan(element.Target, knownTypes),
            PostfixElementAccessExpressionSyntax element => GetExpressionDiagnosticSpan(element.Target, knownTypes),
            CallExpressionSyntax call => GetExpressionDiagnosticSpan(call.Target, knownTypes),
            AssignmentExpressionSyntax assignment => GetExpressionDiagnosticSpan(assignment.Expression, knownTypes),
            CompoundAssignmentExpressionSyntax assignment => GetExpressionDiagnosticSpan(assignment.Expression, knownTypes),
            BinaryExpressionSyntax binary => binary.OperatorToken.Span,
            UnaryExpressionSyntax unary => unary.OperatorToken.Span,
            RangeExpressionSyntax range => range.RangeToken.Span,
            SetLiteralExpressionSyntax setLiteral => setLiteral.OpenBracketToken.Span,
            ProjectorExpressionSyntax projector => projector.NewKeyword.Span,
            TypeTestExpressionSyntax typeTest => typeTest.IsKeyword.Span,
            AsExpressionSyntax asExpression => asExpression.AsKeyword.Span,
            LambdaExpressionSyntax lambda => lambda.SignatureKeyword.Span,
            ParenthesizedExpressionSyntax parenthesized => GetExpressionDiagnosticSpan(parenthesized.Expression, knownTypes),
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
}
