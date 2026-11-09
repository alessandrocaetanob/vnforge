using System.Text.Json.Serialization;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Memory;

/// <summary>Um termo do glossário pessoal: vale em toda VN (お兄ちゃん, 先輩, onomatopeias) e serve de ponto de partida em projeto novo.</summary>
/// <param name="Source">Termo no idioma original. Único no glossário (comparação ordinal).</param>
/// <param name="Target">Tradução que a pessoa prefere.</param>
/// <param name="Notes">Observação para a IA e para quem traduz; nula quando não há.</param>
/// <param name="Projects">Projetos (chaves de <see cref="TranslationMemory.KeyOf"/>) de onde a pessoa guardou o termo; vazio quando foi escrito à mão.</param>
/// <param name="Seed">Se o termo entra sozinho em projeto novo. Ligado por padrão.</param>
public sealed record PersonalTerm(string Source, string Target, string? Notes = null, IReadOnlyList<string>? Projects = null, bool Seed = true)
{
    public IReadOnlyList<string> Projects { get; init; } = Projects ?? [];

    /// <summary>De quantos projetos a pessoa guardou o termo.</summary>
    [JsonIgnore]
    public int ProjectCount => Projects.Count;
}

/// <summary>Como um termo do glossário pessoal está num projeto.</summary>
public enum PersonalTermState
{
    /// <summary>O projeto não tem o termo.</summary>
    NotInProject,

    /// <summary>O projeto tem o termo com a mesma tradução.</summary>
    InProject,

    /// <summary>O projeto tem o termo com outra tradução.</summary>
    Differs,
}

/// <summary>
/// O glossário pessoal (#537): <c>personal-glossary.json</c> na pasta de configuração do usuário, fora dos projetos. Um projeto novo começa com os termos marcados
/// (<see cref="SeedProject"/>) e, a partir daí, o <c>glossary.json</c> dele muda sozinho: editar o projeto não mexe no pessoal e o contrário também.
/// </summary>
public sealed class PersonalGlossary(string directory)
{
    public const string FileName = "personal-glossary.json";

    private readonly UserEntriesFile<PersonalTerm> file = new(System.IO.Path.Combine(directory, FileName), ValidateTerms);
    private readonly object gate = new();

    public string Path => file.Path;

