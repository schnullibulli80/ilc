namespace ILC.Compiler.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record Diagnostic(
    string Id,
    string Message,
    DiagnosticSeverity Severity,
    TextSpan Span);

public sealed class DiagnosticBag : List<Diagnostic>
{
    public void Report(string id, string message, DiagnosticSeverity severity, TextSpan span) =>
        Add(new Diagnostic(id, message, severity, span));
}
