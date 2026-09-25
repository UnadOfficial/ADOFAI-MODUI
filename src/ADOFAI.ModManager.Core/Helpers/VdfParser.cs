namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>
/// 极简、容错的 Valve Data Format（VDF）解析器，专用于 libraryfolders.vdf。
/// 支持：嵌套花括号、带引号/不带引号的键值、"\\" 与 \" 等转义、// 行注释。
/// 输出为 Dictionary&lt;string, object&gt;，值为 string（叶子）或嵌套 Dictionary。
/// </summary>
public static class VdfParser
{
    public static bool TryParse(string? text, out Dictionary<string, object> root)
    {
        root = new Dictionary<string, object>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var parser = new Parser(text);
            parser.ParseInto(root, closingBraceExpected: false);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>在解析树中递归收集指定键的全部字符串值（如 "path"）。</summary>
    public static IReadOnlyList<string> CollectStringValues(IDictionary<string, object>? node, string key)
    {
        var result = new List<string>();
        Collect(node, key, result);
        return result;
    }

    private static void Collect(IDictionary<string, object>? node, string key, List<string> output)
    {
        if (node is null)
        {
            return;
        }

        foreach (var (k, value) in node)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && value is string s)
            {
                output.Add(s);
            }

            if (value is Dictionary<string, object> child)
            {
                Collect(child, key, output);
            }
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _pos;

        public Parser(string text) => _text = text;

        public void ParseInto(Dictionary<string, object> target, bool closingBraceExpected)
        {
            while (true)
            {
                SkipWhitespaceAndComments();

                if (_pos >= _text.Length)
                {
                    if (closingBraceExpected)
                    {
                        throw new FormatException("Unexpected end of file, expected '}'.");
                    }

                    return;
                }

                var c = _text[_pos];
                if (c == '}')
                {
                    if (!closingBraceExpected)
                    {
                        throw new FormatException("Unexpected '}' at top level.");
                    }

                    _pos++; // 消费 '}'，返回上一层
                    return;
                }

                var key = ReadToken();
                SkipWhitespaceAndComments();

                if (_pos < _text.Length && _text[_pos] == '{')
                {
                    _pos++; // 消费 '{'
                    var child = new Dictionary<string, object>(StringComparer.Ordinal);
                    ParseInto(child, closingBraceExpected: true);
                    target[key] = child;
                }
                else
                {
                    var value = ReadToken();
                    target[key] = value;
                }
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_pos < _text.Length)
            {
                var c = _text[_pos];
                if (char.IsWhiteSpace(c))
                {
                    _pos++;
                }
                else if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
                {
                    // 行注释：跳到行尾
                    while (_pos < _text.Length && _text[_pos] != '\n')
                    {
                        _pos++;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private string ReadToken()
        {
            SkipWhitespaceAndComments();
            if (_pos >= _text.Length)
            {
                throw new FormatException("Expected a token.");
            }

            var c = _text[_pos];

            if (c == '"')
            {
                _pos++;
                var sb = new System.Text.StringBuilder();
                while (_pos < _text.Length)
                {
                    var ch = _text[_pos++];
                    if (ch == '\\' && _pos < _text.Length)
                    {
                        var esc = _text[_pos++];
                        sb.Append(esc switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => esc,
                        });
                    }
                    else if (ch == '"')
                    {
                        return sb.ToString();
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }

                throw new FormatException("Unterminated quoted string.");
            }

            // 不带引号的 token：读到空白或花括号
            var start = _pos;
            while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]) && _text[_pos] is not '{' and not '}')
            {
                _pos++;
            }

            if (_pos == start)
            {
                throw new FormatException($"Unexpected character '{_text[_pos]}'.");
            }

            return _text[start.._pos];
        }
    }
}
