using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using VNForge.Core;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Cursor sobre os bytes de um <c>.ybn</c> pequeno. Toda falha de leitura vira <see cref="InvalidFileException"/>
/// com a posição e o campo, para a mensagem apontar o que faltou.
/// </summary>
internal sealed class YbnReader(ReadOnlyMemory<byte> data, string source)
{
    /// <summary>Teto de tamanho que os leitores aceitam para ysc.ybn e yst_list.ybn (32 MiB).</summary>
    public const long MaxFileBytes = 32 * 1024 * 1024;

    public string Source { get; } = source;

    public int Position { get; private set; }

    public int Remaining => data.Length - Position;

    public InvalidFileException Error(string reason) => new(Source, reason);

    /// <summary>Confere assinatura e versão dos 8 primeiros bytes e deixa o cursor depois deles.</summary>
    public uint ReadMagicAndVersion(ReadOnlySpan<byte> magic)
    {
        var name = Encoding.ASCII.GetString(magic);
        if (data.Length < 8)
        {
            throw Error($"tem {data.Length} bytes e o cabeçalho do {name} precisa de pelo menos 8.");
        }

        if (!data.Span.StartsWith(magic))
        {
            throw Error($"não começa com a assinatura \"{name}\"; não é um arquivo {name}.");
        }

        Position = 4;
        var version = ReadUInt32("a versão");
        if (!YbnVersions.IsKnown(version))
        {
            throw new UnknownVersionException(
                Source,
                $"{version.ToString(CultureInfo.InvariantCulture)}, fora das versões conhecidas ({YbnVersions.Min} a {YbnVersions.Max})");
        }

        return version;
    }

    public uint ReadUInt32(string what) => BinaryPrimitives.ReadUInt32LittleEndian(Take(4, what));

    public int ReadInt32(string what) => BinaryPrimitives.ReadInt32LittleEndian(Take(4, what));

    public ulong ReadUInt64(string what) => BinaryPrimitives.ReadUInt64LittleEndian(Take(8, what));

    public byte ReadByte(string what) => Take(1, what)[0];

    public ushort ReadUInt16(string what) => BinaryPrimitives.ReadUInt16LittleEndian(Take(2, what));

    public void Skip(int count, string what) => Take(count, what);

    public string ReadShiftJis(int count, string what) => GameEncodings.ShiftJis.GetString(Take(count, what));

    /// <summary>Lê uma string em Shift-JIS terminada em zero (o zero é consumido).</summary>
    public string ReadZeroTerminatedShiftJis(string what)
    {
        var span = data.Span[Position..];
        var length = span.IndexOf((byte)0);
        if (length < 0)
        {
            throw Error($"{what} (posição {Position}) não termina com o byte 0 antes do fim do arquivo.");
        }

        var text = GameEncodings.ShiftJis.GetString(span[..length]);
        Position += length + 1;
        return text;
    }

    private ReadOnlySpan<byte> Take(int count, string what)
    {
        if (count > Remaining)
        {
            throw Error($"termina na posição {data.Length} ao ler {what} (posição {Position}, precisa de {count} bytes e restam {Remaining}).");
        }

        var slice = data.Span.Slice(Position, count);
        Position += count;
        return slice;
    }

    /// <summary>Lê o fluxo inteiro, recusando acima de <see cref="MaxFileBytes"/>.</summary>
    public static byte[] ReadAll(Stream stream, string source)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("O fluxo precisa ser legível.", nameof(stream));
        }

        var limitMessage = $"passa do limite de {MaxFileBytes / (1024 * 1024)} MiB que o leitor aceita.";
        if (stream.CanSeek && stream.Length - stream.Position > MaxFileBytes)
        {
            throw new InvalidFileException(source, limitMessage);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes)
            {
                throw new InvalidFileException(source, limitMessage);
            }
        }

        return buffer.ToArray();
    }

    public static string SourceOf(Stream stream) => stream is FileStream file ? file.Name : "<fluxo>";
}
