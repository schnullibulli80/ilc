namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;
using System.Diagnostics;

public sealed partial class Binder
{
    [ThreadStatic]
    private static BindingProfiler? currentValidationProfiler;

    private static void ValidateSemantics(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<GlobalVariableSymbol> globals,
        IReadOnlyList<ConstantSymbol> constants,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics,
        BindingProfiler? profiler = null)
    {
        var previousValidationProfiler = currentValidationProfiler;
        currentValidationProfiler = profiler;
        try
        {
            using (Profile(profiler, "ValidateSemantics.DeclarationNameCollisions"))
            {
                ValidateDeclarationNameCollisions(members, diagnostics);
            }

            Dictionary<string, TypeSymbol> topLevelScope;
            using (Profile(profiler, "ValidateSemantics.BuildTopLevelScope"))
            {
                topLevelScope = BuildTopLevelScope(globals);
            }

            foreach (var member in members)
            {
                switch (member)
                {
                    case TopLevelConstantDeclarationSyntax constantDeclaration:
                        using (Profile(profiler, "ValidateSemantics.TopLevelConstants"))
                        {
                            ValidateConstantDeclarators(constantDeclaration.Declarators, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                        }
                        break;
                    case TopLevelExpressionStatementSyntax expressionStatement:
                        using (Profile(profiler, "ValidateSemantics.TopLevelExpression"))
                        {
                            ValidateExpression(expressionStatement.Expression, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                        }
                        break;
                    case TopLevelVariableDeclarationSyntax variableDeclaration:
                        using (Profile(profiler, "ValidateSemantics.TopLevelVariables"))
                        {
                            foreach (var declarator in variableDeclaration.Declarators)
                            {
                                if (declarator.Initializer is not null)
                                {
                                    ValidateExpression(declarator.Initializer, topLevelScope, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
                                }
                            }
                        }
                        break;
                    case ClassDeclarationSyntax classDeclaration:
                        using (Profile(profiler, $"ValidateSemantics.Class:{classDeclaration.Identifier.Text}"))
                        {
                            ValidateClassSemantics(classDeclaration, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, diagnostics, profiler);
                        }
                        break;
                    case InterfaceDeclarationSyntax interfaceDeclaration:
                        using (Profile(profiler, $"ValidateSemantics.Interface:{interfaceDeclaration.Identifier.Text}"))
                        {
                            ValidateInterfaceSemantics(interfaceDeclaration, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, diagnostics, profiler);
                        }
                        break;
                    case DelegateDeclarationSyntax:
                        break;
                    case EnumDeclarationSyntax:
                        break;
                }
            }
        }
        finally
        {
            currentValidationProfiler = previousValidationProfiler;
        }
    }

    private static Dictionary<string, TypeSymbol> BuildTopLevelScope(IReadOnlyList<GlobalVariableSymbol> globals)
    {
        var scope = new Dictionary<string, TypeSymbol>(SemanticFacts.NameComparer);
        foreach (var global in globals)
        {
            scope[global.Name] = global.Type;
        }

        return scope;
    }

    private static void ValidateDeclarationNameCollisions(
        IReadOnlyList<MemberSyntax> members,
        DiagnosticBag diagnostics)
    {
        var typeNames = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        var topLevelValueNames = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);

        foreach (var member in members)
        {
            switch (member)
            {
                case TopLevelVariableDeclarationSyntax variableDeclaration:
                    foreach (var declarator in variableDeclaration.Declarators)
                    {
                        ReportNameCollisionIfNeeded(topLevelValueNames, declarator.Identifier, "top-level value declarations", diagnostics, reportExactDuplicate: true);
                    }
                    break;
                case TopLevelConstantDeclarationSyntax constantDeclaration:
                    foreach (var declarator in constantDeclaration.Declarators)
                    {
                        ReportNameCollisionIfNeeded(topLevelValueNames, declarator.Identifier, "top-level value declarations", diagnostics, reportExactDuplicate: true);
                    }
                    break;
                case ClassDeclarationSyntax classDeclaration:
                    ReportTypeDeclarationNameCollisionIfNeeded(typeNames, classDeclaration.Identifier, classDeclaration.TypeParameters, diagnostics);
                    ValidateTypeParameterNameCollisions(classDeclaration.Identifier.Text, classDeclaration.TypeParameters, diagnostics);
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    ReportTypeDeclarationNameCollisionIfNeeded(typeNames, interfaceDeclaration.Identifier, interfaceDeclaration.TypeParameters, diagnostics);
                    ValidateTypeParameterNameCollisions(interfaceDeclaration.Identifier.Text, interfaceDeclaration.TypeParameters, diagnostics);
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    ReportTypeDeclarationNameCollisionIfNeeded(typeNames, delegateDeclaration.Identifier, delegateDeclaration.TypeParameters, diagnostics);
                    ValidateTypeParameterNameCollisions(delegateDeclaration.Identifier.Text, delegateDeclaration.TypeParameters, diagnostics);
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    ReportTypeDeclarationNameCollisionIfNeeded(typeNames, enumDeclaration.Identifier, null, diagnostics);
                    ValidateEnumMemberNameCollisions(enumDeclaration, diagnostics);
                    break;
            }
        }
    }

    private static void ReportTypeDeclarationNameCollisionIfNeeded(
        Dictionary<string, SyntaxToken> typeNames,
        SyntaxToken identifier,
        TypeParameterListSyntax? typeParameters,
        DiagnosticBag diagnostics)
    {
        var arity = typeParameters?.Parameters.Count ?? 0;
        var key = $"{identifier.Text}/{arity}";
        ReportNameCollisionIfNeeded(typeNames, key, identifier, "top-level type declarations", diagnostics, reportExactDuplicate: true);
    }

    private static void ValidateTypeParameterNameCollisions(
        string declarationName,
        TypeParameterListSyntax? typeParameters,
        DiagnosticBag diagnostics)
    {
        if (typeParameters is null)
        {
            return;
        }

        var names = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        foreach (var parameter in typeParameters.Parameters)
        {
            ReportNameCollisionIfNeeded(names, parameter, $"type parameters of '{declarationName}'", diagnostics, reportExactDuplicate: true);
        }
    }

    private static void ValidateEnumMemberNameCollisions(
        EnumDeclarationSyntax enumDeclaration,
        DiagnosticBag diagnostics)
    {
        var names = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        foreach (var member in enumDeclaration.Members)
        {
            ReportNameCollisionIfNeeded(names, member.Identifier, $"enum '{enumDeclaration.Identifier.Text}'", diagnostics, reportExactDuplicate: true);
        }
    }

    private static void ValidateClassSemantics(
        ClassDeclarationSyntax classDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics,
        BindingProfiler? profiler = null)
    {
        var declaredTypeName = classDeclaration.Identifier.Text;
        TypeSymbol[] typeScope;
        using (Profile(profiler, "ValidateClass.BuildTypeScope"))
        {
            typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
        }

        using (Profile(profiler, "ValidateClass.Inheritance"))
        {
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
        }

        TypeSymbol? baseType;
        IReadOnlyList<TypeSymbol> interfaceTypes;
        using (Profile(profiler, "ValidateClass.ResolveInheritanceTargets"))
        {
            (baseType, interfaceTypes) = ResolveClassInheritanceTargets(classDeclaration, typeScope);
        }

        using (Profile(profiler, "ValidateClass.NameCollisions"))
        {
            ValidateTypeMemberNameCollisions(classDeclaration.Identifier.Text, classDeclaration.Members, diagnostics);
        }

        using (Profile(profiler, "ValidateClass.InterfaceImplementations"))
        {
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
        }

        IReadOnlyList<FieldSymbol> typeFields;
        using (Profile(profiler, "ValidateClass.FindFields"))
        {
            typeFields = FindFields(knownFields, classDeclaration.Identifier.Text);
        }

        using (Profile(profiler, "ValidateClass.Properties"))
        {
            foreach (var property in classDeclaration.Members.OfType<PropertyDeclarationSyntax>())
            {
                ValidatePropertyDeclaration(property, classDeclaration.Identifier.Text, typeFields, knownTypes, diagnostics);
            }
        }

        using (Profile(profiler, "ValidateClass.Constants"))
        {
            foreach (var constant in classDeclaration.Members.OfType<ConstantDeclarationSyntax>())
            {
                ValidateConstantDeclarators(constant.Declarators, new Dictionary<string, TypeSymbol>(SemanticFacts.NameComparer), knownTypes, knownMethods, knownFields, knownConstants, knownProperties, null, diagnostics);
            }
        }

        using (Profile(profiler, "ValidateClass.Methods"))
        {
            foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                using var methodProfile = Profile(profiler, $"ValidateClass.Method:{method.Identifier.Text}");
                var locals = BuildMethodParameterLocals(method, typeScope, diagnostics);

                var boundMethod = FindMethod(knownMethods, classDeclaration.Identifier.Text, method.Identifier.Text, method.Parameters.Count);
                ValidateRoutineKeywordSemantics(method, diagnostics);
                if (boundMethod is not null && !boundMethod.IsStatic)
                {
                    locals["self"] = new TypeSymbol(classDeclaration.Identifier.Text, true);
                }

                var resultParameter = method.Parameters.FirstOrDefault(parameter => SemanticFacts.NameEquals(parameter.Identifier.Text, "Result"));
                if (resultParameter is not null && boundMethod is not null && IsResultAvailable(boundMethod))
                {
                    diagnostics.Report(
                        "ILC2241",
                        "Parameter name 'Result' is reserved for the implicit function result.",
                        DiagnosticSeverity.Error,
                        resultParameter.Identifier.Span);
                }

                if (boundMethod is not null && boundMethod.ReturnType != TypeSymbol.Void)
                {
                    locals["Result"] = boundMethod.ReturnType;
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
                    diagnostics,
                    profiler,
                    [BuildInitialMethodLocalScope(method)]);
                ValidateRequiredOutputAssignments(method.Body.Statements, boundMethod, diagnostics);
                ValidateLocalDefiniteAssignments(method.Body.Statements, typeScope, diagnostics);
            }
        }
    }

    private static List<Dictionary<string, SyntaxToken>> CreateNestedLocalScopes(
        List<Dictionary<string, SyntaxToken>> activeLocalScopes)
    {
        var nestedScopes = new List<Dictionary<string, SyntaxToken>>(activeLocalScopes)
        {
            new(SemanticFacts.NameComparer)
        };
        return nestedScopes;
    }

    private static List<Dictionary<string, SyntaxToken>> CreateNestedLocalScopesWithName(
        List<Dictionary<string, SyntaxToken>> activeLocalScopes,
        SyntaxToken identifier,
        DiagnosticBag diagnostics)
    {
        ReportLocalScopeNameCollisionIfNeeded(activeLocalScopes, identifier, diagnostics);
        var nestedScopes = CreateNestedLocalScopes(activeLocalScopes);
        nestedScopes[^1][identifier.Text] = identifier;
        return nestedScopes;
    }

    private static Dictionary<string, SyntaxToken> BuildInitialMethodLocalScope(MethodDeclarationSyntax method)
    {
        var scopeNames = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        foreach (var parameter in method.Parameters)
        {
            scopeNames[parameter.Identifier.Text] = parameter.Identifier;
        }

        return scopeNames;
    }

    private static void ValidateInterfaceSemantics(
        InterfaceDeclarationSyntax interfaceDeclaration,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        DiagnosticBag diagnostics,
        BindingProfiler? profiler = null)
    {
        TypeSymbol[] typeScope;
        using (Profile(profiler, "ValidateInterface.BuildTypeScope"))
        {
            typeScope = knownTypes.Concat(BindTypeParameters(interfaceDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
        }

        using (Profile(profiler, "ValidateInterface.BaseInterfaces"))
        {
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
        }

        using (Profile(profiler, "ValidateInterface.Members"))
        {
            ValidateTypeMemberNameCollisions(interfaceDeclaration.Identifier.Text, interfaceDeclaration.Members, diagnostics);

            foreach (var member in interfaceDeclaration.Members)
            {
                if (member is MethodDeclarationSyntax routine)
                {
                    ValidateRoutineKeywordSemantics(routine, diagnostics);
                }

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
    }

    private static void ValidateRoutineKeywordSemantics(MethodDeclarationSyntax method, DiagnosticBag diagnostics)
    {
        if (method.Keyword.Kind == SyntaxKind.FunctionKeyword && method.ReturnType is null)
        {
            diagnostics.Report(
                "ILC2245",
                $"Function '{method.Identifier.Text}' must declare a return type.",
                DiagnosticSeverity.Error,
                method.Keyword.Span);
            return;
        }

        if (method.Keyword.Kind is SyntaxKind.ProcedureKeyword or SyntaxKind.ConstructorKeyword &&
            method.ReturnType is not null)
        {
            var routineKind = method.Keyword.Kind == SyntaxKind.ConstructorKeyword ? "Constructor" : "Procedure";
            diagnostics.Report(
                "ILC2246",
                $"{routineKind} '{method.Identifier.Text}' must not declare a return type.",
                DiagnosticSeverity.Error,
                method.ReturnType.Parts[0].Span);
            return;
        }

        if (method.ReturnType is not null &&
            SemanticFacts.NameEquals(method.ReturnType.ToDisplayString(), "Void"))
        {
            diagnostics.Report(
                "ILC2247",
                $"Routine '{method.Identifier.Text}' must not use 'Void' as an explicit source return type. Use 'procedure' or 'method' without a return type instead.",
                DiagnosticSeverity.Error,
                method.ReturnType.Parts[0].Span);
        }
    }

    private static Dictionary<string, TypeSymbol> BuildMethodParameterLocals(
        MethodDeclarationSyntax method,
        IReadOnlyList<TypeSymbol> typeScope,
        DiagnosticBag diagnostics)
    {
        var locals = new Dictionary<string, TypeSymbol>(SemanticFacts.NameComparer);
        var parameterNames = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        foreach (var parameter in method.Parameters)
        {
            ReportCaseOnlyNameCollisionIfNeeded(
                parameterNames,
                parameter.Identifier,
                $"parameter list of method '{method.Identifier.Text}'",
                diagnostics,
                reportExactDuplicate: true);
            locals[parameter.Identifier.Text] = BindType(parameter.TypeName, typeScope);
        }

        return locals;
    }

    private static void ValidateTypeMemberNameCollisions(
        string typeName,
        IReadOnlyList<TypeMemberSyntax> members,
        DiagnosticBag diagnostics)
    {
        var valueMembers = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        var methodMembersByArity = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        var methodSignatures = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);

        foreach (var member in members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var declarator in field.Declarators)
                    {
                        ReportNameCollisionIfNeeded(valueMembers, declarator.Identifier, $"type '{typeName}'", diagnostics, reportExactDuplicate: true);
                    }
                    break;
                case ConstantDeclarationSyntax constant:
                    foreach (var declarator in constant.Declarators)
                    {
                        ReportNameCollisionIfNeeded(valueMembers, declarator.Identifier, $"type '{typeName}'", diagnostics, reportExactDuplicate: true);
                    }
                    break;
                case PropertyDeclarationSyntax property:
                    ReportNameCollisionIfNeeded(valueMembers, property.Identifier, $"type '{typeName}'", diagnostics, reportExactDuplicate: true);
                    break;
                case MethodDeclarationSyntax method:
                    ReportCaseOnlyMethodCollisionIfNeeded(methodMembersByArity, method, typeName, diagnostics);
                    ReportDuplicateMethodSignatureIfNeeded(methodSignatures, method, typeName, diagnostics);
                    break;
            }
        }
    }

    private static void ReportCaseOnlyMethodCollisionIfNeeded(
        Dictionary<string, SyntaxToken> methodMembersByArity,
        MethodDeclarationSyntax method,
        string typeName,
        DiagnosticBag diagnostics)
    {
        if (method.Keyword.Kind == SyntaxKind.ConstructorKeyword)
        {
            return;
        }

        var signatureKey = $"{method.Identifier.Text}/{method.Parameters.Count}";
        ReportCaseOnlyNameCollisionIfNeeded(methodMembersByArity, signatureKey, method.Identifier, $"method overload set of type '{typeName}'", diagnostics);
    }

    private static void ReportDuplicateMethodSignatureIfNeeded(
        Dictionary<string, SyntaxToken> methodSignatures,
        MethodDeclarationSyntax method,
        string typeName,
        DiagnosticBag diagnostics)
    {
        var signatureKey = GetMethodSignatureKey(method);
        if (!methodSignatures.TryGetValue(signatureKey, out _))
        {
            methodSignatures[signatureKey] = method.Identifier;
            return;
        }

        diagnostics.Report(
            "ILC2244",
            $"Duplicate method signature '{GetMethodSignatureDisplay(method)}' in type '{typeName}'. ILC names are case-insensitive.",
            DiagnosticSeverity.Error,
            method.Identifier.Span);
    }

    private static string GetMethodSignatureKey(MethodDeclarationSyntax method)
    {
        var methodName = method.Keyword.Kind == SyntaxKind.ConstructorKeyword
            ? ".ctor"
            : method.Identifier.Text;
        var parameterTypes = string.Join(
            ";",
            method.Parameters.Select(parameter => $"{BindParameterPassingKind(parameter.ModifierKeyword)}:{parameter.TypeName.ToDisplayString()}"));
        return $"{methodName}({parameterTypes})";
    }

    private static string GetMethodSignatureDisplay(MethodDeclarationSyntax method)
    {
        var methodName = method.Keyword.Kind == SyntaxKind.ConstructorKeyword
            ? "constructor"
            : method.Identifier.Text;
        return $"{methodName}({string.Join(", ", method.Parameters.Select(parameter => parameter.TypeName.ToDisplayString()))})";
    }

    private static void ReportCaseOnlyNameCollisionIfNeeded(
        Dictionary<string, SyntaxToken> knownNames,
        SyntaxToken identifier,
        string scopeDescription,
        DiagnosticBag diagnostics,
        bool reportExactDuplicate = false) =>
        ReportNameCollisionIfNeeded(knownNames, identifier.Text, identifier, scopeDescription, diagnostics, reportExactDuplicate);

    private static void ReportCaseOnlyNameCollisionIfNeeded(
        Dictionary<string, SyntaxToken> knownNames,
        string key,
        SyntaxToken identifier,
        string scopeDescription,
        DiagnosticBag diagnostics,
        bool reportExactDuplicate = false) =>
        ReportNameCollisionIfNeeded(knownNames, key, identifier, scopeDescription, diagnostics, reportExactDuplicate);

    private static void ReportNameCollisionIfNeeded(
        Dictionary<string, SyntaxToken> knownNames,
        SyntaxToken identifier,
        string scopeDescription,
        DiagnosticBag diagnostics,
        bool reportExactDuplicate = false) =>
        ReportNameCollisionIfNeeded(knownNames, identifier.Text, identifier, scopeDescription, diagnostics, reportExactDuplicate);

    private static void ReportNameCollisionIfNeeded(
        Dictionary<string, SyntaxToken> knownNames,
        string key,
        SyntaxToken identifier,
        string scopeDescription,
        DiagnosticBag diagnostics,
        bool reportExactDuplicate = false)
    {
        if (knownNames.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.Text, identifier.Text, StringComparison.Ordinal))
            {
                if (reportExactDuplicate)
                {
                    diagnostics.Report(
                        "ILC2244",
                        $"Duplicate name '{identifier.Text}' in {scopeDescription}. ILC names are case-insensitive.",
                        DiagnosticSeverity.Error,
                        identifier.Span);
                }
            }
            else
            {
                diagnostics.Report(
                    "ILC2243",
                    $"Name '{identifier.Text}' differs only by case from '{existing.Text}' in {scopeDescription}. ILC names are case-insensitive.",
                    DiagnosticSeverity.Warning,
                    identifier.Span);
            }

            return;
        }

        knownNames[key] = identifier;
    }

