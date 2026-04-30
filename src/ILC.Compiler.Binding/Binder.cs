namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public sealed partial class Binder
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

        var lambdaArtifacts = CollectSyntheticLambdaArtifacts(
            syntaxTree.Root.Members,
            declaredTypes,
            knownMethods,
            knownFields,
            knownConstants.Concat(topLevelConstants).ToArray(),
            knownProperties);
        foreach (var lambdaType in lambdaArtifacts.Types)
        {
            if (declaredTypes.All(existing => existing.Name != lambdaType.Name))
            {
                declaredTypes.Add(lambdaType);
            }
        }

        var projectorArtifacts = CollectSyntheticProjectorArtifacts(
            syntaxTree.Root.Members,
            declaredTypes,
            knownMethods.Concat(lambdaArtifacts.Methods).ToArray(),
            knownFields,
            knownConstants.Concat(topLevelConstants).ToArray(),
            knownProperties);
        foreach (var projectorType in projectorArtifacts.Types)
        {
            if (declaredTypes.All(existing => existing.Name != projectorType.Name))
            {
                declaredTypes.Add(projectorType);
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
            SymbolLists.CreateTypes([.. TypeSymbol.BuiltInScalarTypes, .. declaredTypes]),
            methods,
            topLevelConstants,
            globals,
            entryPoint);

        return new BindingResult(symbol, diagnostics);
    }

}
