using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using VNForge.Core;
using VNForge.Engines.YuRis.Yscm;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// Lê um <c>yst*.ybn</c> (YSTB) a partir da versão 300: cabeçalho de 32 bytes e quatro seções (instruções, argumentos,
/// recursos e números de linha), cada uma cifrada com o mesmo XOR de 4 bytes. As chaves conhecidas são tentadas primeiro e
/// a derivada dos bytes 8 a 11 da seção de argumentos depois; toda candidata só vale se o script inteiro decifrar de forma
/// consistente (docs/06-yuris-notas-tecnicas.md).
/// </summary>
public static class YstbReader
{
    /// <summary>Primeira versão com o layout de quatro seções; abaixo dela as instruções têm tamanho variável.</summary>
    public const uint FirstSupportedVersion = 300;

    private const int HeaderBytes = 32;
    private const int InstructionBytes = 4;
    private const int ArgumentBytes = 12;
    private const int KeyOffsetInArguments = 8;
    private const byte MaxAssignOp = 8;

    private static readonly uint[] KnownKeyValues = [0xD36FAC96, 0x07B4024A];

    private static readonly ReadOnlyCollection<uint> KnownKeysView = Array.AsReadOnly(KnownKeyValues);

    private static ReadOnlySpan<byte> Magic => "YSTB"u8;

    /// <summary>
    /// Chaves de outros leitores, na notação do yuri.md, tentadas antes da derivada:
    /// <c>0xD36FAC96</c> (versão 290 ou mais) e <c>0x07B4024A</c> (versões 2xx).
    /// </summary>
    public static IReadOnlyList<uint> KnownKeys => KnownKeysView;

