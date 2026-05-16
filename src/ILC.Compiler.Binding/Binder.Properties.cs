namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

public sealed partial class Binder
{
    private static PropertySymbol BindProperty(PropertyDeclarationSyntax propertyDeclaration, string declaringTypeName, IReadOnlyList<FieldSymbol> fields, IReadOnlyList<TypeSymbol> knownTypes)
    {
        var declaringType = knownTypes.OfType<NamedTypeSymbol>().FirstOrDefault(type => SemanticFacts.NameEquals(type.Name, declaringTypeName));
        var synthesizeDeclarationOnlyAccessorMethods = declaringType?.IsInterface == true;
        var isStatic = propertyDeclaration.Modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);
        var isGetterPrivate = propertyDeclaration.GetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.GetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isSetterPrivate = propertyDeclaration.SetterModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword)
            || propertyDeclaration.SetterBlockModifiers.Any(modifier => modifier.Kind == SyntaxKind.PrivateKeyword);
        var isInitOnly = propertyDeclaration.InitKeyword is not null;
        var autoPropertyField = propertyDeclaration.OpenBraceToken is not null
            ? fields.FirstOrDefault(field => SemanticFacts.NameEquals(field.Name, $"__auto_{propertyDeclaration.Identifier.Text}") && SemanticFacts.NameEquals(field.DeclaringTypeName, declaringTypeName))
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

    private static FieldSymbol? BindPropertyFieldReference(QualifiedNameSyntax target, string declaringTypeName, IReadOnlyList<FieldSymbol> fields)
    {
        var fieldName = target.Parts[^1].Text;
        return fields.FirstOrDefault(field =>
            SemanticFacts.NameEquals(field.DeclaringTypeName, declaringTypeName) &&
            SemanticFacts.NameEquals(field.Name, fieldName));
    }
}
