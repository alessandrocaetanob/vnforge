namespace VNForge.Engines.YuRis.Yscfg;

/// <summary>
/// O <c>yscfg.ybn</c> (YSCF) lido: a configuração do jogo, que o VNForge não interpreta, e o título da janela no fim do arquivo.
/// Guarda os bytes de antes do título para o <see cref="YscfgWriter"/> devolvê-los idênticos.
/// </summary>
public sealed class YscfgFile
{
    internal YscfgFile(string source, uint version, byte[] prefix, string title, bool titleIsShiftJis)
    {
        Source = source;
        Version = version;
        Prefix = prefix;
        Title = title;
        TitleIsShiftJis = titleIsShiftJis;
    }

    /// <summary>Caminho do arquivo lido, usado nas mensagens de erro.</summary>
    public string Source { get; }

    /// <summary>Versão do formato gravada no cabeçalho.</summary>
    public uint Version { get; }

    /// <summary>Título da janela do jogo, decodificado de Shift-JIS (ou de Windows-1252, ver <see cref="TitleIsShiftJis"/>). Vazio quando o arquivo grava tamanho zero.</summary>
    public string Title { get; }

    /// <summary>
    /// Falso quando o título não é Shift-JIS válido e foi lido como Windows-1252: o arquivo já tem um título que o build gravou com a estratégia <c>cp1252</c>.
    /// O jogo original traz Shift-JIS.
    /// </summary>
    public bool TitleIsShiftJis { get; }

    /// <summary>Posição do campo de tamanho do título: o cabeçalho e a configuração ocupam os bytes antes dela.</summary>
    public int TitleOffset => Prefix.Length;

    /// <summary>Bytes do início do arquivo até o campo de tamanho do título, sem incluí-lo.</summary>
    internal byte[] Prefix { get; }
}