    private static List<PersonalTerm> ValidateTerms(string path, List<PersonalTerm> terms)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < terms.Count; i++)
        {
            var t = terms[i];
            if (string.IsNullOrWhiteSpace(t.Source) || string.IsNullOrWhiteSpace(t.Target))
            {
                throw new MemoryFileException(path, $"o termo {i + 1} não tem o original ou a tradução.");
            }

            if (!seen.Add(t.Source.Trim()))
            {
                throw new MemoryFileException(path, $"o termo '{t.Source.Trim()}' aparece mais de uma vez.");
            }
        }

        return terms;
    }

    /// <summary>Os termos, na ordem em que foram guardados.</summary>
    /// <exception cref="MemoryFileException">O arquivo existe e não pôde ser lido.</exception>
    public IReadOnlyList<PersonalTerm> Load()
    {
        lock (gate)
        {
            return file.Load();
        }
    }

    /// <summary>Guarda o termo; se o original já estava no glossário, troca a entrada e mantém os projetos de onde ele veio.</summary>
    /// <exception cref="ArgumentException">Original ou tradução vazios.</exception>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido ou gravado.</exception>
    public PersonalTerm Upsert(PersonalTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentException.ThrowIfNullOrWhiteSpace(term.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(term.Target);
        var source = term.Source.Trim();
        var clean = term with { Source = source, Target = term.Target.Trim(), Notes = string.IsNullOrWhiteSpace(term.Notes) ? null : term.Notes.Trim() };

        lock (gate)
        {
            var terms = file.Load();
            var index = terms.FindIndex(t => t.Source == source);
            if (index < 0)
            {
                terms.Add(clean);
            }
            else
            {
                clean = clean with { Projects = terms[index].Projects.Union(clean.Projects, StringComparer.OrdinalIgnoreCase).ToList() };
                terms[index] = clean;
            }

            file.Save(terms);
            return clean;
        }
    }

    /// <summary>"Guardar no glossário pessoal": guarda o termo do projeto e anota o projeto como origem dele.</summary>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido ou gravado.</exception>
    public PersonalTerm Keep(ProjectFolder project, GlossaryTerm term)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(term);
        return Upsert(new PersonalTerm(term.Source, term.Target, term.Notes, [TranslationMemory.ProjectKeyOf(project)]));
    }

    /// <summary>Tira o termo do glossário pessoal. Devolve se ele existia.</summary>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido ou gravado.</exception>
    public bool Remove(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (gate)
        {
            var terms = file.Load();
            if (terms.RemoveAll(t => t.Source == source.Trim()) == 0)
            {
                return false;
            }

            file.Save(terms);
            return true;
        }
    }

    /// <summary>Como <paramref name="term"/> está no <c>glossary.json</c> do projeto.</summary>
    public static PersonalTermState StateIn(PersonalTerm term, IReadOnlyList<GlossaryTerm> projectTerms)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentNullException.ThrowIfNull(projectTerms);
        var there = projectTerms.FirstOrDefault(t => t.Source.Trim() == term.Source);
        if (there is null)
        {
            return PersonalTermState.NotInProject;
        }

        return there.Target.Trim() == term.Target ? PersonalTermState.InProject : PersonalTermState.Differs;
    }

    /// <summary>Os termos do projeto que o glossário pessoal ainda não tem, ou tem com outra tradução: a lista "Guardar no glossário pessoal".</summary>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido.</exception>
    public IReadOnlyList<GlossaryTerm> WorthKeeping(IReadOnlyList<GlossaryTerm> projectTerms)
    {
        ArgumentNullException.ThrowIfNull(projectTerms);
        var mine = Load().ToDictionary(t => t.Source, StringComparer.Ordinal);
        return projectTerms
            .Where(t => !mine.TryGetValue(t.Source.Trim(), out var own) || own.Target != t.Target.Trim())
            .ToList();
    }

    /// <summary>
    /// "Usar no projeto": copia o termo para o <c>glossary.json</c> do projeto. Se o projeto já tem o original com outra tradução, só troca com <paramref name="replace"/>;
    /// sem ele a escolha do projeto fica como está. Devolve o estado em que o termo ficou no projeto.
    /// </summary>
    /// <exception cref="KeyNotFoundException">O glossário pessoal não tem o termo.</exception>
    /// <exception cref="GlossaryFileException">O <c>glossary.json</c> do projeto é inválido; nada é gravado.</exception>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido.</exception>
    public PersonalTermState UseInProject(ProjectFolder project, string source, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var term = Load().FirstOrDefault(t => t.Source == source.Trim()) ?? throw new KeyNotFoundException($"'{source}' não está no glossário pessoal.");
        var terms = project.LoadGlossary().ToList();
        var index = terms.FindIndex(t => t.Source.Trim() == term.Source);
        if (index >= 0 && (!replace || terms[index].Target.Trim() == term.Target))
        {
            return StateIn(term, terms);
        }

        var added = new GlossaryTerm(term.Source, term.Target, term.Notes);
        if (index >= 0)
        {
            terms[index] = added;
        }
        else
        {
            terms.Add(added);
        }

        project.SaveGlossary(terms);
        return PersonalTermState.InProject;
    }

    /// <summary>
    /// Projeto novo: copia para o <c>glossary.json</c> os termos marcados (<see cref="PersonalTerm.Seed"/>) que o projeto ainda não tem. Quem já tem o original, com qualquer
    /// tradução, fica como está. Devolve quantos termos entraram.
    /// </summary>
    /// <exception cref="GlossaryFileException">O <c>glossary.json</c> do projeto é inválido; nada é gravado.</exception>
    /// <exception cref="MemoryFileException">O arquivo não pôde ser lido.</exception>
    public int SeedProject(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var terms = project.LoadGlossary().ToList();
        var known = terms.Select(t => t.Source.Trim()).ToHashSet(StringComparer.Ordinal);
        var seeded = Load().Where(t => t.Seed && !known.Contains(t.Source)).Select(t => new GlossaryTerm(t.Source, t.Target, t.Notes)).ToList();
        if (seeded.Count == 0)
        {
            return 0;
        }

        terms.AddRange(seeded);
        project.SaveGlossary(terms);
        return seeded.Count;
    }
}
