using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Yscm;

/// <summary>
/// Lê o <c>ysc.ybn</c> (YSCM): cabeçalho de 16 bytes e a lista de comandos, cada um com seus parâmetros.
/// O formato está em docs/06-yuris-notas-tecnicas.md.
/// </summary>
public static class YscmReader
{
    /// <summary>O YSTB guarda o opcode em um byte, então a lista tem no máximo 256 comandos.</summary>
    public const int MaxCommands = 256;

    /// <summary>Mensagens de erro depois dos comandos segundo o yuri (<c>NErrStr</c>, <c>common.py:9</c>, <c>yuri.md:636</c>).</summary>
    public const int ErrorMessageCountYuri = 37;

    /// <summary>Mensagens de erro depois dos comandos segundo o VNTextPatch (<c>Notes.txt:16</c>).</summary>
    public const int ErrorMessageCountVnTextPatch = 0x90;

    /// <summary>Tamanho da tabela que fecha o arquivo (<c>yuri.md:637</c>, <c>Notes.txt:17</c>).</summary>
    public const int TableSize = 256;

    private static ReadOnlySpan<byte> Magic => "YSCM"u8;

    /// <summary>Lê o YSCM de um arquivo no disco.</summary>
    /// <exception cref="InvalidFileException">Não é um YSCM ou a lista de comandos está truncada.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscmFile ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Lê o YSCM de um fluxo, do ponto atual até o fim.</summary>
    /// <exception cref="InvalidFileException">Não é um YSCM ou a lista de comandos está truncada.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscmFile Read(Stream stream)
    {
        var source = YbnReader.SourceOf(stream);
        return Read(YbnReader.ReadAll(stream, source), source);
    }

    /// <summary>Lê o YSCM de bytes já em memória.</summary>
    /// <param name="data">Conteúdo do <c>ysc.ybn</c>.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <exception cref="InvalidFileException">Não é um YSCM ou a lista de comandos está truncada.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YscmFile Read(ReadOnlyMemory<byte> data, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reader = new YbnReader(data, source);
        var version = reader.ReadMagicAndVersion(Magic);
        var count = reader.ReadUInt32("a quantidade de comandos");
        reader.ReadUInt32("o campo de padding");

        if (count > MaxCommands)
        {
            throw reader.Error(
                $"declara {count.ToString(CultureInfo.InvariantCulture)} comandos, mas o opcode do YSTB cabe em um byte e a lista tem no máximo {MaxCommands}.");
        }

        var commands = new List<YscmCommand>((int)count);
        for (var id = 0; id < count; id++)
        {
            commands.Add(ReadCommand(reader, (byte)id));
        }

        return new YscmFile(source, version, commands, ReadTrailer(reader, commands.Count));
    }

    /// <summary>
    /// Valida a cauda e devolve a quantidade de mensagens de erro: strings terminadas em 0 até restarem exatamente
    /// 256 bytes (a tabela final), com 37 ou 0x90 strings. Uma contagem de comandos ou de parâmetros errada faz o
    /// leitor consumir as mensagens como comandos, e a cauda deixa de fechar.
    /// </summary>
    private static int ReadTrailer(YbnReader reader, int commandCount)
    {
        var messages = 0;
        while (reader.Remaining > TableSize)
        {
            reader.ReadZeroTerminatedShiftJis($"a mensagem de erro {messages}");
            messages++;
        }

        var hint = $"A contagem de comandos ({commandCount}) ou de parâmetros pode estar errada.";
        if (reader.Remaining != TableSize)
        {
            throw reader.Error(
                $"depois dos comandos a cauda não fecha: as mensagens de erro terminam com {reader.Remaining} bytes restantes e a tabela final tem {TableSize}. {hint}");
        }

        if (messages != ErrorMessageCountYuri && messages != ErrorMessageCountVnTextPatch)
        {
            throw reader.Error(
                $"depois dos comandos há {messages} mensagens de erro, mas o formato tem {ErrorMessageCountYuri} ou {ErrorMessageCountVnTextPatch}. {hint}");
        }

        return messages;
    }

    private static YscmCommand ReadCommand(YbnReader reader, byte id)
    {
        var name = reader.ReadZeroTerminatedShiftJis($"o nome do comando {id}");
        var argumentCount = reader.ReadByte($"a quantidade de parâmetros do comando {id} ('{name}')");
        var arguments = new YscmArgument[argumentCount];
        for (var i = 0; i < arguments.Length; i++)
        {
            var where = $"do parâmetro {i} do comando {id} ('{name}')";
            var argumentName = reader.ReadZeroTerminatedShiftJis($"o nome {where}");
            var typeCode = reader.ReadByte($"o tipo {where}");
            var flags = reader.ReadByte($"o byte de flags {where}");
            arguments[i] = new YscmArgument(argumentName, typeCode, flags);
        }

        return new YscmCommand(id, name, arguments);
    }
}
