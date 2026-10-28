using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.App.Services;
using VNForge.Core.Glossary;

namespace VNForge.App.ViewModels;

/// <summary>Base das linhas das tabelas: guarda quantas linhas do projeto usam o termo ou o personagem. Não é dado do arquivo, então não conta como alteração.</summary>
public abstract partial class UsageRow : ObservableObject
{
    /// <summary>Nulo enquanto o texto do projeto não foi lido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesLabel), nameof(IsUnused))]
    public partial int? Uses { get; set; }

    /// <summary>Lido o texto e nenhuma linha usa: a contagem aparece esmaecida.</summary>
    public bool IsUnused => Uses == 0;

    public string UsesLabel => Uses switch
    {
        null => "…",
        0 => "—",
        { } n => ProjectLabels.Number(n),
    };

    /// <summary>O texto que a contagem procura: o termo ou o nome original.</summary>
    public abstract string Key { get; }

    /// <summary>Linha sem nada escrito: não vai para o arquivo e não conta como problema.</summary>
    public abstract bool IsBlank { get; }

    /// <summary>Por que a linha ainda não pode ser salva (nome vazio ou repetido); nulo quando está tudo certo. A tela marca a linha e as outras são salvas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;
}

/// <summary>Linha editável da tabela de glossário. O texto fica como o usuário digitou; o <see cref="GlossaryViewModel"/> limpa os espaços ao gravar.</summary>
public sealed partial class GlossaryTermRow : UsageRow
{
    public GlossaryTermRow(string source = "", string target = "", string? notes = null)
    {
        Source = source;
        Target = target;
        Notes = notes ?? string.Empty;
    }

    [ObservableProperty]
    public partial string Source { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    public override string Key => Source;

    public override bool IsBlank => string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Notes);

    public GlossaryTerm ToTerm() =>
        new(Source.Trim(), Target.Trim(), string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
}

/// <summary>Linha editável da tabela de personagens.</summary>
public sealed partial class CharacterRow : UsageRow
{
    public CharacterRow(string source = "", string target = "", CharacterGender gender = CharacterGender.Unknown, string? tone = null)
    {
        Source = source;
        Target = target;
        Gender = gender;
        Tone = tone ?? string.Empty;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender))]
    public partial string Source { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender))]
    public partial string Target { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender), nameof(HasSuggestion), nameof(TonePlaceholder))]
    public partial CharacterGender Gender { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender))]
    public partial string Tone { get; set; }

    /// <summary>Personagem preenchido e sem gênero: o seletor fica em destaque, porque a IA precisa dele para concordar o texto.</summary>
    public bool NeedsGender => Gender == CharacterGender.Unknown && !IsBlank;

    /// <summary>O gênero que a IA sugeriu e a pessoa ainda não aceitou. Não vai para o arquivo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion), nameof(SuggestedGenderLabel), nameof(TonePlaceholder))]
    public partial CharacterGender SuggestedGender { get; set; }

    /// <summary>O jeito de falar que veio junto com a sugestão de gênero; só entra no aceite, e só se a coluna estiver vazia.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TonePlaceholder))]
    public partial string SuggestedTone { get; set; } = string.Empty;

    /// <summary>Há uma sugestão da IA esperando o visto: o seletor mostra o valor esmaecido no lugar do vazio.</summary>
    public bool HasSuggestion => Gender == CharacterGender.Unknown && SuggestedGender != CharacterGender.Unknown;

    public string SuggestedGenderLabel => ProjectLabels.Gender(SuggestedGender);

    /// <summary>O texto esmaecido da coluna "Como fala": a sugestão da IA enquanto espera o aceite, ou o rótulo da coluna.</summary>
    public string TonePlaceholder => HasSuggestion && SuggestedTone.Length > 0 ? SuggestedTone : "Como fala";

    public void ClearSuggestion()
    {
        SuggestedGender = CharacterGender.Unknown;
        SuggestedTone = string.Empty;
    }

    public override string Key => Source;

    public override bool IsBlank =>
        string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Tone) && Gender == CharacterGender.Unknown;

    public CharacterEntry ToEntry() =>
        new(Source.Trim(), Target.Trim(), Gender, string.IsNullOrWhiteSpace(Tone) ? null : Tone.Trim());
}

/// <summary>
/// Uma linha do painel "Termos que se repetem": a sequência, em quantas linhas aparece e, depois de a IA traduzir o lote, a tradução e o motivo.
/// Não vai para o arquivo: só vira termo do glossário quando a pessoa clica em Adicionar.
/// </summary>
public sealed partial class RepeatedTermRow : ObservableObject
{
    public RepeatedTermRow(string source, int lines)
    {
        Source = source;
        Lines = lines;
    }

    public string Source { get; }

    public int Lines { get; }

    public string LinesLabel => $"{ProjectLabels.Number(Lines)} {(Lines == 1 ? "linha" : "linhas")}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion))]
    public partial string? Target { get; set; }

    [ObservableProperty]
    public partial string? Reason { get; set; }

    public bool HasSuggestion => !string.IsNullOrWhiteSpace(Target);
}
