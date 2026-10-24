namespace VNForge.App.ViewModels;

/// <summary>Um atalho do mapa: as teclas (uma por cartão <c>kbd</c>) e o que ele faz.</summary>
public sealed record ShortcutEntry(IReadOnlyList<string> Keys, string Description);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutEntry> Entries);

/// <summary>Os atalhos que existem no app hoje, para o mapa aberto com <c>?</c>. Quem cria um atalho novo acrescenta a linha aqui.</summary>
public static class ShortcutMap
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("Editor",
        [
            new(["Ctrl", "Enter"], "Aprovar a linha e ir para a próxima"),
            new(["Ctrl", "Espaço"], "Pedir a sugestão da IA"),
            new(["Alt", "↓"], "Próxima linha"),
            new(["Alt", "↑"], "Linha anterior"),
        ]),
        new("Geral",
        [
            new(["?"], "Abrir ou fechar este mapa"),
            new(["Esc"], "Fechar este mapa"),
        ]),
    ];
}
