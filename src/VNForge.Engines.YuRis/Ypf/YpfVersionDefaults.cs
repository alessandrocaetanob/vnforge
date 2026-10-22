using VNForge.Engines.YuRis.Ypf.Hashing;

namespace VNForge.Engines.YuRis.Ypf;

/// <summary>
/// O que cada versão do YPF costuma usar (docs/06-yuris-notas-tecnicas.md). O leitor só trata estes valores como
/// a primeira tentativa: o que vale para o arquivo é o que confere com o índice.
/// </summary>
public static class YpfVersionDefaults
{
    /// <summary>
    /// Menor versão com evidência nas fontes do doc 06 (yuri.md trata as versões 2xx; o GARbro cita a 247).
    /// Só decide a mensagem de erro: o leitor tenta ler qualquer versão.
    /// </summary>
    public const uint MinKnownVersion = 200;

    /// <summary>
    /// Maior versão da faixa conhecida: as medidas chegam a 500 (YPF-Manager, GARbro#452); o resto é margem.
    /// Só decide a mensagem de erro, como <see cref="MinKnownVersion"/>.
    /// </summary>
    public const uint MaxKnownVersion = 599;

    public const uint FirstCrc32Version = 265;

    /// <summary>
    /// Primeira versão com MurmurHash2 e offset de 64 bits na leitura do YPF-Manager. O yuri.md usa 477; o limiar
    /// real nunca foi medido (GARbro#452 mediu 474 e 479).
    /// </summary>
    public const uint FirstMurmurVersion = 479;

    public const uint FirstHeaderSizeWithHeaderVersion = 300;
    public const uint V290 = 290;
    public const uint V500 = 500;

    public static bool IsKnownVersion(uint version) => version is >= MinKnownVersion and <= MaxKnownVersion;

    public static YpfHashAlgorithm NameHash(uint version) =>
        version < FirstCrc32Version ? YpfHashAlgorithm.Adler32
        : version < FirstMurmurVersion ? YpfHashAlgorithm.Crc32
        : YpfHashAlgorithm.MurmurHash2;

    public static YpfHashAlgorithm DataHash(uint version) =>
        version < FirstMurmurVersion ? YpfHashAlgorithm.Adler32 : YpfHashAlgorithm.MurmurHash2;

    public static YpfOffsetSize OffsetSize(uint version) =>
        version < FirstMurmurVersion ? YpfOffsetSize.Bits32 : YpfOffsetSize.Bits64;

    public static YpfSwapTable SwapTable(uint version) =>
        version == V500 ? YpfSwapTable.V500 : YpfSwapTable.Standard;

    /// <summary>XOR efetivo do nome (<c>K ^ 0xFF</c>): 0xFF, 0xBF na v290 e 0xC9 na v500.</summary>
    public static byte NameXor(uint version) => version switch
    {
        V290 => 0xBF,
        V500 => 0xC9,
        _ => 0xFF,
    };

    public static bool HeaderSizeIncludesHeader(uint version) => version >= FirstHeaderSizeWithHeaderVersion;
}
