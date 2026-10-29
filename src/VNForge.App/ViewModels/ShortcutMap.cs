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
            new(["Ctrl", "B"], "Recolher ou abrir a lista de cenas"),
        ]),
        new("Modo revisão",
        [
            new(["Enter"], "Está boa: aprovar a tradução e ir para a próxima fala"),
            new(["E"], "Corrigir: abrir a fala no editor"),
            new(["M"], "Depois: deixar a fala para mais tarde e ir para a próxima"),
            new(["→"], "Pular: ir para a próxima fala sem marcar esta"),
            new(["←"], "Voltar para a fala anterior da revisão"),
            new(["Esc"], "Voltar ao editor"),
        ]),
        new("Ler como jogador",
        [
            new(["Espaço"], "Mostrar a fala seguinte"),
            new(["←"], "Voltar para a fala anterior"),
            new(["F"], "Marcar a fala como \"soa estranho\" (de novo, tira a marca)"),
            new(["Esc"], "Sair; com falas marcadas, o Editor abre com o filtro delas"),
        ]),
        new("Geral",
        [
            new(["Ctrl", "+"], "Aumentar o texto do Editor (grade, inspetor e Modo revisão)"),
            new(["Ctrl", "-"], "Diminuir o texto do Editor"),
            new(["Ctrl", "0"], "Voltar o texto do Editor a 100%"),
            new(["?"], "Abrir ou fechar este mapa"),
            new(["Esc"], "Fechar este mapa"),
        ]),
    ];
}
