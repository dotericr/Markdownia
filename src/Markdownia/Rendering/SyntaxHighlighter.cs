namespace Markdownia.Rendering;

public enum TokenKind
{
    Plain,
    Keyword,
    Type,
    String,
    Number,
    Comment,
    Preprocessor,
    Function,
    Property,
}

public readonly record struct Token(int Start, int Length, TokenKind Kind);

/// <summary>
/// A small, fast, allocation-light tokenizer for code blocks. It is deliberately approximate: the goal is pleasant,
/// readable coloring for common languages without regex engines, grammars or reflection (keeps Native AOT happy).
/// </summary>
public static class SyntaxHighlighter
{
    public static string? NormalizeLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info))
            return null;
        var lang = info.Trim().Split(' ', '{', ',')[0].Trim().TrimStart('.').ToLowerInvariant();
        return lang.Length == 0 ? null : lang;
    }

    public static string DisplayName(string? lang)
    {
        if (lang is null)
            return "";
        var def = Languages.Find(lang);
        return def?.DisplayName ?? lang;
    }

    public static IReadOnlyList<Token> Tokenize(string code, string? lang)
    {
        if (lang is null)
            return [new Token(0, code.Length, TokenKind.Plain)];
        var def = Languages.Find(lang);
        if (def is null)
            return [new Token(0, code.Length, TokenKind.Plain)];
        return def.Mode switch
        {
            LanguageMode.Markup => TokenizeMarkup(code),
            LanguageMode.Diff => TokenizeDiff(code),
            _ => new Scanner(code, def).Run(),
        };
    }

    private ref struct Scanner(string text, LanguageDefinition def)
    {
        private readonly string _text = text;
        private readonly LanguageDefinition _def = def;
        private readonly List<Token> _tokens = [];
        private int _pos;
        private int _plainStart = -1;

        public List<Token> Run()
        {
            var lineStart = true;
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (c == '\n')
                {
                    Plain(1);
                    lineStart = true;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    Plain(1);
                    continue;
                }

                if (lineStart && _def.PreprocessorHash && c == '#')
                {
                    Emit(LineLength(), TokenKind.Preprocessor);
                    continue;
                }
                lineStart = false;

                if (TryComment() || TryString() || TryNumber() || TryWord())
                    continue;

                if (c == '@' && _def.Decorators && _pos + 1 < _text.Length && IsIdentStart(_text[_pos + 1]))
                {
                    var len = 1;
                    while (_pos + len < _text.Length && (IsIdentPart(_text[_pos + len]) || _text[_pos + len] == '.'))
                        len++;
                    Emit(len, TokenKind.Preprocessor);
                    continue;
                }

                if (c == '$' && _def.ShellVariables && _pos + 1 < _text.Length)
                {
                    var len = 1;
                    if (_text[_pos + 1] == '{')
                    {
                        var close = _text.IndexOf('}', _pos + 2);
                        len = close < 0 ? 2 : close - _pos + 1;
                    }
                    else
                    {
                        while (_pos + len < _text.Length && (IsIdentPart(_text[_pos + len]) || (len == 1 && "?#@*!$0123456789".Contains(_text[_pos + len]))))
                            len++;
                    }
                    if (len > 1)
                    {
                        Emit(len, TokenKind.Property);
                        continue;
                    }
                }

                Plain(1);
            }
            FlushPlain();
            return _tokens;
        }

        private int LineLength()
        {
            var end = _text.IndexOf('\n', _pos);
            return (end < 0 ? _text.Length : end) - _pos;
        }

        private bool StartsWith(string s) => string.CompareOrdinal(_text, _pos, s, 0, s.Length) == 0;

        private bool TryComment()
        {
            foreach (var lc in _def.LineComments)
            {
                if (StartsWith(lc))
                {
                    Emit(LineLength(), TokenKind.Comment);
                    return true;
                }
            }
            foreach (var (open, close) in _def.BlockComments)
            {
                if (StartsWith(open))
                {
                    var end = _text.IndexOf(close, _pos + open.Length, StringComparison.Ordinal);
                    var len = end < 0 ? _text.Length - _pos : end + close.Length - _pos;
                    Emit(len, TokenKind.Comment);
                    return true;
                }
            }
            return false;
        }

        private bool TryString()
        {
            var c = _text[_pos];
            var prefix = 0;

            // String prefixes: C# @"..." $"..." $@"..."; Python r"..." f"..." b"..."; C++ u8"..." L"..."
            if (_def.StringPrefixes.Length > 0 && IsIdentStart(c) || c == '@' || c == '$')
            {
                foreach (var p in _def.StringPrefixes)
                {
                    if (StartsWith(p) && _pos + p.Length < _text.Length && _def.Quotes.Contains(_text[_pos + p.Length]))
                    {
                        prefix = p.Length;
                        break;
                    }
                }
            }

            var qpos = _pos + prefix;
            if (qpos >= _text.Length || !_def.Quotes.Contains(_text[qpos]))
                return false;
            var quote = _text[qpos];

            // Rust/C-like char literals vs lifetimes: treat 'x' only when it closes quickly.
            if (quote == '\'' && _def.ShortCharLiterals)
            {
                var close = _text.IndexOf('\'', qpos + 1);
                if (close < 0 || close - qpos > 4 && _text[qpos + 1] != '\\')
                    return false;
            }

            var verbatim = prefix > 0 && _text.AsSpan(_pos, prefix).Contains('@') || quote == '`' && _def.Id == "go";
            var triple = _def.TripleQuotes && string.CompareOrdinal(_text, qpos, new string(quote, 3), 0, 3) == 0;
            int i;
            if (triple)
            {
                var close = _text.IndexOf(new string(quote, 3), qpos + 3, StringComparison.Ordinal);
                i = close < 0 ? _text.Length : close + 3;
            }
            else
            {
                i = qpos + 1;
                var multiline = quote == '`' || verbatim;
                while (i < _text.Length)
                {
                    var ch = _text[i];
                    if (ch == '\\' && !verbatim && _def.Id != "sql")
                    {
                        i += 2;
                        continue;
                    }
                    if (ch == quote)
                    {
                        if ((verbatim || _def.Id == "sql") && i + 1 < _text.Length && _text[i + 1] == quote)
                        {
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    if (ch == '\n' && !multiline)
                        break;
                    i++;
                }
                i = Math.Min(i, _text.Length);
            }

            var len = i - _pos;
            var kind = TokenKind.String;
            if (_def.KeyStrings)
            {
                // JSON/YAML-style keys: "name": value
                var j = i;
                while (j < _text.Length && (_text[j] == ' ' || _text[j] == '\t'))
                    j++;
                if (j < _text.Length && _text[j] == ':')
                    kind = TokenKind.Property;
            }
            Emit(len, kind);
            return true;
        }

        private bool TryNumber()
        {
            var c = _text[_pos];
            var startsWithDot = c == '.' && _pos + 1 < _text.Length && char.IsAsciiDigit(_text[_pos + 1]);
            if (!char.IsAsciiDigit(c) && !startsWithDot)
                return false;
            if (_pos > 0 && IsIdentPart(_text[_pos - 1]))
                return false;

            var i = _pos;
            if (c == '0' && i + 1 < _text.Length && "xXbBoO".Contains(_text[i + 1]))
            {
                i += 2;
                while (i < _text.Length && (char.IsAsciiHexDigit(_text[i]) || _text[i] == '_'))
                    i++;
            }
            else
            {
                while (i < _text.Length && (char.IsAsciiDigit(_text[i]) || _text[i] == '_' || _text[i] == '.' && i + 1 < _text.Length && char.IsAsciiDigit(_text[i + 1])))
                    i++;
                if (i < _text.Length && (_text[i] == 'e' || _text[i] == 'E'))
                {
                    var j = i + 1;
                    if (j < _text.Length && (_text[j] == '+' || _text[j] == '-'))
                        j++;
                    if (j < _text.Length && char.IsAsciiDigit(_text[j]))
                    {
                        i = j;
                        while (i < _text.Length && char.IsAsciiDigit(_text[i]))
                            i++;
                    }
                }
            }
            // Type suffixes: 10u, 1.0f, 100L, 5usize, 2n
            while (i < _text.Length && char.IsAsciiLetter(_text[i]))
                i++;
            Emit(i - _pos, TokenKind.Number);
            return true;
        }

        private bool TryWord()
        {
            if (!IsIdentStart(_text[_pos]))
                return false;
            var i = _pos + 1;
            while (i < _text.Length && (IsIdentPart(_text[i]) || _def.DashInIdentifiers && _text[i] == '-' && i + 1 < _text.Length && IsIdentPart(_text[i + 1])))
                i++;
            var word = _text.AsSpan(_pos, i - _pos);
            var lookup = _def.CaseInsensitive ? word.ToString().ToLowerInvariant() : word.ToString();

            TokenKind kind;
            if (_def.Keywords.Contains(lookup))
                kind = TokenKind.Keyword;
            else if (_def.Constants.Contains(lookup))
                kind = TokenKind.Number;
            else if (_def.Types.Contains(lookup))
                kind = TokenKind.Type;
            else
            {
                var j = i;
                while (j < _text.Length && _text[j] == ' ')
                    j++;
                var next = j < _text.Length ? _text[j] : '\0';
                if (_def.KeyStrings && next == ':' && IsLineStartWord())
                    kind = TokenKind.Property;
                else if (next == '(' && !_def.KeyStrings)
                    kind = TokenKind.Function;
                else if (_def.CapitalizedTypes && char.IsUpper(word[0]) && word.Length > 1 && HasLower(word))
                    kind = TokenKind.Type;
                else
                    kind = TokenKind.Plain;
            }

            if (kind == TokenKind.Plain)
                Plain(i - _pos);
            else
                Emit(i - _pos, kind);
            return true;
        }

        private bool IsLineStartWord()
        {
            var k = _pos - 1;
            while (k >= 0 && (_text[k] == ' ' || _text[k] == '\t' || _text[k] == '-'))
                k--;
            return k < 0 || _text[k] == '\n';
        }

        private static bool HasLower(ReadOnlySpan<char> s)
        {
            foreach (var ch in s)
                if (char.IsLower(ch))
                    return true;
            return false;
        }

        private void Plain(int len)
        {
            if (_plainStart < 0)
                _plainStart = _pos;
            _pos += len;
        }

        private void FlushPlain()
        {
            if (_plainStart >= 0)
            {
                _tokens.Add(new Token(_plainStart, _pos - _plainStart, TokenKind.Plain));
                _plainStart = -1;
            }
        }

        private void Emit(int len, TokenKind kind)
        {
            FlushPlain();
            len = Math.Min(len, _text.Length - _pos);
            if (len <= 0)
                return;
            _tokens.Add(new Token(_pos, len, kind));
            _pos += len;
        }
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static List<Token> TokenizeDiff(string code)
    {
        var tokens = new List<Token>();
        var pos = 0;
        while (pos < code.Length)
        {
            var end = code.IndexOf('\n', pos);
            var lineEnd = end < 0 ? code.Length : end + 1;
            var kind = code[pos] switch
            {
                '+' => TokenKind.String,
                '-' => TokenKind.Preprocessor,
                '@' => TokenKind.Function,
                _ when code.AsSpan(pos).StartsWith("diff ") || code.AsSpan(pos).StartsWith("index ") => TokenKind.Keyword,
                _ => TokenKind.Plain,
            };
            tokens.Add(new Token(pos, lineEnd - pos, kind));
            pos = lineEnd;
        }
        return tokens;
    }

    /// <summary>HTML/XML: tags, attributes, attribute values, comments and entities.</summary>
    private static List<Token> TokenizeMarkup(string code)
    {
        var tokens = new List<Token>();
        var pos = 0;
        var plainStart = 0;

        void Flush(int upTo)
        {
            if (upTo > plainStart)
                tokens.Add(new Token(plainStart, upTo - plainStart, TokenKind.Plain));
        }

        while (pos < code.Length)
        {
            if (code[pos] == '<')
            {
                Flush(pos);
                if (code.AsSpan(pos).StartsWith("<!--"))
                {
                    var end = code.IndexOf("-->", pos + 4, StringComparison.Ordinal);
                    var len = end < 0 ? code.Length - pos : end + 3 - pos;
                    tokens.Add(new Token(pos, len, TokenKind.Comment));
                    pos += len;
                    plainStart = pos;
                    continue;
                }

                // Tag name
                var i = pos + 1;
                while (i < code.Length && (code[i] == '/' || code[i] == '!' || code[i] == '?'))
                    i++;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] is ':' or '-' or '_' or '.'))
                    i++;
                tokens.Add(new Token(pos, i - pos, TokenKind.Keyword));
                pos = i;

                // Attributes until '>'
                while (pos < code.Length && code[pos] != '>')
                {
                    var ch = code[pos];
                    if (ch is '"' or '\'')
                    {
                        var close = code.IndexOf(ch, pos + 1);
                        var len = close < 0 ? code.Length - pos : close + 1 - pos;
                        tokens.Add(new Token(pos, len, TokenKind.String));
                        pos += len;
                    }
                    else if (char.IsLetter(ch) || ch == '_' || ch == ':' || ch == '@')
                    {
                        var j = pos;
                        while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] is ':' or '-' or '_' or '.' or '@'))
                            j++;
                        tokens.Add(new Token(pos, j - pos, TokenKind.Property));
                        pos = j;
                    }
                    else
                    {
                        tokens.Add(new Token(pos, 1, TokenKind.Plain));
                        pos++;
                    }
                }
                if (pos < code.Length)
                {
                    tokens.Add(new Token(pos, 1, TokenKind.Keyword));
                    pos++;
                }
                plainStart = pos;
            }
            else if (code[pos] == '&')
            {
                var semi = code.IndexOf(';', pos);
                if (semi > pos && semi - pos < 10)
                {
                    Flush(pos);
                    tokens.Add(new Token(pos, semi + 1 - pos, TokenKind.Number));
                    pos = semi + 1;
                    plainStart = pos;
                }
                else
                {
                    pos++;
                }
            }
            else
            {
                pos++;
            }
        }
        Flush(code.Length);
        return tokens;
    }
}
