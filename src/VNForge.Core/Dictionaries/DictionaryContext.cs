using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Dictionaries;

/// <summary>O que as condições de uma regra de dicionário veem de uma linha.</summary>
/// <param name="Source">O original da linha, sem nenhuma troca.</param>
/// <param name="Speakers">Os nomes do falante: o original (o escolhido, quando há) e o traduzido do cadastro. Vazio na narração.</param>
/// <param name="Scenes">Os nomes da cena: a chave do script, o nome dele, o grupo e a pasta.</param>
public sealed record DictionaryLine(string Source, IReadOnlyList<string> Speakers, IReadOnlyList<string> Scenes)
{
    /// <summary>A linha sem falante nem cena conhecidos: só as regras sem condição e as de trecho do original podem bater.</summary>
    public static DictionaryLine Bare(string source) => new(source, [], []);

    /// <summary>Verdadeiro quando todas as condições batem.</summary>
    public bool Matches(IReadOnlyList<DictionaryCondition> conditions)
    {
        return conditions.All(Holds);
    }

    /// <summary>As posições, em <paramref name="conditions"/>, das condições que não batem; vazio quando todas batem.</summary>
    public IReadOnlyList<int> FailedConditions(IReadOnlyList<DictionaryCondition> conditions)
    {
        List<int>? failed = null;
        for (var i = 0; i < conditions.Count; i++)
        {
            if (!Holds(conditions[i]))
            {
                (failed ??= []).Add(i);
            }
        }

        return failed ?? [];
    }

    private bool Holds(DictionaryCondition condition)
    {
        var value = condition.Value.Trim();
        return condition.Kind switch
        {
            DictionaryConditionKind.Speaker => Speakers.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)),
            DictionaryConditionKind.Scene => Scenes.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase)),
            DictionaryConditionKind.SourceContains => condition.Value.Length > 0 && Source.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}

/// <summary>Monta a <see cref="DictionaryLine"/> de uma <see cref="TextUnit"/> com o que o projeto sabe: os nomes dos scripts e o cadastro de personagens.</summary>
public sealed class DictionaryContext(ScriptNames? scripts = null, CharacterBook? characters = null)
{
    private readonly ScriptNames _scripts = scripts ?? ScriptNames.Empty;
    private readonly CharacterBook _characters = characters ?? new CharacterBook([]);

    /// <summary>Contexto sem nome de script nem personagem: a cena vale só pela chave do script (o prefixo do id da linha).</summary>
    public static DictionaryContext Empty { get; } = new();

    /// <summary>Lê de <paramref name="project"/> os nomes dos scripts e os personagens.</summary>
    /// <exception cref="VNForgeException">Arquivo do projeto inválido.</exception>
    public static DictionaryContext For(ProjectFolder project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new(project.LoadScriptNames(), new CharacterBook(project.LoadCharacters()));
    }

    /// <summary>Como <see cref="For(ProjectFolder)"/>, mas só lê os arquivos quando o pós-dicionário do <paramref name="profile"/> tem regra com condição; sem ela, o <see cref="Empty"/>.</summary>
    public static DictionaryContext For(ProjectFolder project, Formatting.FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.Dictionary.Post.Any(r => r.Enabled && r.Conditions.Count > 0) ? For(project) : Empty;
    }

    public DictionaryLine Line(TextUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var speakers = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(unit.EffectiveSpeaker))
        {
            speakers.Add(unit.EffectiveSpeaker.Trim());
            if (_characters.Find(unit.EffectiveSpeaker) is { Target: { } target } && !string.IsNullOrWhiteSpace(target))
            {
                speakers.Add(target.Trim());
            }
        }

        return new DictionaryLine(unit.Source, speakers, SceneNames(unit.Id));
    }

    private List<string> SceneNames(string unitId)
    {
        var colon = unitId.IndexOf(':', StringComparison.Ordinal);
        var key = colon < 0 ? unitId : unitId[..colon];
        var names = new List<string>(4) { key };
        Add(names, _scripts.DisplayName(key));
        Add(names, _scripts.Group(key));
        if (_scripts.Folder(key)?.Trim().TrimEnd('/') is { Length: > 0 } folder)
        {
            names.Add(folder);
            names.Add(folder[(folder.LastIndexOf('/') + 1)..]);
        }

        return names;
    }

    private static void Add(List<string> names, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name.Trim());
        }
    }
}
