namespace ILC.Compiler.Binding;

using ILC.Compiler.Core;

public sealed record BindingResult(
    CompilationUnitSymbol Compilation,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
