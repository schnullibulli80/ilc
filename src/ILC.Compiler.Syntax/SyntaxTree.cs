namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

public sealed partial class SyntaxTree
{
    private SyntaxTree(CompilationUnitSyntax root, IReadOnlyList<Diagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    public CompilationUnitSyntax Root { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public static SyntaxTree Parse(string sourceText)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(sourceText, diagnostics);
        var tokens = lexer.Lex();
        var parser = new Parser(tokens, diagnostics);
        var root = NormalizeUsesAliases(parser.ParseCompilationUnit(sourceText));
        return new SyntaxTree(root, diagnostics);
    }

    public static SyntaxTree Merge(SyntaxTree primary, IEnumerable<SyntaxTree> importedTrees)
    {
        var imported = importedTrees.ToArray();
        if (imported.Length == 0)
        {
            return primary;
        }

        var mergedRoot = new CompilationUnitSyntax(
            primary.Root.Namespace,
            primary.Root.Uses,
            primary.Root.Members.Concat(imported.SelectMany(tree => tree.Root.Members)).ToArray(),
            primary.Root.EndOfFileToken,
            primary.Root.Tokens.Concat(imported.SelectMany(tree => tree.Root.Tokens)).ToArray(),
            primary.Root.SourceText);

        var diagnostics = primary.Diagnostics.Concat(imported.SelectMany(tree => tree.Diagnostics)).ToList();
        diagnostics.AddRange(DetectImportAmbiguities(primary.Root, imported));
        return new SyntaxTree(mergedRoot, diagnostics);
    }
}
