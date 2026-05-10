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

    private static IReadOnlyList<NamedTypeSymbol> CollectConstructedGenericTypes(
        SyntaxTree syntaxTree,
        IReadOnlyList<TypeSymbol> knownTypes,
        BindingProfiler? profiler = null)
    {
        var genericTypeNames = new HashSet<string>(StringComparer.Ordinal);
        using (Profile(profiler, "CollectConstructedGenericTypes.ScanSyntax"))
        {
            CollectConstructedGenericTypeNames(syntaxTree.Root, genericTypeNames);
        }

        using (Profile(profiler, "CollectConstructedGenericTypes.ResolveNames"))
        {
            return genericTypeNames
                .Select(typeName => SemanticFacts.ResolveTypeReference(typeName, knownTypes))
                .OfType<NamedTypeSymbol>()
                .Where(type => type.GenericDefinition is not null)
                .ToArray();
        }
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

    private static void CollectConstructedGenericTypeNames(CompilationUnitSyntax root, HashSet<string> genericTypeNames)
    {
        foreach (var member in root.Members)
        {
            CollectConstructedGenericTypeNames(member, genericTypeNames);
        }
    }

    private static void CollectConstructedGenericTypeNames(MemberSyntax member, HashSet<string> genericTypeNames)
    {
        switch (member)
        {
            case TopLevelVariableDeclarationSyntax variableDeclaration:
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    AddConstructedGenericTypeName(declarator.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(declarator.Initializer, genericTypeNames);
                }
                break;
            case TopLevelConstantDeclarationSyntax constantDeclaration:
                foreach (var declarator in constantDeclaration.Declarators)
                {
                    AddConstructedGenericTypeName(declarator.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(declarator.Initializer, genericTypeNames);
                }
                break;
            case DelegateDeclarationSyntax delegateDeclaration:
                AddConstructedGenericTypeName(delegateDeclaration.ReturnType, genericTypeNames);
                foreach (var parameter in delegateDeclaration.Parameters)
                {
                    AddConstructedGenericTypeName(parameter.TypeName, genericTypeNames);
                }
                break;
            case TopLevelExpressionStatementSyntax expressionStatement:
                CollectConstructedGenericTypeNames(expressionStatement.Expression, genericTypeNames);
                break;
            case ClassDeclarationSyntax classDeclaration:
                AddConstructedGenericTypeName(classDeclaration.BaseType, genericTypeNames);
                foreach (var interfaceType in classDeclaration.InterfaceTypes)
                {
                    AddConstructedGenericTypeName(interfaceType, genericTypeNames);
                }

                foreach (var typeMember in classDeclaration.Members)
                {
                    CollectConstructedGenericTypeNames(typeMember, genericTypeNames);
                }
                break;
            case InterfaceDeclarationSyntax interfaceDeclaration:
                foreach (var baseInterface in interfaceDeclaration.BaseInterfaces)
                {
                    AddConstructedGenericTypeName(baseInterface, genericTypeNames);
                }

                foreach (var typeMember in interfaceDeclaration.Members)
                {
                    CollectConstructedGenericTypeNames(typeMember, genericTypeNames);
                }
                break;
        }
    }

    private static void CollectConstructedGenericTypeNames(TypeMemberSyntax member, HashSet<string> genericTypeNames)
    {
        switch (member)
        {
            case FieldDeclarationSyntax fieldDeclaration:
                foreach (var declarator in fieldDeclaration.Declarators)
                {
                    AddConstructedGenericTypeName(declarator.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(declarator.Initializer, genericTypeNames);
                }
                break;
            case ConstantDeclarationSyntax constantDeclaration:
                foreach (var declarator in constantDeclaration.Declarators)
                {
                    AddConstructedGenericTypeName(declarator.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(declarator.Initializer, genericTypeNames);
                }
                break;
            case PropertyDeclarationSyntax propertyDeclaration:
                AddConstructedGenericTypeName(propertyDeclaration.IndexParameter?.TypeName, genericTypeNames);
                AddConstructedGenericTypeName(propertyDeclaration.TypeName, genericTypeNames);
                AddConstructedGenericTypeName(propertyDeclaration.ReadTarget, genericTypeNames);
                AddConstructedGenericTypeName(propertyDeclaration.WriteTarget, genericTypeNames);
                CollectConstructedGenericTypeNames(propertyDeclaration.GetterBody, genericTypeNames);
                CollectConstructedGenericTypeNames(propertyDeclaration.SetterBody, genericTypeNames);
                break;
            case MethodDeclarationSyntax methodDeclaration:
                foreach (var attribute in methodDeclaration.Attributes)
                {
                    AddConstructedGenericTypeName(attribute.Name, genericTypeNames);
                    foreach (var argument in attribute.Arguments)
                    {
                        CollectConstructedGenericTypeNames(argument.Expression, genericTypeNames);
                    }
                }

                foreach (var parameter in methodDeclaration.Parameters)
                {
                    AddConstructedGenericTypeName(parameter.TypeName, genericTypeNames);
                }

                AddConstructedGenericTypeName(methodDeclaration.ReturnType, genericTypeNames);
                CollectConstructedGenericTypeNames(methodDeclaration.ExpressionBody, genericTypeNames);
                CollectConstructedGenericTypeNames(methodDeclaration.Body, genericTypeNames);
                break;
        }
    }

    private static void CollectConstructedGenericTypeNames(StatementSyntax? statement, HashSet<string> genericTypeNames)
    {
        switch (statement)
        {
            case null:
                return;
            case BlockStatementSyntax block:
                foreach (var child in block.Statements)
                {
                    CollectConstructedGenericTypeNames(child, genericTypeNames);
                }
                break;
            case IfStatementSyntax ifStatement:
                CollectConstructedGenericTypeNames(ifStatement.Condition, genericTypeNames);
                CollectConstructedGenericTypeNames(ifStatement.ThenStatement, genericTypeNames);
                CollectConstructedGenericTypeNames(ifStatement.ElseStatement, genericTypeNames);
                break;
            case WhileStatementSyntax whileStatement:
                CollectConstructedGenericTypeNames(whileStatement.Condition, genericTypeNames);
                CollectConstructedGenericTypeNames(whileStatement.Body, genericTypeNames);
                break;
            case RepeatStatementSyntax repeatStatement:
                foreach (var child in repeatStatement.Statements)
                {
                    CollectConstructedGenericTypeNames(child, genericTypeNames);
                }
                CollectConstructedGenericTypeNames(repeatStatement.Condition, genericTypeNames);
                break;
            case ForStatementSyntax forStatement:
                CollectConstructedGenericTypeNames(forStatement.LowerBound, genericTypeNames);
                CollectConstructedGenericTypeNames(forStatement.UpperBound, genericTypeNames);
                CollectConstructedGenericTypeNames(forStatement.StepExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(forStatement.Body, genericTypeNames);
                break;
            case ForeachStatementSyntax foreachStatement:
                CollectConstructedGenericTypeNames(foreachStatement.Collection, genericTypeNames);
                CollectConstructedGenericTypeNames(foreachStatement.Body, genericTypeNames);
                break;
            case WithStatementSyntax withStatement:
                CollectConstructedGenericTypeNames(withStatement.Receiver, genericTypeNames);
                CollectConstructedGenericTypeNames(withStatement.Body, genericTypeNames);
                break;
            case CaseStatementSyntax caseStatement:
                CollectConstructedGenericTypeNames(caseStatement.Expression, genericTypeNames);
                foreach (var clause in caseStatement.Clauses)
                {
                    foreach (var label in clause.Labels)
                    {
                        CollectConstructedGenericTypeNames(label, genericTypeNames);
                    }
                    CollectConstructedGenericTypeNames(clause.Guard, genericTypeNames);
                    CollectConstructedGenericTypeNames(clause.Body, genericTypeNames);
                }
                foreach (var elseStatement in caseStatement.ElseStatements)
                {
                    CollectConstructedGenericTypeNames(elseStatement, genericTypeNames);
                }
                break;
            case MatchStatementSyntax matchStatement:
                CollectConstructedGenericTypeNames(matchStatement.Expression, genericTypeNames);
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectConstructedGenericTypeNames(label, genericTypeNames);
                    }
                    AddConstructedGenericTypeName(arm.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(arm.Guard, genericTypeNames);
                    CollectConstructedGenericTypeNames(arm.Body, genericTypeNames);
                }
                foreach (var elseStatement in matchStatement.ElseStatements)
                {
                    CollectConstructedGenericTypeNames(elseStatement, genericTypeNames);
                }
                break;
            case ReturnStatementSyntax returnStatement:
                CollectConstructedGenericTypeNames(returnStatement.Expression, genericTypeNames);
                break;
            case RaiseStatementSyntax raiseStatement:
                CollectConstructedGenericTypeNames(raiseStatement.Expression, genericTypeNames);
                break;
            case TryStatementSyntax tryStatement:
                foreach (var child in tryStatement.TryStatements)
                {
                    CollectConstructedGenericTypeNames(child, genericTypeNames);
                }
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    AddConstructedGenericTypeName(clause.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(clause.Body, genericTypeNames);
                }
                foreach (var child in tryStatement.ExceptStatements)
                {
                    CollectConstructedGenericTypeNames(child, genericTypeNames);
                }
                foreach (var child in tryStatement.FinallyStatements)
                {
                    CollectConstructedGenericTypeNames(child, genericTypeNames);
                }
                break;
            case LocalVariableDeclarationStatementSyntax localDeclaration:
                foreach (var declarator in localDeclaration.Declarators)
                {
                    AddConstructedGenericTypeName(declarator.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(declarator.Initializer, genericTypeNames);
                }
                break;
            case IncStatementSyntax incStatement:
                CollectConstructedGenericTypeNames(incStatement.Target, genericTypeNames);
                break;
            case DecStatementSyntax decStatement:
                CollectConstructedGenericTypeNames(decStatement.Target, genericTypeNames);
                break;
            case IncludeStatementSyntax includeStatement:
                CollectConstructedGenericTypeNames(includeStatement.Target, genericTypeNames);
                CollectConstructedGenericTypeNames(includeStatement.Value, genericTypeNames);
                break;
            case ExcludeStatementSyntax excludeStatement:
                CollectConstructedGenericTypeNames(excludeStatement.Target, genericTypeNames);
                CollectConstructedGenericTypeNames(excludeStatement.Value, genericTypeNames);
                break;
            case ExpressionStatementSyntax expressionStatement:
                CollectConstructedGenericTypeNames(expressionStatement.Expression, genericTypeNames);
                break;
        }
    }

    private static void CollectConstructedGenericTypeNames(ExpressionSyntax? expression, HashSet<string> genericTypeNames)
    {
        switch (expression)
        {
            case null:
                return;
            case UnaryExpressionSyntax unary:
                CollectConstructedGenericTypeNames(unary.Operand, genericTypeNames);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                CollectConstructedGenericTypeNames(parenthesized.Expression, genericTypeNames);
                break;
            case SetLiteralExpressionSyntax setLiteral:
                foreach (var element in setLiteral.Elements)
                {
                    CollectConstructedGenericTypeNames(element, genericTypeNames);
                }
                break;
            case ProjectorExpressionSyntax projector:
                foreach (var member in projector.Members)
                {
                    CollectConstructedGenericTypeNames(member.Expression, genericTypeNames);
                }
                break;
            case RangeExpressionSyntax range:
                CollectConstructedGenericTypeNames(range.Start, genericTypeNames);
                CollectConstructedGenericTypeNames(range.End, genericTypeNames);
                break;
            case NewExpressionSyntax newExpression:
                AddConstructedGenericTypeName(newExpression.TypeName, genericTypeNames);
                foreach (var argument in newExpression.Arguments)
                {
                    CollectConstructedGenericTypeNames(argument.Expression, genericTypeNames);
                }
                break;
            case NewArrayExpressionSyntax newArray:
                AddConstructedGenericTypeName(newArray.ElementTypeName, genericTypeNames);
                foreach (var lengthExpression in newArray.LengthExpressions)
                {
                    CollectConstructedGenericTypeNames(lengthExpression, genericTypeNames);
                }
                break;
            case NameExpressionSyntax name:
                AddConstructedGenericTypeName(name.Name, genericTypeNames);
                break;
            case ArrayLengthExpressionSyntax arrayLength:
                AddConstructedGenericTypeName(arrayLength.Target, genericTypeNames);
                break;
            case ElementAccessExpressionSyntax elementAccess:
                AddConstructedGenericTypeName(elementAccess.Target, genericTypeNames);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectConstructedGenericTypeNames(indexExpression, genericTypeNames);
                }
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                CollectConstructedGenericTypeNames(elementAccess.Target, genericTypeNames);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectConstructedGenericTypeNames(indexExpression, genericTypeNames);
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                CollectConstructedGenericTypeNames(memberAccess.Receiver, genericTypeNames);
                break;
            case AssignmentExpressionSyntax assignment:
                CollectConstructedGenericTypeNames(assignment.Target, genericTypeNames);
                CollectConstructedGenericTypeNames(assignment.Expression, genericTypeNames);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                CollectConstructedGenericTypeNames(assignment.Target, genericTypeNames);
                CollectConstructedGenericTypeNames(assignment.Expression, genericTypeNames);
                break;
            case BinaryExpressionSyntax binary:
                CollectConstructedGenericTypeNames(binary.Left, genericTypeNames);
                CollectConstructedGenericTypeNames(binary.Right, genericTypeNames);
                break;
            case MatchNotPatternSyntax notPattern:
                CollectConstructedGenericTypeNames(notPattern.Pattern, genericTypeNames);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    CollectConstructedGenericTypeNames(pattern, genericTypeNames);
                }
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    CollectConstructedGenericTypeNames(pattern, genericTypeNames);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                CollectConstructedGenericTypeNames(relational.Operand, genericTypeNames);
                break;
            case TypeTestExpressionSyntax typeTest:
                CollectConstructedGenericTypeNames(typeTest.Expression, genericTypeNames);
                AddConstructedGenericTypeName(typeTest.TypeName, genericTypeNames);
                break;
            case AsExpressionSyntax asExpression:
                CollectConstructedGenericTypeNames(asExpression.Expression, genericTypeNames);
                AddConstructedGenericTypeName(asExpression.TypeName, genericTypeNames);
                break;
            case CallExpressionSyntax call:
                CollectConstructedGenericTypeNames(call.Target, genericTypeNames);
                foreach (var argument in call.Arguments)
                {
                    CollectConstructedGenericTypeNames(argument.Expression, genericTypeNames);
                }
                break;
            case QueryExpressionSyntax query:
                CollectConstructedGenericTypeNames(query.SourceExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.JoinSourceExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.JoinLeftExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.JoinRightExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.SecondSourceExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.LetExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.PredicateExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.OrderByExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ThenByExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.GroupExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.GroupByExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.SelectExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ContinuationLetExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ContinuationPredicateExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ContinuationOrderByExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ContinuationThenByExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.ContinuationSelectExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.TakeExpression, genericTypeNames);
                CollectConstructedGenericTypeNames(query.SkipExpression, genericTypeNames);
                break;
            case LambdaExpressionSyntax lambda:
                foreach (var parameter in lambda.Parameters)
                {
                    AddConstructedGenericTypeName(parameter.TypeName, genericTypeNames);
                }
                AddConstructedGenericTypeName(lambda.ReturnType, genericTypeNames);
                CollectConstructedGenericTypeNames(lambda.Body, genericTypeNames);
                break;
            case MatchExpressionSyntax matchExpression:
                CollectConstructedGenericTypeNames(matchExpression.Expression, genericTypeNames);
                foreach (var arm in matchExpression.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectConstructedGenericTypeNames(label, genericTypeNames);
                    }
                    AddConstructedGenericTypeName(arm.TypeName, genericTypeNames);
                    CollectConstructedGenericTypeNames(arm.Guard, genericTypeNames);
                    CollectConstructedGenericTypeNames(arm.Expression, genericTypeNames);
                }
                break;
        }
    }

    private static void AddConstructedGenericTypeName(QualifiedNameSyntax? typeName, HashSet<string> genericTypeNames)
    {
        if (typeName is null)
        {
            return;
        }

        var displayName = typeName.ToDisplayString();
        if (displayName.Contains('<', StringComparison.Ordinal))
        {
            genericTypeNames.Add(displayName);
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
        IReadOnlyList<PropertySymbol> knownProperties,
        Dictionary<object, Dictionary<string, TypeSymbol>>? methodQueryScopeCache = null,
        BindingProfiler? profiler = null)
    {
        var types = new List<NamedTypeSymbol>();
        var methods = new List<MethodSymbol>();
        var nextLambdaId = 0;

        foreach (var classDeclaration in members.OfType<ClassDeclarationSyntax>())
        {
            TypeSymbol[] typeScope;
            using (Profile(profiler, "CollectSyntheticLambdaArtifacts.BuildTypeScope"))
            {
                typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
            }

            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                using (Profile(profiler, $"CollectSyntheticLambdaArtifacts.Method:{classDeclaration.Identifier.Text}.{methodDeclaration.Identifier.Text}"))
                {
                    foreach (var artifact in CollectLambdaArtifacts(
                                 methodDeclaration,
                                 classDeclaration.Identifier.Text,
                                 typeScope,
                                 knownMethods,
                                 knownFields,
                                 knownConstants,
                                 knownProperties,
                                 ref nextLambdaId,
                                 methodQueryScopeCache,
                                 profiler))
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
        }

        return new SyntheticLambdaArtifacts(types, methods);
    }

    private static SyntheticProjectorArtifacts CollectSyntheticProjectorArtifacts(
        IReadOnlyList<MemberSyntax> members,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        BindingProfiler? profiler = null)
    {
        var types = new List<NamedTypeSymbol>();

        foreach (var classDeclaration in members.OfType<ClassDeclarationSyntax>())
        {
            TypeSymbol[] typeScope;
            using (Profile(profiler, "CollectSyntheticProjectorArtifacts.BuildTypeScope"))
            {
                typeScope = knownTypes.Concat(BindTypeParameters(classDeclaration.TypeParameters).Cast<TypeSymbol>()).ToArray();
            }

            foreach (var methodDeclaration in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                using (Profile(profiler, $"CollectSyntheticProjectorArtifacts.Method:{classDeclaration.Identifier.Text}.{methodDeclaration.Identifier.Text}"))
                {
                    foreach (var projectorType in CollectProjectorTypes(
                                 methodDeclaration,
                                 classDeclaration.Identifier.Text,
                                 typeScope,
                                 knownMethods,
                                 knownFields,
                                 knownConstants,
                                 knownProperties,
                                 profiler))
                    {
                        types.Add(projectorType);
                    }
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
        IReadOnlyList<PropertySymbol> knownProperties,
        BindingProfiler? profiler = null)
    {
        var directProjectors = new List<ProjectorExpressionSyntax>();
        using (Profile(profiler, "CollectProjectorTypes.ScanDirectProjectors"))
        {
            CollectDirectProjectorExpressions(methodDeclaration, directProjectors);
        }

        var queries = new List<QueryExpressionSyntax>();
        using (Profile(profiler, "CollectProjectorTypes.CollectQueryExpressions"))
        {
            CollectQueryExpressions(methodDeclaration.ExpressionBody, queries);
            CollectQueryExpressions(methodDeclaration.Body, queries);
        }

        if (directProjectors.Count == 0 && queries.Count == 0)
        {
            return [];
        }

        MethodSymbol boundMethod;
        using (Profile(profiler, "CollectProjectorTypes.BindMethod"))
        {
            boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
        }

        var methodScopeRoots = new List<SyntaxNode>(directProjectors.Count + queries.Count);
        methodScopeRoots.AddRange(directProjectors);
        methodScopeRoots.AddRange(queries);
        Dictionary<string, TypeSymbol> methodLocals;
        using (Profile(profiler, "CollectProjectorTypes.CollectMethodQueryScope"))
        {
            methodLocals = CollectFilteredMethodQueryScope(
                methodDeclaration,
                declaringTypeName,
                knownTypes,
                knownMethods,
                knownFields,
                knownConstants,
                knownProperties,
                methodScopeRoots);
        }

        var projectorTypes = new List<NamedTypeSymbol>();
        var projectorTypesBySignature = new Dictionary<string, NamedTypeSymbol>(StringComparer.Ordinal);

        void AddProjector(ProjectorExpressionSyntax projector, IReadOnlyDictionary<string, TypeSymbol> locals)
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

        using (Profile(profiler, "CollectProjectorTypes.BindDirectProjectors"))
        {
            foreach (var projector in directProjectors)
            {
                AddProjector(projector, methodLocals);
            }
        }

        var translatedLambdas = new List<LambdaExpressionSyntax>();
        using (Profile(profiler, "CollectProjectorTypes.TranslateQueries"))
        {
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

                    var lambdaProjectors = new List<ProjectorExpressionSyntax>();
                    CollectProjectorExpressions(lambda.Body, lambdaProjectors);
                    foreach (var projector in lambdaProjectors)
                    {
                        AddProjector(projector, lambdaLocals);
                    }
                }
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
        ref int nextLambdaId,
        Dictionary<object, Dictionary<string, TypeSymbol>>? methodQueryScopeCache = null,
        BindingProfiler? profiler = null)
    {
        var lambdas = new List<LambdaExpressionSyntax>();
        using (Profile(profiler, "CollectLambdaArtifacts.CollectLambdaExpressions"))
        {
            CollectLambdaExpressions(methodDeclaration.ExpressionBody, lambdas);
            CollectLambdaExpressions(methodDeclaration.Body, lambdas);
        }

        var queries = new List<QueryExpressionSyntax>();
        using (Profile(profiler, "CollectLambdaArtifacts.CollectQueryExpressions"))
        {
            CollectQueryExpressions(methodDeclaration.ExpressionBody, queries);
            CollectQueryExpressions(methodDeclaration.Body, queries);
        }

        if (lambdas.Count == 0 && queries.Count == 0)
        {
            return [];
        }

        if (queries.Count > 0)
        {
            Dictionary<string, TypeSymbol> queryLocals;
            using (Profile(profiler, "CollectLambdaArtifacts.CollectQueryScope"))
            {
                queryLocals = CollectFilteredMethodQueryScope(
                    methodDeclaration,
                    declaringTypeName,
                    knownTypes,
                    knownMethods,
                    knownFields,
                    knownConstants,
                    knownProperties,
                    queries);
            }

            MethodSymbol boundMethod;
            using (Profile(profiler, "CollectLambdaArtifacts.BindMethod"))
            {
                boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
            }

            using (Profile(profiler, "CollectLambdaArtifacts.TranslateQueries"))
            {
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
            }
        }

        if (lambdas.Count == 0)
        {
            return [];
        }

        Dictionary<string, TypeSymbol> outerLocals;
        using (Profile(profiler, "CollectLambdaArtifacts.CollectCaptureScope"))
        {
            outerLocals = CollectMethodLambdaCaptureScope(methodDeclaration, knownTypes);
        }

        var artifacts = new List<SyntheticLambdaArtifact>();
        using (Profile(profiler, "CollectLambdaArtifacts.BuildArtifacts"))
        {
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
        }

        return artifacts;
    }

    private static Dictionary<string, TypeSymbol> GetCachedMethodQueryScope(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        Dictionary<object, Dictionary<string, TypeSymbol>>? methodQueryScopeCache)
    {
        if (methodQueryScopeCache is null)
        {
            return CollectMethodQueryScope(methodDeclaration, declaringTypeName, knownTypes, knownMethods, knownFields, knownConstants, knownProperties);
        }

        if (!methodQueryScopeCache.TryGetValue(methodDeclaration, out var cachedScope))
        {
            cachedScope = CollectMethodQueryScope(methodDeclaration, declaringTypeName, knownTypes, knownMethods, knownFields, knownConstants, knownProperties);
            methodQueryScopeCache.Add(methodDeclaration, cachedScope);
        }

        return new Dictionary<string, TypeSymbol>(cachedScope, StringComparer.Ordinal);
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

    private static Dictionary<string, TypeSymbol> CollectFilteredMethodQueryScope(
        MethodDeclarationSyntax methodDeclaration,
        string declaringTypeName,
        IReadOnlyList<TypeSymbol> knownTypes,
        IReadOnlyList<MethodSymbol> knownMethods,
        IReadOnlyList<FieldSymbol> knownFields,
        IReadOnlyList<ConstantSymbol> knownConstants,
        IReadOnlyList<PropertySymbol> knownProperties,
        IReadOnlyList<SyntaxNode> referencedRoots)
    {
        var locals = methodDeclaration.Parameters.ToDictionary(
            parameter => parameter.Identifier.Text,
            parameter => BindType(parameter.TypeName, knownTypes),
            StringComparer.Ordinal);
        if (methodDeclaration.Body is null || referencedRoots.Count == 0)
        {
            return locals;
        }

        var declarationList = methodDeclaration.Body.Statements
            .OfType<LocalVariableDeclarationStatementSyntax>()
            .SelectMany(statement => statement.Declarators)
            .ToArray();
        var declarations = new Dictionary<string, VariableDeclaratorSyntax>(StringComparer.Ordinal);
        foreach (var declarator in declarationList)
        {
            declarations[declarator.Identifier.Text] = declarator;
        }

        if (declarations.Count == 0)
        {
            return locals;
        }

        var neededLocals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var referencedRoot in referencedRoots)
        {
            CollectReferencedNames(referencedRoot, neededLocals);
        }

        neededLocals.RemoveWhere(name => !declarations.ContainsKey(name));
        var pending = new Queue<string>(neededLocals);
        while (pending.Count > 0)
        {
            var localName = pending.Dequeue();
            if (!declarations.TryGetValue(localName, out var declarator) || declarator.Initializer is null)
            {
                continue;
            }

            var referencedNames = new HashSet<string>(StringComparer.Ordinal);
            CollectReferencedNames(declarator.Initializer, referencedNames);
            foreach (var referencedName in referencedNames)
            {
                if (declarations.ContainsKey(referencedName) && neededLocals.Add(referencedName))
                {
                    pending.Enqueue(referencedName);
                }
            }
        }

        var boundMethod = BindMethod(methodDeclaration, declaringTypeName, knownTypes);
        foreach (var declarator in declarationList)
        {
            if (!neededLocals.Contains(declarator.Identifier.Text))
            {
                continue;
            }

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

        return locals;
    }

    private static void CollectDirectProjectorExpressions(
        MethodDeclarationSyntax methodDeclaration,
        List<ProjectorExpressionSyntax> projectors)
    {
        CollectProjectorExpressions(methodDeclaration.ExpressionBody, projectors);
        if (methodDeclaration.Body is null)
        {
            return;
        }

        foreach (var statement in methodDeclaration.Body.Statements)
        {
            if (statement is LocalVariableDeclarationStatementSyntax localDeclaration)
            {
                foreach (var declarator in localDeclaration.Declarators)
                {
                    CollectProjectorExpressions(declarator.Initializer, projectors);
                }
            }
            else if (statement is ExpressionStatementSyntax expressionStatement)
            {
                CollectProjectorExpressions(expressionStatement.Expression, projectors);
            }
            else if (statement is ReturnStatementSyntax returnStatement)
            {
                CollectProjectorExpressions(returnStatement.Expression, projectors);
            }
        }
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
        StatementSyntax? statement,
        Dictionary<string, TypeSymbol> locals,
        IReadOnlyList<TypeSymbol> knownTypes)
    {
        switch (statement)
        {
            case null:
                return;
            case BlockStatementSyntax block:
                foreach (var child in block.Statements)
                {
                    CollectDeclaredLocals(child, locals, knownTypes);
                }
                break;
            case IfStatementSyntax ifStatement:
                CollectDeclaredLocals(ifStatement.ThenStatement, locals, knownTypes);
                CollectDeclaredLocals(ifStatement.ElseStatement, locals, knownTypes);
                break;
            case WhileStatementSyntax whileStatement:
                CollectDeclaredLocals(whileStatement.Body, locals, knownTypes);
                break;
            case RepeatStatementSyntax repeatStatement:
                foreach (var child in repeatStatement.Statements)
                {
                    CollectDeclaredLocals(child, locals, knownTypes);
                }
                break;
            case ForStatementSyntax forStatement:
                CollectDeclaredLocals(forStatement.Body, locals, knownTypes);
                break;
            case ForeachStatementSyntax foreachStatement:
                CollectDeclaredLocals(foreachStatement.Body, locals, knownTypes);
                break;
            case WithStatementSyntax withStatement:
                CollectDeclaredLocals(withStatement.Body, locals, knownTypes);
                break;
            case CaseStatementSyntax caseStatement:
                foreach (var clause in caseStatement.Clauses)
                {
                    CollectDeclaredLocals(clause.Body, locals, knownTypes);
                }
                foreach (var elseStatement in caseStatement.ElseStatements)
                {
                    CollectDeclaredLocals(elseStatement, locals, knownTypes);
                }
                break;
            case MatchStatementSyntax matchStatement:
                foreach (var arm in matchStatement.Arms)
                {
                    CollectDeclaredLocals(arm.Body, locals, knownTypes);
                }
                foreach (var elseStatement in matchStatement.ElseStatements)
                {
                    CollectDeclaredLocals(elseStatement, locals, knownTypes);
                }
                break;
            case TryStatementSyntax tryStatement:
                foreach (var child in tryStatement.TryStatements)
                {
                    CollectDeclaredLocals(child, locals, knownTypes);
                }
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    locals[clause.Identifier.Text] = BindType(clause.TypeName, knownTypes);
                    CollectDeclaredLocals(clause.Body, locals, knownTypes);
                }
                foreach (var child in tryStatement.ExceptStatements)
                {
                    CollectDeclaredLocals(child, locals, knownTypes);
                }
                foreach (var child in tryStatement.FinallyStatements)
                {
                    CollectDeclaredLocals(child, locals, knownTypes);
                }
                break;
            case LocalVariableDeclarationStatementSyntax localDeclaration:
                foreach (var declarator in localDeclaration.Declarators)
                {
                    locals[declarator.Identifier.Text] = declarator.TypeName is not null
                        ? BindType(declarator.TypeName, knownTypes)
                        : TypeSymbol.Integer;
                }
                break;
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

    private static void CollectReferencedNames(SyntaxNode? node, HashSet<string> names)
    {
        switch (node)
        {
            case null:
                return;
            case ExpressionSyntax expression:
                CollectReferencedNames(expression, names);
                break;
            case StatementSyntax statement:
                CollectReferencedNames(statement, names);
                break;
        }
    }

    private static void CollectReferencedNames(StatementSyntax? statement, HashSet<string> names)
    {
        switch (statement)
        {
            case null:
                return;
            case BlockStatementSyntax block:
                foreach (var child in block.Statements)
                {
                    CollectReferencedNames(child, names);
                }
                break;
            case IfStatementSyntax ifStatement:
                CollectReferencedNames(ifStatement.Condition, names);
                CollectReferencedNames(ifStatement.ThenStatement, names);
                CollectReferencedNames(ifStatement.ElseStatement, names);
                break;
            case WhileStatementSyntax whileStatement:
                CollectReferencedNames(whileStatement.Condition, names);
                CollectReferencedNames(whileStatement.Body, names);
                break;
            case RepeatStatementSyntax repeatStatement:
                foreach (var child in repeatStatement.Statements)
                {
                    CollectReferencedNames(child, names);
                }
                CollectReferencedNames(repeatStatement.Condition, names);
                break;
            case ForStatementSyntax forStatement:
                CollectReferencedNames(forStatement.LowerBound, names);
                CollectReferencedNames(forStatement.UpperBound, names);
                CollectReferencedNames(forStatement.StepExpression, names);
                CollectReferencedNames(forStatement.Body, names);
                break;
            case ForeachStatementSyntax foreachStatement:
                CollectReferencedNames(foreachStatement.Collection, names);
                CollectReferencedNames(foreachStatement.Body, names);
                break;
            case WithStatementSyntax withStatement:
                CollectReferencedNames(withStatement.Receiver, names);
                CollectReferencedNames(withStatement.Body, names);
                break;
            case CaseStatementSyntax caseStatement:
                CollectReferencedNames(caseStatement.Expression, names);
                foreach (var clause in caseStatement.Clauses)
                {
                    foreach (var label in clause.Labels)
                    {
                        CollectReferencedNames(label, names);
                    }
                    CollectReferencedNames(clause.Guard, names);
                    CollectReferencedNames(clause.Body, names);
                }
                foreach (var elseStatement in caseStatement.ElseStatements)
                {
                    CollectReferencedNames(elseStatement, names);
                }
                break;
            case MatchStatementSyntax matchStatement:
                CollectReferencedNames(matchStatement.Expression, names);
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectReferencedNames(label, names);
                    }
                    CollectReferencedNames(arm.Guard, names);
                    CollectReferencedNames(arm.Body, names);
                }
                foreach (var elseStatement in matchStatement.ElseStatements)
                {
                    CollectReferencedNames(elseStatement, names);
                }
                break;
            case ReturnStatementSyntax returnStatement:
                CollectReferencedNames(returnStatement.Expression, names);
                break;
            case RaiseStatementSyntax raiseStatement:
                CollectReferencedNames(raiseStatement.Expression, names);
                break;
            case TryStatementSyntax tryStatement:
                foreach (var child in tryStatement.TryStatements)
                {
                    CollectReferencedNames(child, names);
                }
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    CollectReferencedNames(clause.Body, names);
                }
                foreach (var child in tryStatement.ExceptStatements)
                {
                    CollectReferencedNames(child, names);
                }
                foreach (var child in tryStatement.FinallyStatements)
                {
                    CollectReferencedNames(child, names);
                }
                break;
            case LocalVariableDeclarationStatementSyntax localDeclaration:
                foreach (var declarator in localDeclaration.Declarators)
                {
                    CollectReferencedNames(declarator.Initializer, names);
                }
                break;
            case IncStatementSyntax incStatement:
                CollectReferencedNames(incStatement.Target, names);
                break;
            case DecStatementSyntax decStatement:
                CollectReferencedNames(decStatement.Target, names);
                break;
            case IncludeStatementSyntax includeStatement:
                CollectReferencedNames(includeStatement.Target, names);
                CollectReferencedNames(includeStatement.Value, names);
                break;
            case ExcludeStatementSyntax excludeStatement:
                CollectReferencedNames(excludeStatement.Target, names);
                CollectReferencedNames(excludeStatement.Value, names);
                break;
            case ExpressionStatementSyntax expressionStatement:
                CollectReferencedNames(expressionStatement.Expression, names);
                break;
        }
    }

    private static void CollectReferencedNames(ExpressionSyntax? expression, HashSet<string> names)
    {
        switch (expression)
        {
            case null or LambdaExpressionSyntax:
                return;
            case NameExpressionSyntax nameExpression:
                if (nameExpression.Name.Parts.Count == 1)
                {
                    names.Add(nameExpression.Name.Parts[0].Text);
                }
                break;
            case UnaryExpressionSyntax unary:
                CollectReferencedNames(unary.Operand, names);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                CollectReferencedNames(parenthesized.Expression, names);
                break;
            case SetLiteralExpressionSyntax setLiteral:
                foreach (var element in setLiteral.Elements)
                {
                    CollectReferencedNames(element, names);
                }
                break;
            case ProjectorExpressionSyntax projector:
                foreach (var member in projector.Members)
                {
                    CollectReferencedNames(member.Expression, names);
                }
                break;
            case RangeExpressionSyntax range:
                CollectReferencedNames(range.Start, names);
                CollectReferencedNames(range.End, names);
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    CollectReferencedNames(argument.Expression, names);
                }
                break;
            case NewArrayExpressionSyntax newArray:
                foreach (var lengthExpression in newArray.LengthExpressions)
                {
                    CollectReferencedNames(lengthExpression, names);
                }
                break;
            case ElementAccessExpressionSyntax elementAccess:
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectReferencedNames(indexExpression, names);
                }
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                CollectReferencedNames(elementAccess.Target, names);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectReferencedNames(indexExpression, names);
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                CollectReferencedNames(memberAccess.Receiver, names);
                break;
            case AssignmentExpressionSyntax assignment:
                CollectReferencedNames(assignment.Target, names);
                CollectReferencedNames(assignment.Expression, names);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                CollectReferencedNames(assignment.Target, names);
                CollectReferencedNames(assignment.Expression, names);
                break;
            case BinaryExpressionSyntax binary:
                CollectReferencedNames(binary.Left, names);
                CollectReferencedNames(binary.Right, names);
                break;
            case MatchNotPatternSyntax notPattern:
                CollectReferencedNames(notPattern.Pattern, names);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    CollectReferencedNames(pattern, names);
                }
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    CollectReferencedNames(pattern, names);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                CollectReferencedNames(relational.Operand, names);
                break;
            case TypeTestExpressionSyntax typeTest:
                CollectReferencedNames(typeTest.Expression, names);
                break;
            case AsExpressionSyntax asExpression:
                CollectReferencedNames(asExpression.Expression, names);
                break;
            case CallExpressionSyntax call:
                CollectReferencedNames(call.Target, names);
                foreach (var argument in call.Arguments)
                {
                    CollectReferencedNames(argument.Expression, names);
                }
                break;
            case QueryExpressionSyntax query:
                CollectReferencedNames(query.SourceExpression, names);
                CollectReferencedNames(query.JoinSourceExpression, names);
                CollectReferencedNames(query.JoinLeftExpression, names);
                CollectReferencedNames(query.JoinRightExpression, names);
                CollectReferencedNames(query.SecondSourceExpression, names);
                CollectReferencedNames(query.LetExpression, names);
                CollectReferencedNames(query.PredicateExpression, names);
                CollectReferencedNames(query.OrderByExpression, names);
                CollectReferencedNames(query.ThenByExpression, names);
                CollectReferencedNames(query.GroupExpression, names);
                CollectReferencedNames(query.GroupByExpression, names);
                CollectReferencedNames(query.SelectExpression, names);
                CollectReferencedNames(query.ContinuationLetExpression, names);
                CollectReferencedNames(query.ContinuationPredicateExpression, names);
                CollectReferencedNames(query.ContinuationOrderByExpression, names);
                CollectReferencedNames(query.ContinuationThenByExpression, names);
                CollectReferencedNames(query.ContinuationSelectExpression, names);
                CollectReferencedNames(query.TakeExpression, names);
                CollectReferencedNames(query.SkipExpression, names);
                break;
            case MatchExpressionSyntax matchExpression:
                CollectReferencedNames(matchExpression.Expression, names);
                foreach (var arm in matchExpression.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectReferencedNames(label, names);
                    }
                    CollectReferencedNames(arm.Guard, names);
                    CollectReferencedNames(arm.Expression, names);
                }
                break;
        }
    }

    private static void CollectLambdaExpressions(ExpressionSyntax? expression, List<LambdaExpressionSyntax> lambdas) =>
        CollectExpressionArtifacts(expression, lambdas, null, null);

    private static void CollectLambdaExpressions(StatementSyntax? statement, List<LambdaExpressionSyntax> lambdas) =>
        CollectStatementArtifacts(statement, lambdas, null, null);

    private static void CollectQueryExpressions(ExpressionSyntax? expression, List<QueryExpressionSyntax> queries) =>
        CollectExpressionArtifacts(expression, null, queries, null);

    private static void CollectQueryExpressions(StatementSyntax? statement, List<QueryExpressionSyntax> queries) =>
        CollectStatementArtifacts(statement, null, queries, null);

    private static void CollectProjectorExpressions(ExpressionSyntax? expression, List<ProjectorExpressionSyntax> projectors) =>
        CollectExpressionArtifacts(expression, null, null, projectors);

    private static void CollectProjectorExpressions(StatementSyntax? statement, List<ProjectorExpressionSyntax> projectors) =>
        CollectStatementArtifacts(statement, null, null, projectors);

    private static void CollectStatementArtifacts(
        StatementSyntax? statement,
        List<LambdaExpressionSyntax>? lambdas,
        List<QueryExpressionSyntax>? queries,
        List<ProjectorExpressionSyntax>? projectors)
    {
        switch (statement)
        {
            case null:
                return;
            case BlockStatementSyntax block:
                foreach (var child in block.Statements)
                {
                    CollectStatementArtifacts(child, lambdas, queries, projectors);
                }
                break;
            case IfStatementSyntax ifStatement:
                CollectExpressionArtifacts(ifStatement.Condition, lambdas, queries, projectors);
                CollectStatementArtifacts(ifStatement.ThenStatement, lambdas, queries, projectors);
                CollectStatementArtifacts(ifStatement.ElseStatement, lambdas, queries, projectors);
                break;
            case WhileStatementSyntax whileStatement:
                CollectExpressionArtifacts(whileStatement.Condition, lambdas, queries, projectors);
                CollectStatementArtifacts(whileStatement.Body, lambdas, queries, projectors);
                break;
            case RepeatStatementSyntax repeatStatement:
                foreach (var child in repeatStatement.Statements)
                {
                    CollectStatementArtifacts(child, lambdas, queries, projectors);
                }
                CollectExpressionArtifacts(repeatStatement.Condition, lambdas, queries, projectors);
                break;
            case ForStatementSyntax forStatement:
                CollectExpressionArtifacts(forStatement.LowerBound, lambdas, queries, projectors);
                CollectExpressionArtifacts(forStatement.UpperBound, lambdas, queries, projectors);
                CollectExpressionArtifacts(forStatement.StepExpression, lambdas, queries, projectors);
                CollectStatementArtifacts(forStatement.Body, lambdas, queries, projectors);
                break;
            case ForeachStatementSyntax foreachStatement:
                CollectExpressionArtifacts(foreachStatement.Collection, lambdas, queries, projectors);
                CollectStatementArtifacts(foreachStatement.Body, lambdas, queries, projectors);
                break;
            case WithStatementSyntax withStatement:
                CollectExpressionArtifacts(withStatement.Receiver, lambdas, queries, projectors);
                CollectStatementArtifacts(withStatement.Body, lambdas, queries, projectors);
                break;
            case CaseStatementSyntax caseStatement:
                CollectExpressionArtifacts(caseStatement.Expression, lambdas, queries, projectors);
                foreach (var clause in caseStatement.Clauses)
                {
                    foreach (var label in clause.Labels)
                    {
                        CollectExpressionArtifacts(label, lambdas, queries, projectors);
                    }
                    CollectExpressionArtifacts(clause.Guard, lambdas, queries, projectors);
                    CollectStatementArtifacts(clause.Body, lambdas, queries, projectors);
                }
                foreach (var elseStatement in caseStatement.ElseStatements)
                {
                    CollectStatementArtifacts(elseStatement, lambdas, queries, projectors);
                }
                break;
            case MatchStatementSyntax matchStatement:
                CollectExpressionArtifacts(matchStatement.Expression, lambdas, queries, projectors);
                foreach (var arm in matchStatement.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectExpressionArtifacts(label, lambdas, queries, projectors);
                    }
                    CollectExpressionArtifacts(arm.Guard, lambdas, queries, projectors);
                    CollectStatementArtifacts(arm.Body, lambdas, queries, projectors);
                }
                foreach (var elseStatement in matchStatement.ElseStatements)
                {
                    CollectStatementArtifacts(elseStatement, lambdas, queries, projectors);
                }
                break;
            case ReturnStatementSyntax returnStatement:
                CollectExpressionArtifacts(returnStatement.Expression, lambdas, queries, projectors);
                break;
            case RaiseStatementSyntax raiseStatement:
                CollectExpressionArtifacts(raiseStatement.Expression, lambdas, queries, projectors);
                break;
            case TryStatementSyntax tryStatement:
                foreach (var child in tryStatement.TryStatements)
                {
                    CollectStatementArtifacts(child, lambdas, queries, projectors);
                }
                foreach (var clause in tryStatement.ExceptionClauses)
                {
                    CollectStatementArtifacts(clause.Body, lambdas, queries, projectors);
                }
                foreach (var child in tryStatement.ExceptStatements)
                {
                    CollectStatementArtifacts(child, lambdas, queries, projectors);
                }
                foreach (var child in tryStatement.FinallyStatements)
                {
                    CollectStatementArtifacts(child, lambdas, queries, projectors);
                }
                break;
            case LocalVariableDeclarationStatementSyntax localDeclaration:
                foreach (var declarator in localDeclaration.Declarators)
                {
                    CollectExpressionArtifacts(declarator.Initializer, lambdas, queries, projectors);
                }
                break;
            case IncStatementSyntax incStatement:
                CollectExpressionArtifacts(incStatement.Target, lambdas, queries, projectors);
                break;
            case DecStatementSyntax decStatement:
                CollectExpressionArtifacts(decStatement.Target, lambdas, queries, projectors);
                break;
            case IncludeStatementSyntax includeStatement:
                CollectExpressionArtifacts(includeStatement.Target, lambdas, queries, projectors);
                CollectExpressionArtifacts(includeStatement.Value, lambdas, queries, projectors);
                break;
            case ExcludeStatementSyntax excludeStatement:
                CollectExpressionArtifacts(excludeStatement.Target, lambdas, queries, projectors);
                CollectExpressionArtifacts(excludeStatement.Value, lambdas, queries, projectors);
                break;
            case ExpressionStatementSyntax expressionStatement:
                CollectExpressionArtifacts(expressionStatement.Expression, lambdas, queries, projectors);
                break;
        }
    }

    private static void CollectExpressionArtifacts(
        ExpressionSyntax? expression,
        List<LambdaExpressionSyntax>? lambdas,
        List<QueryExpressionSyntax>? queries,
        List<ProjectorExpressionSyntax>? projectors)
    {
        switch (expression)
        {
            case null:
                return;
            case LambdaExpressionSyntax lambda:
                if (lambdas is not null)
                {
                    lambdas.Add(lambda);
                    return;
                }
                CollectExpressionArtifacts(lambda.Body, lambdas, queries, projectors);
                break;
            case QueryExpressionSyntax query:
                if (queries is not null)
                {
                    queries.Add(query);
                    return;
                }
                CollectExpressionArtifacts(query.SourceExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.JoinSourceExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.JoinLeftExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.JoinRightExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.SecondSourceExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.LetExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.PredicateExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.OrderByExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ThenByExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.GroupExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.GroupByExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.SelectExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ContinuationLetExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ContinuationPredicateExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ContinuationOrderByExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ContinuationThenByExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.ContinuationSelectExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.TakeExpression, lambdas, queries, projectors);
                CollectExpressionArtifacts(query.SkipExpression, lambdas, queries, projectors);
                break;
            case ProjectorExpressionSyntax projector:
                projectors?.Add(projector);
                foreach (var member in projector.Members)
                {
                    CollectExpressionArtifacts(member.Expression, lambdas, queries, projectors);
                }
                break;
            case UnaryExpressionSyntax unary:
                CollectExpressionArtifacts(unary.Operand, lambdas, queries, projectors);
                break;
            case ParenthesizedExpressionSyntax parenthesized:
                CollectExpressionArtifacts(parenthesized.Expression, lambdas, queries, projectors);
                break;
            case SetLiteralExpressionSyntax setLiteral:
                foreach (var element in setLiteral.Elements)
                {
                    CollectExpressionArtifacts(element, lambdas, queries, projectors);
                }
                break;
            case RangeExpressionSyntax range:
                CollectExpressionArtifacts(range.Start, lambdas, queries, projectors);
                CollectExpressionArtifacts(range.End, lambdas, queries, projectors);
                break;
            case NewExpressionSyntax newExpression:
                foreach (var argument in newExpression.Arguments)
                {
                    CollectExpressionArtifacts(argument.Expression, lambdas, queries, projectors);
                }
                break;
            case NewArrayExpressionSyntax newArray:
                foreach (var lengthExpression in newArray.LengthExpressions)
                {
                    CollectExpressionArtifacts(lengthExpression, lambdas, queries, projectors);
                }
                break;
            case ElementAccessExpressionSyntax elementAccess:
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectExpressionArtifacts(indexExpression, lambdas, queries, projectors);
                }
                break;
            case PostfixElementAccessExpressionSyntax elementAccess:
                CollectExpressionArtifacts(elementAccess.Target, lambdas, queries, projectors);
                foreach (var indexExpression in elementAccess.IndexExpressions)
                {
                    CollectExpressionArtifacts(indexExpression, lambdas, queries, projectors);
                }
                break;
            case MemberAccessExpressionSyntax memberAccess:
                CollectExpressionArtifacts(memberAccess.Receiver, lambdas, queries, projectors);
                break;
            case AssignmentExpressionSyntax assignment:
                CollectExpressionArtifacts(assignment.Target, lambdas, queries, projectors);
                CollectExpressionArtifacts(assignment.Expression, lambdas, queries, projectors);
                break;
            case CompoundAssignmentExpressionSyntax assignment:
                CollectExpressionArtifacts(assignment.Target, lambdas, queries, projectors);
                CollectExpressionArtifacts(assignment.Expression, lambdas, queries, projectors);
                break;
            case BinaryExpressionSyntax binary:
                CollectExpressionArtifacts(binary.Left, lambdas, queries, projectors);
                CollectExpressionArtifacts(binary.Right, lambdas, queries, projectors);
                break;
            case MatchNotPatternSyntax notPattern:
                CollectExpressionArtifacts(notPattern.Pattern, lambdas, queries, projectors);
                break;
            case MatchOrPatternSyntax orPattern:
                foreach (var pattern in orPattern.Patterns)
                {
                    CollectExpressionArtifacts(pattern, lambdas, queries, projectors);
                }
                break;
            case MatchAndPatternSyntax andPattern:
                foreach (var pattern in andPattern.Patterns)
                {
                    CollectExpressionArtifacts(pattern, lambdas, queries, projectors);
                }
                break;
            case MatchRelationalPatternSyntax relational:
                CollectExpressionArtifacts(relational.Operand, lambdas, queries, projectors);
                break;
            case TypeTestExpressionSyntax typeTest:
                CollectExpressionArtifacts(typeTest.Expression, lambdas, queries, projectors);
                break;
            case AsExpressionSyntax asExpression:
                CollectExpressionArtifacts(asExpression.Expression, lambdas, queries, projectors);
                break;
            case CallExpressionSyntax call:
                CollectExpressionArtifacts(call.Target, lambdas, queries, projectors);
                foreach (var argument in call.Arguments)
                {
                    CollectExpressionArtifacts(argument.Expression, lambdas, queries, projectors);
                }
                break;
            case MatchExpressionSyntax matchExpression:
                CollectExpressionArtifacts(matchExpression.Expression, lambdas, queries, projectors);
                foreach (var arm in matchExpression.Arms)
                {
                    foreach (var label in arm.Labels)
                    {
                        CollectExpressionArtifacts(label, lambdas, queries, projectors);
                    }
                    CollectExpressionArtifacts(arm.Guard, lambdas, queries, projectors);
                    CollectExpressionArtifacts(arm.Expression, lambdas, queries, projectors);
                }
                break;
        }
    }

}
