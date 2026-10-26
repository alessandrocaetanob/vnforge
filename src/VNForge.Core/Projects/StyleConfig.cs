namespace VNForge.Core.Projects;

/// <summary>O que fazer com os sufixos de tratamento japoneses (-san, -kun, -chan, -sama) e com senpai.</summary>
public enum HonorificsPolicy
{
    /// <summary>Deixa como no original: Misaki-chan, senpai.</summary>
    Keep,

    /// <summary>Tira todos: Misaki, veterano.</summary>
    Remove,

    /// <summary>-chan e -kun saem, -san vira "senhor" ou "senhora" só em fala formal e senpai segue o glossário.</summary>
    Adapt,
}

/// <summary>Como os personagens se tratam em português.</summary>
public enum AddressForm
{
    /// <summary>"Você vai se molhar."</summary>
    Voce,

    /// <summary>"Tu vais te molhar."</summary>
    Tu,
}

/// <summary>Pessoa gramatical do narrador.</summary>
public enum NarratorPerson
{
    First,
    Third,
}

/// <summary>Gênero gramatical do narrador, que decide a concordância ("cansado", "cansada").</summary>
public enum NarratorGender
{
    Neutral,
    Male,
    Female,
}

/// <summary>Quem narra: nome livre (pode ficar vazio), pessoa e gênero.</summary>
public sealed record NarratorStyle
{
    /// <summary>Quem narra, em texto livre ("Kenta, o protagonista"); vazio quando a pessoa não disse.</summary>
    public string? Who { get; init; }

    public NarratorPerson Person { get; init; } = NarratorPerson.First;

    public NarratorGender Gender { get; init; } = NarratorGender.Neutral;

    /// <summary><see cref="Who"/> sem espaços nas pontas; <see langword="null"/> quando vazio.</summary>
    public string? WhoOrNull => string.IsNullOrWhiteSpace(Who) ? null : Who.Trim();
}

/// <summary>
/// As escolhas de estilo da tradução, gravadas em <c>vnforge.json</c> (<c>style</c>). Vão em cada pedido à IA, e as regras de QA
/// <c>honorifico-fora-do-estilo</c> e <c>personagem-sem-genero</c> conferem a tradução contra elas.
/// </summary>
public sealed record StyleConfig
{
    /// <summary>Estilo de um projeto que nunca abriu a aba Estilo.</summary>
    public static StyleConfig Default { get; } = new();

    public HonorificsPolicy Honorifics { get; init; } = HonorificsPolicy.Adapt;

    public AddressForm Address { get; init; } = AddressForm.Voce;

    public NarratorStyle Narrator { get; init; } = new();

    /// <summary>Traduzir as onomatopeias (ドキドキ vira "tum-tum"); desligado, ficam como soam em japonês.</summary>
    public bool TranslateOnomatopoeia { get; init; } = true;

    /// <summary>Escrever os nomes na ordem ocidental (Misaki Sakura em vez de Sakura Misaki).</summary>
    public bool WesternNameOrder { get; init; } = true;

    /// <summary>
    /// Trocar a pontuação japonesa (<c>。 ～ （</c>) pela do teclado (<c>. ~ (</c>) na tradução que a IA devolve e na que a pessoa grava, e pedir pontuação
    /// ocidental à IA. Fica fora de <see cref="CacheKey"/>: a troca vem depois do cache, então ligar ou desligar vale para o que já foi pago.
    /// </summary>
    public bool KeyboardPunctuation { get; init; } = true;

    /// <summary>
    /// Forma estável das escolhas, para entrar na chave do cache de traduções. O nome do narrador sem espaços nas pontas e o
    /// vazio contam como o mesmo valor.
    /// </summary>
    public string CacheKey() =>
        string.Join('|', Honorifics, Address, Narrator.WhoOrNull, Narrator.Person, Narrator.Gender, TranslateOnomatopoeia, WesternNameOrder);
}
