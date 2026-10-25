using VNForge.Core.Engines;

namespace VNForge.Engines.YuRis;

/// <summary>
/// Troca no executável o nome da fonte das falas, <c>ＭＳ ゴシック</c> em Shift-JIS, por <c>MS Gothic</c> em ASCII (#62, #65).
/// Sem o japonês como idioma de programas não Unicode, o Windows não acha a fonte pelo nome japonês e o jogo cai na <c>System</c>,
/// proporcional, que deixa um vão depois das letras estreitas; com o nome em ASCII o motor pede a MS Gothic, de largura fixa.
/// </summary>
/// <remarks>
/// O endereço do nome muda de um executável para outro, então o patch procura o nome e só segue quando acha exatamente um. A busca
/// confere o cabeçalho <c>MZ</c>, que o nome começa uma string (início do arquivo ou depois de um zero) e termina em zero, e recusa
/// um executável em que o nome some ou se repete.
/// Muda só os bytes do nome (13 no jogo de referência); o resto do arquivo fica igual.
/// </remarks>
public sealed class YuRisFontPatch : IGamePatch
{
    /// <summary>Valor de <see cref="Id"/>.</summary>
    public const string PatchId = "exe-font";

    private const string FontName = "ＭＳ ゴシック";

    /// <summary><c>ＭＳ ゴシック</c> em Shift-JIS (página 932), 13 bytes.</summary>
    private static readonly byte[] JapaneseName = [0x82, 0x6C, 0x82, 0x72, 0x20, 0x83, 0x53, 0x83, 0x56, 0x83, 0x62, 0x83, 0x4E];

    /// <summary>
    /// <c>MS Gothic</c> em ASCII e 5 zeros: o que o patch deixa, pelo menos, no lugar do nome. Os 4 bytes que sobram dos 13 do nome viram zero
    /// e o terminador que a busca exigiu depois do nome continua lá, então vale para qualquer exe aceito, com um ou mais zeros depois do nome.
    /// </summary>
    private static readonly byte[] PatchedSlot = [.. "MS Gothic"u8, 0, 0, 0, 0, 0];

    /// <summary>O mesmo nome em UTF-16, a fonte de uma caixa de diálogo do Windows (0x11EF02 no jogo de referência).</summary>
    private static readonly byte[] UnicodeName = System.Text.Encoding.Unicode.GetBytes(FontName);

    private readonly string _executable;

    /// <param name="executable">Executável relativo à pasta do jogo (<c>game.exe</c> do projeto).</param>
    public YuRisFontPatch(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
        AffectedFiles = [executable];
    }

    public string Id => PatchId;

    public string Description => $"Troca o nome da fonte {FontName} do executável por MS Gothic, para as falas saírem em fonte de largura fixa.";

    public IReadOnlyList<string> AffectedFiles { get; }

    public bool CanApply(string gameDir, out string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        reason = Inspect(gameDir).Refusal;
        return reason is null;
    }

    public void Apply(string gameDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        var inspection = Inspect(gameDir);
        if (inspection.Refusal is not null)
        {
            throw new InvalidOperationException(inspection.Refusal);
        }

        if (inspection.Offset is not { } offset)
        {
            return;
        }

        var bytes = inspection.Bytes!;
        Array.Clear(bytes, offset, JapaneseName.Length);
        "MS Gothic"u8.CopyTo(bytes.AsSpan(offset));
        File.WriteAllBytes(inspection.Path!, bytes);
    }

    /// <summary>Lê o executável e acha o nome. <c>Offset</c> nulo sem recusa quer dizer que o patch já está aplicado.</summary>
    private Inspection Inspect(string gameDir)
    {
        string path;
        try
        {
            path = GamePaths.Resolve(gameDir, _executable);
        }
        catch (ArgumentException ex)
        {
            return Inspection.Refuse($"O caminho do executável '{_executable}' não vale: {ex.Message}");
        }

        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return Inspection.Refuse($"O executável '{_executable}' não existe na pasta do jogo.");
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Inspection.Refuse($"Não foi possível ler o executável '{_executable}' (o jogo está aberto?): {ex.Message}");
        }

        if (bytes.Length < 2 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
        {
            return Inspection.Refuse($"'{_executable}' não é um executável do Windows (falta a assinatura MZ no início).");
        }

        var names = FindStandalone(bytes, JapaneseName, zeroAfter: true);
        if (names.Count == 1)
        {
            return new Inspection(null, bytes, path, names[0]);
        }

        if (names.Count > 1)
        {
            return Inspection.Refuse(
                $"O nome da fonte {FontName} (Shift-JIS) aparece {names.Count} vezes no executável '{_executable}', e não dá para saber qual é o das falas.");
        }

        // Já aplicado: o espaço do nome tem `MS Gothic` e o nome da caixa de diálogo, em UTF-16, que o patch não toca, continua no exe.
        // Só a string ASCII não basta: um exe qualquer pode trazê-la.
        if (FindStandalone(bytes, PatchedSlot, zeroAfter: false).Count == 1 && bytes.AsSpan().IndexOf(UnicodeName) >= 0)
        {
            return new Inspection(null, bytes, path, null);
        }

        return Inspection.Refuse(
            $"Não achei o nome da fonte {FontName} (Shift-JIS) no executável '{_executable}': ele não é o jogo para o qual o patch foi feito.");
    }

    /// <summary>
    /// Posições de <paramref name="pattern"/> que são uma string inteira: começam no início do arquivo ou depois de um byte zero e,
    /// com <paramref name="zeroAfter"/>, terminam num byte zero. Um trecho igual no meio de um texto maior não conta.
    /// </summary>
    private static List<int> FindStandalone(byte[] bytes, byte[] pattern, bool zeroAfter)
    {
        var found = new List<int>();
        var from = 0;
        while (from <= bytes.Length - pattern.Length)
        {
            var at = bytes.AsSpan(from).IndexOf(pattern);
            if (at < 0)
            {
                break;
            }

            var offset = from + at;
            var end = offset + pattern.Length;
            var startsString = offset == 0 || bytes[offset - 1] == 0;
            if (startsString && (!zeroAfter || (end < bytes.Length && bytes[end] == 0)))
            {
                found.Add(offset);
            }

            from = offset + 1;
        }

        return found;
    }

    private sealed record Inspection(string? Refusal, byte[]? Bytes, string? Path, int? Offset)
    {
        public static Inspection Refuse(string reason) => new(reason, null, null, null);
    }
}
