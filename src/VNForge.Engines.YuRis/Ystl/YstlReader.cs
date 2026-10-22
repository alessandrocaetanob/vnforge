using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Ystl;

/// <summary>
/// Lê o <c>yst_list.ybn</c> (YSTL): cabeçalho de 12 bytes e um registro por script. O campo final de cada registro
/// (<c>txt_count</c>) existe só em parte das versões e as fontes divergem no limiar (docs/06), então o leitor
/// tenta o layout da versão primeiro e o outro depois, e vale o que consome o arquivo inteiro.
/// </summary>
public static class YstlReader
{
    /// <summary>
    /// Primeira versão em que o leitor tenta o layout com <c>txt_count</c> primeiro: a <c>VScrNew</c> do ystl.py
    /// (<c>ystl.py:8</c>). O yuri.md usa 477 (<c>yuri.md:783</c>), então 474 a 476 cai na segunda tentativa se o arquivo for assim.
    /// </summary>
    public const uint FirstTextCountVersion = 474;

    private const int FixedEntryBytes = 4 + 4 + 8 + 4 + 4;
    private static ReadOnlySpan<byte> Magic => "YSTL"u8;

    /// <summary>Lê o YSTL de um arquivo no disco.</summary>
    /// <exception cref="InvalidFileException">Não é um YSTL ou nenhum dos dois layouts leu o arquivo.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstlFile ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Lê o YSTL de um fluxo, do ponto atual até o fim.</summary>
    /// <exception cref="InvalidFileException">Não é um YSTL ou nenhum dos dois layouts leu o arquivo.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstlFile Read(Stream stream)
    {
        var source = YbnReader.SourceOf(stream);
        return Read(YbnReader.ReadAll(stream, source), source);
    }

    /// <summary>Lê o YSTL de bytes já em memória.</summary>
    /// <param name="data">Conteúdo do <c>yst_list.ybn</c>.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <exception cref="InvalidFileException">Não é um YSTL ou nenhum dos dois layouts leu o arquivo.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstlFile Read(ReadOnlyMemory<byte> data, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var version = new YbnReader(data, source).ReadMagicAndVersion(Magic);

        var preferred = version >= FirstTextCountVersion;
        var first = Attempt(data, source, version, preferred);
        if (first.File is not null)
        {
            return first.File;
        }

        var second = Attempt(data, source, version, !preferred);
        if (second.File is not null)
        {
            return second.File;
        }

        var furthest = second.Position > first.Position ? second : first;
        throw new InvalidFileException(
            source,
            $"{furthest.Error!.Reason} (tentei os layouts com e sem o campo de contagem de textos; este é o erro do que foi mais longe).");
    }

    private static Result Attempt(ReadOnlyMemory<byte> data, string source, uint version, bool withTextCount)
    {
        var reader = new YbnReader(data, source);
        try
        {
            reader.ReadMagicAndVersion(Magic);
            return new Result(ReadScripts(reader, version, withTextCount), null, reader.Position);
        }
        catch (InvalidFileException ex)
        {
            return new Result(null, ex, reader.Position);
        }
    }

    private static YstlFile ReadScripts(YbnReader reader, uint version, bool withTextCount)
    {
        var count = reader.ReadUInt32("a quantidade de scripts");
        var entryMin = (long)FixedEntryBytes + (withTextCount ? 4 : 0);
        if (count * entryMin > reader.Remaining)
        {
            throw reader.Error(
                $"declara {count.ToString(CultureInfo.InvariantCulture)} scripts, que precisam de pelo menos {count * entryMin} bytes, mas restam {reader.Remaining} depois do cabeçalho.");
        }

        var scripts = new List<YstlScript>((int)count);
        for (uint i = 0; i < count; i++)
        {
            scripts.Add(ReadScript(reader, i, withTextCount));
        }

        if (reader.Remaining != 0)
        {
            throw reader.Error($"sobram {reader.Remaining} bytes depois do último script (posição {reader.Position}).");
        }

        return new YstlFile(reader.Source, version, withTextCount, scripts);
    }

    private static YstlScript ReadScript(YbnReader reader, uint position, bool withTextCount)
    {
        var index = reader.ReadUInt32($"o índice do script {position}");
        if (index != position)
        {
            throw reader.Error($"o script na posição {position} traz o índice {index}; os índices precisam ser sequenciais a partir de 0.");
        }

        var pathSize = reader.ReadUInt32($"o tamanho do caminho do script {position}");
        if (pathSize > reader.Remaining)
        {
            throw reader.Error(
                $"o caminho do script {position} declara {pathSize} bytes, mas restam só {reader.Remaining} no arquivo (posição {reader.Position}).");
        }

        var path = reader.ReadShiftJis((int)pathSize, $"o caminho do script {position}");
        var modified = reader.ReadUInt64($"a data de modificação do script {position} ('{path}')");
        var variables = reader.ReadInt32($"a contagem de variáveis do script {position} ('{path}')");
        var labels = reader.ReadUInt32($"a contagem de rótulos do script {position} ('{path}')");
        uint? texts = withTextCount ? reader.ReadUInt32($"a contagem de textos do script {position} ('{path}')") : null;
        return new YstlScript(index, path, modified, variables, labels, texts);
    }

    private readonly record struct Result(YstlFile? File, InvalidFileException? Error, int Position);
}
