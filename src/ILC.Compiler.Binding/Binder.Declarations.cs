namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public sealed partial class Binder
{
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

    private sealed record SyntheticProjectorArtifacts(
        IReadOnlyList<NamedTypeSymbol> Types);

    private static SyntheticLambdaArtifacts CollectSyntheticLambdaArtifacts(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties)
    {
        var types = new List<NamedTypeSymbol>();
        var methods = new List<MethodSymbol>();
        var nextLambdaId = 0;

        foreach (var classDeclaration in members.OfType<ClassDeclarationSyntax>())
        {
            var typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                foreach (var artifact in CollectLambdaArtifacts(
                             methodDeclaration,
                             classDeclaration.Identifier.Text,
                             typeScope,
                             knownMethods,
                             knownFields,
                             knownConstants,
                             knownProperties,
                             ref nextLambdaId))
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

    private static SyntheticProjectorArtifacts CollectSyntheticProjectorArtifacts(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties)
    {
        var types = new List<NamedTypeSymbol>();

        foreach (var classDeclaration in members.OfType<ClassDeclarationSyntax>())
        {
            var typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                foreach (var projectorType in CollectProjectorTypes(
                             methodDeclaration,
                             classDeclaration.Identifier.Text,
                             typeScope,
                             knownMethods,
                             knownFields,
                             knownConstants,
                             knownProperties))
                {
                    types.Add(projectorType);
                }
            }
        }

        return new SyntheticProjectorArtifacts(types);
    }

    private static IReadOnlyList<NamedTypeSymbol> CollectProjectorTypes(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties)
    {
        var boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
        var methodLocals = CollectMethodQueryScope(
            methodDeclaration,
            declaringTypeName,
            knownTypes,
            knownMethods,
            knownFields,
            knownConstants,
            knownProperties);
        var projectorTypes = new List<NamedTypeSymbol>();
        var projectorTypesBySignature = new Dictionary<string, NamedTypeSymbol>(StringComparer.Ordinal);

        void AddProjectorsFromExpression(ExpressionSyntax? expression, IReadOnlyDictionary<string, TypeSymbol> locals)
        {
            if (expression is null)
            {
                return;
            }

            var projectors = new List<ProjectorExpressionSyntax>();
            CollectProjectorExpressions(expression, projectors);
            foreach (var projector in projectors)
            {
                var signature = GetProjectorSignature(projector, locals, knownTypes, knownMethods, knownFields, knownConstants, knownProperties, boundMethod);
                if (!projectorTypesBySignature.TryGetValue(signature, out var projectorType))
                {
                    projectorType = BindSyntheticProjectorType(
                        projector,
                        signature,
                        locals,
                        knownTypes,
                        knownMethods,
                        knownFields,
                        knownConstants,
                        knownProperties,
                        boundMethod);
                    projectorTypesBySignature[signature] = projectorType;
                    projectorTypes.Add(projectorType);
                }
            }
        }

        AddProjectorsFromExpression(methodDeclaration.ExpressionBody, methodLocals);
        if (methodDeclaration.Body is not null)
        {
            foreach (var statement in methodDeclaration.Body.Statements)
            {
                if (statement is LocalVariableDeclarationStatementSyntax localDeclaration)
                {
                    foreach (var declarator in localDeclaration.Declarators)
                    {
                        AddProjectorsFromExpression(declarator.Initializer, methodLocals);
                    }
                }
                else if (statement is ExpressionStatementSyntax expressionStatement)
                {
                    AddProjectorsFromExpression(expressionStatement.Expression, methodLocals);
                }
                else if (statement is ReturnStatementSyntax returnStatement)
                {
                    AddProjectorsFromExpression(returnStatement.Expression, methodLocals);
                }
            }
        }

        var translatedLambdas = new List<LambdaExpressionSyntax>();
        CollectLambdaExpressions(methodDeclaration.ExpressionBody, translatedLambdas);
        CollectLambdaExpressions(methodDeclaration.Body, translatedLambdas);
        var queries = new List<QueryExpressionSyntax>();
        CollectQueryExpressions(methodDeclaration.ExpressionBody, queries);
        CollectQueryExpressions(methodDeclaration.Body, queries);
        foreach (var query in queries)
        {
            if (!SemanticFacts.TryTranslateQueryExpression(
                    query,
                    methodLocals,
                    knownTypes,
                    knownMethods,
                    knownFields,
                    knownConstants,
                    knownProperties,
                    boundMethod,
                    out var translatedQuery))
            {
                continue;
            }

            translatedLambdas.Clear();
            CollectLambdaExpressions(translatedQuery, translatedLambdas);
            foreach (var lambda in translatedLambdas)
            {
                var lambdaLocals = new Dictionary<string, TypeSymbol>(methodLocals, StringComparer.Ordinal);
                foreach (var parameter in lambda.Parameters)
                {
                    lambdaLocals[parameter.Identifier.Text] = BindType(parameter.TypeName, knownTypes);
                }

                AddProjectorsFromExpression(lambda.Body, lambdaLocals);
            }
        }

        return projectorTypes
            .GroupBy(type => type.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static string GetProjectorSignature(
        ProjectorExpressionSyntax projector,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol? currentMethod) =>
        string.Join(
            "|",
            projector.Members.Select(member =>
                $"{member.Identifier.Text}:{SemanticFacts.InferExpressionType(member.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes).Name}={SemanticFacts.GetExpressionDisplayName(member.Expression)}"));

    private static NamedTypeSymbol BindSyntheticProjectorType(
        ProjectorExpressionSyntax projector,
        string signature,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        MethodSymbol currentMethod)
    {
        var typeName = SemanticFacts.GetProjectorTypeName(signature);
        var fields = projector.Members
            .Select(member => new FieldSymbol(
                member.Identifier.Text,
                SemanticFacts.InferExpressionType(member.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes),
                typeName,
                false,
                null))
            .ToArray();

        var parameters = projector.Members
            .Select(member => new ParameterSymbol(
                member.Identifier.Text,
                SemanticFacts.InferExpressionType(member.Expression, locals, knownMethods, knownFields, knownConstants, knownProperties, currentMethod, knownTypes)))
            .ToArray();

        var constructorDeclaration = CreateSyntheticProjectorConstructor(typeName, parameters, fields);
        var constructorMethod = BindMethod(constructorDeclaration, typeName, knownTypes) with
        {
            IsSynthetic = true
        };

        return new NamedTypeSymbol(
            typeName,
            true,
            false,
            false,
            TypeSymbol.Object,
            [],
            [constructorMethod],
            fields,
            [],
            [],
            0,
            [],
            null,
            null,
            false,
            projector);
    }

    private static MethodDeclarationSyntax CreateSyntheticProjectorConstructor(
        string typeName,
        IReadOnlyList<ParameterSymbol> parameters,
        IReadOnlyList<FieldSymbol> fields)
    {
        var statements = new List<StatementSyntax>();
        for (var index = 0; index < parameters.Count && index < fields.Count; index++)
        {
            var field = fields[index];
            var parameter = parameters[index];
            statements.Add(new ExpressionStatementSyntax(
                new AssignmentExpressionSyntax(
                    new MemberAccessExpressionSyntax(
                        CreateNameExpression("self"),
                        CreateToken(SyntaxKind.DotToken, "."),
                        CreateToken(SyntaxKind.IdentifierToken, field.Name)),
                    CreateToken(SyntaxKind.AssignToken, ":="),
                    CreateNameExpression(parameter.Name)),
                CreateToken(SyntaxKind.SemicolonToken, ";")));
        }

        return new MethodDeclarationSyntax(
            [],
            [],
            CreateToken(SyntaxKind.ConstructorKeyword, "constructor"),
            CreateToken(SyntaxKind.IdentifierToken, typeName),
            CreateToken(SyntaxKind.OpenParenToken, "("),
            parameters.Select(parameter => new ParameterSyntax(
                null,
                CreateToken(SyntaxKind.IdentifierToken, parameter.Name),
                CreateToken(SyntaxKind.ColonToken, ":"),
                CreateQualifiedName(parameter.Type.Name))).ToArray(),
            CreateToken(SyntaxKind.CloseParenToken, ")"),
            null,
            null,
            null,
            null,
            new BlockStatementSyntax(
                CreateToken(SyntaxKind.BeginKeyword, "begin"),
                statements,
                CreateToken(SyntaxKind.EndKeyword, "end"),
                CreateToken(SyntaxKind.SemicolonToken, ";")),
            CreateToken(SyntaxKind.SemicolonToken, ";"));

        static NameExpressionSyntax CreateNameExpression(string displayName) =>
            new(CreateQualifiedName(displayName));

        static QualifiedNameSyntax CreateQualifiedName(string displayName)
        {
            var parts = displayName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => CreateToken(SyntaxKind.IdentifierToken, part))
                .ToArray();
            return new QualifiedNameSyntax(parts);
        }

        static SyntaxToken CreateToken(SyntaxKind kind, string text) =>
            new(kind, text, null, new TextSpan(0, text.Length));
    }

    private static IReadOnlyList<SyntheticLambdaArtifact> CollectLambdaArtifacts(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        ref int nextLambdaId)
    {
        var outerLocals = CollectMethodLambdaCaptureScope(methodDeclaration, knownTypes);
        var queryLocals = CollectMethodQueryScope(
            methodDeclaration,
            declaringTypeName,
            knownTypes,
            knownMethods,
            knownFields,
            knownConstants,
            knownProperties);
        var lambdas = new List<LambdaExpressionSyntax>();
        CollectLambdaExpressions(methodDeclaration.ExpressionBody, lambdas);
        CollectLambdaExpressions(methodDeclaration.Body, lambdas);
        var queries = new List<QueryExpressionSyntax>();
        CollectQueryExpressions(methodDeclaration.ExpressionBody, queries);
        CollectQueryExpressions(methodDeclaration.Body, queries);
        var boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
        foreach (var query in queries)
        {
            if (SemanticFacts.TryTranslateQueryExpression(
                    query,
                    queryLocals,
                    knownTypes,
                    knownMethods,
                    knownFields,
                    knownConstants,
                    knownProperties,
                    boundMethod,
                    out var translatedQuery))
            {
                CollectLambdaExpressions(translatedQuery, lambdas);
            }
        }

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

    private static Dictionary<string, TypeSymbol> CollectMethodQueryScope(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties)
    {
        var boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
        var locals = methodDeclaration.Parameters.ToDictionary(
            parameter => parameter.Identifier.Text,
            parameter => BindType(parameter.TypeName, knownTypes),
            StringComparer.Ordinal);

        if (methodDeclaration.Body is null)
        {
            return locals;
        }

        foreach (var statement in methodDeclaration.Body.Statements)
        {
            if (statement is not LocalVariableDeclarationStatementSyntax localDeclaration)
            {
                continue;
            }

            foreach (var declarator in localDeclaration.Declarators)
            {
                var localType = declarator.TypeName is not null
                    ? BindType(declarator.TypeName, knownTypes)
                    : declarator.Initializer is not null
                        ? SemanticFacts.InferExpressionType(
                            declarator.Initializer,
                            locals,
                            knownMethods,
                            knownFields,
                            knownConstants,
                            knownProperties,
                            boundMethod,
                            knownTypes)
                        : TypeSymbol.Integer;
                locals[declarator.Identifier.Text] = localType;
            }
        }

        return locals;
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

    private static void CollectQueryExpressions(object? value, List<QueryExpressionSyntax> queries)
    {
        if (value is null or string or SyntaxToken)
        {
            return;
        }

        if (value is QueryExpressionSyntax query)
        {
            queries.Add(query);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectQueryExpressions(item, queries);
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

            CollectQueryExpressions(property.GetValue(value), queries);
        }
    }

    private static void CollectProjectorExpressions(object? value, List<ProjectorExpressionSyntax> projectors)
    {
        if (value is null or string or SyntaxToken)
        {
            return;
        }

        if (value is ProjectorExpressionSyntax projector)
        {
            projectors.Add(projector);
        }

        if (value is System.Collections.IEnumerable enumerable and not SyntaxNode)
        {
            foreach (var item in enumerable)
            {
                CollectProjectorExpressions(item, projectors);
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

            CollectProjectorExpressions(property.GetValue(value), projectors);
        }
    }
}
