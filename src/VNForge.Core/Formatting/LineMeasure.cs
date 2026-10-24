using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>Um trecho da linha na régua de colunas: um glifo (com a largura da tabela) ou um marcador, que não ocupa coluna.</summary>
/// <param name="Text">O que desenhar: o glifo, ou o marcador como está (<c>\c</c>, <c>{VAR:x}</c>). <c>\\</c> e <c>\{</c> viram <c>\</c> e <c>{</c>.</param>
/// <param name="Column">Coluna em que o trecho começa.</param>
/// <param name="Width">Colunas que ocupa; 0 para marcador.</param>
/// <param name="IsMarker">Verdadeiro para marcador de controle ou variável.</param>
public readonly record struct LineSegment(string Text, int Column, int Width, bool IsMarker);

/// <summary>Uma linha de uma caixa: o texto como está (com marcadores como <c>\c</c> e <c>{VAR:x}</c>), as colunas que ocupa e os trechos dela na régua de colunas.</summary>
public readonly record struct MeasuredLine(string Text, int Columns, IReadOnlyList<LineSegment> Segments);

/// <summary>
/// Mede um texto já formatado: as colunas de cada linha de cada caixa. Usa a mesma tabela de larguras e o mesmo tratamento de marcadores
/// que o <see cref="WordWrapper"/> (marcadores não ocupam coluna; <c>\n</c> fecha a linha; <c>\p</c> fecha a caixa).
/// </summary>
public static class LineMeasure
{
    /// <summary>Colunas de cada linha, agrupadas por caixa.</summary>
    /// <exception cref="ArgumentException">Marcador inválido ou chave sem fechamento no texto.</exception>
    public static IReadOnlyList<IReadOnlyList<int>> Columns(string text, CharWidth width) =>
        [.. Lines(text, width).Select(box => (IReadOnlyList<int>)[.. box.Select(line => line.Columns)])];

    /// <summary>
    /// O texto de cada linha (sem os <c>\n</c> e <c>\p</c> que a separam; os demais marcadores ficam) e as colunas dela, agrupadas por caixa.
    /// Divide nos mesmos pontos que <see cref="Columns"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Marcador inválido ou chave sem fechamento no texto.</exception>
    public static IReadOnlyList<IReadOnlyList<MeasuredLine>> Lines(string text, CharWidth width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(width);

        var state = new Measurement();

        foreach (var (kind, w, start, end) in Tokens(text, width))
        {
            switch (kind)
            {
                case 'n':
                    state.EndLine();
                    break;
                case 'p':
                    state.EndBox();
                    break;
                default:
                    var raw = text.Substring(start, end - start);
                    state.Segments.Add(new LineSegment(kind == 'g' && raw[0] == '\\' ? raw[1..] : raw, state.Column, w, kind == 'm'));
                    state.Column += w;
                    state.HasContent |= kind != 's';
                    state.Text.Append(raw);
                    break;
            }
        }

        if (state.HasContent || state.Lines.Count > 0 || state.Boxes.Count == 0)
        {
            state.EndBox();
        }

        return state.Boxes;
    }

    /// <summary>O que a leitura acumulou: a coluna e o texto da linha aberta, as linhas da caixa aberta e as caixas fechadas.</summary>
    private sealed class Measurement
    {
        public List<IReadOnlyList<MeasuredLine>> Boxes { get; } = [];

        public List<MeasuredLine> Lines { get; private set; } = [];

        public StringBuilder Text { get; } = new();

        public List<LineSegment> Segments { get; } = [];

        public int Column { get; set; }

        public bool HasContent { get; set; }

        public void EndLine()
        {
            Lines.Add(new MeasuredLine(Text.ToString(), Column, [.. Segments]));
            Text.Clear();
            Segments.Clear();
            Column = 0;
            HasContent = false;
        }

        public void EndBox()
        {
            if (HasContent)
            {
                EndLine();
            }

            Boxes.Add(Lines);
            Lines = [];
        }
    }

    // 'n' = \n, 'p' = \p, 's' = espaço, 'g' = glifo, 'm' = marcador sem largura. Start e End são os índices do trecho no texto.
    private static IEnumerable<(char Kind, int Width, int Start, int End)> Tokens(string text, CharWidth width)
    {
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            (char Kind, int Width) token;
            if (text[i] == '\\')
            {
                token = ReadEscape(text, ref i, width);
            }
            else if (text[i] == '{')
            {
                token = ReadBraced(text, ref i);
            }
            else
            {
                var rune = Rune.GetRuneAt(text, i);
                i += rune.Utf16SequenceLength;
                token = (rune.Value == ' ' ? 's' : 'g', width(rune));
            }

            yield return (token.Kind, token.Width, start, i);
        }
    }

    private static (char Kind, int Width) ReadEscape(string text, ref int i, CharWidth width)
    {
        if (i + 1 >= text.Length)
        {
            throw new ArgumentException("Barra invertida no fim do texto, sem caractere de marcador.", nameof(text));
        }

        var next = text[i + 1];
        i += 2;
        return next switch
        {
            'n' => ('n', 0),
            'p' => ('p', 0),
            'c' or 'u' => ('m', 0),
            '\\' or '{' => ('g', width(new Rune(next))),
            _ => throw new ArgumentException($"Marcador inválido '\\{next}' no texto.", nameof(text)),
        };
    }

    private static (char Kind, int Width) ReadBraced(string text, ref int i)
    {
        var close = text.IndexOf('}', i);
        if (close < 0)
        {
            throw new ArgumentException("Chave '{' sem fechamento no texto.", nameof(text));
        }

        i = close + 1;
        return ('m', 0);
    }
}
