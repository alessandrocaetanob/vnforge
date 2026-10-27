using System.Buffers.Binary;
using System.Text;
using VNForge.Core;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Yscfg;

/// <summary>
/// Lê o <c>yscfg.ybn</c> (YSCF): <c>YSCF</c>, a versão (4 bytes), a configuração e, no fim do arquivo, o tamanho do título em 2 bytes
/// e o título em Shift-JIS, sem terminador. O VNForge só conhece o título; o resto é a configuração do jogo e passa intacto.
/// O formato está em docs/06-yuris-notas-tecnicas.md.
/// </summary>
/// <remarks>
/// O tamanho da configuração varia com a versão e nenhum campo diz onde o título começa, então o leitor procura a posição <c>p</c> em que
/// os 2 bytes lidos como tamanho batem exatamente com o que sobra até o fim do arquivo e os bytes seguintes são um Shift-JIS válido, sem
/// caractere de controle. Mais de uma posição possível recusa o arquivo em vez de adivinhar. Sem posição em Shift-JIS, tenta Windows-1252, a codificação
/// de um título que o build gravou com a estratégia <c>cp1252</c> (<see cref="YscfgFile.TitleIsShiftJis"/>).
/// </remarks>
public static class YscfgReader
{
    /// <summary>O tamanho do título cabe em 2 bytes.</summary>
    public const int MaxTitleBytes = ushort.MaxValue;

    private const int HeaderSize = 8;
    private const int LengthFieldSize = 2;

    private static ReadOnlySpan<byte> Magic => "YSCF"u8;

    /// <summary>Lê o YSCF de um arquivo no disco.</summary>
    /// <exception cref="InvalidFileException">Não é um YSCF ou não acho o título no fim.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscfgFile ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Lê o YSCF de um fluxo, do ponto atual até o fim.</summary>
    /// <exception cref="InvalidFileException">Não é um YSCF ou não acho o título no fim.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscfgFile Read(Stream stream)
    {
        var source = YbnReader.SourceOf(stream);
        return Read(YbnReader.ReadAll(stream, source), source);
    }

    /// <summary>Lê o YSCF de bytes já em memória.</summary>
    /// <param name="data">Conteúdo do <c>yscfg.ybn</c>.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <exception cref="InvalidFileException">Não é um YSCF ou não acho o título no fim.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscfgFile Read(ReadOnlyMemory<byte> data, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var version = new YbnReader(data, source).ReadMagicAndVersion(Magic);
        var span = data.Span;

        // O jogo original grava Shift-JIS. Um jogo que o VNForge já traduziu com a estratégia cp1252 grava o título em Windows-1252, que o
        // Shift-JIS estrito recusa (o í, 0xED, abre um caractere de 2 bytes): sem candidato em Shift-JIS, a segunda tentativa lê como Windows-1252.
        var (found, candidates) = Locate(span, StrictShiftJis());
        var shiftJis = true;
        if (candidates == 0)
        {
            (found, candidates) = Locate(span, GameEncodings.Windows1252);
            shiftJis = false;
        }

        if (candidates == 0)
        {
            throw new InvalidFileException(
                source,
                $"não achei o campo do título no fim do arquivo: esperava o tamanho em 2 bytes e o título em Shift-JIS até o byte {span.Length}, sem nada depois.");
        }

        if (candidates > 1)
        {
            throw new InvalidFileException(source, $"há {candidates} posições possíveis para o campo do título no fim do arquivo, e não dá para saber qual vale.");
        }

        return new YscfgFile(source, version, span[..found.Offset].ToArray(), found.Title, shiftJis);
    }

    private static ((int Offset, string Title) Found, int Count) Locate(ReadOnlySpan<byte> span, Encoding encoding)
    {
        (int Offset, string Title) found = (-1, string.Empty);
        var candidates = 0;
        for (var p = HeaderSize; p <= span.Length - LengthFieldSize; p++)
        {
            var declared = BinaryPrimitives.ReadUInt16LittleEndian(span[p..]);
            if (declared != span.Length - p - LengthFieldSize)
            {
                continue;
            }

            if (TryDecode(encoding, span.Slice(p + LengthFieldSize, declared)) is not { } text)
            {
                continue;
            }

            candidates++;
            found = (p, text);
        }

        return (found, candidates);
    }

    private static Encoding StrictShiftJis()
    {
        _ = GameEncodings.ShiftJis;
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string? TryDecode(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b < 0x20)
            {
                return null;
            }
        }

        try
        {
            return encoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
