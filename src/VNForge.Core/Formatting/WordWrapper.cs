using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>
/// Quebra de linha por palavra (E4.4). Função pura: recebe a tradução com marcadores (doc 02) e devolve o
/// texto com <c>\n</c> inseridos. Marcadores não ocupam colunas e nunca são partidos; <c>\n</c> já existentes
/// são respeitados; <c>\p</c> começa uma caixa nova e zera a contagem de linhas.
/// </summary>
public static class WordWrapper
{
    private enum Kind { Glyph, Space, Marker, Newline, Page }

    private readonly record struct Token(string Raw, int Width, Kind Kind);

    /// <exception cref="ArgumentException">Marcador inválido ou chave sem fechamento no texto.</exception>
    public static WrapResult Wrap(string text, WrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxLines is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "O limite de linhas deve ser de ao menos 1.");
        }

        if (options.MaxWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A largura máxima deve ser de ao menos 1 coluna.");
        }

        var state = new State(options);
        var word = new List<Token>();
        foreach (Token token in Tokenize(text, options.Width))
        {
            if (token.Kind is Kind.Glyph or Kind.Marker)
            {
                word.Add(token);
                continue;
            }

            state.PlaceWord(word);
            word.Clear();
            switch (token.Kind)
            {
                case Kind.Space:
                    state.AddSpace(token);
                    break;
                case Kind.Newline:
                    state.ExistingNewline();
                    break;
                default:
                    state.Page();
                    break;
            }
        }

        state.PlaceWord(word);
        return state.Finish();
    }

    private static IEnumerable<Token> Tokenize(string text, CharWidth widthOf)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    throw new ArgumentException("Barra invertida no fim do texto, sem caractere de marcador.", nameof(text));
                }

                char next = text[i + 1];
                string raw = text.Substring(i, 2);
                i += 2;
                switch (next)
                {
                    case 'n':
                        yield return new Token(raw, 0, Kind.Newline);
                        break;
                    case 'p':
                        yield return new Token(raw, 0, Kind.Page);
                        break;
                    case 'c':
                    case 'u':
                        yield return new Token(raw, 0, Kind.Marker);
                        break;
                    case '\\':
                    case '{':
                        yield return new Token(raw, widthOf(new Rune(next)), Kind.Glyph);
                        break;
                    default:
                        throw new ArgumentException($"Marcador inválido '{raw}' no texto.", nameof(text));
                }

                continue;
            }

            if (c == '{')
            {
                int close = text.IndexOf('}', i);
                if (close < 0)
                {
                    throw new ArgumentException("Chave '{' sem fechamento no texto.", nameof(text));
                }

                yield return new Token(text[i..(close + 1)], 0, Kind.Marker);
                i = close + 1;
                continue;
            }

            Rune rune = Rune.GetRuneAt(text, i);
            string glyph = text.Substring(i, rune.Utf16SequenceLength);
            i += rune.Utf16SequenceLength;
            yield return new Token(glyph, widthOf(rune), rune.Value == ' ' ? Kind.Space : Kind.Glyph);
        }
    }

    private sealed class State(WrapOptions options)
    {
        private readonly StringBuilder _out = new();
        private readonly List<int> _boxes = [];
        private readonly StringBuilder _pending = new();
        private int _pendingWidth;
        private int _column;
        private int _line;
        private bool _lineHasContent;

        private int Max => options.MaxWidth;

        public void AddSpace(Token space)
        {
            _pending.Append(space.Raw);
            _pendingWidth += space.Width;
        }

        public void PlaceWord(List<Token> word)
        {
            if (word.Count == 0)
            {
                return;
            }

            int wordWidth = word.Sum(t => t.Width);
            if (_column + _pendingWidth + wordWidth <= Max)
            {
                Flush();
                Append(word);
                return;
            }

            // Espaços que ficariam no fim ou no começo de uma linha quebrada são descartados.
            DropPending();
            if (_column > 0 && wordWidth > 0)
            {
                AutoNewline();
            }

            if (wordWidth <= Max)
            {
                Append(word);
                return;
            }

            Cut(word);
        }

        public void ExistingNewline()
        {
            DropPending();
            _out.Append("\\n");
            NextLine();
        }

        public void Page()
        {
            DropPending();
            _out.Append("\\p");
            CloseBox();
        }

        public WrapResult Finish()
        {
            Flush();
            if (_line > 0 || _lineHasContent || _boxes.Count == 0)
            {
                CloseBox();
            }

            return new WrapResult(_out.ToString(), _boxes, options.MaxLines);
        }

        private void Flush()
        {
            if (_pending.Length == 0)
            {
                return;
            }

            _out.Append(_pending);
            _column += _pendingWidth;
            _lineHasContent = true;
            DropPending();
        }

        private void DropPending()
        {
            _pending.Clear();
            _pendingWidth = 0;
        }

        private void Append(List<Token> tokens)
        {
            foreach (Token t in tokens)
            {
                _out.Append(t.Raw);
                _column += t.Width;
            }

            _lineHasContent = true;
        }

        // Palavra maior que a linha: corta no limite. Um caractere sozinho sempre é colocado, mesmo mais
        // largo que a linha, para a quebra avançar.
        private void Cut(List<Token> word)
        {
            foreach (Token t in word)
            {
                if (t.Width > 0 && _column > 0 && _column + t.Width > Max)
                {
                    AutoNewline();
                }

                _out.Append(t.Raw);
                _column += t.Width;
            }

            _lineHasContent = true;
        }

        private void AutoNewline()
        {
            _out.Append("\\n");
            NextLine();
        }

        private void NextLine()
        {
            _line++;
            _column = 0;
            _lineHasContent = false;
        }

        private void CloseBox()
        {
            _boxes.Add(_line + (_lineHasContent ? 1 : 0));
            _line = 0;
            _column = 0;
            _lineHasContent = false;
        }
    }
}
