using VNForge.Core.Glossary;
using VNForge.Core.Review;

namespace VNForge.App.ViewModels;

public sealed partial class GlossaryViewModel
{
    private CharacterRow? CharacterOf(string speaker) =>
        Characters.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Source) && CharacterBook.NameComparer.Equals(c.Source, speaker));

    /// <summary>O "Como fala" que está na tela para o falante (vazio se ainda não escreveram); nulo quando o falante não está na tabela de personagens.</summary>
    public string? ToneOf(string speaker) => CharacterOf(speaker)?.Tone;

    /// <summary>O nome que a pessoa lê para o falante: a tradução do cadastro ou, sem ela, o nome do jogo.</summary>
    public string NameOf(string speaker) => CharacterOf(speaker) is { Target: var target } && !string.IsNullOrWhiteSpace(target) ? target : speaker;

    /// <summary>
    /// Acrescenta a troca repetida ao "Como fala" do personagem, e o salvamento automático do Glossário a grava. Falso quando o falante não está na tabela
    /// ou a tabela não pode ser editada (arquivo inválido).
    /// </summary>
    public bool AddToneRule(string speaker, RepeatedCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        if (CharacterOf(speaker) is not { } row || !CanEdit)
        {
            return false;
        }

        row.Tone = correction.AddTo(row.Tone);
        return true;
    }
}
