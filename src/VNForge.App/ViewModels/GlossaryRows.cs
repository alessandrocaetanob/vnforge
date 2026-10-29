using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.App.Controls;
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
    public CharacterRow(string source = "", string target = "", CharacterGender gender = CharacterGender.Unknown, string? tone = null, string? color = null)
    {
        Source = source;
        Target = target;
        Gender = gender;
        Tone = tone ?? string.Empty;
        ColorKey = CharacterPalette.Find(color)?.Key ?? color;
        CommittedSource = source.Trim();
    }

    /// <summary>O nome com que a linha nasceu: o lido do arquivo ou, depois de cada salvamento, o salvo. Os tratamentos que citam o personagem seguem dele para o nome atual ao salvar (#514).</summary>
    public string CommittedSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender), nameof(Color), nameof(IsBlank), nameof(OpenSheetLabel))]
    public partial string Source { get; set; }

    /// <summary>O id da cor que a pessoa escolheu (ou que o arquivo traz); nulo deixa o app escolher pelo nome. Só vai para o arquivo quando definido.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Color), nameof(HasCustomColor))]
    public partial string? ColorKey { get; set; }

    /// <summary>A linha tem uma cor gravada (escolhida ou lida do arquivo): o botão de voltar à automática aparece.</summary>
    public bool HasCustomColor => !string.IsNullOrWhiteSpace(ColorKey);

    /// <summary>Tira a cor gravada e deixa o app escolher pelo nome de novo; o <c>color</c> some do <c>characters.json</c> no próximo salvamento.</summary>
    [RelayCommand]
    private void UseAutomaticColor() => ColorKey = null;

    /// <summary>A cor da linha para o seletor: a escolhida ou, sem escolha, a que o app dá a esse nome. Escolher grava o id.</summary>
    public CharacterColor Color
    {
        get => CharacterPalette.Resolve(ColorKey, Source);
        set
        {
            if (value is not null)
            {
                ColorKey = value.Key;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender), nameof(IsBlank), nameof(OpenSheetLabel))]
    public partial string Target { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender), nameof(HasSuggestion), nameof(TonePlaceholder), nameof(IsBlank))]
    public partial CharacterGender Gender { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsGender), nameof(IsBlank))]
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

    // Quem escolhe um gênero à mão encerra a sugestão: ela não volta se a pessoa devolver o seletor a "não definido".
    partial void OnGenderChanged(CharacterGender value)
    {
        if (value != CharacterGender.Unknown)
        {
            ClearSuggestion();
        }
    }

    public void ClearSuggestion()
    {
        SuggestedGender = CharacterGender.Unknown;
        SuggestedTone = string.Empty;
    }

    public override string Key => Source;

    /// <summary>O nome do botão que abre a ficha, com o personagem dito, para o leitor de tela distinguir as linhas.</summary>
    public string OpenSheetLabel => $"Abrir a ficha de {(string.IsNullOrWhiteSpace(Target) ? Source.Trim() : Target.Trim())}".TrimEnd();

    public override bool IsBlank =>
        string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Tone) && Gender == CharacterGender.Unknown;

    public CharacterEntry ToEntry() =>
        new(Source.Trim(), Target.Trim(), Gender, string.IsNullOrWhiteSpace(Tone) ? null : Tone.Trim(), string.IsNullOrWhiteSpace(ColorKey) ? null : ColorKey.Trim());
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

/// <summary>
/// Uma linha do painel "Termos que você corrigiu": o trecho japonês, a tradução que a pessoa pôs nele em várias falas e o que a IA tinha escrito.
/// Não vai para o arquivo: só vira termo do glossário quando a pessoa clica em Adicionar.
/// </summary>
public sealed class CorrectedTermRow(TermPair pair)
{
    public string Source { get; } = pair.Source;

    public string Target { get; } = pair.Target;

    public string From { get; } = pair.From;

    public int Lines { get; } = pair.Lines;

    public string LinesLabel => $"{ProjectLabels.Number(Lines)} {(Lines == 1 ? "fala" : "falas")}";

    public string ChangeLabel => $"A IA escreveu \"{From}\"; você gravou \"{Target}\".";
}
