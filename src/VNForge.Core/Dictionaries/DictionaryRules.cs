using VNForge.Core.Formatting;

namespace VNForge.Core.Dictionaries;

/// <summary>O que a condição de uma regra olha na linha.</summary>
public enum DictionaryConditionKind
{
    /// <summary>O falante da linha: o nome original ou o traduzido do cadastro de personagens.</summary>
    Speaker,

    /// <summary>A cena da linha: a chave do script, o nome do script, o grupo ou a pasta de <c>scripts.json</c>.</summary>
    Scene,

    /// <summary>O original da linha contém o trecho.</summary>
    SourceContains,
}

/// <summary>Uma condição de <see cref="DictionaryRule"/>. Não diferencia maiúsculas; o trecho do original compara como texto, sem fronteira de palavra.</summary>
/// <param name="Kind">O que olhar na linha.</param>
/// <param name="Value">Nome do falante, da cena ou trecho do original. Não pode ser vazio.</param>
public sealed record DictionaryCondition(DictionaryConditionKind Kind, string Value);

/// <summary>
/// Uma regra do pré ou do pós-dicionário. Troca <paramref name="Pattern"/> por <paramref name="With"/> no texto da linha quando todas as
/// <see cref="Conditions"/> batem; sem condição, a troca vale em toda linha.
/// </summary>
/// <param name="Pattern">Texto a procurar (literal) ou expressão regular, conforme <paramref name="Regex"/>. Não pode ser vazio.</param>
/// <param name="With">Texto que entra no lugar. Com <paramref name="Regex"/>, aceita <c>$1</c> e <c>${nome}</c>.</param>
/// <param name="Regex">Trata <paramref name="Pattern"/> como expressão regular do .NET.</param>
public sealed record DictionaryRule(string Pattern, string With, bool Regex = false)
{
    /// <summary>Regra desligada fica na lista e não troca nada.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Todas precisam bater para a regra valer.</summary>
    public IReadOnlyList<DictionaryCondition> Conditions { get; init; } = [];

    internal Replacement AsReplacement() => new(Pattern, With, Regex);
}

/// <summary>
/// As regras de dicionário do projeto (<c>dictionary</c> do <c>formatting.json</c>). O <see cref="Pre"/> normaliza o original antes de ele ir à IA
/// (e entra na chave do cache de traduções pelo texto que resulta); o <see cref="Post"/> troca no texto traduzido, no build, antes das regras do perfil.
/// </summary>
public sealed record DictionaryRules(IReadOnlyList<DictionaryRule> Pre, IReadOnlyList<DictionaryRule> Post)
{
    public static DictionaryRules None { get; } = new([], []);

    public bool IsEmpty => Pre.Count == 0 && Post.Count == 0;
}
