using Spectre.Console;

namespace VNForge.Cli;

/// <summary>Cores da saída da CLI, as do tema escuro do app (seg-* e accent do design system v3, signal-* do aviso e do erro), para o terminal e a janela contarem a mesma história.</summary>
public static class CliPalette
{
    public static readonly Color Approved = new(0x5C, 0xCB, 0x8A);

    public static readonly Color Reviewed = new(0x49, 0xB9, 0xCF);

    public static readonly Color Translated = new(0x7F, 0xA2, 0xEC);

    /// <summary>Linha com tradução de IA ainda não revisada (<c>machine</c> no <c>text/*.json</c>).</summary>
    public static readonly Color Ai = new(0xA5, 0x8B, 0xF2);

    /// <summary>Linha sem tradução. Cinza (seg-pending): lê como "ainda não feito" sem competir com os estados com tradução.</summary>
    public static readonly Color Pending = new(0x7C, 0x84, 0x92);

    public static readonly Color Warning = new(0xE9, 0xB0, 0x4F);

    public static readonly Color Error = new(0xF5, 0x9A, 0xB2);

    /// <summary>Cor de destaque: bordas de painel, títulos e o que o usuário deve notar.</summary>
    public static readonly Color Accent = new(0x4F, 0xD1, 0xB5);

    /// <summary>Fundo da caixa de texto do jogo, o mesmo do app (<c>Vnf.TextboxBg</c>).</summary>
    public static readonly Color TextboxBackground = new(0x14, 0x16, 0x1C);

    /// <summary>Tinta do texto na caixa (<c>Vnf.TextboxInk</c>).</summary>
    public static readonly Color TextboxInk = new(0xF1, 0xEE, 0xE6);

    /// <summary>Texto que passa do limite da caixa, em coral como no app (<c>Vnf.TextboxOverflow</c>).</summary>
    public static readonly Color TextboxOverflow = new(0xFF, 0x9B, 0x85);

    /// <summary>Placa com o nome do falante (<c>Vnf.TextboxPlate</c>).</summary>
    public static readonly Color TextboxPlate = new(0x2B, 0x4C, 0x7E);

    /// <summary>Cor em <c>#RRGGBB</c>, a forma que o markup do Spectre aceita (<c>[#5CCB8A]texto[/]</c>).</summary>
    public static string Hex(Color color) => "#" + color.ToHex().ToUpperInvariant();
}
