using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VNForge.Core;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;

namespace VNForge.App.ViewModels;

/// <summary>Um termo do glossário pessoal na aba "Meu glossário": de quantos projetos vem e como ele está neste projeto.</summary>
public sealed class PersonalTermRow(PersonalTerm term, PersonalTermState state)
{
    public string Source => term.Source;

    public string Target => term.Target;

    public string Notes => term.Notes ?? string.Empty;

    /// <summary>"4 projetos"; um termo escrito à mão, que não veio de projeto nenhum, diz "à mão".</summary>
    public string ProjectsText => term.ProjectCount switch
    {
        0 => "à mão",
        1 => "1 projeto",
        var n => $"{n.ToString(CultureInfo.InvariantCulture)} projetos",
    };

    public PersonalTermState State => state;

    public bool IsInProject => state == PersonalTermState.InProject;

    public bool Differs => state == PersonalTermState.Differs;

    public bool CanUse => state == PersonalTermState.NotInProject;

    public GlossaryTerm ToTerm() => new(term.Source, term.Target, term.Notes);
}

/// <summary>Um termo do projeto que o glossário pessoal ainda não tem (ou tem com outra tradução): a lista "Guardar no glossário pessoal".</summary>
public sealed record KeepCandidateRow(GlossaryTerm Term)
{
    public string Label => $"{Term.Source} · {Term.Target}";
}

/// <summary>
/// A aba "Meu glossário" (#537): os termos que valem em toda VN, guardados no computador da pessoa, e o que cada um é neste projeto. "Usar no projeto" põe o termo na tabela
/// Termos (que grava sozinha), "Guardar" leva um termo do projeto para cá. Um arquivo ilegível vira uma mensagem na aba e não derruba o Glossário.
/// </summary>
/// <param name="store">O glossário pessoal; nulo quando o app não o montou.</param>
/// <param name="projectTerms">Os termos que a tabela Termos tem agora, com o que ainda não foi gravado.</param>
/// <param name="addToProject">Põe o termo na tabela Termos do projeto.</param>
/// <param name="projectRoot">A pasta do projeto aberto; nula sem projeto.</param>
public sealed partial class PersonalGlossaryViewModel(
    Func<PersonalGlossary?> store,
    Func<IReadOnlyList<GlossaryTerm>> projectTerms,
    Action<GlossaryTerm> addToProject,
    Func<string?> projectRoot) : ObservableObject
{
    public ObservableCollection<PersonalTermRow> Rows { get; } = [];

    public ObservableCollection<KeepCandidateRow> Candidates { get; } = [];

    public int Count => Rows.Count;

    public bool IsEmpty => Rows.Count == 0;

    public bool HasCandidates => Candidates.Count > 0;

    public string Summary => $"{Count.ToString(CultureInfo.InvariantCulture)} {(Count == 1 ? "termo" : "termos")} · guardados no seu computador, fora dos projetos";

    public string FooterText => $"{Count.ToString(CultureInfo.InvariantCulture)} {(Count == 1 ? "termo" : "termos")}. Quando você cria um projeto, os termos do glossário pessoal entram no dele. Depois disso, cada projeto muda sozinho.";

    public string EmptyText => "Nenhum termo ainda. Guarde os termos deste projeto na coluna ao lado e eles valem em toda VN.";

    public string ProjectTermsText => Summarize(ProjectTermCount);

    public string BannerTitle => $"{Rows.Count(r => r.IsInProject).ToString(CultureInfo.InvariantCulture)} de {Count.ToString(CultureInfo.InvariantCulture)} termos entraram";

    public string BannerText => "Entram quando o projeto é criado ou pelo botão Usar no projeto.";

    public bool HasBanner => Count > 0;

    /// <summary>Por que a aba não pôde ler o arquivo; nulo quando leu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    private int ProjectTermCount { get; set; }

    private static string Summarize(int n) => n == 1 ? "1 termo" : $"{n.ToString(CultureInfo.InvariantCulture)} termos";

    /// <summary>Relê o glossário pessoal e confere cada termo com a tabela Termos de agora.</summary>
    public void Refresh()
    {
        Rows.Clear();
        Candidates.Clear();
        Error = null;
        var terms = projectTerms();
        ProjectTermCount = terms.Count;
        if (store() is { } personal)
        {
            try
            {
                foreach (var term in personal.Load())
                {
                    Rows.Add(new PersonalTermRow(term, PersonalGlossary.StateIn(term, terms)));
                }

                foreach (var candidate in personal.WorthKeeping(terms))
                {
                    Candidates.Add(new KeepCandidateRow(candidate));
                }
            }
            catch (MemoryFileException e)
            {
                Error = $"O glossário pessoal não pôde ser lido: {e.Message}";
            }
        }

        NotifyAll();
    }

    private void NotifyAll()
    {
        foreach (var name in new[]
        {
            nameof(Count), nameof(IsEmpty), nameof(HasCandidates), nameof(Summary), nameof(FooterText), nameof(ProjectTermsText), nameof(BannerTitle), nameof(HasBanner),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>"Usar no projeto": o termo entra na tabela Termos. Se o projeto já traduz o original de outro jeito o botão nem aparece, a escolha do projeto fica.</summary>
    [RelayCommand]
    private void Use(PersonalTermRow? row)
    {
        if (row is null || !row.CanUse)
        {
            return;
        }

        addToProject(row.ToTerm());
        Refresh();
    }

    /// <summary>"Guardar": leva o termo do projeto para o glossário pessoal, anotando este projeto como origem dele.</summary>
    [RelayCommand]
    private void Keep(KeepCandidateRow? candidate)
    {
        if (candidate is null || store() is not { } personal || projectRoot() is not { } root)
        {
            return;
        }

        try
        {
            personal.Keep(ProjectFolder.Open(root), candidate.Term);
        }
        catch (VNForgeException e)
        {
            Error = e.Message;
            return;
        }

        Refresh();
    }
}
