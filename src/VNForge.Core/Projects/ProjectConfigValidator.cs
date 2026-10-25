using System.Text.RegularExpressions;

namespace VNForge.Core.Projects;

internal static partial class ProjectConfigValidator
{
    public static readonly string[] EncodingStrategies = ["sjis", "fold", "remap", "cp1252"];
    public static readonly string[] Efforts = ["low", "medium", "high"];
    public static readonly string[] AnthropicModels = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1"];

    public static readonly string[] Providers = ["anthropic"];

    private const int MaxBatchSize = 200;

    public static void Validate(ProjectConfig config)
    {
        NotBlank(config.Name, "$.name");

        NotBlank(config.Game.Path, "$.game.path");
        NotBlank(config.Game.Exe, "$.game.exe");

        NotBlank(config.Engine.Id, "$.engine.id");
        if (config.Engine.Version is <= 0)
        {
            throw new ProjectConfigException("$.engine.version", "deve ser um número inteiro positivo.");
        }

        if (config.Engine.ScriptKey is { } key && !ScriptKeyPattern().IsMatch(key))
        {
            throw new ProjectConfigException(
                "$.engine.scriptKey", "o valor não é uma chave válida; use 0x seguido de até 8 dígitos hexadecimais.");
        }

        NotBlank(config.Languages.Source, "$.languages.source");
        NotBlank(config.Languages.Target, "$.languages.target");

        OneOf(config.Encoding.Strategy, EncodingStrategies, "$.encoding.strategy");

        NotBlank(config.Ai.Provider, "$.ai.provider");
        OneOf(config.Ai.Provider, Providers, "$.ai.provider");
        NotBlank(config.Ai.Model, "$.ai.model");
        OneOf(config.Ai.Model, AnthropicModels, "$.ai.model");

        OneOf(config.Ai.Effort, Efforts, "$.ai.effort");
        if (config.Ai.BatchSize is < 1 or > MaxBatchSize)
        {
            throw new ProjectConfigException("$.ai.batchSize", $"deve estar entre 1 e {MaxBatchSize}.");
        }

        ValidateStyle(config.Style);
    }

    private static void ValidateStyle(StyleConfig style)
    {
        OneOf(style.Honorifics, StyleNames.Honorifics, "$.style.honorifics");
        OneOf(style.Address, StyleNames.Addresses, "$.style.address");
        OneOf(style.Narrator.Person, StyleNames.Persons, "$.style.narrator.person");
        OneOf(style.Narrator.Gender, StyleNames.Genders, "$.style.narrator.gender");
    }

    private static void NotBlank(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProjectConfigException(field, "não pode ser vazio.");
        }
    }

    private static void OneOf<T>(T value, IReadOnlyDictionary<string, T> allowed, string field)
    {
        if (!allowed.Values.Contains(value))
        {
            throw new ProjectConfigException(field, $"valor não aceito; use um destes: {string.Join(", ", allowed.Keys)}.");
        }
    }

    private static void OneOf(string value, string[] allowed, string field)
    {
        if (!allowed.Contains(value))
        {
            throw new ProjectConfigException(field, $"valor não aceito; use um destes: {string.Join(", ", allowed)}.");
        }
    }

    [GeneratedRegex("^0[xX][0-9A-Fa-f]{1,8}$")]
    private static partial Regex ScriptKeyPattern();
}
