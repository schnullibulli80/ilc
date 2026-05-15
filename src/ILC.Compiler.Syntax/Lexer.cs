namespace ILC.Compiler.Syntax;

using ILC.Compiler.Core;

internal sealed class Lexer
{
    private readonly string _text;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Lexer(string text, DiagnosticBag diagnostics)
    {
        _text = text;
        _diagnostics = diagnostics;
    }

    public List<SyntaxToken> Lex()
    {
        var tokens = new List<SyntaxToken>();

        while (true)
        {
            var token = NextToken();
            if (token.Kind != SyntaxKind.BadToken)
            {
                tokens.Add(token);
            }

            if (token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        return tokens;
    }

    private SyntaxToken NextToken()
    {
        SkipTrivia();

        if (_position >= _text.Length)
        {
            return new SyntaxToken(SyntaxKind.EndOfFileToken, string.Empty, null, new TextSpan(_position, 0));
        }

        var start = _position;
        var current = _text[_position];

        if (char.IsLetter(current) || current == '_')
        {
            _position++;
            while (_position < _text.Length &&
                   (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }

            var text = _text[start.._position];
            if (_position < _text.Length && _text[_position] == '=')
            {
                if (string.Equals(text, "and", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.AndAssignToken, "and=", null, new TextSpan(start, 4));
                }

                if (string.Equals(text, "or", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.OrAssignToken, "or=", null, new TextSpan(start, 3));
                }

                if (string.Equals(text, "xor", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.XorAssignToken, "xor=", null, new TextSpan(start, 4));
                }

                if (string.Equals(text, "mod", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ModAssignToken, "mod=", null, new TextSpan(start, 4));
                }

                if (string.Equals(text, "div", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.DivAssignToken, "div=", null, new TextSpan(start, 4));
                }

                if (string.Equals(text, "shl", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ShlAssignToken, "shl=", null, new TextSpan(start, 4));
                }

                if (string.Equals(text, "shr", StringComparison.OrdinalIgnoreCase))
                {
                    _position++;
                    return new SyntaxToken(SyntaxKind.ShrAssignToken, "shr=", null, new TextSpan(start, 4));
                }
            }

            return new SyntaxToken(GetKeywordKind(text), text, null, new TextSpan(start, text.Length));
        }

        if (char.IsDigit(current))
        {
            _position++;
            while (_position < _text.Length &&
                   (char.IsDigit(_text[_position]) || _text[_position] == '_'))
            {
                _position++;
            }

            var text = _text[start.._position];
            var digits = text.Replace("_", string.Empty);
            object? value = new NumericLiteralValue(
                text,
                digits,
                int.TryParse(digits, out var parsedValue) ? parsedValue : null);
            return new SyntaxToken(SyntaxKind.NumberToken, text, value, new TextSpan(start, text.Length));
        }

        if (current is '"' or '\'')
        {
            return LexString();
        }

        _position++;
        return current switch
        {
            ';' => new SyntaxToken(SyntaxKind.SemicolonToken, ";", null, new TextSpan(start, 1)),
            ':' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.AssignToken, ":="),
            ':' => new SyntaxToken(SyntaxKind.ColonToken, ":", null, new TextSpan(start, 1)),
            '<' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.LessOrEqualsToken, "<="),
            '<' when Peek() == '>' => LexTwoCharacterToken(SyntaxKind.NotEqualsToken, "<>"),
            '<' => new SyntaxToken(SyntaxKind.LessToken, "<", null, new TextSpan(start, 1)),
            '=' when Peek() == '>' => LexTwoCharacterToken(SyntaxKind.ArrowToken, "=>"),
            '=' => new SyntaxToken(SyntaxKind.EqualsToken, "=", null, new TextSpan(start, 1)),
            '>' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.GreaterOrEqualsToken, ">="),
            '>' => new SyntaxToken(SyntaxKind.GreaterToken, ">", null, new TextSpan(start, 1)),
            '+' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.PlusAssignToken, "+="),
            '+' => new SyntaxToken(SyntaxKind.PlusToken, "+", null, new TextSpan(start, 1)),
            '-' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.MinusAssignToken, "-="),
            '-' => new SyntaxToken(SyntaxKind.MinusToken, "-", null, new TextSpan(start, 1)),
            '*' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.StarAssignToken, "*="),
            '*' => new SyntaxToken(SyntaxKind.StarToken, "*", null, new TextSpan(start, 1)),
            '/' when Peek() == '=' => LexTwoCharacterToken(SyntaxKind.SlashAssignToken, "/="),
            '/' => new SyntaxToken(SyntaxKind.SlashToken, "/", null, new TextSpan(start, 1)),
            '?' when Peek() == '?' && Peek(1) == '=' => LexThreeCharacterToken(SyntaxKind.NullCoalescingAssignToken, "??="),
            '?' when Peek() == '?' => LexTwoCharacterToken(SyntaxKind.NullCoalescingToken, "??"),
            ',' => new SyntaxToken(SyntaxKind.CommaToken, ",", null, new TextSpan(start, 1)),
            '.' when Peek() == '.' => LexTwoCharacterToken(SyntaxKind.RangeToken, ".."),
            '.' => new SyntaxToken(SyntaxKind.DotToken, ".", null, new TextSpan(start, 1)),
            '(' => new SyntaxToken(SyntaxKind.OpenParenToken, "(", null, new TextSpan(start, 1)),
            ')' => new SyntaxToken(SyntaxKind.CloseParenToken, ")", null, new TextSpan(start, 1)),
            '{' => new SyntaxToken(SyntaxKind.OpenBraceToken, "{", null, new TextSpan(start, 1)),
            '}' => new SyntaxToken(SyntaxKind.CloseBraceToken, "}", null, new TextSpan(start, 1)),
            '[' => new SyntaxToken(SyntaxKind.OpenBracketToken, "[", null, new TextSpan(start, 1)),
            ']' => new SyntaxToken(SyntaxKind.CloseBracketToken, "]", null, new TextSpan(start, 1)),
            _ => BadToken(start, current)
        };
    }

    private SyntaxToken LexTwoCharacterToken(SyntaxKind kind, string text)
    {
        var start = _position - 1;
        _position++;
        return new SyntaxToken(kind, text, null, new TextSpan(start, 2));
    }

    private SyntaxToken LexThreeCharacterToken(SyntaxKind kind, string text)
    {
        var start = _position - 1;
        _position += 2;
        return new SyntaxToken(kind, text, null, new TextSpan(start, 3));
    }

    private SyntaxToken LexString()
    {
        var delimiter = _text[_position];
        var start = _position;
        _position++;

        while (_position < _text.Length && _text[_position] != delimiter)
        {
            _position++;
        }

        if (_position >= _text.Length)
        {
            _diagnostics.Report(
                "ILC1001",
                "Unterminated string literal.",
                DiagnosticSeverity.Error,
                new TextSpan(start, _text.Length - start));

            return new SyntaxToken(SyntaxKind.StringToken, _text[start..], _text[start..], new TextSpan(start, _text.Length - start));
        }

        _position++;
        var text = _text[start.._position];
        var value = text[1..^1];
        return new SyntaxToken(SyntaxKind.StringToken, text, value, new TextSpan(start, text.Length));
    }

    private SyntaxToken BadToken(int start, char current)
    {
        _diagnostics.Report(
            "ILC1000",
            $"Unexpected character '{current}'.",
            DiagnosticSeverity.Warning,
            new TextSpan(start, 1));

        return new SyntaxToken(SyntaxKind.BadToken, current.ToString(), null, new TextSpan(start, 1));
    }

    private char Peek() => Peek(0);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _text.Length ? _text[index] : '\0';
    }

    private void SkipTrivia()
    {
        while (_position < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_position]))
            {
                _position++;
                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '/' && _text[_position + 1] == '/')
            {
                _position += 2;
                while (_position < _text.Length && _text[_position] != '\n')
                {
                    _position++;
                }

                continue;
            }

            if (_position + 1 < _text.Length && _text[_position] == '(' && _text[_position + 1] == '*')
            {
                _position += 2;
                while (_position + 1 < _text.Length && !(_text[_position] == '*' && _text[_position + 1] == ')'))
                {
                    _position++;
                }

                if (_position + 1 < _text.Length)
                {
                    _position += 2;
                }

                continue;
            }

            break;
        }
    }

    private static SyntaxKind GetKeywordKind(string text) =>
        text.ToLowerInvariant() switch
        {
            "namespace" => SyntaxKind.NamespaceKeyword,
            "uses" => SyntaxKind.UsesKeyword,
            "array" => SyntaxKind.ArrayKeyword,
            "set" => SyntaxKind.SetKeyword,
            "of" => SyntaxKind.OfKeyword,
            "var" => SyntaxKind.VarKeyword,
            "const" => SyntaxKind.ConstKeyword,
            "enum" => SyntaxKind.EnumKeyword,
            "delegate" => SyntaxKind.DelegateKeyword,
            "class" => SyntaxKind.ClassKeyword,
            "record" => SyntaxKind.RecordKeyword,
            "interface" => SyntaxKind.InterfaceKeyword,
            "begin" => SyntaxKind.BeginKeyword,
            "end" => SyntaxKind.EndKeyword,
            "method" => SyntaxKind.MethodKeyword,
            "function" => SyntaxKind.FunctionKeyword,
            "procedure" => SyntaxKind.ProcedureKeyword,
            "constructor" => SyntaxKind.ConstructorKeyword,
            "property" => SyntaxKind.PropertyKeyword,
            "read" => SyntaxKind.ReadKeyword,
            "write" => SyntaxKind.WriteKeyword,
            "get" => SyntaxKind.GetKeyword,
            "init" => SyntaxKind.InitKeyword,
            "return" => SyntaxKind.ReturnKeyword,
            "exit" => SyntaxKind.ExitKeyword,
            "break" => SyntaxKind.BreakKeyword,
            "continue" => SyntaxKind.ContinueKeyword,
            "raise" => SyntaxKind.RaiseKeyword,
            "throw" => SyntaxKind.ThrowKeyword,
            "case" => SyntaxKind.CaseKeyword,
            "match" => SyntaxKind.MatchKeyword,
            "if" => SyntaxKind.IfKeyword,
            "then" => SyntaxKind.ThenKeyword,
            "else" => SyntaxKind.ElseKeyword,
            "while" => SyntaxKind.WhileKeyword,
            "repeat" => SyntaxKind.RepeatKeyword,
            "until" => SyntaxKind.UntilKeyword,
            "for" => SyntaxKind.ForKeyword,
            "each" => SyntaxKind.EachKeyword,
            "foreach" => SyntaxKind.ForeachKeyword,
            "with" => SyntaxKind.WithKeyword,
            "from" => SyntaxKind.FromKeyword,
            "join" => SyntaxKind.JoinKeyword,
            "into" => SyntaxKind.IntoKeyword,
            "let" => SyntaxKind.LetKeyword,
            "where" => SyntaxKind.WhereKeyword,
            "select" => SyntaxKind.SelectKeyword,
            "orderby" => SyntaxKind.OrderByKeyword,
            "take" => SyntaxKind.TakeKeyword,
            "skip" => SyntaxKind.SkipKeyword,
            "div" => SyntaxKind.DivKeyword,
            "to" => SyntaxKind.ToKeyword,
            "downto" => SyntaxKind.DowntoKeyword,
            "in" => SyntaxKind.InKeyword,
            "do" => SyntaxKind.DoKeyword,
            "step" => SyntaxKind.StepKeyword,
            "inc" => SyntaxKind.IncKeyword,
            "dec" => SyntaxKind.DecKeyword,
            "include" => SyntaxKind.IncludeKeyword,
            "exclude" => SyntaxKind.ExcludeKeyword,
            "try" => SyntaxKind.TryKeyword,
            "except" => SyntaxKind.ExceptKeyword,
            "finally" => SyntaxKind.FinallyKeyword,
            "on" => SyntaxKind.OnKeyword,
            "new" => SyntaxKind.NewKeyword,
            "static" => SyntaxKind.StaticKeyword,
            "default" => SyntaxKind.DefaultKeyword,
            "readonly" => SyntaxKind.ReadonlyKeyword,
            "virtual" => SyntaxKind.VirtualKeyword,
            "override" => SyntaxKind.OverrideKeyword,
            "out" => SyntaxKind.OutKeyword,
            "ref" => SyntaxKind.RefKeyword,
            "public" => SyntaxKind.PublicKeyword,
            "private" => SyntaxKind.PrivateKeyword,
            "protected" => SyntaxKind.ProtectedKeyword,
            "internal" => SyntaxKind.InternalKeyword,
            "params" => SyntaxKind.ParamsKeyword,
            "extern" => SyntaxKind.ExternKeyword,
            "as" => SyntaxKind.AsKeyword,
            "is" => SyntaxKind.IsKeyword,
            "and" => SyntaxKind.AndKeyword,
            "not" => SyntaxKind.NotKeyword,
            "or" => SyntaxKind.OrKeyword,
            "xor" => SyntaxKind.XorKeyword,
            "when" => SyntaxKind.WhenKeyword,
            "mod" => SyntaxKind.ModKeyword,
            "shl" => SyntaxKind.ShlKeyword,
            "shr" => SyntaxKind.ShrKeyword,
            "nil" => SyntaxKind.NilKeyword,
            "true" => SyntaxKind.TrueKeyword,
            "false" => SyntaxKind.FalseKeyword,
            _ => SyntaxKind.IdentifierToken
        };
}

