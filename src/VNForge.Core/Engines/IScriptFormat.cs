using VNForge.Core.Text;

namespace VNForge.Core.Engines;

/// <summary>
/// Um formato de script do jogo (ex.: YSTB). Tira as <see cref="TextUnit"/> de um script e o reconstrói com as traduções,
/// com reescrita mínima: o que não mudou sai byte a byte igual (regra 3 do <c>CLAUDE.md</c>).
/// </summary>
/// <remarks>
/// As duas operações são puras: não gravam em disco e não guardam estado entre chamadas, então a mesma instância
/// serve a vários scripts, inclusive em paralelo, desde que o <see cref="EngineContext"/> recebido seja tratado como somente leitura.
/// </remarks>
public interface IScriptFormat
{
    /// <summary>Identificador estável do formato, em minúsculas (ex.: <c>"ystb"</c>).</summary>
    string Id { get; }

    /// <summary>Extensões comuns dos scripts do formato, com ponto e em minúsculas (ex.: <c>".ybn"</c>).</summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Lê o script e devolve suas unidades de texto, na ordem em que aparecem no arquivo. Não altera <paramref name="file"/>.
    /// </summary>
    /// <remarks>
    /// Cada <see cref="TextUnit.Id"/> sai de <see cref="ScriptFile.UnitId"/>, único no script e igual em extrações repetidas
    /// do mesmo conteúdo. As unidades saem com <see cref="TranslationStatus.Untranslated"/>, <see cref="TextUnit.Translation"/> nula e
    /// <see cref="TextUnit.Locator"/> preenchido com o que <see cref="Rebuild"/> precisa para reinjetar sem reparsear.
    /// </remarks>
    /// <param name="file">Script a ler.</param>
    /// <param name="ctx">Contexto da engine deste jogo; precisa ser o criado pela mesma engine.</param>
    /// <exception cref="InvalidDataException">O conteúdo não é do formato ou está inconsistente; a mensagem, em português, diz o que falhou.</exception>
    /// <exception cref="ArgumentException">O contexto é de outra engine.</exception>
    ScriptExtraction Extract(ScriptFile file, EngineContext ctx);

    /// <summary>
    /// Gera o script com as traduções aplicadas. Não altera <paramref name="file"/> nem <paramref name="units"/>.
    /// </summary>
    /// <remarks>
    /// Reescrita mínima, por unidade:
    /// <list type="bullet">
    /// <item><description>Unidade que não precisa de reescrita (<see cref="TextUnit.NeedsRewrite"/> falso: sem tradução, status
    /// <see cref="TranslationStatus.Untranslated"/>, tradução vazia ou igual ao <see cref="TextUnit.Source"/>) tem o trecho original copiado cru,
    /// byte a byte, sem decodificar nem recodificar.</description></item>
    /// <item><description>Unidade ausente de <paramref name="units"/> também é copiada crua. A lista pode ser um subconjunto da extração, em
    /// qualquer ordem; a engine casa cada unidade com o script pelo <see cref="TextUnit.Locator"/>, e o <see cref="TextUnit.Id"/> serve a mensagens de erro.</description></item>
    /// <item><description>Só a unidade com <see cref="TextUnit.NeedsRewrite"/> verdadeiro é codificada com <paramref name="encoder"/> e gravada.</description></item>
    /// </list>
    /// Sem nenhuma unidade a reescrever, o resultado é idêntico ao <see cref="ScriptFile.Content"/>.
    /// </remarks>
    /// <param name="file">Script original, o mesmo que foi extraído.</param>
    /// <param name="units">Unidades com as traduções, normalmente lidas de <c>text/*.json</c>.</param>
    /// <param name="encoder">Codificador do texto para os bytes do jogo.</param>
    /// <param name="ctx">Contexto da engine deste jogo; precisa ser o criado pela mesma engine.</param>
    /// <exception cref="InvalidDataException">
    /// O script é inválido, ou o <see cref="TextUnit.Locator"/> de uma unidade a reescrever não corresponde ao script
    /// (ausente, de outra versão do script, ou apontando para outro texto). Nenhum resultado parcial é devolvido.
    /// </exception>
    /// <exception cref="ArgumentException">O contexto é de outra engine.</exception>
    byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx);
}

/// <summary>
/// Os bytes de um script do jogo, já tirados do pacote. O Core não sabe de onde vêm (pacote, pasta, teste).
/// </summary>
public sealed record ScriptFile
{
    /// <summary>Cria o arquivo.</summary>
    /// <param name="name">Nome do script no jogo, sem pasta nenhuma obrigatória (ex.: <c>"yst00123.ybn"</c>); a extensão é ignorada pela chave.</param>
    /// <param name="content">Conteúdo completo, como está no jogo.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> vazio.</exception>
    public ScriptFile(string name, ReadOnlyMemory<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Content = content;
    }

    /// <summary>Nome do script no jogo (ex.: <c>"yst00123.ybn"</c>).</summary>
    public string Name { get; }

    /// <summary>Conteúdo completo do script. A engine não o altera.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>
    /// Chave do script em <see cref="TextUnit.Id"/>: o nome do arquivo sem pasta e sem extensão, em minúsculas
    /// (<c>"ybin/YST00123.ybn"</c> vira <c>"yst00123"</c>).
    /// </summary>
    public string Key => Path.GetFileNameWithoutExtension(Name.Replace('\\', '/')).ToLowerInvariant();

    /// <summary>
    /// Monta o <see cref="TextUnit.Id"/> da unidade de posição <paramref name="ordinal"/> (contada a partir de 0, na ordem do arquivo):
    /// a chave, dois-pontos e a posição com ao menos quatro dígitos (<c>"yst00123:0042"</c>). O id só depende do conteúdo do script,
    /// por isso repete entre extrações; se uma versão nova do jogo inserir texto antes da unidade, a posição muda e a re-extração (E3.5)
    /// detecta pela comparação do original.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> negativo.</exception>
    public string UnitId(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        return $"{Key}:{ordinal:D4}";
    }
}

/// <summary>Resultado de <see cref="IScriptFormat.Extract"/>.</summary>
/// <param name="Units">Unidades de texto, na ordem do arquivo, com <see cref="TextUnit.Id"/> únicos.</param>
public sealed record ScriptExtraction(IReadOnlyList<TextUnit> Units)
{
    /// <summary>Avisos de algo que a engine pulou ou não entendeu sem ser erro (em português, vão para o log). Nunca nulo.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Dados do jogo que a engine carrega uma vez e reusa a cada script (no YU-RIS, a tabela de opcodes do YSCM e a lista do YSTL).
/// O Core não sabe o que há dentro: cada engine deriva esta classe, cria a instância e só aceita as que ela mesma criou.
/// </summary>
/// <remarks>
/// Deve ser imutável depois de criada, para <see cref="IScriptFormat"/> poder usá-la em paralelo. Quem a cria na prática
/// (a engine, a partir dos arquivos do jogo) fica para as tarefas de cada engine.
/// </remarks>
public class EngineContext
{
    /// <summary>Cria o contexto de uma engine.</summary>
    /// <param name="engineId"><see cref="IEngine.Id"/> da engine dona do contexto.</param>
    public EngineContext(string engineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        EngineId = engineId;
    }

    /// <summary><see cref="IEngine.Id"/> da engine que criou o contexto. O formato de script confere antes de usar.</summary>
    public string EngineId { get; }
}
