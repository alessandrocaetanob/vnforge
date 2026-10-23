using System.Buffers.Binary;
using System.Globalization;
using VNForge.Core;

namespace VNForge.Engines.YuRis.Ystb;

/// <summary>
/// Grava um YSTB com reescrita mínima (regra 3 do <c>CLAUDE.md</c>): o cabeçalho, as instruções, os números de linha e todos os
/// argumentos saem do arquivo lido, e só os argumentos trocados ganham <c>size</c> e <c>offset</c> novos. Os bytes novos vão
/// para o fim da seção de recursos, depois dos antigos; os trechos substituídos ficam onde estavam, sem uso. As quatro seções
/// são cifradas de novo com a <see cref="YstbFile.Key"/> do arquivo lido.
/// </summary>
/// <remarks>
/// Sem troca nenhuma, o resultado é idêntico ao arquivo lido. Só o <c>size</c> e o <c>offset</c> mudam no argumento trocado;
/// <c>id</c>, <c>type</c> e <c>assign_op</c> são copiados. Um argumento trocado por bytes vazios fica com <c>size</c> 0 e o
/// <c>offset</c> no fim dos recursos (o leitor aceita qualquer <c>offset</c> com tamanho 0).
/// </remarks>
public static class YstbWriter
{
    private const int HeaderBytes = 32;
    private const int InstructionBytes = 4;
    private const int ArgumentBytes = 12;
    private const int SizeFieldOffset = 4;
    private const int OffsetFieldOffset = 8;

    /// <summary>Monta o arquivo com os argumentos trocados.</summary>
    /// <param name="file">O YSTB lido, que fornece as seções em claro, a chave e o padding.</param>
    /// <param name="replacements">
    /// Os novos bytes do recurso de cada argumento trocado, pelo índice do argumento entre todos os do arquivo
    /// (<see cref="YstbInstruction.FirstArgumentIndex"/> mais a posição na instrução). Os bytes são gravados como estão.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Algum índice não é de um argumento do arquivo.</exception>
    /// <exception cref="VNForgeException">
    /// O primeiro argumento seria trocado num arquivo cuja chave só se descobre pelo <c>offset</c> dele (o leitor não poderia mais
    /// abri-lo), ou o resultado passaria dos limites do formato ou do leitor.
    /// </exception>
    public static byte[] Write(YstbFile file, IReadOnlyDictionary<int, ReadOnlyMemory<byte>> replacements)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(replacements);

        var arguments = file.ArgumentBytes.ToArray();
        var appended = new List<ReadOnlyMemory<byte>>(replacements.Count);
        long resourcesLength = file.Resources.Length;

        foreach (var (index, bytes) in replacements.OrderBy(pair => pair.Key))
        {
            if (index < 0 || index >= file.ArgumentCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replacements),
                    index,
                    $"O argumento {Number(index)} não existe em '{file.Source}', que tem {Number(file.ArgumentCount)} argumentos.");
            }

            if (resourcesLength + bytes.Length > YbnReader.MaxFileBytes)
            {
                throw TooLarge(file, resourcesLength + bytes.Length);
            }

            var field = arguments.AsSpan(index * ArgumentBytes, ArgumentBytes);
            BinaryPrimitives.WriteUInt32LittleEndian(field[SizeFieldOffset..], checked((uint)bytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(field[OffsetFieldOffset..], checked((uint)resourcesLength));
            RequireDerivableKey(file, index, resourcesLength);

            appended.Add(bytes);
            resourcesLength += bytes.Length;
        }

        var totalLength = HeaderBytes
            + ((long)file.Instructions.Count * InstructionBytes)
            + arguments.Length
            + resourcesLength
            + ((long)file.Instructions.Count * InstructionBytes);
        if (totalLength > YbnReader.MaxFileBytes)
        {
            throw TooLarge(file, totalLength);
        }

        return Assemble(file, arguments, appended, (int)resourcesLength, (int)totalLength);
    }

    private static VNForgeException TooLarge(YstbFile file, long length) =>
        new($"O script '{file.Source}' ficaria com pelo menos {Number(length)} bytes, acima do limite de {YbnReader.MaxFileBytes / (1024 * 1024)} MiB que o leitor aceita.");

    /// <summary>
    /// Sem chave conhecida, o leitor deriva a chave do <c>offset</c> do primeiro argumento, que precisa ser 0. Trocar esse argumento
    /// moveria o offset e o arquivo gravado não seria mais lido por esta ferramenta (o jogo não é afetado).
    /// </summary>
    private static void RequireDerivableKey(YstbFile file, int argumentIndex, long newOffset)
    {
        if (argumentIndex == 0
            && newOffset != 0
            && file.KeyOrigin == YstbKeyOrigin.Derived
            && !YstbReader.KnownKeys.Contains(file.Key))
        {
            throw new VNForgeException(
                $"O primeiro argumento de '{file.Source}' não pode ser trocado: a chave 0x{file.Key:X8} do script só é descoberta pelo offset dele, que passaria de 0 para {Number(newOffset)}, e o arquivo gravado não seria mais lido.");
        }
    }

    private static byte[] Assemble(YstbFile file, byte[] arguments, List<ReadOnlyMemory<byte>> appended, int resourcesLength, int totalLength)
    {
        var count = file.Instructions.Count;
        var instructions = new byte[count * InstructionBytes];
        var lineNumbers = new byte[count * InstructionBytes];
        for (var i = 0; i < count; i++)
        {
            var instruction = file.Instructions[i];
            instructions[i * InstructionBytes] = instruction.Opcode;
            instructions[(i * InstructionBytes) + 1] = checked((byte)instruction.Arguments.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(instructions.AsSpan((i * InstructionBytes) + 2), instruction.Extra);
            BinaryPrimitives.WriteUInt32LittleEndian(lineNumbers.AsSpan(i * InstructionBytes), instruction.LineNumber);
        }

        var resources = new byte[resourcesLength];
        file.Resources.CopyTo(resources);
        var position = file.Resources.Length;
        foreach (var bytes in appended)
        {
            bytes.CopyTo(resources.AsMemory(position));
            position += bytes.Length;
        }

        var output = new byte[totalLength];
        "YSTB"u8.CopyTo(output);
        var header = output.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(header, file.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)instructions.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)arguments.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)resources.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)lineNumbers.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], file.Padding);

        var cursor = HeaderBytes;
        foreach (var section in (byte[][])[instructions, arguments, resources, lineNumbers])
        {
            YstbCipher.Apply(section, file.Key, output.AsSpan(cursor, section.Length));
            cursor += section.Length;
        }

        return output;
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
