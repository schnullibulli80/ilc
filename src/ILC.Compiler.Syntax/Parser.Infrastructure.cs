namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed partial class Parser
{
    private readonly IReadOnlyList<SyntaxToken> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private readonly string _sourceText;
    private int _position;
    private bool _stopExpressionAtMatchArmBoundary;

    public Parser(IReadOnlyList<SyntaxToken> tokens, DiagnosticBag diagnostics, string sourceText)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
        _sourceText = sourceText;
    }

    private SyntaxToken Match(SyntaxKind kind)
    {
        if (Current.Kind == kind)
        {
            return NextToken();
        }

        if (kind == SyntaxKind.IdentifierToken && IsIdentifierLike(Current.Kind))
        {
            var current = NextToken();
            return new SyntaxToken(SyntaxKind.IdentifierToken, current.Text, current.Value, current.Span);
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

    private static bool IsIdentifierLike(SyntaxKind kind) =>
        kind == SyntaxKind.IdentifierToken ||
        kind is >= SyntaxKind.NamespaceKeyword and <= SyntaxKind.FalseKeyword;

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
        if (!IsIdentifierLike(Peek(offset).Kind))
        {
            return false;
        }

        offset++;
        while (Peek(offset).Kind == SyntaxKind.DotToken && IsIdentifierLike(Peek(offset + 1).Kind))
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

            if (Peek(offset).Kind == SyntaxKind.DotToken && IsIdentifierLike(Peek(offset + 1).Kind))
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

    private bool IsAtMatchArmBoundary()
    {
        if (!_stopExpressionAtMatchArmBoundary || !HasLineBreakBeforeCurrent())
        {
            return false;
        }

        var offset = 0;
        if (Peek(offset).Kind == SyntaxKind.NotKeyword)
        {
            offset++;
        }

        if (!TryScanRelationalMatchPattern(ref offset))
        {
            return false;
        }

        while (Peek(offset).Kind == SyntaxKind.AndKeyword)
        {
            offset++;
            if (!TryScanRelationalMatchPattern(ref offset))
            {
                return false;
            }
        }

        return Peek(offset).Kind == SyntaxKind.ArrowToken;
    }

    private bool TryScanRelationalMatchPattern(ref int offset)
    {
        if (Peek(offset).Kind is not (SyntaxKind.LessToken or SyntaxKind.LessOrEqualsToken or SyntaxKind.GreaterToken or SyntaxKind.GreaterOrEqualsToken))
        {
            return false;
        }

        offset++;
        if (Peek(offset).Kind is SyntaxKind.EndOfFileToken or SyntaxKind.ArrowToken)
        {
            return false;
        }

        while (Peek(offset).Kind is not (SyntaxKind.AndKeyword or SyntaxKind.OrKeyword or SyntaxKind.CommaToken or SyntaxKind.WhenKeyword or SyntaxKind.ArrowToken or SyntaxKind.EndKeyword or SyntaxKind.EndOfFileToken))
        {
            offset++;
        }

        return true;
    }

    private bool HasLineBreakBeforeCurrent()
    {
        if (_position <= 0 || _position >= _tokens.Count)
        {
            return false;
        }

        var previous = PeekAbsolute(_position - 1);
        var current = Current;
        var start = previous.Span.Start + previous.Span.Length;
        var length = current.Span.Start - start;
        if (length <= 0 || start < 0 || start >= _sourceText.Length)
        {
            return false;
        }

        var end = Math.Min(start + length, _sourceText.Length);
        for (var index = start; index < end; index++)
        {
            if (_sourceText[index] is '\n' or '\r')
            {
                return true;
            }
        }

        return false;
    }
}
