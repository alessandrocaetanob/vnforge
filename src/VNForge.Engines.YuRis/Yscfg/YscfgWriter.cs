using System.Buffers.Binary;

namespace VNForge.Engines.YuRis.Yscfg;

/// <summary>
/// Regrava o <c>yscfg.ybn</c> com outro título. Os bytes do cabeçalho e da configuração saem como foram lidos; muda só o campo de
/// tamanho e o título (regra 3 do <c>CLAUDE.md</c>). Com o título original, devolve o arquivo idêntico.
/// </summary>
public static class YscfgWriter
{
    /// <summary>Monta o arquivo com o título já codificado.</summary>
    /// <param name="file">O arquivo lido por <see cref="YscfgReader"/>.</param>
    /// <param name="titleBytes">Título nos bytes do jogo, sem terminador.</param>
    /// <exception cref="ArgumentException">O título passa de <see cref="YscfgReader.MaxTitleBytes"/> bytes, o que cabe no campo de tamanho.</exception>
    public static byte[] WithTitle(YscfgFile file, ReadOnlySpan<byte> titleBytes)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (titleBytes.Length > YscfgReader.MaxTitleBytes)
        {
            throw new ArgumentException(
                $"O título tem {titleBytes.Length} bytes e o campo de tamanho do {file.Source} guarda no máximo {YscfgReader.MaxTitleBytes}.",
                nameof(titleBytes));
        }

        var result = new byte[file.Prefix.Length + 2 + titleBytes.Length];
        file.Prefix.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(file.Prefix.Length), (ushort)titleBytes.Length);
        titleBytes.CopyTo(result.AsSpan(file.Prefix.Length + 2));
        return result;
    }
}
