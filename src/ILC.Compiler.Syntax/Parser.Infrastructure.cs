namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private readonly IReadOnlyList<SyntaxToken> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Parser(IReadOnlyList<SyntaxToken> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    private SyntaxToken Match(SyntaxKind kind)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        _diagnostics.Report(
            "ILC1002",
            $"Expected token '{kind}', but found '{Current.Kind}'.",
            DiagnosticSeverity.Error,
            Current.Span);

        if (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            _position++;
        }

        return new SyntaxToken(kind, string.Empty, null, new TextSpan(Current.Span.Start, 0));
    }

    private SyntaxToken NextToken()
    {
        var current = Current;
        _position++;
        return current;
    }

    private SyntaxToken Current => Peek(0);

    private bool IsAssignmentTarget()
    {
        var offset = 0;
        if (Peek(offset).Kind != SyntaxKind.IdentifierToken)
        {
            return false;
        }

        offset++;
        while (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
        {
            offset += 2;
        }

        while (true)
        {
            if (Peek(offset).Kind == SyntaxKind.OpenBracketToken)
            {
                offset++;
                var depth = 1;
                while (depth > 0 && Peek(offset).Kind != SyntaxKind.EndOfFileToken)
                {
                    if (Peek(offset).Kind == SyntaxKind.OpenBracketToken)
                    {
                        depth++;
                    }
                    else if (Peek(offset).Kind == SyntaxKind.CloseBracketToken)
                    {
                        depth--;
                    }

                    offset++;
                }
                continue;
            }

            if (Peek(offset).Kind == SyntaxKind.DotToken && Peek(offset + 1).Kind == SyntaxKind.IdentifierToken)
            {
                offset += 2;
                continue;
            }

            break;
        }

        return Peek(offset).Kind is SyntaxKind.AssignToken
            or SyntaxKind.PlusAssignToken
            or SyntaxKind.MinusAssignToken
            or SyntaxKind.StarAssignToken
            or SyntaxKind.SlashAssignToken
            or SyntaxKind.DivAssignToken
            or SyntaxKind.ModAssignToken
            or SyntaxKind.AndAssignToken
            or SyntaxKind.OrAssignToken
            or SyntaxKind.XorAssignToken
            or SyntaxKind.ShlAssignToken
            or SyntaxKind.ShrAssignToken
            or SyntaxKind.NullCoalescingAssignToken;
    }

    private SyntaxToken Peek(int offset)
    {
        var index = _position + offset;
        return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
    }

    private SyntaxToken PeekAbsolute(int index) =>
        index < 0 || index >= _tokens.Count ? _tokens[^1] : _tokens[index];
}
