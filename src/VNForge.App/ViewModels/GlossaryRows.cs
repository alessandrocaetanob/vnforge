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

    public bool IsBlank => string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Notes);

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
    public partial string Source { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; }

    [ObservableProperty]
    public partial CharacterGender Gender { get; set; }

    [ObservableProperty]
    public partial string Tone { get; set; }

    public override string Key => Source;

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Tone) && Gender == CharacterGender.Unknown;

    public CharacterEntry ToEntry() =>
        new(Source.Trim(), Target.Trim(), Gender, string.IsNullOrWhiteSpace(Tone) ? null : Tone.Trim());
}
