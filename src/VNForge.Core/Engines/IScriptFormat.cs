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
    /// <see cref="TextUnit.Locator"/> preenchido com o que <see cref="Rebuild"/> precisa para reinjetar sem reinterpretar os comandos do script.
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
    /// Validação, antes de gravar qualquer coisa (<see cref="ScriptFile.ValidateUnits"/> faz as duas primeiras):
    /// <list type="bullet">
    /// <item><description>Unidade cujo <see cref="TextUnit.Id"/> não começa com <c>file.Key + ":"</c> lança <see cref="ArgumentException"/>.</description></item>
    /// <item><description><see cref="TextUnit.Id"/> duplicado em <paramref name="units"/> lança <see cref="ArgumentException"/>.</description></item>
    /// <item><description>Unidade a reescrever confere que o trecho apontado pelo <see cref="TextUnit.Locator"/> ainda decodifica para o
    /// <see cref="TextUnit.Source"/>; se não, o arquivo é de outra versão e a engine lança <see cref="InvalidDataException"/>.</description></item>
    /// </list>
    /// Reescrita mínima, por unidade:
    /// <list type="bullet">
    /// <item><description>Unidade que não precisa de reescrita (<see cref="TextUnit.NeedsRewrite"/> falso: sem tradução, status
    /// <see cref="TranslationStatus.Untranslated"/> (mesmo com <see cref="TextUnit.Translation"/> preenchida), tradução vazia ou igual ao
    /// <see cref="TextUnit.Source"/>) tem o trecho original copiado cru, byte a byte, sem decodificar nem recodificar.</description></item>
    /// <item><description>Unidade ausente de <paramref name="units"/> também é copiada crua. A lista pode ser um subconjunto da extração, em
    /// qualquer ordem; a engine casa cada unidade com o script pelo <see cref="TextUnit.Locator"/>. O <see cref="TextUnit.Id"/> é opaco: serve
    /// à validação acima e às mensagens de erro, nunca é interpretado.</description></item>
    /// <item><description>A unidade com <see cref="TextUnit.NeedsRewrite"/> verdadeiro é codificada com <paramref name="encoder"/> (a engine parte o
    /// texto nos marcadores e codifica só os trechos de texto). Se os bytes codificados forem iguais aos originais (ex.: o <c>fold</c> reduz a
    /// tradução ao mesmo texto), o argumento é copiado cru, como se não houvesse tradução; a regra 3 do <c>CLAUDE.md</c> vale também aqui.</description></item>
    /// </list>
    /// Sem nenhuma unidade a reescrever, o resultado é idêntico ao <see cref="ScriptFile.Content"/>, salvo num script que <see cref="RequiresRebuild"/> aponta.
    /// </remarks>
    /// <param name="file">Script original, o mesmo que foi extraído.</param>
    /// <param name="units">Unidades com as traduções, normalmente lidas de <c>text/*.json</c>.</param>
    /// <param name="encoder">Codificador do texto para os bytes do jogo.</param>
    /// <param name="ctx">Contexto da engine deste jogo; precisa ser o criado pela mesma engine.</param>
    /// <exception cref="InvalidDataException">
    /// O script é inválido, ou o <see cref="TextUnit.Locator"/> de uma unidade a reescrever não corresponde ao script
    /// (ausente, de outra versão do script, ou apontando para um texto diferente do <see cref="TextUnit.Source"/>). A mensagem cita o <see cref="TextUnit.Id"/>.
    /// </exception>
    /// <exception cref="EncoderFallbackException">
    /// A tradução de uma unidade tem caractere que <paramref name="encoder"/> não representa. A exceção sobe sem resultado parcial e a
    /// mensagem cita o <see cref="TextUnit.Id"/> (a original vai em <see cref="Exception.InnerException"/>).
    /// </exception>
    /// <exception cref="ArgumentException">O contexto é de outra engine, ou os ids de <paramref name="units"/> não passam na validação acima.</exception>
    byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx);

    /// <summary>
    /// Verdadeiro quando o script precisa passar pelo <see cref="Rebuild"/> mesmo sem nenhuma unidade a reescrever, porque a codificação escolhida
    /// muda algo que não é texto traduzido (no YU-RIS, os marcadores de falante que o jogo procura, #434). O padrão é falso.
    /// </summary>
    /// <param name="file">Script original.</param>
    /// <param name="encoder">Codificador do texto para os bytes do jogo.</param>
    /// <param name="ctx">Contexto da engine deste jogo.</param>
    bool RequiresRebuild(ScriptFile file, ITextEncoder encoder, EngineContext ctx) => false;
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
    /// (<c>"ybin/YST00123.ybn"</c> vira <c>"yst00123"</c>). Precisa ser única no jogo: a engine garante que dois scripts não
    /// tenham o mesmo nome-base (nas engines atuais os nomes são únicos, como <c>yst00123</c>).
    /// </summary>
    public string Key => Path.GetFileNameWithoutExtension(Name.Replace('\\', '/')).ToLowerInvariant();

    /// <summary>
    /// Monta o <see cref="TextUnit.Id"/> da unidade na posição <paramref name="position"/>: a chave, dois-pontos e a posição com ao menos
    /// seis dígitos (<c>"yst00123:000042"</c>). A posição é uma chave estável e única dentro do script, escolhida pela engine; o padrão é a
    /// ordem da unidade no arquivo, mas uma engine pode usar outro número, como o índice da instrução. O id só depende do conteúdo do
    /// script, por isso repete entre extrações. É opaco: ninguém extrai informação dele, o <see cref="TextUnit.Locator"/> é que localiza.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> negativa.</exception>
    public string UnitId(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return $"{Key}:{position:D6}";
    }

    /// <summary>
    /// Confere que os ids de <paramref name="units"/> pertencem a este script (começam com <c>Key + ":"</c>) e não se repetem.
    /// As engines chamam no início de <see cref="IScriptFormat.Rebuild"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Id de outro script ou duplicado; a mensagem cita o id.</exception>
    public void ValidateUnits(IReadOnlyList<TextUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        var prefixo = Key + ":";
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in units.Select(unit => unit.Id))
        {
            if (!id.StartsWith(prefixo, StringComparison.Ordinal))
            {
                throw new ArgumentException($"A unidade '{id}' não pertence ao script '{Name}' (o id deveria começar com '{prefixo}').", nameof(units));
            }

            if (!vistos.Add(id))
            {
                throw new ArgumentException($"O id '{id}' aparece mais de uma vez nas unidades do script '{Name}'.", nameof(units));
            }
        }
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
public abstract class EngineContext
{
    /// <summary>Cria o contexto de uma engine.</summary>
    /// <param name="engineId"><see cref="IEngine.Id"/> da engine dona do contexto.</param>
    protected EngineContext(string engineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        EngineId = engineId;
    }

    /// <summary><see cref="IEngine.Id"/> da engine que criou o contexto. O formato de script confere antes de usar.</summary>
    public string EngineId { get; }
}