    /// <summary>Lê o YSTB de um arquivo no disco.</summary>
    /// <param name="path">Caminho do <c>yst*.ybn</c>.</param>
    /// <param name="commands">O <c>ysc.ybn</c> do mesmo jogo, que dá o nome de cada opcode.</param>
    /// <exception cref="InvalidFileException">Não é um YSTB suportado, o layout não fecha ou nenhuma chave decifra o script.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstbFile ReadFile(string path, YscmFile commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream, commands);
    }

    /// <summary>Lê o YSTB de um fluxo, do ponto atual até o fim.</summary>
    /// <exception cref="InvalidFileException">Não é um YSTB suportado, o layout não fecha ou nenhuma chave decifra o script.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstbFile Read(Stream stream, YscmFile commands)
    {
        var source = YbnReader.SourceOf(stream);
        return Read(YbnReader.ReadAll(stream, source), source, commands);
    }

    /// <summary>Lê o YSTB de bytes já em memória.</summary>
    /// <param name="data">Conteúdo do <c>yst*.ybn</c>.</param>
    /// <param name="source">Nome do arquivo, para as mensagens de erro.</param>
    /// <param name="commands">O <c>ysc.ybn</c> do mesmo jogo, que dá o nome de cada opcode.</param>
    /// <exception cref="InvalidFileException">Não é um YSTB suportado, o layout não fecha ou nenhuma chave decifra o script.</exception>
    /// <exception cref="UnknownVersionException">A versão do cabeçalho está fora da faixa conhecida.</exception>
    public static YstbFile Read(ReadOnlyMemory<byte> data, string source, YscmFile commands)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(commands);

        var reader = new YbnReader(data, source);
        var version = reader.ReadMagicAndVersion(Magic);
        if (version < FirstSupportedVersion)
        {
            throw reader.Error(
                $"o layout do YSTB da versão {version} (abaixo da {FirstSupportedVersion}, com comandos e argumentos na mesma seção) não é suportado; o leitor trata da versão {FirstSupportedVersion} em diante.");
        }

        var layout = ReadLayout(reader, data.Length);
        var rules = RangeRules.From(commands);
        var candidates = BuildCandidates(data.Span, layout);

        Failure? furthest = null;
        foreach (var candidate in candidates)
        {
            try
            {
                return Decode(data, source, version, layout, candidate, commands, rules);
            }
            catch (DecodeFailure failure)
            {
                if (furthest is null || failure.Position > furthest.Position)
                {
                    furthest = new Failure(failure.Position, failure.Reason, candidate.Key);
                }
            }
        }

        var tried = string.Join(", ", candidates.Select(c => $"0x{c.Key:X8}"));
        throw reader.Error(
            $"nenhuma chave decifrou o script (tentei {tried}); a chave vem dos bytes 8 a 11 da seção de argumentos e só vale se o primeiro argumento tem offset 0. "
            + $"Erro da tentativa que foi mais longe, com a chave 0x{furthest!.Key:X8}: {furthest.Reason}");
    }

    private static Layout ReadLayout(YbnReader reader, int fileLength)
    {
        var count = reader.ReadUInt32("a quantidade de instruções");
        var instructionsSize = reader.ReadUInt32("o tamanho da seção de instruções");
        var argumentsSize = reader.ReadUInt32("o tamanho da seção de argumentos");
        var resourcesSize = reader.ReadUInt32("o tamanho da seção de recursos");
        var lineNumbersSize = reader.ReadUInt32("o tamanho da seção de números de linha");
        var padding = reader.ReadUInt32("o padding do cabeçalho");

        if (instructionsSize != (long)count * InstructionBytes)
        {
            throw reader.Error(
                $"o cabeçalho declara {Text(count)} instruções e uma seção de instruções de {Text(instructionsSize)} bytes; o tamanho precisa ser {Text(count)} x {InstructionBytes} = {(long)count * InstructionBytes}.");
        }

        if (lineNumbersSize != (long)count * InstructionBytes)
        {
            throw reader.Error(
                $"o cabeçalho declara {Text(count)} instruções e uma seção de números de linha de {Text(lineNumbersSize)} bytes; o tamanho precisa ser {Text(count)} x {InstructionBytes} = {(long)count * InstructionBytes}.");
        }

        if (argumentsSize % ArgumentBytes != 0)
        {
            throw reader.Error(
                $"a seção de argumentos declara {Text(argumentsSize)} bytes, que não é múltiplo de {ArgumentBytes} (cada argumento tem {ArgumentBytes} bytes).");
        }

        var expected = HeaderBytes + (long)instructionsSize + argumentsSize + resourcesSize + lineNumbersSize;
        if (expected != fileLength)
        {
            throw reader.Error(
                $"o cabeçalho prevê {expected} bytes ({HeaderBytes} de cabeçalho, {Text(instructionsSize)} de instruções, {Text(argumentsSize)} de argumentos, {Text(resourcesSize)} de recursos e {Text(lineNumbersSize)} de números de linha), mas o arquivo tem {fileLength}.");
        }

        var instructionsOffset = HeaderBytes;
        var argumentsOffset = instructionsOffset + (int)instructionsSize;
        var resourcesOffset = argumentsOffset + (int)argumentsSize;
        var lineNumbersOffset = resourcesOffset + (int)resourcesSize;
        return new Layout(
            (int)count,
            padding,
            new Section(instructionsOffset, (int)instructionsSize),
            new Section(argumentsOffset, (int)argumentsSize),
            new Section(resourcesOffset, (int)resourcesSize),
            new Section(lineNumbersOffset, (int)lineNumbersSize));
    }

    private static List<Candidate> BuildCandidates(ReadOnlySpan<byte> data, Layout layout)
    {
        var candidates = new List<Candidate>();
        if (layout.Arguments.Size < KeyOffsetInArguments + 4)
        {
            candidates.Add(new Candidate(0, YstbKeyOrigin.None));
            foreach (var key in KnownKeyValues)
            {
                candidates.Add(new Candidate(key, YstbKeyOrigin.Known));
            }

            return candidates;
        }

        // As conhecidas vêm antes da derivada: uma derivada errada (primeiro offset que muda só alguns bytes da chave, por
        // exemplo os bytes 2 e 3) erra poucos bits e, sem argumento conferido que pegue o desvio, passa nas conferências;
        // uma conhecida errada decifra lixo e não passa.
        var derived = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(layout.Arguments.Offset + KeyOffsetInArguments, 4));
        foreach (var key in KnownKeyValues)
        {
            candidates.Add(new Candidate(key, key == derived ? YstbKeyOrigin.Derived : YstbKeyOrigin.Known));
        }

        if (Array.IndexOf(KnownKeyValues, derived) < 0)
        {
            candidates.Add(new Candidate(derived, YstbKeyOrigin.Derived));
        }

        return candidates;
    }

    private static YstbFile Decode(
        ReadOnlyMemory<byte> data,
        string source,
        uint version,
        Layout layout,
        Candidate candidate,
        YscmFile commands,
        RangeRules rules)
    {
        var instructionBytes = Decrypt(data.Span, layout.Instructions, candidate.Key);
        var argumentBytes = Decrypt(data.Span, layout.Arguments, candidate.Key);
        var resources = Decrypt(data.Span, layout.Resources, candidate.Key);
        var lineNumbers = Decrypt(data.Span, layout.LineNumbers, candidate.Key);

        var argumentCount = argumentBytes.Length / ArgumentBytes;
        var instructions = new List<YstbInstruction>(layout.Count);
        var nextArgument = 0;
        for (var i = 0; i < layout.Count; i++)
        {
            var position = layout.Instructions.Offset + (i * InstructionBytes);
            var span = instructionBytes.AsSpan(i * InstructionBytes, InstructionBytes);
            var opcode = span[0];
            var declared = span[1];
            if (opcode >= commands.Commands.Count)
            {
                throw new DecodeFailure(
                    position,
                    $"a instrução {i} (posição {position}) tem o opcode {opcode}, que não está na lista do YSCM ({commands.Commands.Count} comandos).");
            }

            if (declared > argumentCount - nextArgument)
            {
                throw new DecodeFailure(
                    position,
                    $"a instrução {i} (posição {position}) declara {declared} argumentos, mas restam {argumentCount - nextArgument} na seção de argumentos.");
            }

            YstbArgument[] arguments = declared == 0 ? [] : new YstbArgument[declared];
            for (var j = 0; j < declared; j++)
            {
                var argumentPosition = layout.Arguments.Offset + ((nextArgument + j) * ArgumentBytes);
                var raw = argumentBytes.AsSpan((nextArgument + j) * ArgumentBytes, ArgumentBytes);
                var argument = new YstbArgument(
                    BinaryPrimitives.ReadUInt16LittleEndian(raw),
                    raw[2],
                    raw[3],
                    BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw[8..]));
                if (argument.AssignOp > MaxAssignOp)
                {
                    throw new DecodeFailure(
                        argumentPosition,
                        $"o argumento {nextArgument + j} (posição {argumentPosition}) tem o operador de atribuição {argument.AssignOp}; só existem 0 a {MaxAssignOp}.");
                }

                if (!rules.IsExempt(opcode, j, declared) && argument.Size != 0 && (long)argument.Offset + argument.Size > resources.Length)
                {
                    throw new DecodeFailure(
                        argumentPosition,
                        $"o argumento {nextArgument + j} (posição {argumentPosition}) do comando {commands.Commands[opcode].Name} aponta para os bytes {Text(argument.Offset)} a {(long)argument.Offset + argument.Size} dos recursos, que têm {resources.Length} bytes.");
                }

                arguments[j] = argument;
            }

            var extra = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
            var lineNumber = BinaryPrimitives.ReadUInt32LittleEndian(lineNumbers.AsSpan(i * InstructionBytes, InstructionBytes));
            instructions.Add(new YstbInstruction(i, opcode, commands.Commands[opcode].Name, extra, lineNumber, nextArgument, arguments));
            nextArgument += declared;
        }

        if (nextArgument != argumentCount)
        {
            var position = layout.Arguments.Offset + (nextArgument * ArgumentBytes);
            throw new DecodeFailure(
                position,
                $"sobram {argumentCount - nextArgument} argumentos sem instrução (a partir da posição {position}); as instruções usam {nextArgument} dos {argumentCount}.");
        }

        return new YstbFile(source, version, layout.Padding, candidate.Key, candidate.Origin, instructions, argumentBytes, resources);
    }

    /// <summary>XOR cíclico com a chave em ordem big-endian, recomeçando no início de cada seção.</summary>
    private static byte[] Decrypt(ReadOnlySpan<byte> data, Section section, uint key)
    {
        Span<byte> keyBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyBytes, key);
        var source = data.Slice(section.Offset, section.Size);
        var result = new byte[section.Size];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (byte)(source[i] ^ keyBytes[i & 3]);
        }

        return result;
    }

    private static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Argumentos cujo <c>size</c> e <c>offset</c> não apontam para os recursos (<c>ystb.py:78-80,86-87,93</c>): o
    /// <c>RETURNCODE</c>, o 2º e o 3º argumento de <c>IF</c>/<c>ELSE</c> com 3 argumentos e o 2º de <c>LOOP</c>.
    /// </summary>
    private readonly record struct RangeRules(byte ReturnCode, byte? If, byte? Else, byte? Loop)
    {
        public static RangeRules From(YscmFile commands)
        {
            var returnCode = commands.GetOpcodes().ReturnCode;
            return new RangeRules(returnCode, Optional(commands, "IF"), Optional(commands, "ELSE"), Optional(commands, "LOOP"));
        }

        public bool IsExempt(byte opcode, int argumentIndex, int declared) =>
            opcode == ReturnCode
            || ((opcode == If || opcode == Else) && declared == 3 && argumentIndex >= 1)
            || (opcode == Loop && argumentIndex == 1);

        private static byte? Optional(YscmFile commands, string name) => commands.TryGetOpcode(name, out var opcode) ? opcode : null;
    }

    private readonly record struct Section(int Offset, int Size);

    private readonly record struct Layout(int Count, uint Padding, Section Instructions, Section Arguments, Section Resources, Section LineNumbers);

    private readonly record struct Candidate(uint Key, YstbKeyOrigin Origin);

    private sealed record Failure(int Position, string Reason, uint Key);

    private sealed class DecodeFailure(int position, string reason) : Exception(reason)
    {
        public int Position { get; } = position;

        public string Reason { get; } = reason;
    }
}
