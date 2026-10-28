using System.Buffers.Binary;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Tests.Yslb.Synthetic;

/// <summary>Rótulo do YSLB sintético; o hash do nome fica 0 porque o leitor não o confere.</summary>
public sealed record SyntheticYslbLabel(string Name, uint CodePosition, ushort ScriptId, byte IfLevel = 0, byte LoopLevel = 0);

/// <summary>
/// Monta um <c>ysl.ybn</c> (YSLB): cabeçalho de 12 bytes, a tabela <c>idx_begin</c> de 256 inteiros e um registro por rótulo
/// (docs/06-yuris-notas-tecnicas.md, "Saltos entre scripts").
/// </summary>
public sealed class SyntheticYslbBuilder
{
    private readonly List<SyntheticYslbLabel> _labels = [];
    private uint _version = 485;

    public SyntheticYslbBuilder WithVersion(uint version)
    {
        _version = version;
        return this;
    }

    public SyntheticYslbBuilder AddLabel(string name, ushort scriptId, uint codePosition = 0)
    {
        _labels.Add(new SyntheticYslbLabel(name, codePosition, scriptId));
        return this;
    }

    public byte[] Build()
    {
        using var stream = new MemoryStream();
        stream.Write("YSLB"u8);
        WriteUInt32(stream, _version);
        WriteUInt32(stream, (uint)_labels.Count);
        stream.Write(new byte[256 * 4]);
        foreach (var label in _labels)
        {
            var name = GameEncodings.ShiftJis.GetBytes(label.Name);
            stream.WriteByte(checked((byte)name.Length));
            stream.Write(name);
            WriteUInt32(stream, 0);
            WriteUInt32(stream, label.CodePosition);
            stream.Write(BitConverter.GetBytes(label.ScriptId));
            stream.WriteByte(label.IfLevel);
            stream.WriteByte(label.LoopLevel);
        }

        return stream.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
