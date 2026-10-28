using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Yslb;

/// <summary>
/// Lê o <c>ysl.ybn</c> (YSLB): cabeçalho de 12 bytes, a tabela <c>idx_begin</c> de 256 inteiros (índice de busca por hash, que o
/// leitor pula) e um registro por rótulo. O hash do nome não é conferido: a busca é por nome (docs/06, "Saltos entre scripts").
/// </summary>
public static class YslbReader
{
    /// <summary>Bytes de um registro sem o nome: o tamanho do nome (1), o hash (4), a posição (4), o script (2) e os dois níveis (2).</summary>
    private const int FixedLabelBytes = 1 + 4 + 4 + 2 + 1 + 1;

    private const int IndexTableBytes = 256 * 4;
    private static ReadOnlySpan<byte> Magic => "YSLB"u8;

    /// <summary>Lê o YSLB de um arquivo no disco.</summary>
    /// <exception cref="InvalidFileException">Não é um YSLB ou o conteúdo não fecha.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YslbFile ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(YbnReader.ReadAll(stream, YbnReader.SourceOf(stream)), YbnReader.SourceOf(stream));
    }

    /// <summary>Lê o YSLB de bytes já em memória.</summary>
    /// <param name="data">Conteúdo do <c>ysl.ybn</c>.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <exception cref="InvalidFileException">Não é um YSLB ou o conteúdo não fecha.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YslbFile Read(ReadOnlyMemory<byte> data, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reader = new YbnReader(data, source);
        var version = reader.ReadMagicAndVersion(Magic);
        var count = reader.ReadUInt32("a quantidade de rótulos");
        reader.Skip(IndexTableBytes, "a tabela de índice por hash");
        if (count * (long)FixedLabelBytes > reader.Remaining)
        {
            throw reader.Error(
                $"declara {count.ToString(CultureInfo.InvariantCulture)} rótulos, que precisam de pelo menos {count * (long)FixedLabelBytes} bytes, mas restam {reader.Remaining} depois da tabela de índice.");
        }

        var labels = new List<YslbLabel>((int)count);
        for (uint i = 0; i < count; i++)
        {
            labels.Add(ReadLabel(reader, i));
        }

        if (reader.Remaining != 0)
        {
            throw reader.Error($"sobram {reader.Remaining} bytes depois do último rótulo (posição {reader.Position}).");
        }

        return new YslbFile(source, version, labels);
    }

    private static YslbLabel ReadLabel(YbnReader reader, uint position)
    {
        var nameSize = reader.ReadByte($"o tamanho do nome do rótulo {position}");
        var name = reader.ReadShiftJis(nameSize, $"o nome do rótulo {position}");
        reader.Skip(4, $"o hash do rótulo {position} ('{name}')");
        var codePosition = reader.ReadUInt32($"a posição do rótulo {position} ('{name}')");
        var scriptId = reader.ReadUInt16($"o script do rótulo {position} ('{name}')");
        var ifLevel = reader.ReadByte($"o nível de IF do rótulo {position} ('{name}')");
        var loopLevel = reader.ReadByte($"o nível de LOOP do rótulo {position} ('{name}')");
        return new YslbLabel(name, codePosition, scriptId, ifLevel, loopLevel);
    }
}