    private static void ReportLocalScopeNameCollisionIfNeeded(
        List<Dictionary<string, SyntaxToken>> activeLocalScopes,
        SyntaxToken identifier,
        DiagnosticBag diagnostics)
    {
        for (var scopeIndex = activeLocalScopes.Count - 1; scopeIndex >= 0; scopeIndex--)
        {
            var scope = activeLocalScopes[scopeIndex];
            if (!scope.TryGetValue(identifier.Text, out var existingInScope))
            {
                continue;
            }

            if (string.Equals(existingInScope.Text, identifier.Text, StringComparison.Ordinal))
            {
                diagnostics.Report(
                    "ILC2244",
                    $"Duplicate local name '{identifier.Text}' in an active scope. ILC names are case-insensitive.",
                    DiagnosticSeverity.Error,
                    identifier.Span);
            }
            else
            {
                diagnostics.Report(
                    "ILC2243",
                    $"Name '{identifier.Text}' differs only by case from '{existingInScope.Text}' in local scope. ILC names are case-insensitive.",
                    DiagnosticSeverity.Warning,
                    identifier.Span);
            }

            return;
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
                var initializerType = InferValidationExpressionType(declarator.Initializer, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        var field = typeFields.FirstOrDefault(candidate => SemanticFacts.NameEquals(candidate.Name, fieldName));
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
        DiagnosticBag diagnostics,
        BindingProfiler? profiler = null,
        List<Dictionary<string, SyntaxToken>>? activeLocalScopes = null)
    {
        activeLocalScopes ??= [new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer)];
        var localScopeNames = activeLocalScopes[^1];
        foreach (var statement in statements)
        {
            using var statementProfile = Profile(profiler, $"ValidateStatements.{statement.GetType().Name}");
            switch (statement)
            {
                case BlockStatementSyntax block:
                    ValidateStatements(block.Statements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, CreateNestedLocalScopes(activeLocalScopes));
                    break;
                case LocalVariableDeclarationStatementSyntax localVariable:
                    foreach (var declarator in localVariable.Declarators)
                    {
                        if (IsResultAvailable(currentMethod) && SemanticFacts.NameEquals(declarator.Identifier.Text, "Result"))
                        {
                            diagnostics.Report(
                                "ILC2241",
                                "Local variable name 'Result' is reserved for the implicit function result.",
                                DiagnosticSeverity.Error,
                                declarator.Identifier.Span);
                        }
                        else
                        {
                            ReportLocalScopeNameCollisionIfNeeded(activeLocalScopes, declarator.Identifier, diagnostics);
                        }

                        var declaredType = declarator.TypeName is not null ? BindType(declarator.TypeName, knownTypes) : null;
                        TypeSymbol? initializerType = null;
                        if (declarator.Initializer is not null)
                        {
                            initializerType = ValidateExpressionForExpectedType(
                                declarator.Initializer,
                                declaredType,
                                locals,
                                knownTypes,
                                knownMethods,
                                knownFields,
                                knownConstants,
                                knownProperties,
                                currentMethod,
                                diagnostics);
                        }

                        locals[declarator.Identifier.Text] = declaredType ?? initializerType ?? TypeSymbol.Unknown;
                        localScopeNames[declarator.Identifier.Text] = declarator.Identifier;
                    }
                    break;
                case ReturnStatementSyntax returnStatement:
                    ValidateReturnStatement(returnStatement, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
                    ValidateStatements([ifStatement.ThenStatement], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    if (ifStatement.ElseStatement is not null)
                    {
                        ValidateStatements([ifStatement.ElseStatement], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    }
                    break;
                case WhileStatementSyntax whileStatement:
                    ValidateExpression(whileStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    ValidateStatements([whileStatement.Body], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics, profiler, activeLocalScopes);
                    break;
                case RepeatStatementSyntax repeatStatement:
                    ValidateStatements(repeatStatement.Statements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics, profiler, activeLocalScopes);
                    ValidateExpression(repeatStatement.Condition, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                case ForStatementSyntax forStatement:
                    var forLoopLocals = locals;
                    var forLoopScopes = activeLocalScopes;
                    TypeSymbol? loopType = null;
                    if (forStatement.VarKeyword is not null)
                    {
                        forLoopLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                        {
                            [forStatement.Identifier.Text] = TypeSymbol.Integer
                        };
                        forLoopScopes = CreateNestedLocalScopesWithName(activeLocalScopes, forStatement.Identifier, diagnostics);
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

                    if (InferValidationExpressionType(forStatement.LowerBound, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2138",
                            $"For-loop lower bound for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.LowerBound, knownTypes));
                    }

                    if (InferValidationExpressionType(forStatement.UpperBound, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2139",
                            $"For-loop upper bound for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.UpperBound, knownTypes));
                    }

                    if (forStatement.StepExpression is not null &&
                        InferValidationExpressionType(forStatement.StepExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) != TypeSymbol.Integer)
                    {
                        diagnostics.Report(
                            "ILC2189",
                            $"For-loop step for '{forStatement.Identifier.Text}' must be Integer.",
                            DiagnosticSeverity.Error,
                            GetExpressionDiagnosticSpan(forStatement.StepExpression, knownTypes));
                    }

                    ValidateStatements([forStatement.Body], forLoopLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics, profiler, forLoopScopes);
                    break;
                case ForeachStatementSyntax foreachStatement:
                    ValidateExpression(foreachStatement.Collection, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var collectionType = InferValidationExpressionType(foreachStatement.Collection, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var foreachScopes = activeLocalScopes;
                    TypeSymbol foreachType;
                    if (foreachStatement.VarKeyword is not null)
                    {
                        foreachType = elementType!;
                        foreachLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                        {
                            [foreachStatement.Identifier.Text] = foreachType
                        };
                        foreachScopes = CreateNestedLocalScopesWithName(activeLocalScopes, foreachStatement.Identifier, diagnostics);
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

                    ValidateStatements([foreachStatement.Body], foreachLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, true, diagnostics, profiler, foreachScopes);
                    break;
                case WithStatementSyntax withStatement:
                    ValidateExpression(withStatement.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var rewrittenWithBody = RewriteWithStatement(withStatement.Body, withStatement.Receiver, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    ValidateStatements([rewrittenWithBody], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    break;
                case CaseStatementSyntax caseStatement:
                    ValidateExpression(caseStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var caseExpressionType = InferValidationExpressionType(caseStatement.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                            if (label is not RangeExpressionSyntax)
                            {
                                ValidateExpression(label, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            }

                            ValidateCaseLabel(label, caseExpressionType, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        }

                        if (clause.Guard is not null)
                        {
                            ValidateExpression(clause.Guard, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                            var guardType = InferValidationExpressionType(clause.Guard, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                            if (guardType != TypeSymbol.Boolean)
                            {
                                diagnostics.Report(
                                    "ILC2192",
                                    "Case clause guard must be Boolean.",
                                    DiagnosticSeverity.Error,
                                    GetExpressionDiagnosticSpan(clause.Guard, knownTypes));
                            }
                        }

                        ValidateStatements([clause.Body], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    }

                    ValidateStatements(caseStatement.ElseStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    break;
                case MatchStatementSyntax matchStatement:
                    ValidateExpression(matchStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var matchExpressionType = InferValidationExpressionType(matchStatement.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                        var armScopes = activeLocalScopes;
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
                                armLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                                {
                                    [arm.Identifier.Text] = armType
                                };
                                armScopes = CreateNestedLocalScopesWithName(activeLocalScopes, arm.Identifier, diagnostics);
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
                            var guardType = InferValidationExpressionType(arm.Guard, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                            if (guardType != TypeSymbol.Boolean)
                            {
                                diagnostics.Report(
                                    "ILC2179",
                                    "Match arm guard must be Boolean.",
                                    DiagnosticSeverity.Error,
                                    GetExpressionDiagnosticSpan(arm.Guard, knownTypes));
                            }
                        }

                        ValidateStatements([arm.Body], armLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, armScopes);
                    }

                    ValidateStatements(matchStatement.ElseStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
                    break;
                case TryStatementSyntax tryStatement:
                    ValidateStatements(tryStatement.TryStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, inExceptionHandler, inLoop, diagnostics, profiler, activeLocalScopes);
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

                            var clauseLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                            {
                                [clause.Identifier.Text] = clauseType
                            };
                            var clauseScopes = CreateNestedLocalScopesWithName(activeLocalScopes, clause.Identifier, diagnostics);
                            ValidateStatements([clause.Body], clauseLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, true, inLoop, diagnostics, profiler, clauseScopes);
                        }

                        ValidateStatements(tryStatement.ExceptStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, true, inLoop, diagnostics, profiler, activeLocalScopes);
                    }

                    if (tryStatement.FinallyKeyword is not null)
                    {
                        ValidateStatements(tryStatement.FinallyStatements, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, false, inLoop, diagnostics, profiler, activeLocalScopes);
                    }
                    break;
            }
        }
    }

    private static void ValidateReturnStatement(
        ReturnStatementSyntax returnStatement,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (returnStatement.Expression is null)
        {
            return;
        }

        if (currentMethod is null || currentMethod.ReturnType == TypeSymbol.Void)
        {
            diagnostics.Report(
                "ILC2250",
                $"'{returnStatement.ReturnKeyword.Text}' with a value is only valid inside functions or value-returning methods.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(returnStatement.Expression, knownTypes));
            ValidateExpression(returnStatement.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        ValidateExpressionForExpectedType(
            returnStatement.Expression,
            currentMethod.ReturnType,
            locals,
            knownTypes,
            knownMethods,
            knownFields,
            knownConstants,
            knownProperties,
            currentMethod,
            diagnostics);
    }

    private static bool ContainsRoutineExit(IReadOnlyList<StatementSyntax> statements)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                    return true;
                case BlockStatementSyntax block when ContainsRoutineExit(block.Statements):
                    return true;
                case IfStatementSyntax ifStatement when ContainsRoutineExit([ifStatement.ThenStatement]) || (ifStatement.ElseStatement is not null && ContainsRoutineExit([ifStatement.ElseStatement])):
                    return true;
                case WhileStatementSyntax whileStatement when ContainsRoutineExit([whileStatement.Body]):
                    return true;
                case RepeatStatementSyntax repeatStatement when ContainsRoutineExit(repeatStatement.Statements):
                    return true;
                case ForStatementSyntax forStatement when ContainsRoutineExit([forStatement.Body]):
                    return true;
                case ForeachStatementSyntax foreachStatement when ContainsRoutineExit([foreachStatement.Body]):
                    return true;
                case CaseStatementSyntax caseStatement when caseStatement.Clauses.Any(clause => ContainsRoutineExit([clause.Body])) || ContainsRoutineExit(caseStatement.ElseStatements):
                    return true;
                case MatchStatementSyntax matchStatement when matchStatement.Arms.Any(arm => ContainsRoutineExit([arm.Body])) || ContainsRoutineExit(matchStatement.ElseStatements):
                    return true;
                case TryStatementSyntax tryStatement when ContainsRoutineExit(tryStatement.TryStatements) || ContainsRoutineExit(tryStatement.ExceptStatements) || ContainsRoutineExit(tryStatement.FinallyStatements):
                    return true;
            }
        }

        return false;
    }

    private sealed record OutputAssignmentFlow(HashSet<string> Assigned, bool CanContinue);

    private sealed record LocalAssignmentFlow(HashSet<string> Assigned, bool CanContinue);

    private sealed class LocalAssignmentScope(HashSet<string> declared)
    {
        public HashSet<string> Declared { get; } = declared;
    }

    private static void ValidateRequiredOutputAssignments(
        IReadOnlyList<StatementSyntax> statements,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (currentMethod is null)
        {
            return;
        }

        var methodDeclaration = currentMethod.Declaration;
        if (methodDeclaration is null)
        {
            return;
        }

        var required = new Dictionary<string, TextSpan>(SemanticFacts.NameComparer);
        foreach (var parameter in currentMethod.Parameters)
        {
            if (parameter.PassingKind == ParameterPassingKind.Out &&
                parameter.Type is not TypeParameterSymbol)
            {
                required[parameter.Name] = methodDeclaration.Parameters
                    .FirstOrDefault(parameterSyntax => SemanticFacts.NameEquals(parameterSyntax.Identifier.Text, parameter.Name))
                    ?.Identifier.Span ?? methodDeclaration.Identifier.Span;
            }
        }

        if (IsResultAvailable(currentMethod))
        {
            required["Result"] = methodDeclaration.Identifier.Span;
        }

        if (required.Count == 0)
        {
            return;
        }

        using var profile = Profile(currentValidationProfiler, "ValidateRequiredOutputAssignments");
        var flow = AnalyzeRequiredOutputAssignments(statements, new HashSet<string>(SemanticFacts.NameComparer), required, diagnostics);
        if (flow.CanContinue)
        {
            ReportMissingRequiredOutputAssignments(flow.Assigned, required, methodDeclaration.Identifier.Span, diagnostics);
        }
    }

    private static OutputAssignmentFlow AnalyzeRequiredOutputAssignments(
        IReadOnlyList<StatementSyntax> statements,
        HashSet<string> assigned,
        IReadOnlyDictionary<string, TextSpan> required,
        DiagnosticBag diagnostics)
    {
        var current = new HashSet<string>(assigned, SemanticFacts.NameComparer);
        var canContinue = true;
        foreach (var statement in statements)
        {
            if (!canContinue)
            {
                break;
            }

            var flow = AnalyzeRequiredOutputAssignment(statement, current, required, diagnostics);
            current = flow.Assigned;
            canContinue = flow.CanContinue;
        }

        return new OutputAssignmentFlow(current, canContinue);
    }

    private static OutputAssignmentFlow AnalyzeRequiredOutputAssignment(
        StatementSyntax statement,
        HashSet<string> assigned,
        IReadOnlyDictionary<string, TextSpan> required,
        DiagnosticBag diagnostics)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                return AnalyzeRequiredOutputAssignments(block.Statements, assigned, required, diagnostics);
            case LocalVariableDeclarationStatementSyntax localDeclaration:
            {
                var localAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                foreach (var declarator in localDeclaration.Declarators)
                {
                    if (declarator.Initializer is not null)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(declarator.Initializer, localAssigned, required);
                    }
                }

                return new OutputAssignmentFlow(localAssigned, true);
            }
            case ExpressionStatementSyntax expressionStatement:
            {
                var expressionAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                CollectRequiredOutputAssignmentsFromExpression(expressionStatement.Expression, expressionAssigned, required);
                return new OutputAssignmentFlow(expressionAssigned, true);
            }
            case ReturnStatementSyntax returnStatement:
            {
                var exitAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                if (returnStatement.Expression is not null)
                {
                    CollectRequiredOutputAssignmentsFromExpression(returnStatement.Expression, exitAssigned, required);
                    if (required.ContainsKey("Result"))
                    {
                        exitAssigned.Add("Result");
                    }
                }

                ReportMissingRequiredOutputAssignments(exitAssigned, required, returnStatement.ReturnKeyword.Span, diagnostics);
                return new OutputAssignmentFlow(exitAssigned, false);
            }
            case IfStatementSyntax ifStatement:
            {
                CollectRequiredOutputAssignmentsFromExpression(ifStatement.Condition, assigned, required);
                var thenFlow = AnalyzeRequiredOutputAssignments([ifStatement.ThenStatement], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                var elseFlow = ifStatement.ElseStatement is not null
                    ? AnalyzeRequiredOutputAssignments([ifStatement.ElseStatement], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics)
                    : new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return MergeBranchOutputAssignmentFlows(thenFlow, elseFlow);
            }
            case WhileStatementSyntax whileStatement:
                CollectRequiredOutputAssignmentsFromExpression(whileStatement.Condition, assigned, required);
                _ = AnalyzeRequiredOutputAssignments([whileStatement.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case RepeatStatementSyntax repeatStatement:
                _ = AnalyzeRequiredOutputAssignments(repeatStatement.Statements, new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                CollectRequiredOutputAssignmentsFromExpression(repeatStatement.Condition, assigned, required);
                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case ForStatementSyntax forStatement:
                CollectRequiredOutputAssignmentsFromExpression(forStatement.LowerBound, assigned, required);
                CollectRequiredOutputAssignmentsFromExpression(forStatement.UpperBound, assigned, required);
                if (forStatement.StepExpression is not null)
                {
                    CollectRequiredOutputAssignmentsFromExpression(forStatement.StepExpression, assigned, required);
                }
                _ = AnalyzeRequiredOutputAssignments([forStatement.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case ForeachStatementSyntax foreachStatement:
                CollectRequiredOutputAssignmentsFromExpression(foreachStatement.Collection, assigned, required);
                _ = AnalyzeRequiredOutputAssignments([foreachStatement.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case WithStatementSyntax withStatement:
                CollectRequiredOutputAssignmentsFromExpression(withStatement.Receiver, assigned, required);
                return AnalyzeRequiredOutputAssignments([withStatement.Body], assigned, required, diagnostics);
            case CaseStatementSyntax caseStatement:
            {
                CollectRequiredOutputAssignmentsFromExpression(caseStatement.Expression, assigned, required);
                OutputAssignmentFlow? mergedFlow = null;
                foreach (var clause in caseStatement.Clauses)
                {
                    foreach (var label in clause.Labels)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(label, assigned, required);
                    }

                    if (clause.Guard is not null)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(clause.Guard, assigned, required);
                    }

                    var clauseFlow = AnalyzeRequiredOutputAssignments([clause.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                    mergedFlow = mergedFlow is null ? clauseFlow : MergeBranchOutputAssignmentFlows(mergedFlow, clauseFlow);
                }

                var elseFlow = caseStatement.ElseStatements.Count > 0
                    ? AnalyzeRequiredOutputAssignments(caseStatement.ElseStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics)
                    : new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return mergedFlow is null ? elseFlow : MergeBranchOutputAssignmentFlows(mergedFlow, elseFlow);
            }
            case MatchStatementSyntax matchStatement:
            {
                CollectRequiredOutputAssignmentsFromExpression(matchStatement.Expression, assigned, required);
                OutputAssignmentFlow? mergedFlow = null;
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(label, assigned, required);
                    }

                    if (arm.Guard is not null)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(arm.Guard, assigned, required);
                    }

                    var armFlow = AnalyzeRequiredOutputAssignments([arm.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                    mergedFlow = mergedFlow is null ? armFlow : MergeBranchOutputAssignmentFlows(mergedFlow, armFlow);
                }

                var elseFlow = matchStatement.ElseStatements.Count > 0
                    ? AnalyzeRequiredOutputAssignments(matchStatement.ElseStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics)
                    : new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return mergedFlow is null ? elseFlow : MergeBranchOutputAssignmentFlows(mergedFlow, elseFlow);
            }
            case TryStatementSyntax tryStatement:
            {
                var tryFlow = AnalyzeRequiredOutputAssignments(tryStatement.TryStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                OutputAssignmentFlow? exceptionFlow = null;
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    var clauseFlow = AnalyzeRequiredOutputAssignments([clause.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                    exceptionFlow = exceptionFlow is null ? clauseFlow : MergeBranchOutputAssignmentFlows(exceptionFlow, clauseFlow);
                }

                if (tryStatement.ExceptStatements.Count > 0)
                {
                    var exceptStatementsFlow = AnalyzeRequiredOutputAssignments(tryStatement.ExceptStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), required, diagnostics);
                    exceptionFlow = exceptionFlow is null ? exceptStatementsFlow : MergeBranchOutputAssignmentFlows(exceptionFlow, exceptStatementsFlow);
                }

                if (tryStatement.ExceptKeyword is not null && exceptionFlow is null)
                {
                    exceptionFlow = new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                }

                var merged = exceptionFlow is null
                    ? tryFlow
                    : MergeBranchOutputAssignmentFlows(tryFlow, exceptionFlow);
                if (tryStatement.FinallyStatements.Count == 0)
                {
                    return merged;
                }

                var finallyFlow = AnalyzeRequiredOutputAssignments(tryStatement.FinallyStatements, new HashSet<string>(merged.Assigned, SemanticFacts.NameComparer), required, diagnostics);
                return new OutputAssignmentFlow(finallyFlow.Assigned, merged.CanContinue && finallyFlow.CanContinue);
            }
            case RaiseStatementSyntax raiseStatement:
                if (raiseStatement.Expression is not null)
                {
                    CollectRequiredOutputAssignmentsFromExpression(raiseStatement.Expression, assigned, required);
                }

                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), false);
            default:
                return new OutputAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
        }
    }

    private static OutputAssignmentFlow MergeBranchOutputAssignmentFlows(OutputAssignmentFlow left, OutputAssignmentFlow right)
    {
        if (!left.CanContinue && !right.CanContinue)
        {
            return new OutputAssignmentFlow(new HashSet<string>(left.Assigned, SemanticFacts.NameComparer), false);
        }

        if (!left.CanContinue)
        {
            return new OutputAssignmentFlow(new HashSet<string>(right.Assigned, SemanticFacts.NameComparer), true);
        }

        if (!right.CanContinue)
        {
            return new OutputAssignmentFlow(new HashSet<string>(left.Assigned, SemanticFacts.NameComparer), true);
        }

        var assigned = new HashSet<string>(left.Assigned, SemanticFacts.NameComparer);
        assigned.IntersectWith(right.Assigned);
        return new OutputAssignmentFlow(assigned, true);
    }

    private static void CollectRequiredOutputAssignmentsFromExpression(
        ExpressionSyntax expression,
        HashSet<string> assigned,
        IReadOnlyDictionary<string, TextSpan> required)
    {
        switch (expression)
        {
            case AssignmentExpressionSyntax assignment:
                if (TryGetRequiredOutputAssignmentTarget(assignment.Target, required) is { } assignedName)
                {
                    assigned.Add(assignedName);
                }

                CollectRequiredOutputAssignmentsFromExpression(assignment.Expression, assigned, required);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                CollectRequiredOutputAssignmentsFromExpression(assignment.Target, assigned, required);
                CollectRequiredOutputAssignmentsFromExpression(assignment.Expression, assigned, required);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                CollectRequiredOutputAssignmentsFromExpression(parenthesized.Expression, assigned, required);
                break;
            case BinaryExpressionSyntax binary:
                CollectRequiredOutputAssignmentsFromExpression(binary.Left, assigned, required);
                CollectRequiredOutputAssignmentsFromExpression(binary.Right, assigned, required);
                break;
            case CallExpressionSyntax call:
                CollectRequiredOutputAssignmentsFromExpression(call.Target, assigned, required);
                foreach (var argument in call.Arguments)
                {
                    if (TryGetRequiredOutputAssignmentTarget(argument.Expression, required) is { } argumentName &&
                        argument.ModifierKeyword?.Kind is SyntaxKind.OutKeyword or null)
                    {
                        assigned.Add(argumentName);
                    }

                    CollectRequiredOutputAssignmentsFromExpression(argument.Expression, assigned, required);
                }
                break;
            case ElementAccessExpressionSyntax elementAccess:
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectRequiredOutputAssignmentsFromExpression(indexExpression, assigned, required);
                }
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                CollectRequiredOutputAssignmentsFromExpression(elementAccess.Target, assigned, required);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectRequiredOutputAssignmentsFromExpression(indexExpression, assigned, required);
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                CollectRequiredOutputAssignmentsFromExpression(memberAccess.Receiver, assigned, required);
                break;
            case ArrayLengthExpressionSyntax:
                break;
            case RangeExpressionSyntax range:
                CollectRequiredOutputAssignmentsFromExpression(range.Start, assigned, required);
                CollectRequiredOutputAssignmentsFromExpression(range.End, assigned, required);
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    CollectRequiredOutputAssignmentsFromExpression(argument.Expression, assigned, required);
                }
                break;
            case NewArrayExpressionSyntax newArray:
                foreach (var lengthExpression in newArray.LengthExpressions)
                {
                    CollectRequiredOutputAssignmentsFromExpression(lengthExpression, assigned, required);
                }
                break;
            case ProjectorExpressionSyntax projector:
                foreach (var member in projector.Members)
                {
                    CollectRequiredOutputAssignmentsFromExpression(member.Expression, assigned, required);
                }
                break;
            case MatchNotPatternSyntax notPattern:
                CollectRequiredOutputAssignmentsFromExpression(notPattern.Pattern, assigned, required);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    CollectRequiredOutputAssignmentsFromExpression(pattern, assigned, required);
                }
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    CollectRequiredOutputAssignmentsFromExpression(pattern.Operand, assigned, required);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                CollectRequiredOutputAssignmentsFromExpression(relational.Operand, assigned, required);
                break;
            case TypeTestExpressionSyntax typeTest:
                CollectRequiredOutputAssignmentsFromExpression(typeTest.Expression, assigned, required);
                break;
            case AsExpressionSyntax asExpression:
                CollectRequiredOutputAssignmentsFromExpression(asExpression.Expression, assigned, required);
                break;
            case SetLiteralExpressionSyntax setLiteral:
                foreach (var element in setLiteral.Elements)
                {
                    CollectRequiredOutputAssignmentsFromExpression(element, assigned, required);
                }
                break;
            case QueryExpressionSyntax query:
                CollectRequiredOutputAssignmentsFromExpression(query.SourceExpression, assigned, required);
                CollectRequiredOutputAssignmentsFromExpression(query.SelectExpression, assigned, required);
                foreach (var optionalExpression in new[]
                         {
                             query.JoinSourceExpression,
                             query.JoinLeftExpression,
                             query.JoinRightExpression,
                             query.SecondSourceExpression,
                             query.LetExpression,
                             query.PredicateExpression,
                             query.OrderByExpression,
                             query.ThenByExpression,
                             query.GroupExpression,
                             query.GroupByExpression,
                             query.ContinuationLetExpression,
                             query.ContinuationPredicateExpression,
                             query.ContinuationOrderByExpression,
                             query.ContinuationThenByExpression,
                             query.ContinuationSelectExpression,
                             query.TakeExpression,
                             query.SkipExpression
                         })
                {
                    if (optionalExpression is not null)
                    {
                        CollectRequiredOutputAssignmentsFromExpression(optionalExpression, assigned, required);
                    }
                }
                break;
        }
    }

    private static string? TryGetRequiredOutputAssignmentTarget(
        ExpressionSyntax target,
        IReadOnlyDictionary<string, TextSpan> required)
    {
        if (target is NameExpressionSyntax { Name.Parts.Count: 1 } name &&
            required.ContainsKey(name.Name.Parts[0].Text))
        {
            return name.Name.Parts[0].Text;
        }

        return null;
    }

    private static void ReportMissingRequiredOutputAssignments(
        HashSet<string> assigned,
        IReadOnlyDictionary<string, TextSpan> required,
        TextSpan span,
        DiagnosticBag diagnostics)
    {
        foreach (var requiredOutput in required)
        {
            if (assigned.Contains(requiredOutput.Key))
            {
                continue;
            }

            var isResult = SemanticFacts.NameEquals(requiredOutput.Key, "Result");
            diagnostics.Report(
                isResult ? "ILC2255" : "ILC2256",
                isResult
                    ? "Function result must be assigned before the routine exits."
                    : $"Out parameter '{requiredOutput.Key}' must be assigned before the routine exits.",
                DiagnosticSeverity.Error,
                span);
        }
    }

    private static void ValidateLocalDefiniteAssignments(
        IReadOnlyList<StatementSyntax> statements,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        using var profile = Profile(currentValidationProfiler, "ValidateLocalDefiniteAssignments");
        var scopes = new List<LocalAssignmentScope>
        {
            new(new HashSet<string>(SemanticFacts.NameComparer))
        };
        _ = AnalyzeLocalDefiniteAssignments(statements, new HashSet<string>(SemanticFacts.NameComparer), scopes, knownTypes, diagnostics);
    }

    private static LocalAssignmentFlow AnalyzeLocalDefiniteAssignments(
        IReadOnlyList<StatementSyntax> statements,
        HashSet<string> assigned,
        List<LocalAssignmentScope> scopes,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        var current = new HashSet<string>(assigned, SemanticFacts.NameComparer);
        var canContinue = true;
        foreach (var statement in statements)
        {
            if (!canContinue)
            {
                break;
            }

            var flow = AnalyzeLocalDefiniteAssignment(statement, current, scopes, knownTypes, diagnostics);
            current = flow.Assigned;
            canContinue = flow.CanContinue;
        }

        return new LocalAssignmentFlow(current, canContinue);
    }

    private static LocalAssignmentFlow AnalyzeLocalDefiniteAssignment(
        StatementSyntax statement,
        HashSet<string> assigned,
        List<LocalAssignmentScope> scopes,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        switch (statement)
        {
            case BlockStatementSyntax block:
                return AnalyzeLocalDefiniteAssignments(block.Statements, assigned, PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
            case LocalVariableDeclarationStatementSyntax localDeclaration:
            {
                var localAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                foreach (var declarator in localDeclaration.Declarators)
                {
                    if (declarator.Initializer is not null)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(declarator.Initializer, localAssigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    scopes[^1].Declared.Add(declarator.Identifier.Text);
                    if (declarator.Initializer is not null ||
                        IsGenericDefaultLocal(declarator, knownTypes))
                    {
                        localAssigned.Add(declarator.Identifier.Text);
                    }
                }

                return new LocalAssignmentFlow(localAssigned, true);
            }
            case ExpressionStatementSyntax expressionStatement:
            {
                var expressionAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                AnalyzeLocalDefiniteAssignmentExpression(expressionStatement.Expression, expressionAssigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                return new LocalAssignmentFlow(expressionAssigned, true);
            }
            case ReturnStatementSyntax returnStatement:
                if (returnStatement.Expression is not null)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(returnStatement.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }

                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), false);
            case IfStatementSyntax ifStatement:
            {
                AnalyzeLocalDefiniteAssignmentExpression(ifStatement.Condition, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                var thenFlow = AnalyzeLocalDefiniteAssignments([ifStatement.ThenStatement], new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                var elseFlow = ifStatement.ElseStatement is not null
                    ? AnalyzeLocalDefiniteAssignments([ifStatement.ElseStatement], new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics)
                    : new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return MergeLocalAssignmentFlows(thenFlow, elseFlow);
            }
            case WhileStatementSyntax whileStatement:
                AnalyzeLocalDefiniteAssignmentExpression(whileStatement.Condition, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                _ = AnalyzeLocalDefiniteAssignments([whileStatement.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case RepeatStatementSyntax repeatStatement:
                _ = AnalyzeLocalDefiniteAssignments(repeatStatement.Statements, new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                AnalyzeLocalDefiniteAssignmentExpression(repeatStatement.Condition, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            case ForStatementSyntax forStatement:
            {
                var loopAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                var loopScopes = PushLocalAssignmentScope(scopes);
                if (forStatement.VarKeyword is not null)
                {
                    loopScopes[^1].Declared.Add(forStatement.Identifier.Text);
                    loopAssigned.Add(forStatement.Identifier.Text);
                }
                else if (IsVisibleLocal(forStatement.Identifier.Text, scopes))
                {
                    loopAssigned.Add(forStatement.Identifier.Text);
                }

                AnalyzeLocalDefiniteAssignmentExpression(forStatement.LowerBound, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(forStatement.UpperBound, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                if (forStatement.StepExpression is not null)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(forStatement.StepExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }

                _ = AnalyzeLocalDefiniteAssignments([forStatement.Body], loopAssigned, loopScopes, knownTypes, diagnostics);
                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            }
            case ForeachStatementSyntax foreachStatement:
            {
                AnalyzeLocalDefiniteAssignmentExpression(foreachStatement.Collection, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                var loopAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                var loopScopes = PushLocalAssignmentScope(scopes);
                if (foreachStatement.VarKeyword is not null)
                {
                    loopScopes[^1].Declared.Add(foreachStatement.Identifier.Text);
                    loopAssigned.Add(foreachStatement.Identifier.Text);
                }
                else if (IsVisibleLocal(foreachStatement.Identifier.Text, scopes))
                {
                    loopAssigned.Add(foreachStatement.Identifier.Text);
                }

                _ = AnalyzeLocalDefiniteAssignments([foreachStatement.Body], loopAssigned, loopScopes, knownTypes, diagnostics);
                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
            }
            case WithStatementSyntax withStatement:
                AnalyzeLocalDefiniteAssignmentExpression(withStatement.Receiver, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                return AnalyzeLocalDefiniteAssignments([withStatement.Body], assigned, PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
            case CaseStatementSyntax caseStatement:
            {
                AnalyzeLocalDefiniteAssignmentExpression(caseStatement.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                LocalAssignmentFlow? mergedFlow = null;
                foreach (var clause in caseStatement.Clauses)
                {
                    foreach (var label in clause.Labels)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(label, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    if (clause.Guard is not null)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(clause.Guard, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    var clauseFlow = AnalyzeLocalDefiniteAssignments([clause.Body], new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                    mergedFlow = mergedFlow is null ? clauseFlow : MergeLocalAssignmentFlows(mergedFlow, clauseFlow);
                }

                var elseFlow = caseStatement.ElseStatements.Count > 0
                    ? AnalyzeLocalDefiniteAssignments(caseStatement.ElseStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics)
                    : new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return mergedFlow is null ? elseFlow : MergeLocalAssignmentFlows(mergedFlow, elseFlow);
            }
            case MatchStatementSyntax matchStatement:
            {
                AnalyzeLocalDefiniteAssignmentExpression(matchStatement.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                LocalAssignmentFlow? mergedFlow = null;
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(label, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    var armScopes = PushLocalAssignmentScope(scopes);
                    var armAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                    if (arm.Identifier is not null)
                    {
                        armScopes[^1].Declared.Add(arm.Identifier.Text);
                        armAssigned.Add(arm.Identifier.Text);
                    }

                    if (arm.Guard is not null)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(arm.Guard, armAssigned, armScopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    var armFlow = AnalyzeLocalDefiniteAssignments([arm.Body], armAssigned, armScopes, knownTypes, diagnostics);
                    mergedFlow = mergedFlow is null ? armFlow : MergeLocalAssignmentFlows(mergedFlow, armFlow);
                }

                var elseFlow = matchStatement.ElseStatements.Count > 0
                    ? AnalyzeLocalDefiniteAssignments(matchStatement.ElseStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics)
                    : new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                return mergedFlow is null ? elseFlow : MergeLocalAssignmentFlows(mergedFlow, elseFlow);
            }
            case TryStatementSyntax tryStatement:
            {
                var tryFlow = AnalyzeLocalDefiniteAssignments(tryStatement.TryStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                LocalAssignmentFlow? exceptionFlow = null;
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    var clauseScopes = PushLocalAssignmentScope(scopes);
                    var clauseAssigned = new HashSet<string>(assigned, SemanticFacts.NameComparer);
                    clauseScopes[^1].Declared.Add(clause.Identifier.Text);
                    clauseAssigned.Add(clause.Identifier.Text);
                    var clauseFlow = AnalyzeLocalDefiniteAssignments([clause.Body], clauseAssigned, clauseScopes, knownTypes, diagnostics);
                    exceptionFlow = exceptionFlow is null ? clauseFlow : MergeLocalAssignmentFlows(exceptionFlow, clauseFlow);
                }

                if (tryStatement.ExceptStatements.Count > 0)
                {
                    var exceptStatementsFlow = AnalyzeLocalDefiniteAssignments(tryStatement.ExceptStatements, new HashSet<string>(assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                    exceptionFlow = exceptionFlow is null ? exceptStatementsFlow : MergeLocalAssignmentFlows(exceptionFlow, exceptStatementsFlow);
                }

                if (tryStatement.ExceptKeyword is not null && exceptionFlow is null)
                {
                    exceptionFlow = new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
                }

                var merged = exceptionFlow is null
                    ? tryFlow
                    : MergeLocalAssignmentFlows(tryFlow, exceptionFlow);
                if (tryStatement.FinallyStatements.Count == 0)
                {
                    return merged;
                }

                var finallyFlow = AnalyzeLocalDefiniteAssignments(tryStatement.FinallyStatements, new HashSet<string>(merged.Assigned, SemanticFacts.NameComparer), PushLocalAssignmentScope(scopes), knownTypes, diagnostics);
                return new LocalAssignmentFlow(finallyFlow.Assigned, merged.CanContinue && finallyFlow.CanContinue);
            }
            case RaiseStatementSyntax raiseStatement:
                if (raiseStatement.Expression is not null)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(raiseStatement.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }

                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), false);
            default:
                return new LocalAssignmentFlow(new HashSet<string>(assigned, SemanticFacts.NameComparer), true);
        }
    }

    private static LocalAssignmentFlow MergeLocalAssignmentFlows(LocalAssignmentFlow left, LocalAssignmentFlow right)
    {
        if (!left.CanContinue && !right.CanContinue)
        {
            return new LocalAssignmentFlow(new HashSet<string>(left.Assigned, SemanticFacts.NameComparer), false);
        }

        if (!left.CanContinue)
        {
            return new LocalAssignmentFlow(new HashSet<string>(right.Assigned, SemanticFacts.NameComparer), true);
        }

        if (!right.CanContinue)
        {
            return new LocalAssignmentFlow(new HashSet<string>(left.Assigned, SemanticFacts.NameComparer), true);
        }

        var assigned = new HashSet<string>(left.Assigned, SemanticFacts.NameComparer);
        assigned.IntersectWith(right.Assigned);
        return new LocalAssignmentFlow(assigned, true);
    }

    private static void AnalyzeLocalDefiniteAssignmentExpression(
        ExpressionSyntax expression,
        HashSet<string> assigned,
        List<LocalAssignmentScope> scopes,
        DiagnosticBag diagnostics,
        bool treatAssignmentTargetAsWrite)
    {
        switch (expression)
        {
            case NameExpressionSyntax name:
                AnalyzeLocalDefiniteAssignmentName(name.Name, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite);
                break;
            case AssignmentExpressionSyntax assignment:
                AnalyzeLocalDefiniteAssignmentExpression(assignment.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(assignment.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: true);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                AnalyzeLocalDefiniteAssignmentExpression(assignment.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(assignment.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(assignment.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: true);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                AnalyzeLocalDefiniteAssignmentExpression(parenthesized.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite);
                break;
            case BinaryExpressionSyntax binary:
                AnalyzeLocalDefiniteAssignmentExpression(binary.Left, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(binary.Right, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case UnaryExpressionSyntax unary:
                AnalyzeLocalDefiniteAssignmentExpression(unary.Operand, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case CallExpressionSyntax call:
                AnalyzeLocalDefiniteAssignmentExpression(call.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                foreach (var argument in call.Arguments)
                {
                    var isOutArgument = argument.ModifierKeyword?.Kind == SyntaxKind.OutKeyword;
                    if (!isOutArgument)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(argument.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }

                    if (argument.ModifierKeyword?.Kind is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(argument.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: argument.ModifierKeyword.Kind == SyntaxKind.OutKeyword);
                    }
                }
                break;
            case ElementAccessExpressionSyntax elementAccess:
                AnalyzeLocalDefiniteAssignmentName(elementAccess.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(indexExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                if (treatAssignmentTargetAsWrite)
                {
                    AnalyzeLocalDefiniteAssignmentName(elementAccess.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: true);
                }
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                AnalyzeLocalDefiniteAssignmentExpression(elementAccess.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(indexExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                AnalyzeLocalDefiniteAssignmentExpression(memberAccess.Receiver, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case ArrayLengthExpressionSyntax arrayLength:
                AnalyzeLocalDefiniteAssignmentName(arrayLength.Target, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case RangeExpressionSyntax range:
                AnalyzeLocalDefiniteAssignmentExpression(range.Start, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(range.End, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(argument.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case NewArrayExpressionSyntax newArray:
                foreach (var lengthExpression in newArray.LengthExpressions)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(lengthExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case ProjectorExpressionSyntax projector:
                foreach (var member in projector.Members)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(member.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case MatchExpressionSyntax matchExpression:
                AnalyzeLocalDefiniteAssignmentExpression(matchExpression.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                foreach (var arm in matchExpression.Arms)
                {
                    if (arm.Guard is not null)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(arm.Guard, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }
                    AnalyzeLocalDefiniteAssignmentExpression(arm.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case MatchNotPatternSyntax notPattern:
                AnalyzeLocalDefiniteAssignmentExpression(notPattern.Pattern, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(pattern, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(pattern.Operand, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                AnalyzeLocalDefiniteAssignmentExpression(relational.Operand, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case TypeTestExpressionSyntax typeTest:
                AnalyzeLocalDefiniteAssignmentExpression(typeTest.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case AsExpressionSyntax asExpression:
                AnalyzeLocalDefiniteAssignmentExpression(asExpression.Expression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                break;
            case SetLiteralExpressionSyntax setLiteral:
                foreach (var element in setLiteral.Elements)
                {
                    AnalyzeLocalDefiniteAssignmentExpression(element, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                }
                break;
            case QueryExpressionSyntax query:
                AnalyzeLocalDefiniteAssignmentExpression(query.SourceExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                AnalyzeLocalDefiniteAssignmentExpression(query.SelectExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                foreach (var optionalExpression in new[]
                         {
                             query.JoinSourceExpression,
                             query.JoinLeftExpression,
                             query.JoinRightExpression,
                             query.SecondSourceExpression,
                             query.LetExpression,
                             query.PredicateExpression,
                             query.OrderByExpression,
                             query.ThenByExpression,
                             query.GroupExpression,
                             query.GroupByExpression,
                             query.ContinuationLetExpression,
                             query.ContinuationPredicateExpression,
                             query.ContinuationOrderByExpression,
                             query.ContinuationThenByExpression,
                             query.ContinuationSelectExpression,
                             query.TakeExpression,
                             query.SkipExpression
                         })
                {
                    if (optionalExpression is not null)
                    {
                        AnalyzeLocalDefiniteAssignmentExpression(optionalExpression, assigned, scopes, diagnostics, treatAssignmentTargetAsWrite: false);
                    }
                }
                break;
        }
    }

    private static void AnalyzeLocalDefiniteAssignmentName(
        QualifiedNameSyntax name,
        HashSet<string> assigned,
        List<LocalAssignmentScope> scopes,
        DiagnosticBag diagnostics,
        bool treatAssignmentTargetAsWrite)
    {
        if (name.Parts.Count != 1 ||
            !IsVisibleLocal(name.Parts[0].Text, scopes))
        {
            return;
        }

        var localName = name.Parts[0].Text;
        if (treatAssignmentTargetAsWrite)
        {
            assigned.Add(localName);
            return;
        }

        if (!assigned.Contains(localName))
        {
            diagnostics.Report(
                "ILC2257",
                $"Local variable '{localName}' must be assigned before it is read.",
                DiagnosticSeverity.Error,
                name.Parts[0].Span);
        }
    }

    private static List<LocalAssignmentScope> PushLocalAssignmentScope(List<LocalAssignmentScope> scopes)
    {
        var nested = new List<LocalAssignmentScope>(scopes)
        {
            new(new HashSet<string>(SemanticFacts.NameComparer))
        };
        return nested;
    }

    private static bool IsVisibleLocal(string name, List<LocalAssignmentScope> scopes)
    {
        for (var index = scopes.Count - 1; index >= 0; index--)
        {
            if (scopes[index].Declared.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGenericDefaultLocal(VariableDeclaratorSyntax declarator, IReadOnlyList<TypeSymbol> knownTypes) =>
        declarator.Initializer is null &&
        declarator.TypeName is not null &&
        BindType(declarator.TypeName, knownTypes) is TypeParameterSymbol;

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
        var profiler = currentValidationProfiler;
        if (profiler is null)
        {
            ValidateExpressionCore(expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        ValidateExpressionCore(expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        profiler.AddFlatSample($"ValidateExpression.{expression.GetType().Name}", Stopwatch.GetElapsedTime(startedAt));
    }

    private static void ValidateExpressionCore(
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
                if (name.Name.Parts.Count == 1 &&
                    locals.ContainsKey(name.Name.Parts[0].Text))
                {
                    using var localNameProfile = Profile(currentValidationProfiler, "ValidateName.LocalFastPath");
                    break;
                }

                ValidateNameReference(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                ValidateExpression(parenthesized.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case AssignmentExpressionSyntax assignment:
                ValidateAssignmentTarget(assignment.Target, locals, knownTypes, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                ValidateExpressionForExpectedType(
                    assignment.Expression,
                    InferValidationExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
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
                    var targetType = InferValidationExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var targetType = InferValidationExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var valueType = InferValidationExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (SemanticFacts.IsUnknownType(targetType) || SemanticFacts.IsUnknownType(valueType))
                    {
                        break;
                    }

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
                    var targetType = InferValidationExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var valueType = InferValidationExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (SemanticFacts.IsUnknownType(targetType) || SemanticFacts.IsUnknownType(valueType))
                    {
                        break;
                    }

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
                    var targetType = InferValidationExpressionType(assignment.Target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var valueType = InferValidationExpressionType(assignment.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    if (SemanticFacts.IsUnknownType(targetType) || SemanticFacts.IsUnknownType(valueType))
                    {
                        break;
                    }

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
                using (Profile(currentValidationProfiler, "ValidateBinary.Operands"))
                {
                    using (Profile(currentValidationProfiler, "ValidateBinary.Left"))
                    {
                        ValidateExpression(binary.Left, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }

                    using (Profile(currentValidationProfiler, "ValidateBinary.Right"))
                    {
                        ValidateExpression(binary.Right, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }
                }

                if (binary.OperatorToken.Kind is SyntaxKind.InKeyword or SyntaxKind.NotInKeyword)
                {
                    using (Profile(currentValidationProfiler, "ValidateBinary.SetMembership"))
                    {
                        ValidateSetMembership(binary, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }
                }
                else if (binary.OperatorToken.Kind == SyntaxKind.NullCoalescingToken)
                {
                    TypeSymbol leftType;
                    TypeSymbol rightType;
                    using (Profile(currentValidationProfiler, "ValidateBinary.Infer.NullCoalescing"))
                    {
                        leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    }

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
                    TypeSymbol leftType;
                    TypeSymbol rightType;
                    using (Profile(currentValidationProfiler, "ValidateBinary.Infer.Shift"))
                    {
                        leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    }

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
                    TypeSymbol leftType;
                    TypeSymbol rightType;
                    using (Profile(currentValidationProfiler, "ValidateBinary.Infer.Div"))
                    {
                        leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    }

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
                    TypeSymbol leftType;
                    TypeSymbol rightType;
                    using (Profile(currentValidationProfiler, "ValidateBinary.Infer.Mod"))
                    {
                        leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    }

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
                    bool mayBeSetExpression;
                    using (Profile(currentValidationProfiler, "ValidateBinary.MayBeSetExpression"))
                    {
                        mayBeSetExpression =
                            MayBeSetExpression(binary.Left, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) ||
                            MayBeSetExpression(binary.Right, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                    }

                    if (mayBeSetExpression)
                    {
                        using (Profile(currentValidationProfiler, "ValidateBinary.SetBinary"))
                        {
                            ValidateSetBinary(binary, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        }
                    }
                }
                else if (binary.OperatorToken.Kind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword)
                {
                    TypeSymbol leftType;
                    TypeSymbol rightType;
                    using (Profile(currentValidationProfiler, "ValidateBinary.Infer.Logical"))
                    {
                        leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    }

                    if (SemanticFacts.IsSetType(leftType) || SemanticFacts.IsSetType(rightType))
                    {
                        using (Profile(currentValidationProfiler, "ValidateBinary.SetBinary"))
                        {
                            ValidateSetBinary(binary, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        }
                    }
                    else
                    {
                        ValidateLogicalBinary(binary, leftType, rightType, knownTypes, diagnostics);
                    }
                }
                break;
            case UnaryExpressionSyntax unary:
                ValidateExpression(unary.Operand, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (unary.OperatorToken.Kind == SyntaxKind.NotKeyword)
                {
                    var operandType = InferValidationExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var operandType = InferValidationExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var operandType = InferValidationExpressionType(unary.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                var matchedExpressionType = InferValidationExpressionType(matchExpression.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                            armLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
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
                        var guardType = InferValidationExpressionType(arm.Guard, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var armType = InferValidationExpressionType(arm.Expression, armLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                diagnostics.Report(
                    "ILC2254",
                    "Range expressions are only valid in slice accesses, set literals, and case labels.",
                    DiagnosticSeverity.Error,
                    range.RangeToken.Span);
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
                if (elementAccess.IndexExpressions.Any(expression => expression is RangeExpressionSyntax))
                {
                    ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                }

                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    ValidateExpression(indexExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                }

                ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                ValidateExpression(elementAccess.Target, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                if (elementAccess.IndexExpressions.Any(expression => expression is RangeExpressionSyntax))
                {
                    ValidateArrayAccess(elementAccess.Target, elementAccess.IndexExpressions, elementAccess.OpenBracketToken.Span, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    break;
                }

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
                var lengthTargetType = InferValidationExpressionType(new NameExpressionSyntax(arrayLength.Target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                InvocationResolution? invocation;
                using (Profile(currentValidationProfiler, "ValidateCall.ResolveInvocation"))
                {
                    invocation = SemanticFacts.ResolveInvocation(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
                }

                var reportedInvalidMethodAccess = false;
                if (RequiresInvalidMethodAccessCheck(call.Target, invocation, currentMethod))
                {
                    using (Profile(currentValidationProfiler, "ValidateCall.InvalidMethodAccess"))
                    {
                        reportedInvalidMethodAccess = TryReportInvalidMethodAccess(call.Target, call.Arguments.Count, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    }
                }

                if (reportedInvalidMethodAccess)
                {
                }
                else if (invocation?.Method is { } invokedMethod &&
                    TryReportReadonlySelfMethodCall(call.Target, invokedMethod, currentMethod, diagnostics))
                {
                }
                else if (invocation is null &&
                    TryReportReadonlySelfMethodCall(call.Target, call.Arguments.Count, knownMethods, currentMethod, diagnostics))
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
                    using (Profile(currentValidationProfiler, "ValidateCall.Argument"))
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
                }

                if (invocation?.Method is { } resolvedMethod &&
                    SemanticFacts.NameEquals(resolvedMethod.Name, "TryParse") &&
                    SemanticFacts.NameEquals(resolvedMethod.DeclaringTypeName, TypeSymbol.Integer.Name) &&
                    resolvedMethod.IsStatic &&
                    call.Arguments.Count == 2)
                {
                    using (Profile(currentValidationProfiler, "ValidateCall.TryParseSpecialCase"))
                    {
                        var parseInputType = InferValidationExpressionType(call.Arguments[0].Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        if (!SemanticFacts.NameEquals(parseInputType.Name, TypeSymbol.String.Name))
                        {
                            diagnostics.Report(
                                "ILC2163",
                                "Integer.TryParse expects a String as its first argument.",
                                DiagnosticSeverity.Error,
                                GetExpressionDiagnosticSpan(call.Arguments[0].Expression, knownTypes));
                        }
                    }
                }
                break;
            case QueryExpressionSyntax query:
                ValidateExpression(query.SourceExpression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var querySourceType = InferValidationExpressionType(query.SourceExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

                var queryLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                {
                    [query.Identifier.Text] = enumerablePattern.ElementType
                };

                if (query.JoinSourceExpression is not null && query.JoinIdentifier is not null)
                {
                    ValidateExpression(query.JoinSourceExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var joinSourceType = InferValidationExpressionType(query.JoinSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                        var joinLeftType = InferValidationExpressionType(query.JoinLeftExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        var joinRightType = InferValidationExpressionType(query.JoinRightExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var secondSourceType = InferValidationExpressionType(query.SecondSourceExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var letType = InferValidationExpressionType(query.LetExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    queryLocals[query.LetIdentifier.Text] = letType;
                }

                if (query.PredicateExpression is not null)
                {
                    ValidateExpression(query.PredicateExpression, queryLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                    var predicateType = InferValidationExpressionType(query.PredicateExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var orderByType = InferValidationExpressionType(query.OrderByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var thenByType = InferValidationExpressionType(query.ThenByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var groupKeyType = InferValidationExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                                $"Grouping<{InferValidationExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}, {InferValidationExpressionType(query.GroupExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}>",
                                knownTypes)
                                ?? new TypeSymbol(
                                    $"Grouping<{InferValidationExpressionType(query.GroupByExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}, {InferValidationExpressionType(query.GroupExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}>",
                                    true)
                            : InferValidationExpressionType(query.SelectExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                    var continuationLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer)
                    {
                        [query.IntoIdentifier.Text] = continuationRangeType
                    };

                    if (query.ContinuationLetExpression is not null && query.ContinuationLetIdentifier is not null)
                    {
                        ValidateExpression(query.ContinuationLetExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationLetType = InferValidationExpressionType(query.ContinuationLetExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                        continuationLocals[query.ContinuationLetIdentifier.Text] = continuationLetType;
                    }

                    if (query.ContinuationPredicateExpression is not null)
                    {
                        ValidateExpression(query.ContinuationPredicateExpression, continuationLocals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                        var continuationPredicateType = InferValidationExpressionType(query.ContinuationPredicateExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                        var continuationOrderByType = InferValidationExpressionType(query.ContinuationOrderByExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                        var continuationThenByType = InferValidationExpressionType(query.ContinuationThenByExpression, continuationLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var takeType = InferValidationExpressionType(query.TakeExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                    var skipType = InferValidationExpressionType(query.SkipExpression, queryLocals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

    private static TypeSymbol ValidateExpressionForExpectedType(
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
            return expectedType ?? TypeSymbol.Unknown;
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
            return expectedType ?? TypeSymbol.Unknown;
        }

        ValidateExpression(expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
        var actualType = InferValidationExpressionType(expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (expectedType is null || actualType == TypeSymbol.Unknown)
        {
            return actualType;
        }

        if (!IsAssignableTo(actualType, expectedType, knownTypes))
        {
            diagnostics.Report(
                "ILC2240",
                $"Cannot assign expression of type '{actualType.Name}' to target type '{expectedType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(expression, knownTypes));
        }

        return actualType;
    }

    private static bool IsAssignableTo(TypeSymbol sourceType, TypeSymbol targetType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        if (sourceType == TypeSymbol.Unknown || targetType == TypeSymbol.Unknown)
        {
            return true;
        }

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
        var sourceGenericStart = sourceType.Name.IndexOf('<', StringComparison.Ordinal);
        var sourceDefinitionName = sourceGenericStart > 0 ? sourceType.Name[..sourceGenericStart] : sourceType.Name;
        var sourceGenericArity = CountGenericArguments(sourceType.Name);
        var resolvedSource =
            ResolveGenericDefinition(sourceDefinitionName, sourceGenericArity, knownTypes) ??
            SemanticFacts.ResolveTypeReference(sourceType.Name, knownTypes) as NamedTypeSymbol;
        if (resolvedSource is null)
        {
            return false;
        }

        foreach (var sourceCandidate in EnumerateGenericSourceCandidates(resolvedSource, sourceDefinitionName, knownTypes))
        {
            foreach (var candidateType in EnumerateAssignableTypeHierarchy(sourceCandidate, knownTypes))
            {
                foreach (var interfaceType in candidateType.InterfaceTypes)
                {
                    if (ImplementsGenericInterfaceDefinition(interfaceType, targetDefinitionName, knownTypes))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<NamedTypeSymbol> EnumerateGenericSourceCandidates(NamedTypeSymbol resolvedSource, string sourceDefinitionName, IReadOnlyList<TypeSymbol> knownTypes)
    {
        yield return resolvedSource;
        foreach (var candidate in knownTypes.OfType<NamedTypeSymbol>())
        {
            if (!ReferenceEquals(candidate, resolvedSource) && SemanticFacts.NameEquals(candidate.Name, sourceDefinitionName))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<NamedTypeSymbol> EnumerateAssignableTypeHierarchy(NamedTypeSymbol sourceType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        for (NamedTypeSymbol? current = sourceType; current is not null;)
        {
            yield return current;
            current = current.BaseType is null
                ? null
                : SemanticFacts.ResolveTypeReference(current.BaseType.Name, knownTypes) as NamedTypeSymbol;
        }
    }

    private static bool ImplementsGenericInterfaceDefinition(TypeSymbol interfaceType, string targetDefinitionName, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var interfaceGenericStart = interfaceType.Name.IndexOf('<', StringComparison.Ordinal);
        if (interfaceGenericStart > 0 && SemanticFacts.NameEquals(interfaceType.Name[..interfaceGenericStart], targetDefinitionName))
        {
            return true;
        }

        var interfaceDefinitionStart = interfaceType.Name.IndexOf('<', StringComparison.Ordinal);
        var interfaceDefinitionName = interfaceDefinitionStart > 0 ? interfaceType.Name[..interfaceDefinitionStart] : interfaceType.Name;
        var interfaceGenericArity = CountGenericArguments(interfaceType.Name);
        var resolvedInterface =
            ResolveGenericDefinition(interfaceDefinitionName, interfaceGenericArity, knownTypes) ??
            SemanticFacts.ResolveTypeReference(interfaceType.Name, knownTypes) as NamedTypeSymbol;
        if (resolvedInterface is null)
        {
            return false;
        }

        return resolvedInterface.InterfaceTypes.Any(inheritedInterface =>
            ImplementsGenericInterfaceDefinition(inheritedInterface, targetDefinitionName, knownTypes));
    }

    private static NamedTypeSymbol? ResolveGenericDefinition(string definitionName, int genericArity, IReadOnlyList<TypeSymbol> knownTypes) =>
        knownTypes
            .OfType<NamedTypeSymbol>()
            .FirstOrDefault(type => SemanticFacts.NameEquals(type.Name, definitionName) && type.GenericArity == genericArity) ??
        knownTypes
            .OfType<NamedTypeSymbol>()
            .FirstOrDefault(type => SemanticFacts.NameEquals(type.Name, definitionName));

    private static int CountGenericArguments(string typeName)
    {
        var genericStart = typeName.IndexOf('<', StringComparison.Ordinal);
        if (genericStart < 0 || !typeName.EndsWith(">", StringComparison.Ordinal))
        {
            return 0;
        }

        var depth = 0;
        var count = 1;
        for (var index = genericStart + 1; index < typeName.Length - 1; index++)
        {
            var current = typeName[index];
            if (current == '<')
            {
                depth++;
            }
            else if (current == '>')
            {
                depth--;
            }
            else if (current == ',' && depth == 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsCompatibleArrayAssignment(string sourceName, string targetName)
    {
        if (!sourceName.EndsWith(']') || !targetName.EndsWith(']'))
        {
            return false;
        }

        var sourceBracket = sourceName.IndexOf('[', StringComparison.Ordinal);
        var targetBracket = targetName.IndexOf('[', StringComparison.Ordinal);
        if (sourceBracket <= 0 || targetBracket <= 0 || !SemanticFacts.NameEquals(sourceName[..sourceBracket], targetName[..targetBracket]))
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
            SemanticFacts.NameEquals(sourceType.Name[..sourceGenericStart], targetType.Name[..targetGenericStart]))
        {
            return true;
        }

        if (SemanticFacts.NameEquals(targetType.Name, "IEnumerable") && sourceType.IsReferenceType)
        {
            return true;
        }

        if (targetType.Name.Contains('<', StringComparison.Ordinal) && SemanticFacts.NameEquals(sourceType.Name, targetType.Name[..targetType.Name.IndexOf('<', StringComparison.Ordinal)]))
        {
            return true;
        }

        if (targetGenericStart > 0 && SemanticFacts.NameEquals(sourceType.Name, targetType.Name[..targetGenericStart]))
        {
            return true;
        }

        if (sourceGenericStart > 0 && SemanticFacts.NameEquals(targetType.Name, sourceType.Name[..sourceGenericStart]))
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

        var invokeMethod = delegateType.Methods.FirstOrDefault(method => SemanticFacts.NameEquals(method.Name, "Invoke") && !method.IsStatic);
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

        var elementType = InferValidationExpressionType(element, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        var leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        var leftType = InferValidationExpressionType(binary.Left, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var rightType = InferValidationExpressionType(binary.Right, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

    private static void ValidateLogicalBinary(
        BinaryExpressionSyntax binary,
        TypeSymbol leftType,
        TypeSymbol rightType,
        IReadOnlyList<TypeSymbol> knownTypes,
        DiagnosticBag diagnostics)
    {
        if (SemanticFacts.IsUnknownType(leftType) || SemanticFacts.IsUnknownType(rightType))
        {
            return;
        }

        if (leftType != TypeSymbol.Boolean)
        {
            diagnostics.Report(
                "ILC2251",
                $"Left-hand side of logical '{binary.OperatorToken.Text}' must be Boolean, but got '{leftType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary.Left, knownTypes));
        }

        if (rightType != TypeSymbol.Boolean)
        {
            diagnostics.Report(
                "ILC2252",
                $"Right-hand side of logical '{binary.OperatorToken.Text}' must be Boolean, but got '{rightType.Name}'.",
                DiagnosticSeverity.Error,
                GetExpressionDiagnosticSpan(binary.Right, knownTypes));
        }
    }

    private static TypeSymbol InferValidationExpressionType(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        IReadOnlyList<TypeSymbol> knownTypes)
        => SemanticFacts.InferExpressionType(expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);

    private static bool MayBeSetExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod)
    {
        switch (expression)
        {
            case SetLiteralExpressionSyntax:
                return true;
            case ParenthesizedExpressionSyntax parenthesized:
                return MayBeSetExpression(parenthesized.Expression, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            case NameExpressionSyntax name:
                if (locals.TryGetValue(name.Name.ToDisplayString(), out var localType))
                {
                    return SemanticFacts.IsSetType(localType);
                }

                return SemanticFacts.ResolveName(name.Name, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod).Type is { } resolvedType &&
                    SemanticFacts.IsSetType(resolvedType);
            case BinaryExpressionSyntax binary when binary.OperatorToken.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken or SyntaxKind.StarToken:
                return MayBeSetExpression(binary.Left, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod) ||
                    MayBeSetExpression(binary.Right, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod);
            default:
                return false;
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

        var labelType = InferValidationExpressionType(label, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (SemanticFacts.IsUnknownType(caseExpressionType) || SemanticFacts.IsUnknownType(labelType))
        {
            return;
        }

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

            var operandType = InferValidationExpressionType(relational.Operand, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        if (IsResultName(name) && !IsResultAvailable(currentMethod))
        {
            ReportInvalidResultUsage(name.Parts[0].Span, diagnostics);
            return;
        }

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
        if (TryReportReadonlyInParameterAssignment(target, currentMethod, diagnostics))
        {
            return;
        }

        if (target is ElementAccessExpressionSyntax elementAccess)
        {
            ValidateNameReference(elementAccess.Target, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            foreach (var indexExpression in elementAccess.IndexExpressions)
            {
                ValidateExpression(indexExpression, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
            }

            var indexedTargetType = InferValidationExpressionType(new NameExpressionSyntax(elementAccess.Target), locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            var resolvedIndexer = SemanticFacts.ResolveIndexerReference(elementAccess.Target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

            var indexedTargetType = InferValidationExpressionType(postfixElementAccess.Target, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
            var resolvedIndexer = SemanticFacts.ResolveIndexerReference(postfixElementAccess.Target, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                if (TryReportReadonlySelfMutation(memberTarget, knownFields, knownProperties, currentMethod, diagnostics))
                {
                    return;
                }

                ValidateExpression(memberTarget.Receiver, locals, knownTypes, [], knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
                var memberResolution = SemanticFacts.ResolveMemberAccess(memberTarget, locals, [], knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
                if (memberResolution.Property is not null)
                {
                    if (TryReportReadonlySelfMutation(memberTarget, memberResolution.Property, currentMethod, diagnostics))
                    {
                        return;
                    }

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
                        !(currentMethod?.IsConstructor == true && SemanticFacts.NameEquals(currentMethod.DeclaringTypeName, memberResolution.Property.DeclaringTypeName)))
                    {
                        diagnostics.Report(
                            "ILC2123",
                            $"Init-only property '{memberResolution.DisplayName}' can only be assigned in a constructor of '{memberResolution.Property.DeclaringTypeName}'.",
                            DiagnosticSeverity.Error,
                            memberTarget.MemberName.Span);
                        return;
                    }

                    if (memberResolution.Property.IsSetterPrivate && !SemanticFacts.NameEquals(memberResolution.Property.DeclaringTypeName, currentMethod?.DeclaringTypeName))
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
                    if (TryReportReadonlySelfMutation(memberTarget, memberResolution.Field, currentMethod, diagnostics))
                    {
                        return;
                    }

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
        if (targetName.Parts.Count == 1 &&
            locals.ContainsKey(targetName.ToDisplayString()))
        {
            return;
        }

        if (TryReportInvalidFieldAccess(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes, diagnostics))
        {
            return;
        }

        if (TryReportReadonlySelfMutation(targetName, knownFields, knownProperties, currentMethod, diagnostics))
        {
            return;
        }

        var property = SemanticFacts.ResolvePropertyReference(targetName, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (property is not null && TryReportReadonlySelfMutation(targetName, property, currentMethod, diagnostics))
        {
            return;
        }

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
            !(currentMethod?.IsConstructor == true && SemanticFacts.NameEquals(currentMethod.DeclaringTypeName, property.DeclaringTypeName)))
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

        var field = SemanticFacts.ResolveFieldIgnoringAccess(targetName, knownFields, currentMethod);
        if (field is not null && TryReportReadonlySelfMutation(targetName, field, currentMethod, diagnostics))
        {
            return;
        }

        if (targetName.Parts.Count == 1)
        {
            var name = targetName.ToDisplayString();
            if (SemanticFacts.NameEquals(name, "Result") && !IsResultAvailable(currentMethod))
            {
                ReportInvalidResultUsage(targetName.Parts[0].Span, diagnostics);
                return;
            }

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
            if (resolution.Field is not null && TryReportReadonlySelfMutation(targetName, resolution.Field, currentMethod, diagnostics))
            {
                return;
            }

            return;
        }

        diagnostics.Report(
            "ILC2101",
            $"Assignment target '{targetName.ToDisplayString()}' is not assignable in the current bootstrap compiler.",
            DiagnosticSeverity.Error,
            GetReferenceDiagnosticSpan(targetName, knownTypes));
    }

    private static bool TryReportReadonlyInParameterAssignment(
        ExpressionSyntax target,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (currentMethod is null ||
            TryGetWritableRootName(target) is not { } rootName)
        {
            return false;
        }

        var parameter = currentMethod.Parameters.FirstOrDefault(parameter =>
            parameter.PassingKind == ParameterPassingKind.In &&
            SemanticFacts.NameEquals(parameter.Name, rootName));
        if (parameter is null)
        {
            return false;
        }

        diagnostics.Report(
            "ILC2253",
            $"In parameter '{parameter.Name}' is read-only and cannot be used as an assignment target.",
            DiagnosticSeverity.Error,
            GetExpressionDiagnosticSpan(target, []));
        return true;
    }

    private static string? TryGetWritableRootName(ExpressionSyntax target) =>
        target switch
        {
            NameExpressionSyntax name when name.Name.Parts.Count > 0 => name.Name.Parts[0].Text,
            ElementAccessExpressionSyntax elementAccess when elementAccess.Target.Parts.Count > 0 => elementAccess.Target.Parts[0].Text,
            PostfixElementAccessExpressionSyntax elementAccess => TryGetWritableRootName(elementAccess.Target),
            MemberAccessExpressionSyntax memberAccess => TryGetWritableRootName(memberAccess.Receiver),
            ParenthesizedExpressionSyntax parenthesized => TryGetWritableRootName(parenthesized.Expression),
            _ => null
        };

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
        if (indexExpressions.Any(expression => expression is RangeExpressionSyntax) &&
            !SemanticFacts.IsSliceAccess(indexExpressions))
        {
            diagnostics.Report(
                "ILC2161",
                "Slice access must use a single range index.",
                DiagnosticSeverity.Error,
                indexSpan);
            return;
        }

        if (SemanticFacts.IsSliceAccess(indexExpressions))
        {
            ValidateSliceAccess(target, indexExpressions[0], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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
                var indexType = InferValidationExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

        var indexedType = InferValidationExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
                var indexType = InferValidationExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
            var indexType = InferValidationExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

    private static bool IsResultName(QualifiedNameSyntax name) =>
        name.Parts.Count == 1 &&
        SemanticFacts.NameEquals(name.Parts[0].Text, "Result");

    private static bool IsResultAvailable(MethodSymbol? currentMethod) =>
        currentMethod is not null &&
        currentMethod.Declaration?.Keyword.Kind is not SyntaxKind.ProcedureKeyword &&
        currentMethod.ReturnType != TypeSymbol.Void;

    private static bool IsReadonlySelfRoutine(MethodSymbol? currentMethod) =>
        currentMethod is { IsStatic: false, Declaration.Keyword.Kind: SyntaxKind.FunctionKeyword or SyntaxKind.ProcedureKeyword };

    private static bool IsMutatingSourceMethod(MethodSymbol method) =>
        method.Declaration?.Keyword.Kind == SyntaxKind.MethodKeyword;

    private static bool IsCurrentObjectMember(string? declaringTypeName, MethodSymbol? currentMethod) =>
        currentMethod?.DeclaringTypeName is not null &&
        declaringTypeName is not null &&
        SemanticFacts.NameEquals(declaringTypeName, currentMethod.DeclaringTypeName);

    private static bool IsExplicitSelfReceiver(ExpressionSyntax receiver) =>
        receiver is NameExpressionSyntax { Name.Parts.Count: 1 } name &&
        SemanticFacts.NameEquals(name.Name.Parts[0].Text, "self");

    private static bool IsExplicitSelfQualifiedName(QualifiedNameSyntax name) =>
        name.Parts.Count > 1 &&
        SemanticFacts.NameEquals(name.Parts[^2].Text, "self");

    private static bool IsImplicitOrExplicitSelfTarget(QualifiedNameSyntax name, MethodSymbol? currentMethod)
    {
        if (currentMethod?.DeclaringTypeName is null || currentMethod.IsStatic)
        {
            return false;
        }

        return name.Parts.Count == 1 || IsExplicitSelfQualifiedName(name);
    }

    private static bool TryReportReadonlySelfMutation(
        QualifiedNameSyntax target,
        FieldSymbol field,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            field.IsStatic ||
            !IsCurrentObjectMember(field.DeclaringTypeName, currentMethod) ||
            !IsImplicitOrExplicitSelfTarget(target, currentMethod))
        {
            return false;
        }

        diagnostics.Report(
            "ILC2248",
            $"Readonly routine '{currentMethod!.Name}' cannot modify instance field '{target.ToDisplayString()}'. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            target.Parts[^1].Span);
        return true;
    }

    private static bool TryReportReadonlySelfMutation(
        QualifiedNameSyntax target,
        PropertySymbol property,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            property.IsStatic ||
            !IsCurrentObjectMember(property.DeclaringTypeName, currentMethod) ||
            !IsImplicitOrExplicitSelfTarget(target, currentMethod))
        {
            return false;
        }

        diagnostics.Report(
            "ILC2248",
            $"Readonly routine '{currentMethod!.Name}' cannot modify instance property '{target.ToDisplayString()}'. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            target.Parts[^1].Span);
        return true;
    }

    private static bool TryReportReadonlySelfMutation(
        MemberAccessExpressionSyntax target,
        FieldSymbol field,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            field.IsStatic ||
            !IsCurrentObjectMember(field.DeclaringTypeName, currentMethod) ||
            !IsExplicitSelfReceiver(target.Receiver))
        {
            return false;
        }

        diagnostics.Report(
            "ILC2248",
            $"Readonly routine '{currentMethod!.Name}' cannot modify instance field '{SemanticFacts.GetExpressionDisplayName(target)}'. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            target.MemberName.Span);
        return true;
    }

    private static bool TryReportReadonlySelfMutation(
        MemberAccessExpressionSyntax target,
        PropertySymbol property,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            property.IsStatic ||
            !IsCurrentObjectMember(property.DeclaringTypeName, currentMethod) ||
            !IsExplicitSelfReceiver(target.Receiver))
        {
            return false;
        }

        diagnostics.Report(
            "ILC2248",
            $"Readonly routine '{currentMethod!.Name}' cannot modify instance property '{SemanticFacts.GetExpressionDisplayName(target)}'. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            target.MemberName.Span);
        return true;
    }

    private static bool TryReportReadonlySelfMethodCall(
        ExpressionSyntax target,
        MethodSymbol invokedMethod,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            invokedMethod.IsStatic ||
            !IsMutatingSourceMethod(invokedMethod) ||
            !IsCurrentObjectMember(invokedMethod.DeclaringTypeName, currentMethod))
        {
            return false;
        }

        var isSelfCall = target switch
        {
            NameExpressionSyntax { Name.Parts.Count: 1 } => true,
            NameExpressionSyntax name => IsExplicitSelfQualifiedName(name.Name),
            MemberAccessExpressionSyntax member => IsExplicitSelfReceiver(member.Receiver),
            _ => false
        };

        if (!isSelfCall)
        {
            return false;
        }

        diagnostics.Report(
            "ILC2249",
            $"Readonly routine '{currentMethod!.Name}' cannot call mutating method '{invokedMethod.Name}' on self. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            GetExpressionDiagnosticSpan(target, []));
        return true;
    }

    private static bool TryReportReadonlySelfMutation(
        MemberAccessExpressionSyntax target,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            currentMethod?.DeclaringTypeName is null ||
            !IsExplicitSelfReceiver(target.Receiver))
        {
            return false;
        }

        var property = knownProperties.FirstOrDefault(property =>
            !property.IsStatic &&
            IsCurrentObjectMember(property.DeclaringTypeName, currentMethod) &&
            SemanticFacts.NameEquals(property.Name, target.MemberName.Text));
        if (property is not null)
        {
            diagnostics.Report(
                "ILC2248",
                $"Readonly routine '{currentMethod.Name}' cannot modify instance property '{SemanticFacts.GetExpressionDisplayName(target)}'. Use 'method' for mutating members.",
                DiagnosticSeverity.Error,
                target.MemberName.Span);
            return true;
        }

        var field = knownFields.FirstOrDefault(field =>
            !field.IsStatic &&
            IsCurrentObjectMember(field.DeclaringTypeName, currentMethod) &&
            SemanticFacts.NameEquals(field.Name, target.MemberName.Text));
        if (field is not null)
        {
            diagnostics.Report(
                "ILC2248",
                $"Readonly routine '{currentMethod.Name}' cannot modify instance field '{SemanticFacts.GetExpressionDisplayName(target)}'. Use 'method' for mutating members.",
                DiagnosticSeverity.Error,
                target.MemberName.Span);
            return true;
        }

        return false;
    }

    private static bool TryReportReadonlySelfMutation(
        QualifiedNameSyntax target,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            currentMethod?.DeclaringTypeName is null ||
            !IsExplicitSelfQualifiedName(target))
        {
            return false;
        }

        var property = knownProperties.FirstOrDefault(property =>
            !property.IsStatic &&
            IsCurrentObjectMember(property.DeclaringTypeName, currentMethod) &&
            SemanticFacts.NameEquals(property.Name, target.Parts[^1].Text));
        if (property is not null)
        {
            diagnostics.Report(
                "ILC2248",
                $"Readonly routine '{currentMethod.Name}' cannot modify instance property '{target.ToDisplayString()}'. Use 'method' for mutating members.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        var field = knownFields.FirstOrDefault(field =>
            !field.IsStatic &&
            IsCurrentObjectMember(field.DeclaringTypeName, currentMethod) &&
            SemanticFacts.NameEquals(field.Name, target.Parts[^1].Text));
        if (field is not null)
        {
            diagnostics.Report(
                "ILC2248",
                $"Readonly routine '{currentMethod.Name}' cannot modify instance field '{target.ToDisplayString()}'. Use 'method' for mutating members.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        return false;
    }

    private static bool TryReportReadonlySelfMethodCall(
        ExpressionSyntax target,
        int argumentCount,
        IEnumerable<MethodSymbol> knownMethods,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        if (!IsReadonlySelfRoutine(currentMethod) ||
            currentMethod?.DeclaringTypeName is null)
        {
            return false;
        }

        string? methodName = target switch
        {
            MemberAccessExpressionSyntax member when IsExplicitSelfReceiver(member.Receiver) => member.MemberName.Text,
            NameExpressionSyntax name when IsExplicitSelfQualifiedName(name.Name) => name.Name.Parts[^1].Text,
            _ => null
        };

        if (methodName is null)
        {
            return false;
        }

        var invokedMethod = knownMethods.FirstOrDefault(method =>
            !method.IsStatic &&
            IsMutatingSourceMethod(method) &&
            IsCurrentObjectMember(method.DeclaringTypeName, currentMethod) &&
            SemanticFacts.NameEquals(method.Name, methodName) &&
            SemanticFacts.SupportsArgumentCount(method, argumentCount));
        if (invokedMethod is null)
        {
            return false;
        }

        diagnostics.Report(
            "ILC2249",
            $"Readonly routine '{currentMethod.Name}' cannot call mutating method '{invokedMethod.Name}' on self. Use 'method' for mutating members.",
            DiagnosticSeverity.Error,
            GetExpressionDiagnosticSpan(target, []));
        return true;
    }

    private static void ReportInvalidResultUsage(TextSpan span, DiagnosticBag diagnostics)
    {
        diagnostics.Report(
            "ILC2242",
            "'Result' is only available inside functions with a return value.",
            DiagnosticSeverity.Error,
            span);
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

        var targetType = InferValidationExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (SemanticFacts.IsUnknownType(targetType))
        {
            return;
        }

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

        var targetType = InferValidationExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

        var valueType = InferValidationExpressionType(value, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
            ValidateSliceAccess(new NameExpressionSyntax(target), indexExpressions[0], locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, diagnostics);
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

            var resolvedIndexType = InferValidationExpressionType(indexExpressions[0], locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

        var indexedType = InferValidationExpressionType(new NameExpressionSyntax(target), locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
            var indexType = InferValidationExpressionType(indexExpression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod,
        DiagnosticBag diagnostics)
    {
        var targetType = InferValidationExpressionType(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        if (targetType != TypeSymbol.String &&
            (!SemanticFacts.IsArrayType(targetType) ||
             SemanticFacts.GetArrayRank(targetType) != 1 ||
             SemanticFacts.ResolveIndexerReference(target, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes) is not null))
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

        var startType = InferValidationExpressionType(range.Start, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
        var endType = InferValidationExpressionType(range.End, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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
        var receiverType = target.Parts.Count >= 2 && !SemanticFacts.NameEquals(target.Parts[0].Text, "self")
            ? SemanticFacts.TryResolveValueReceiverType(target, locals, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)
            : null;
        if (receiverType is not null)
        {
            var field = knownFields.FirstOrDefault(candidate =>
                SemanticFacts.NameEquals(candidate.DeclaringTypeName, receiverType.Name) &&
                SemanticFacts.NameEquals(candidate.Name, target.Parts[^1].Text));
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
        if (target.Parts.Count > 1 && SemanticFacts.NameEquals(qualifier, "self") && candidate.IsStatic)
        {
            diagnostics.Report(
                "ILC2111",
                $"Static field '{target.ToDisplayString()}' cannot be accessed through self.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count > 1 && qualifier is not null && !SemanticFacts.NameEquals(qualifier, "self") && !candidate.IsStatic)
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

    private static bool RequiresInvalidMethodAccessCheck(
        ExpressionSyntax target,
        InvocationResolution? invocation,
        MethodSymbol? currentMethod)
    {
        if (invocation is null)
        {
            return true;
        }

        return target is NameExpressionSyntax { Name.Parts.Count: 1 } &&
            !invocation.Method.IsStatic &&
            (currentMethod is null || currentMethod.IsStatic);
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
            if (candidate is not null && SemanticFacts.NameEquals(receiverDisplayName, "self") && candidate.Method.IsStatic)
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
                    SemanticFacts.NameEquals(method.DeclaringTypeName, receiverType.Name) &&
                    SemanticFacts.NameEquals(method.Name, target.MemberName.Text) &&
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
        if (target.Parts.Count > 1 && SemanticFacts.NameEquals(qualifier, "self") && candidate.Method.IsStatic)
        {
            diagnostics.Report(
                "ILC2108",
                $"Static method '{target.ToDisplayString()}' cannot be called through self.",
                DiagnosticSeverity.Error,
                target.Parts[^1].Span);
            return true;
        }

        if (target.Parts.Count > 1 && qualifier is not null && !SemanticFacts.NameEquals(qualifier, "self") && !candidate.Method.IsStatic)
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

        var receiverType = InferValidationExpressionType(memberAccess.Receiver, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes);
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

        if (knownTypes.Any(type => SemanticFacts.NameEquals(type.Name, name.Parts[0].Text)))
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
            SemanticFacts.NameEquals(method.DeclaringTypeName, declaringTypeName) &&
            SemanticFacts.NameEquals(method.Name, name) &&
            method.Parameters.Count == parameterCount);

    private static IReadOnlyList<FieldSymbol> FindFields(
        IEnumerable<FieldSymbol> knownFields,
        string? declaringTypeName) =>
        knownFields
            .Where(field => SemanticFacts.NameEquals(field.DeclaringTypeName, declaringTypeName))
            .ToArray();

    private static IReadOnlyList<FieldSymbol> FindInstanceFields(
        IEnumerable<FieldSymbol> knownFields,
        string? declaringTypeName) =>
        knownFields
            .Where(field => SemanticFacts.NameEquals(field.DeclaringTypeName, declaringTypeName) && !field.IsStatic)
            .ToArray();

    private static IReadOnlyList<ConstantSymbol> FindConstants(
        IEnumerable<ConstantSymbol> knownConstants,
        string? declaringTypeName) =>
        knownConstants
            .Where(constant => SemanticFacts.NameEquals(constant.DeclaringTypeName, declaringTypeName))
            .ToArray();

    private static bool HasDeclaredConstructors(
        QualifiedNameSyntax typeName,
        IEnumerable<TypeSymbol> knownTypes,
        IEnumerable<MethodSymbol> knownMethods)
    {
        var resolvedType = SemanticFacts.ResolveTypeReference(typeName.ToDisplayString(), knownTypes);
        return resolvedType is not null && knownMethods.Any(method => SemanticFacts.NameEquals(method.DeclaringTypeName, resolvedType.Name) && method.IsConstructor);
    }

    private static bool IsReferenceClassOrInterfaceType(TypeSymbol type) =>
        type == TypeSymbol.Object ||
        (type.IsReferenceType && type is NamedTypeSymbol {
            IsRecord: false or true,
            IsInterface: false or true
        });

    private static NamedTypeSymbol? ResolveNamedType(TypeSymbol type, IEnumerable<TypeSymbol> knownTypes) =>
        knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(candidate => SemanticFacts.NameEquals(candidate.Name, type.Name)) ??
        (SemanticFacts.ResolveTypeReference(type.Name, knownTypes) as NamedTypeSymbol) ??
        (type as NamedTypeSymbol);

    private static bool CreatesTypeCycle(string declaredTypeName, TypeSymbol baseType, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var visited = new HashSet<string>(SemanticFacts.NameComparer);
        var current = ResolveNamedType(baseType, knownTypes);
        while (current is not null && visited.Add(current.Name))
        {
            if (SemanticFacts.NameEquals(current.Name, declaredTypeName))
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
        var visited = new HashSet<string>(SemanticFacts.NameComparer);

        while (current is not null && visited.Add(current.Name))
        {
            yield return current;
            current = ResolveNamedType(current.BaseType ?? TypeSymbol.Object, knownTypes);
        }
    }

    private static IEnumerable<NamedTypeSymbol> GetInterfaceHierarchy(TypeSymbol interfaceType, IEnumerable<TypeSymbol> knownTypes)
    {
        var pending = new Queue<NamedTypeSymbol>();
        var visited = new HashSet<string>(SemanticFacts.NameComparer);
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
                            SemanticFacts.NameEquals(candidate.Name, interfaceMethod.Name) &&
                            !candidate.IsStatic)
                        .Concat(knownMethods.Where(candidate =>
                            SemanticFacts.NameEquals(candidate.DeclaringTypeName, type.Name) &&
                            SemanticFacts.NameEquals(candidate.Name, interfaceMethod.Name) &&
                            !candidate.IsStatic)))
                    .FirstOrDefault(candidate => AreInterfaceMethodSignaturesCompatible(interfaceMethod, candidate, knownTypes));

                if (implementation is null)
                {
                    var hierarchy = GetTypeHierarchy(new TypeSymbol(declaringTypeName, true), knownTypes).ToArray();
                    var candidates = hierarchy
                        .SelectMany(type => type.Methods
                            .Where(candidate =>
                                SemanticFacts.NameEquals(candidate.Name, interfaceMethod.Name) &&
                                !candidate.IsStatic)
                            .Concat(knownMethods.Where(candidate =>
                                SemanticFacts.NameEquals(candidate.DeclaringTypeName, type.Name) &&
                                SemanticFacts.NameEquals(candidate.Name, interfaceMethod.Name) &&
                                !candidate.IsStatic)))
                        .Select(candidate => $"{candidate.DeclaringTypeName}.{candidate.Name}({string.Join(", ", candidate.Parameters.Select(parameter => $"{parameter.PassingKind}:{parameter.Type.Name}"))}):{candidate.ReturnType.Name}")
                        .Distinct(SemanticFacts.NameComparer)
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
                SemanticFacts.NameEquals(knownMethod.DeclaringTypeName, baseTypeEntry.Name) &&
                SemanticFacts.NameEquals(knownMethod.Name, method.Name));

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
            SemanticFacts.NameEquals(candidate.DeclaringTypeName, declaringTypeName) &&
            SemanticFacts.NameEquals(candidate.Name, name) &&
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

        var invokeMethod = delegateType.Methods.FirstOrDefault(method => SemanticFacts.NameEquals(method.Name, "Invoke") && !method.IsStatic);
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

        var lambdaLocals = new Dictionary<string, TypeSymbol>(locals, SemanticFacts.NameComparer);
        var lambdaParameterNames = new Dictionary<string, SyntaxToken>(SemanticFacts.NameComparer);
        for (var parameterIndex = 0; parameterIndex < lambda.Parameters.Count; parameterIndex++)
        {
            var parameter = lambda.Parameters[parameterIndex];
            var delegateParameter = invokeMethod.Parameters[parameterIndex];
            var parameterType = BindType(parameter.TypeName, knownTypes);
            ReportNameCollisionIfNeeded(
                lambdaParameterNames,
                parameter.Identifier,
                "lambda parameter list",
                diagnostics,
                reportExactDuplicate: true);
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
        var bodyType = InferValidationExpressionType(lambda.Body, lambdaLocals, knownMethods, knownFields, knownConstants, knownProperties, null, knownTypes);
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
