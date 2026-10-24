using CommunityToolkit.Mvvm.ComponentModel;
using VNForge.Core.Glossary;

namespace VNForge.App.ViewModels;

/// <summary>Linha editável da tabela de glossário. O texto fica como o usuário digitou; o <see cref="GlossaryViewModel"/> limpa os espaços ao gravar.</summary>
public sealed partial class GlossaryTermRow : ObservableObject
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

    public bool IsBlank => string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Notes);

    public GlossaryTerm ToTerm() =>
        new(Source.Trim(), Target.Trim(), string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
}

/// <summary>Linha editável da tabela de personagens.</summary>
public sealed partial class CharacterRow : ObservableObject
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

    public bool IsBlank =>
        string.IsNullOrWhiteSpace(Source) && string.IsNullOrWhiteSpace(Target) && string.IsNullOrWhiteSpace(Tone) && Gender == CharacterGender.Unknown;

    public CharacterEntry ToEntry() =>
        new(Source.Trim(), Target.Trim(), Gender, string.IsNullOrWhiteSpace(Tone) ? null : Tone.Trim());
}
