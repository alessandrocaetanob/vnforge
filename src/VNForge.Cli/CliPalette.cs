using Spectre.Console;

namespace VNForge.Cli;

/// <summary>Cores da saída da CLI, as mesmas do app, para o terminal e a janela contarem a mesma história.</summary>
public static class CliPalette
{
    public static readonly Color Approved = new(0x5F, 0xBF, 0x7F);

    public static readonly Color Reviewed = new(0x3E, 0x9A, 0xAE);

    public static readonly Color Translated = new(0x6E, 0x8F, 0xCB);

    /// <summary>Linha com tradução de IA ainda não revisada (<c>machine</c> no <c>text/*.json</c>).</summary>
    public static readonly Color Ai = new(0x9A, 0x7F, 0xD6);

    /// <summary>Linha sem tradução. Cinza-azulado escuro: some no fundo escuro de propósito.</summary>
    public static readonly Color Pending = new(0x3A, 0x42, 0x50);

    public static readonly Color Warning = new(0xF2, 0xC4, 0x6D);

    public static readonly Color Error = new(0xF5, 0x9A, 0xB2);

    /// <summary>Cor de destaque: bordas de painel, títulos e o que o usuário deve notar.</summary>
    public static readonly Color Accent = new(0x8D, 0xB0, 0xEA);

    /// <summary>Fundo da caixa de texto do jogo, o mesmo do app (<c>Vnf.TextboxBg</c>).</summary>
    public static readonly Color TextboxBackground = new(0x14, 0x16, 0x1C);

    /// <summary>Tinta do texto na caixa (<c>Vnf.TextboxInk</c>).</summary>
    public static readonly Color TextboxInk = new(0xF1, 0xEE, 0xE6);

    /// <summary>Texto que passa do limite da caixa, em coral como no app (<c>Vnf.TextboxOverflow</c>).</summary>
    public static readonly Color TextboxOverflow = new(0xFF, 0x9B, 0x85);

    /// <summary>Placa com o nome do falante (<c>Vnf.TextboxPlate</c>).</summary>
    public static readonly Color TextboxPlate = new(0x2B, 0x4C, 0x7E);

    /// <summary>Cor em <c>#RRGGBB</c>, a forma que o markup do Spectre aceita (<c>[#5FBF7F]texto[/]</c>).</summary>
    public static string Hex(Color color) => "#" + color.ToHex().ToUpperInvariant();
}
