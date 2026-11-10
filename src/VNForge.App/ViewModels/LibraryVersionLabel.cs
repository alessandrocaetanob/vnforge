using System.Globalization;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>
/// O que a Biblioteca escreve sobre o que há na pasta de um jogo: a etiqueta do cartão, a pílula e as frases da gaveta. O estado vem do Core; aqui só moram as palavras.
/// <see cref="Tone"/> é o tom de <c>lib-pill</c>: <c>ok</c> para o patch inteiro, <c>pend</c> para o jogo como veio e <c>warn</c> para o que não é a tradução inteira.
/// </summary>
public sealed record LibraryVersionLabel(string? Tag, string Pill, string Tone, string Detail, string? Note)
{
    private const string BackupNote = "Voltar ao original usa a cópia de segurança que o VNForge fez. Nenhum arquivo novo é escrito.";

    public static LibraryVersionLabel For(LibraryTranslationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsKnown(state))
        {
            return new LibraryVersionLabel(null, "Estado desconhecido", "warn", state.Reason ?? "O VNForge não sabe o que há na pasta deste jogo.", null);
        }

        if (state.Kind == LibraryTranslationKind.Original)
        {
            return new LibraryVersionLabel("Original", "Sem patch no jogo", "pend", "O jogo está como veio.", "Traduzido confere a tradução, guarda uma cópia de segurança dos arquivos do jogo e só então grava.");
        }

        var patch = state.Patch!;
        var since = patch.AppliedAt.ToLocalTime().ToString("dd/MM", CultureInfo.InvariantCulture);
        if (!state.PatchRecorded)
        {
            return new LibraryVersionLabel("Patch no jogo", "Patch no jogo", "warn", $"Há uma injeção de {since} que o projeto não descreve (pode ser só o ajuste do executável). Traduzido grava a tradução inteira.", BackupNote);
        }

        if (patch.Kind == GameTestKind.AccentTest)
        {
            return new LibraryVersionLabel("Teste de acentos", "Teste de acentos no jogo", "warn", $"Só a frase dos acentos está no jogo, desde {since}. Traduzido grava a tradução inteira.", BackupNote);
        }

        return patch.ScriptKey is { } scene
            ? new LibraryVersionLabel("Uma cena no jogo", "Só uma cena no jogo", "warn", $"Só a cena {scene} está no jogo, desde {since}. Traduzido grava a tradução inteira.", BackupNote)
            : new LibraryVersionLabel("PT-BR no jogo", "Patch do VNForge no jogo", "ok", $"No jogo desde {since}.", BackupNote);
    }

    /// <summary>O Core sabe o que há na pasta; um "traduzido" sem o patch descrito é tratado como desconhecido.</summary>
    public static bool IsKnown(LibraryTranslationState state) =>
        state.Kind == LibraryTranslationKind.Original || state is { Kind: LibraryTranslationKind.Translated, Patch: not null };

    /// <summary>A tradução inteira está na pasta: é o que o botão "Traduzido" mostra como marcado.</summary>
    public static bool IsFullTranslation(LibraryTranslationState state) =>
        state is { Kind: LibraryTranslationKind.Translated, PatchRecorded: true, Patch: { Kind: GameTestKind.Translation, ScriptKey: null } };
}
