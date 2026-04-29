namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

public sealed partial class SyntaxTree
{
    private static IReadOnlyList<Diagnostic> DetectImportAmbiguities(CompilationUnitSyntax primaryRoot, IReadOnlyList<SyntaxTree> importedTrees)
    {
        if (primaryRoot.Uses is null)
        {
            return [];
        }

        var unaliasedImports = primaryRoot.Uses.Imports
            .Where(importSyntax => importSyntax.AliasIdentifier is null)
            .ToDictionary(
                importSyntax => importSyntax.NamespaceName.ToDisplayString(),
                importSyntax => importSyntax.NamespaceName.Parts[0].Span,
                StringComparer.Ordinal);
        if (unaliasedImports.Count < 2)
        {
            return [];
        }

        var importedTypes = importedTrees
            .Select(tree => new
            {
                Namespace = tree.Root.Namespace?.Name.ToDisplayString(),
                TypeNames = tree.Root.Members
                    .Select(member => member switch
                    {
                        ClassDeclarationSyntax classDeclaration => classDeclaration.Identifier.Text,
                        InterfaceDeclarationSyntax interfaceDeclaration => interfaceDeclaration.Identifier.Text,
                        EnumDeclarationSyntax enumDeclaration => enumDeclaration.Identifier.Text,
                        DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier.Text,
                        _ => null
                    })
                    .Where(typeName => typeName is not null)
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item => item.Namespace is not null && unaliasedImports.ContainsKey(item.Namespace))
            .ToArray();

        return importedTypes
            .SelectMany(item => item.TypeNames.Select(typeName => (item.Namespace!, TypeName: typeName)))
            .GroupBy(item => item.TypeName, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Item1).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group =>
            {
                var namespaces = group.Select(item => item.Item1).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var span = unaliasedImports[namespaces[0]];
                return new Diagnostic(
                    "ILC2184",
                    $"Imported type '{group.Key}' is ambiguous between namespaces {string.Join(", ", namespaces)}. Use an alias-qualified import.",
                    DiagnosticSeverity.Error,
                    span);
            })
            .ToArray();
    }
}
