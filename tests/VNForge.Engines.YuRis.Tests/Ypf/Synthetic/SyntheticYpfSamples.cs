namespace VNForge.Engines.YuRis.Tests.Ypf.Synthetic;

/// <summary>Pacotes de exemplo gerados em código, compartilhados pelos testes do formato YPF.</summary>
public static class SyntheticYpfSamples
{
    internal const string Script1 = @"ysbin\yst00001.ybn";
    internal const string Image = @"bg\fundo.png";
    internal const string Sound = @"se\porta.ogg";
    internal const string Script2 = @"ysbin\yst00002.ybn";
    internal const string Empty = @"ysbin\vazio.ybn";

    internal static readonly byte[] SmallBinary = [0x10, 0x20, 0x30, 0x40, 0x50];
    internal static readonly byte[] Repetitive = Enumerable.Repeat((byte)'A', 4000).ToArray();

    private const SyntheticYpfHashAlgorithm A32 = SyntheticYpfHashAlgorithm.Adler32;
    private const SyntheticYpfHashAlgorithm Crc = SyntheticYpfHashAlgorithm.Crc32;
    private const SyntheticYpfHashAlgorithm Mur = SyntheticYpfHashAlgorithm.MurmurHash2;
    private const SyntheticYpfOffsetSize Off32 = SyntheticYpfOffsetSize.Bits32;
    private const SyntheticYpfOffsetSize Off64 = SyntheticYpfOffsetSize.Bits64;

    // Versão, offset, hash do nome e hash dos dados, todos explícitos. As linhas 477 e 478 cobrem as duas leituras do limiar
    // do MurmurHash2 (docs/06): a antiga (CRC32, Adler32, 32 bits) e a nova (MurmurHash2, MurmurHash2, 64 bits).
    public static TheoryData<uint, SyntheticYpfOffsetSize, SyntheticYpfHashAlgorithm, SyntheticYpfHashAlgorithm> Variants => new()
    {
        { 200, Off32, A32, A32 },
        { 265, Off32, Crc, A32 },
        { 300, Off32, Crc, A32 },
        { 477, Off32, Crc, A32 },
        { 477, Off64, Mur, Mur },
        { 479, Off32, Mur, Mur },
        { 500, Off64, Mur, Mur },
        { 500, Off32, Mur, Mur },
    };

    // Subconjunto de Variants para os testes de troca de entrada: uma versão de cada família de hash e de tamanho de offset.
    public static TheoryData<uint, SyntheticYpfOffsetSize, SyntheticYpfHashAlgorithm, SyntheticYpfHashAlgorithm> CoreVariants => new()
    {
        { 200, Off32, A32, A32 },
        { 300, Off32, Crc, A32 },
        { 477, Off64, Mur, Mur },
        { 500, Off32, Mur, Mur },
    };

    internal static SyntheticYpfBuilder DefaultSample(uint version) =>
        new SyntheticYpfBuilder(version)
            .AddFile(Script1, SmallBinary)
            .AddFile(Image, Repetitive, SyntheticYpfFileType.Png, compress: true)
            .AddFile(Sound, [1, 2, 3, 4, 5, 6, 7, 8, 9], SyntheticYpfFileType.Ogg)
            .AddFile(Script2, Repetitive, compress: true)
            .AddFile(Empty, []);

    // Cada vetor foi montado com um script Python independente (zlib.crc32, zlib.adler32 e um MurmurHash2 escrito
    // à parte), seguindo yuri.md:440-457 e o layout de ypf.py:121-145. Uma entrada, conteúdo "ABC" sem zlib, tipo png (2),
    // nome "bg\scene_0001.png" (17 bytes: o tamanho passa pela tabela de troca).
    //
    //   0x00  "YPF\0" · versão · 1 arquivo · header_size · 16 bytes zero
    //   0x20  name_hash · name_size · nome · tipo · compressed · size · stored · offset · data_hash
    //   fim   "ABC"
    private const string Hex03000000 = "03000000";

    internal const string GoldenV200 =
        "595046" + "00" + "c8000000" + "01000000" + "28000000" + "00000000000000000000000000000000" +
        "c705a736" + "e6" + "9d98a38c9c9a919aa0cfcfcfced18f9198" + "02" + "00" + Hex03000000 + Hex03000000 + "48000000" + "c7008d01" +
        "414243";

    internal const string GoldenV300 =
        "595046" + "00" + "2c010000" + "01000000" + "48000000" + "00000000000000000000000000000000" +
        "d0ab82e5" + "e6" + "9d98a38c9c9a919aa0cfcfcfced18f9198" + "02" + "00" + Hex03000000 + Hex03000000 + "48000000" + "c7008d01" +
        "414243";

    internal const string GoldenV500 =
        "595046" + "00" + "f4010000" + "01000000" + "4c000000" + "00000000000000000000000000000000" +
        "4bf899e0" + "e7" + "abae95baaaaca7ac96f9f9f9f8e7b9a7ae" + "02" + "00" + Hex03000000 + Hex03000000 + "4c00000000000000" + "ffa817fd" +
        "414243";
}
