using System.Text;

namespace VNForge.Core.Formatting;

/// <summary>
/// Mede um texto já formatado: as colunas de cada linha de cada caixa. Usa a mesma tabela de larguras e o mesmo tratamento de marcadores
/// que o <see cref="WordWrapper"/> (marcadores não ocupam coluna; <c>\n</c> fecha a linha; <c>\p</c> fecha a caixa).
/// </summary>
public static class LineMeasure
{
    /// <summary>Colunas de cada linha, agrupadas por caixa.</summary>
    /// <exception cref="ArgumentException">Marcador inválido ou chave sem fechamento no texto.</exception>
    public static IReadOnlyList<IReadOnlyList<int>> Columns(string text, CharWidth width)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(width);

        var boxes = new List<IReadOnlyList<int>>();
        var lines = new List<int>();
        var column = 0;
        var hasContent = false;

        void EndLine()
        {
            lines.Add(column);
            column = 0;
            hasContent = false;
        }

        void EndBox()
        {
            if (hasContent)
            {
                EndLine();
            }

            boxes.Add(lines);
            lines = [];
        }

        foreach (var (kind, w) in Tokens(text, width))
        {
            switch (kind)
            {
                case 'n':
                    EndLine();
                    break;
                case 'p':
                    EndBox();
                    break;
                default:
                    column += w;
                    hasContent |= kind != 's';
                    break;
            }
        }

        if (hasContent || lines.Count > 0 || boxes.Count == 0)
        {
            EndBox();
        }

        return boxes;
    }

    // 'n' = \n, 'p' = \p, 's' = espaço, 'g' = glifo, 'm' = marcador sem largura.
    private static IEnumerable<(char Kind, int Width)> Tokens(string text, CharWidth width)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    throw new ArgumentException("Barra invertida no fim do texto, sem caractere de marcador.", nameof(text));
                }

                var next = text[i + 1];
                i += 2;
                switch (next)
                {
                    case 'n':
                        yield return ('n', 0);
                        break;
                    case 'p':
                        yield return ('p', 0);
                        break;
                    case 'c':
                    case 'u':
                        yield return ('m', 0);
                        break;
                    case '\\':
                    case '{':
                        yield return ('g', width(new Rune(next)));
                        break;
                    default:
                        throw new ArgumentException($"Marcador inválido '\\{next}' no texto.", nameof(text));
                }

                continue;
            }

            if (c == '{')
            {
                var close = text.IndexOf('}', i);
                if (close < 0)
                {
                    throw new ArgumentException("Chave '{' sem fechamento no texto.", nameof(text));
                }

                yield return ('m', 0);
                i = close + 1;
                continue;
            }

            var rune = Rune.GetRuneAt(text, i);
            i += rune.Utf16SequenceLength;
            yield return (rune.Value == ' ' ? 's' : 'g', width(rune));
        }
    }
}
