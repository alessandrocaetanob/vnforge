using System.Buffers.Binary;
using VNForge.Core.Text;

namespace VNForge.Engines.YuRis.Tests.Ystl.Synthetic;

/// <summary>Um script do YSTL sintético, com os campos como serão gravados.</summary>
public sealed record SyntheticYstlScript(
    string Path,
    ulong ModifiedFileTime = SyntheticYstlScript.DefaultFileTime,
    int VariableCount = 3,
    uint LabelCount = 2,
    uint TextCount = 10)
{
    /// <summary>2024-01-01 00:00:00 UTC como FILETIME (100 ns desde 1601-01-01).</summary>
    public const ulong DefaultFileTime = 133_485_408_000_000_000;
}

/// <summary>Resultado do <see cref="SyntheticYstlBuilder"/>: os bytes do <c>yst_list.ybn</c> e o que foi escrito.</summary>
public sealed record SyntheticYstl(byte[] Bytes, uint Version, bool WritesTextCount, IReadOnlyList<SyntheticYstlScript> Scripts);

/// <summary>
/// Monta um <c>yst_list.ybn</c> (YSTL) sintético: cabeçalho de 12 bytes e um registro por script
/// (docs/06-yuris-notas-tecnicas.md). O campo <c>txt_count</c> segue a versão, mas pode ser forçado para
/// testar os dois layouts na faixa em que as fontes divergem.
/// </summary>
public sealed class SyntheticYstlBuilder
{
    private readonly List<SyntheticYstlScript> _scripts = [];
    private uint _version = 500;
    private bool? _writesTextCount;
    private uint? _indexOverrideAt;
    private uint _indexOverrideValue;

    public SyntheticYstlBuilder WithVersion(uint version)
    {
        _version = version;
        return this;
    }

    /// <summary>Força o layout com ou sem <c>txt_count</c>; sem isto vale o limiar 477 do yuri.md.</summary>
    public SyntheticYstlBuilder WithTextCount(bool writes)
    {
        _writesTextCount = writes;
        return this;
    }

    /// <summary>Grava outro valor no campo <c>index</c> do script na posição dada.</summary>
    public SyntheticYstlBuilder WithIndexAt(uint position, uint value)
    {
        _indexOverrideAt = position;
        _indexOverrideValue = value;
        return this;
    }

    public SyntheticYstlBuilder AddScript(string path) => AddScript(new SyntheticYstlScript(path));

    public SyntheticYstlBuilder AddScript(SyntheticYstlScript script)
    {
        _scripts.Add(script);
        return this;
    }

    public SyntheticYstl Build()
    {
        var writesTextCount = _writesTextCount ?? _version >= 477;
        var sjis = GameEncodings.ShiftJis;
        using var stream = new MemoryStream();
        stream.Write("YSTL"u8);
        WriteUInt32(stream, _version);
        WriteUInt32(stream, (uint)_scripts.Count);

        for (var i = 0; i < _scripts.Count; i++)
        {
            var script = _scripts[i];
            var pathBytes = sjis.GetBytes(script.Path);
            WriteUInt32(stream, _indexOverrideAt == i ? _indexOverrideValue : (uint)i);
            WriteUInt32(stream, (uint)pathBytes.Length);
            stream.Write(pathBytes);
            WriteUInt64(stream, script.ModifiedFileTime);
            WriteUInt32(stream, unchecked((uint)script.VariableCount));
            WriteUInt32(stream, script.LabelCount);
            if (writesTextCount)
            {
                WriteUInt32(stream, script.TextCount);
            }
        }

        return new SyntheticYstl(stream.ToArray(), _version, writesTextCount, _scripts.ToArray());
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
