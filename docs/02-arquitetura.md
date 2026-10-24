# 02 — Arquitetura inicial

## Stack

| Item | Escolha | Motivo |
|---|---|---|
| Runtime | **.NET 10** (SDK 10.0.x, LTS) | Versão mais recente, suporte longo |
| Linguagem | C# 14 | Vem com o .NET 10 |
| CLI | `System.CommandLine` 2.x | Parser oficial da Microsoft, estável desde o .NET 10 |
| App | **Avalonia 12** (`net10.0`, tema `FluentTheme`) + CommunityToolkit.Mvvm | Decisão D3 (#96, 2026-10-24). Roda em Windows, Linux e macOS, compila no mesmo job do Core e da CLI, e o Fluent segue o tema claro ou escuro do Windows. A grade usa `ListBox` virtualizado; ver alternativas no fim e `docs/design/avalonia.md` |
| IA — Claude | SDK oficial `Anthropic` para C# | Único provedor da primeira versão (é a chave que temos). Cliente oficial, com retries e tipos prontos |
| IA — outras (depois) | Cliente "compatível com OpenAI" via HttpClient | Fica para um épico posterior. Um único cliente cobre OpenAI, Gemini, OpenRouter, DeepSeek, Ollama e LM Studio |
| Compressão | `System.IO.Compression.ZLibStream` | Os arquivos YPF usam zlib |
| Hashes | `System.IO.Hashing` (CRC32) + Adler32 e MurmurHash2 próprios | Os índices YPF usam esses três, conforme a versão |
| Codificação | `CodePagesEncodingProvider` (CP932 / Shift-JIS, CP1252) | O texto do jogo é Shift-JIS |
| Testes | xUnit + arquivos sintéticos gerados no próprio teste | Não dá para commitar arquivos de jogo comercial |
| Logs | `Microsoft.Extensions.Logging` + provedor de arquivo próprio (`VNForgeLogging`) | Mesmo log na CLI e no app, gravado em `.cache/logs/` do projeto. Erro esperado é `VNForgeException` (mensagem curta em português no console); o resto vai completo para o log |

## Estrutura da solução

```
vnforge/
├─ VNForge.slnx
├─ global.json                    # SDK 10.0.x e runner de testes (Microsoft.Testing.Platform)
├─ Directory.Build.props          # net10.0, nullable, warnings as errors, versão
├─ Directory.Packages.props       # versões centralizadas de pacotes
├─ tools/                         # Generate-Tokens.ps1 (tokens.json -> Tokens.axaml), Update-Icons.ps1 (Fluent Icons -> Icons.axaml)
├─ src/
│  ├─ VNForge.Core/               # modelo do projeto, pipeline, formatação, codificação, QA
│  ├─ VNForge.Engines.YuRis/      # YPF, YSTB, YSCM, YSTL, patch do executável
│  ├─ VNForge.AI/                 # provedores de IA, prompts, orquestração em lote
│  ├─ VNForge.Cli/                # executável "vnf"
│  └─ VNForge.App/                # Avalonia 12 (compila e testa em qualquer sistema; Mica e barra de título só no Windows)
├─ tests/
│  ├─ VNForge.Core.Tests/
│  ├─ VNForge.App.Tests/         # ViewModels, rótulos e a conferência de Tokens.axaml contra o tokens.json
│  ├─ VNForge.Engines.YuRis.Tests/
│  └─ VNForge.AI.Tests/           # com provedor falso, sem chamar API real
├─ docs/
└─ samples/                       # perfis de formatação e glossários de exemplo (sem texto de jogo)
```

Dependências entre projetos (setas = "depende de"):

```
VNForge.Cli ─┐
             ├─► VNForge.AI ─────────► VNForge.Core
VNForge.App ─┤                            ▲
             └─► VNForge.Engines.YuRis ───┘
```

Os testes usam xUnit v3 (`xunit.v3.mtp-v2`) sobre o Microsoft.Testing.Platform. No SDK 10, o `dotnet test` precisa de `global.json` com `test.runner = Microsoft.Testing.Platform`, e a solução passa por `--solution` (`dotnet test --solution VNForge.slnx`; sem argumento, ele acha a solução da pasta).

**CI e Sonar.** O CircleCI (`.circleci/config.yml`) tem dois jobs: `linux` e `windows` compilam e testam `VNForge.slnx`, com o app Avalonia nos dois. O `linux` prova que o app e os ViewModels não dependem do Windows; o `windows` leva a análise do Sonar. O filtro `VNForge.NoApp.slnf` deixou de existir: o app Avalonia é `net10.0` e não precisa ficar de fora. A análise do SonarCloud (organização `alessandrocaetanob`, projeto `alessandrocaetanob_vnforge`) roda dentro do job `windows`, com o `dotnet-sonarscanner`: `sonarscanner begin`, `dotnet build`, `dotnet-coverage collect "dotnet test ..."` e `sonarscanner end`. A cobertura vai por `sonar.cs.vscoveragexml.reportsPaths`. Em PR, o `begin` recebe `sonar.pullrequest.key` (o número em `CIRCLE_PULL_REQUEST`), `sonar.pullrequest.branch` e `sonar.pullrequest.base=main`; em outra branch, `sonar.branch.name`; na `main`, nada disso. Referência: [SonarScanner for .NET](https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/scanners/sonarscanner-for-dotnet/using) e [cobertura em .NET](https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/test-coverage/dotnet-test-coverage).

O `SONAR_TOKEN` fica no contexto `sonarcloud` do CircleCI, usado só pelo job `windows`. PR de fork não recebe o contexto ("Pass secrets to builds from forked pull requests" fica desligado): o job detecta a falta do token, pula o Sonar e roda só build e testes. Nenhum segredo entra no repositório.

**Publicação da CLI (E9.3).** Uma tag `vX.Y.Z` (ou `vX.Y.Z-rc1`) dispara o job `publicar`, que só roda depois de `linux` e `windows` verdes. Os dois também levam `filters: tags` no workflow: sem isso o CircleCI não os dispara por tag e o `publicar` nunca começa. O job publica `src/VNForge.Cli` como executável único autocontido para `win-x64` e `linux-x64` (sem trimming; ver doc 03, "Em aberto"), com `-p:Version` tirado da tag, empacota em `.zip` e `.tar.gz`, confere que o `vnf` do Linux responde `vnf <versão>` e anexa os arquivos à release do GitHub com `curl`. O pacote não fica como artefato do CircleCI: sai só na release. Se a release da tag já existe (criada pela interface do GitHub), os arquivos entram nela; senão o job cria uma, como pré-release quando a tag tem sufixo. O `GITHUB_TOKEN` (token com permissão de escrever em releases) fica no contexto `github-release` do CircleCI, que só o `publicar` recebe; sem ele o job falha antes de compilar.

**Armadilha: análise automática.** A "Automatic Analysis" do SonarCloud não passa pelo build, então não enxerga o `Nullable` do `Directory.Build.props` e acusa a S8970 em todo operador `!`. Ela também ignora `sonar-project.properties`, e desativar uma regra exige perfil de qualidade personalizado, que é pago. Por isso a análise automática fica desligada no projeto (as duas não rodam juntas); ligá-la de novo traz a S8970 de volta e conflita com a análise do CI.

`Core` não conhece nenhuma engine nem nenhum provedor de IA. As engines e a IA implementam interfaces do `Core`.

## Componentes do Core

### 1. Abstração de engine

```csharp
public interface IEngine
{
    string Id { get; }                                  // "yuris"
    EngineDetection Detect(string gameDir);             // "parece YU-RIS 5.x, ysbin.ypf v500"
    IReadOnlyList<IArchiveFormat> Archives { get; }     // YPF
    IReadOnlyList<IScriptFormat> Scripts { get; }       // YSTB
    IReadOnlyList<IGamePatch> Patches { get; }          // ex.: patch de codepage no .exe
}

public sealed record EngineDetection(bool IsMatch, string Summary, IReadOnlyDictionary<string, string> Details);

public interface IArchiveFormat
{
    string Id { get; }                                  // "ypf"
    IReadOnlyList<string> FileExtensions { get; }       // ".ypf"
    bool CanOpen(Stream stream);                        // lê do início; a posição volta ao valor original
    IArchive Open(Stream stream);                       // lê do início; InvalidDataException se inválido ou com Path duplicado
}

public interface IArchive : IDisposable                 // não é seguro para chamadas concorrentes
{
    IReadOnlyList<ArchiveEntry> Entries { get; }        // Path único no pacote
    Stream OpenEntry(ArchiveEntry entry);               // MemoryStream independente, descomprimido, hash conferido (fail-fast);
                                                        // ArgumentException se a entrada é de outro pacote
    void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements);
        // output gravável e seekable; chave inexistente: ArgumentException antes de gravar;
        // troca igual ao conteúdo atual conta como não trocada (cópia crua); a troca mantém IsCompressed
}

public record ArchiveEntry(string Path, long Size, bool IsCompressed);   // aberta a herança: YpfEntry : ArchiveEntry

public interface IGamePatch
{
    string Id { get; }
    string Description { get; }
    IReadOnlyList<string> AffectedFiles { get; }        // GamePaths.Resolve valida; o fluxo de injeção faz o backup; pode não existir
    bool CanApply(string gameDir, out string? reason);  // verdadeiro também quando já aplicado
    void Apply(string gameDir);                         // idempotente
}

public static class GamePaths
{
    public static string Resolve(string gameDir, string relative);   // ArgumentException se vazio, enraizado, com ':' ou fora da pasta
}

public interface IScriptFormat
{
    string Id { get; }                                  // "ystb"
    IReadOnlyList<string> FileExtensions { get; }       // ".ybn"
    ScriptExtraction Extract(ScriptFile file, EngineContext ctx);          // bytes -> unidades de texto
    byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units,          // unidades -> bytes
                   ITextEncoder encoder, EngineContext ctx);
}

public sealed record ScriptFile(string Name, ReadOnlyMemory<byte> Content)   // Key e UnitId(n) montam os ids
public sealed record ScriptExtraction(IReadOnlyList<TextUnit> Units)         // + Warnings (init, nunca nulo)
public class EngineContext(string engineId)             // dados do jogo carregados pela engine (no YU-RIS, o YSCM); cada engine deriva
```

Uma engine nova (KiriKiri, Ren'Py, SiglusEngine, Artemis...) é só outro projeto `VNForge.Engines.X` que implementa essas interfaces.

Decisões dos contratos (E1.2, no namespace `VNForge.Core.Engines`):

- As coleções são `IReadOnlyList<T>` em vez de array e `IEnumerable`, para o chamador poder indexar e contar sem enumerar duas vezes.
- Os pacotes trabalham sobre `Stream` com busca, não sobre caminho: os testes usam `MemoryStream` sem tocar o disco, e a reescrita grava num fluxo de destino que o fluxo de injeção controla.
- Tudo é síncrono. Os arquivos do jogo são locais e o gargalo não está na leitura; o `async` fica para a IA, que faz chamadas de rede.
- O doc original só citava `IArchiveFormat`; ele passa a abrir um `IArchive` (índice, leitura de entrada, `WriteTo` com reescrita mínima), que é o que a regra 3 do `CLAUDE.md` exige do escritor.
- `IGamePatch` não faz backup: declara `AffectedFiles` e o fluxo de injeção faz o backup antes de `Apply` (regra 2 do `CLAUDE.md`). `GamePaths.Resolve` valida cada caminho (vazio, enraizado, `C:foo`, UNC, `:` e `..` são recusados; `\` vale como separador em qualquer plataforma).
- `WriteTo` trata uma troca com conteúdo idêntico ao atual como entrada não trocada, porque o zlib não reproduz os bytes comprimidos do original; sem isso, um `pack` que passa todos os arquivos quebraria a regra 3.
- `WriteTo` do YPF recusa o destino que é o fluxo de origem (o mesmo objeto, ou um `FileStream` no mesmo caminho) e valida chaves, conteúdo e destino antes de gravar o primeiro byte: o fluxo de injeção, com backup, é quem troca o original (regra 2 do `CLAUDE.md`). Troca num YPF sem algoritmo de hash confirmado é recusada com `NotSupportedException`; os detalhes estão no doc 06.
- `ArchiveEntry` não é selada: cada formato deriva dela (`YpfEntry`) para guardar os campos internos da reescrita. `Path` é único no pacote.
- `OpenEntry` falha na hora quando o hash diverge. A verificação é do formato: o algoritmo de hash por versão está como não testado no doc 06, com ambiguidade nas versões 477 e 478.
- `CanApply` devolve verdadeiro com o patch já aplicado, e `Apply` é idempotente.
- `IScriptFormat` e `IEngine.Scripts` entraram na E2.1 (`namespace VNForge.Core.Engines`). Além do que o doc original listava, `IScriptFormat` ganhou `Id` e `FileExtensions`, como `IArchiveFormat`, para a CLI escolher o formato pela extensão.
- `Extract` e `Rebuild` são puros: não gravam em disco, não guardam estado e não alteram `file` nem `units`. `Extract` devolve as unidades na ordem do arquivo, com status `untranslated`, tradução nula e `locator` preenchido. Formato inválido lança `InvalidDataException`, contexto de outra engine lança `ArgumentException`. O `locator` guarda o que o `Rebuild` precisa para reinjetar sem reinterpretar os comandos do script.
- Validação do `Rebuild`, antes de gravar: unidade cujo `id` não começa com `file.Key + ":"` lança `ArgumentException`; `id` duplicado lança `ArgumentException` (`ScriptFile.ValidateUnits` faz as duas conferências); unidade a reescrever confere que o trecho apontado pelo `locator` ainda decodifica para o `source`, senão `InvalidDataException` (arquivo desatualizado). Nada parcial é devolvido, e as mensagens citam o `id`.
- Regra de `Rebuild` por unidade: se `TextUnit.NeedsRewrite` é falso (sem tradução, status `untranslated` mesmo com `translation` preenchida, tradução vazia ou igual ao `source`), o trecho original é copiado cru, byte a byte, sem decodificar nem recodificar. Unidade fora da lista também é copiada crua, então `units` pode ser um subconjunto, em qualquer ordem. Com `NeedsRewrite` verdadeiro, a engine parte a tradução nos marcadores, codifica os trechos de texto com o `ITextEncoder` e concatena os bytes de controle. Se os bytes codificados forem iguais aos originais (por exemplo, o `fold` reduz a tradução ao mesmo texto), o argumento é copiado cru: a regra 3 do `CLAUDE.md` vale também aí. Sem nada a reescrever, o resultado é idêntico ao original.
- `id` é opaco: o `Rebuild` localiza o trecho pelo `locator`, e o `id` serve à validação e às mensagens. A ordem das unidades na lista (e no `text/*.json`) é a da extração, a do arquivo.
- `EngineContext` guarda o que a engine carrega do jogo uma vez (no YU-RIS, a tabela de opcodes do YSCM) e é imutável. O Core só conhece a base abstrata com `EngineId`; cada engine deriva e confere se o contexto é o dela. Quem o cria a partir dos arquivos do jogo fica para as tarefas de cada engine.
- `ITextEncoder` existe na E2.1 só com `Id`, `Encode(string)` e `FindUnencodable`, o mínimo que o `Rebuild` e o QA usam. O contrato: `Encode` nunca substitui nem descarta caractere e lança `EncoderFallbackException` para o que não representa (o fallback padrão do .NET para o CP932 faz `ação` virar `acao` em silêncio, medido); `FindUnencodable(string)` devolve os índices que `Encode` recusaria, sem lançar e sem efeito colateral, para a regra de QA. O codificador recebe só texto exibível, sem marcadores: a engine parte a tradução nos marcadores, codifica os trechos e concatena os bytes de controle, porque no `cp1252` o `ï` (0xEF) colide com o prefixo de controle do YU-RIS (doc 06). `Rebuild` propaga a `EncoderFallbackException` sem resultado parcial, com o `id` na mensagem. A E2.2 acrescenta a estratégia Shift-JIS.
- `TextUnit`, `TextUnitKind`, `TranslationStatus` e `TranslationOrigin` ficam em `VNForge.Core.Text`. `TextUnitJson` define o formato dos `text/*.json`.
- **Extração (E3.4, `VNForge.Core.Engines.IGameUnpacker`)**: a engine implementa a interface além de `IEngine` (o `IEngine` não mudou). `Unpack(gameDir, destino, ct)` descompacta os pacotes que carregam os scripts, uma subpasta por pacote com o caminho dele relativo ao jogo (`ysbin.ypf/`, `pac/ysbin.ypf/`), e só lê o jogo. `Open(pastaDescompactada)` não grava nada: acha os scripts (`UnpackedScript`: nome, caminho, `IScriptFormat`, nome legível), carrega o `EngineContext` e devolve avisos; é por ele que o `vnf build` reabre `.cache/` sem tocar o jogo. No YU-RIS, `YuRisEngine` o implementa: `YuRisContext` (derivado de `EngineContext`) guarda o `YscmFile` e o `YstlFile` (opcional), e `YstbScriptFormat` é o `IScriptFormat` de `Scripts`. O `Extract` dele lê o script e chama o `YstbTextExtractor`, convertendo erro de formato em `InvalidDataException`; o `Rebuild` valida os ids, lê o script de novo e entrega as unidades ao `YstbTextInjector` (E2.7), convertendo erro de formato em `InvalidDataException`.
- **`ProjectExtractor.Extract(projeto, engines, ct)` (Core, `VNForge.Core.Projects`)** faz a primeira extração: acha a engine do `vnforge.json`, recusa se `.cache/` está dentro do jogo, apaga `.cache/game/`, chama `Unpack` e `Open`, extrai cada script com o `IScriptFormat` e grava `text/<chave>.json` com `ProjectFolder.SaveText`. Só grava `text/` depois que todos os scripts foram lidos; falha ou cancelamento na gravação devolve os arquivos ao que eram. Devolve `ExtractionResult` (pacotes, scripts com quantidade de linhas, linhas por `TextUnitKind`, avisos, `Reextraction`).
- **Re-extração que preserva traduções (E3.5, `TextReextraction`)**: com `text/*.json` já existente, `Extract` lê tudo antes (arquivo inválido interrompe sem tocar em nada) e casa cada linha nova pelo `id`. Mesmo `source`: fica a linha salva (tradução, status, origem, notas, flags), com `kind`, `speaker` e `locator` da extração nova. `source` diferente: a linha sai com o original novo, a tradução antiga continua nela, o status volta para `untranslated` (o build a ignora e a regra de QA "tradução ignorada" a aponta) e `previousSource` guarda o original que ela traduzia; a marca é um campo, não um status novo, para não mexer na ordem de `TranslationStatus` nem nas contagens. A marca sai na próxima re-extração em que a linha já não está `untranslated`. Linha só no jogo novo entra sem tradução; linha ou script que sumiu do jogo sai de `text/`. Antes de trocar ou apagar um arquivo, o anterior vai para `.cache/text-anterior/<data-hora>/` (uma subpasta por re-extração, nunca apagada; sem mudança, nada é regravado e nada é criado). `ExtractionResult.Reextraction` conta linhas mantidas, desatualizadas, novas e removidas; é nulo na primeira extração.
- **Build (E8.1, `VNForge.Core.Engines.IGameRepacker`)**: a engine implementa a interface além de `IEngine` e `IGameUnpacker`. `Repack(gameDir, pastaDescompactada, rebuilt, destino, ct)` grava no destino, no mesmo caminho relativo ao jogo, cada pacote que o `Unpack` descompactou, com os scripts de `rebuilt` (`RebuiltScript`: caminho na pasta descompactada, conteúdo original e conteúdo novo) no lugar dos originais. O que não mudou sai byte a byte igual; pacote sem script alterado é copiado idêntico. Cada script a trocar confere antes que a entrada do pacote do jogo ainda é igual ao `Original` (o `vnf extract` descompactou aquele conteúdo); se o jogo mudou depois da extração, o build recusa e pede um novo `vnf extract`. No YU-RIS, `YuRisEngine` a implementa com `YuRisRepacker`, que percorre os mesmos `.ypf` do `YuRisUnpacker` e grava com `YpfArchive.WriteTo` direto no destino, sem montar pasta intermediária nem escrever no `.cache/`.
- **`ProjectBuilder.Build(projeto, engines, ct)` (Core, `VNForge.Core.Projects`)** faz o `vnf build`: acha a engine do `vnforge.json` (precisa ser `IGameUnpacker` e `IGameRepacker`), reabre `.cache/game/` com `Open`, lê `text/*.json` e reconstrói com `IScriptFormat.Rebuild` e `ShiftJisTextEncoder` só os scripts que têm alguma unidade com `NeedsRewrite`; script sem tradução não é reconstruído. Entrega os que mudaram ao `Repack`, que grava em `build.tmp/` (irmã de `build/`); só no fim move o `build/` anterior para `build.old/`, renomeia `build.tmp/` para `build/` e apaga o `build.old/` (se a troca falha, o anterior volta). Recusa um `build/` que fique dentro da pasta do jogo, como o `extract` faz com o `.cache/`. Erro, tradução inválida ou cancelamento removem o `build.tmp/` e deixam o `build/` anterior como estava. Todo pacote descompactado sai em `build/` com o caminho relativo ao jogo (`build/ysbin.ypf`, `build/pac/x.ypf`), que é o que o `GameInjector` espera; o pacote sem alteração sai idêntico e o `inject` o ignora por hash. Erros de reconstrução citam o script e o `id` da linha. O `ITextEncoder` vem do `encoding.strategy` do `vnforge.json` (E5.1, abaixo) e antes de reconstruir o build confere todas as traduções e recusa o conjunto inteiro, com todos os casos, se a estratégia não representa algum caractere. Esta versão não formata o texto; o perfil de formatação entra no M2. Devolve `BuildResult` (pacotes com a quantidade de scripts trocados, scripts reconstruídos com linhas, avisos). **Incremental (E8.4)**: com `onlyChanged` (o padrão do `vnf build`), `BuildState` (`.cache/build-state.json` e `.cache/build-scripts/`) guarda por script o hash do original, da estratégia e das unidades a gravar, mais os bytes reconstruídos; hash igual reaproveita os bytes sem chamar `Rebuild` (`BuildResult.ReusedScripts`), e sem nenhum script alterado e com `build/` intacto o `Repack` nem roda. `encodingStrategy` (`--encoding`) troca a estratégia só naquele build.
- **Escolha da estratégia e relatório de codificação (E5.1, `VNForge.Core.Text`)**: `TextEncoderRegistry` cria o `ITextEncoder` pelo id do `encoding.strategy` (`TextEncoderRegistry.Default`, uma linha por estratégia; hoje `sjis`, `fold` e `cp1252`; `remap` é aceito pelo `vnforge.json` mas ainda não tem encoder). Id conhecido sem encoder pronto (`remap`) dá `VNForgeException` em português, nunca Shift-JIS em silêncio; `Default` ganha uma linha por encoder novo, e `ProjectBuilder.Build` aceita outro registro nos testes. `IReplacingTextEncoder` (opcional) acrescenta `GetReplacement(caractere)` para a estratégia que troca caracteres: o `FoldTextEncoder` a implementa sobre o `CharacterFolder`, sem repetir a tabela de troca, e devolve o substituto (`«` vira `"`, `ª` vira `a`, `ã` vira `a`), ou `null` quando o caractere segue como está. O `cp1252` e o `sjis` não trocam nada: recusam o que não representam. `EncodingReporter.Create(texts, encoder)` percorre as unidades com `NeedsRewrite`, chama `FindUnencodable` e `GetReplacement` e devolve um `EncodingReport` com um `EncodingIssue` por ocorrência (`UnitId`, `ScriptKey`, `Position` em `char` UTF-16 a partir de 0, `Character`, `CodePoint` como `U+00AB`, `Replacement`). `Replacement` nulo é bloqueante: `ProjectBuilder.Build` lança `EncodingReportException` (que carrega o relatório) antes de reconstruir qualquer script, com todas as ocorrências de uma vez (até 20 na mensagem). Trocas com substituto não bloqueiam e vão em `BuildResult.Encoding`. `ProjectBuilder.ReportEncoding` gera só o relatório, sem abrir `.cache/`. O relatório olha a tradução inteira, marcadores incluídos; eles são ASCII e passam em qualquer estratégia.
- O teste `ArchitectureTests`, em `tests/VNForge.Core.Tests`, falha se o Core referenciar `VNForge.Engines.*` ou `VNForge.AI`.

### 2. Unidade de texto (`TextUnit`)

É o centro de tudo. Uma linha traduzível com o original, a tradução e onde ela mora no arquivo.

```jsonc
{
  "id": "yst00123:0042",          // estável entre extrações
  "kind": "dialogue",             // dialogue | narration | name | choice | system | other
  "speaker": "美咲",               // separado do texto quando a engine permite
  "source": "今日は、いい天気だね。\\p",
  "translation": "Hoje está um dia bonito, né?\\p",
  "status": "reviewed",           // untranslated | machine | translated | reviewed | approved
  "origin": { "provider": "anthropic", "model": "claude-opus-5-5", "at": "2026-10-17T18:00:00Z" },
  "notes": "",
  "flags": [],                    // avisos do QA que o revisor marcou como "ok"
  "previousSource": null,         // original anterior, quando a re-extração achou o original mudado (E3.5)
  "locator": { "command": 812, "args": [0] }   // dado opaco da engine para reinjeção
}
```

Decisões do formato (E2.1, `TextUnitJson` define as opções):

- Propriedades em camelCase, nesta ordem. `kind` e `status` são strings em minúsculas, com exatamente os valores acima; número ou valor desconhecido é recusado na leitura. O texto vai em Unicode sem escape (japonês legível em diff) e `origin.at` sempre em UTC com `Z`; na leitura, `at` sem `Z` nem deslocamento é recusado. `Provider` e `at` de `origin` são obrigatórios.
- Uma unidade por linha, como diz a árvore de pastas: o arquivo é um array JSON com um objeto compacto por linha (`[`, os objetos separados por vírgula e quebra de linha, `]`), bom para diff e merge. Na leitura, campo desconhecido é erro (`"traslation": "Oi"` não pode apagar a tradução no próximo salvamento), e comentários e vírgula final são aceitos, como no `vnforge.json`. O arquivo em disco (E3.2) põe esse array dentro de um envelope; `TextUnitJson` só cuida das unidades e `TextFileSerializer` do envelope.
- **Arquivo `text/<chave>.json`** (E3.2): `{ "version": 1, "units": [ ... ] }`, com dois espaços de indentação, uma unidade por linha, fim de linha `\n`, quebra de linha no final e UTF-8 sem BOM (a leitura aceita BOM e CRLF). O nome do arquivo é a chave do script (`ScriptFile.Key`, em minúsculas), e todo id do arquivo começa com `chave:` e não se repete. Gravar o mesmo conteúdo duas vezes gera arquivos idênticos, e ler e regravar não muda nada. Os marcadores ficam como texto: a barra invertida de `\n` e `\p` vira `\\` no JSON e volta igual, e todo texto fica literal em UTF-8: o `MinimalStringConverter` escapa só o que o JSON exige (aspas, barra invertida e U+0000 a U+001F), então o espaço ideográfico U+3000, o U+00A0, o U+2028, o japonês e o emoji saem como estão e o diff continua legível. Isso vem de um conversor de string (`MinimalStringConverter`), sem código `unsafe` no Core; as strings dentro do `locator` (JSON livre da engine) seguem o escape relaxado do .NET. Texto com surrogate solitário (UTF-16 inválido) não é gravado: vira `TextFileException` citando o id. `version` desconhecido, campo desconhecido, JSON malformado, id de outro script e dois arquivos com a mesma chave são `TextFileException`, em português, com o caminho do arquivo e, quando cabe, o campo e a linha e coluna. A gravação é atômica (arquivo temporário e troca). `ProjectFolder` expõe `TextPath`, `SaveText`, `LoadText`, `LoadAllText` e a estática `LoadTextFiles(paths)`, que lê arquivos por caminho e é onde a conferência de chave repetida acontece (pública para o teste cobrir o caso de maiúsculas e minúsculas num sistema de arquivos que não diferencia).
- `speaker`, `translation`, `origin` e `locator` são nulos quando não há valor e continuam no arquivo como `null`, para o formato ter sempre a mesma forma. `notes` ausente vale `""` e `flags` ausente vale lista vazia; `notes` ou `flags` com `null` e `flags` com elemento nulo são recusados, assim como `null` como elemento da lista de unidades. `provider` de `origin` é `"anthropic"` para a IA e `"manual"` para uma pessoa; `model` é nulo quando não há modelo.
- `id`, `kind`, `source` e `status` são obrigatórios na leitura (ausentes ou nulos geram `JsonException`). As opções são somente leitura.
- `status` anda na ordem `untranslated`, `machine`, `translated`, `reviewed`, `approved`.
- **`id`**: a engine o monta com `ScriptFile.UnitId(posição)`: chave do script (nome do arquivo sem pasta e sem extensão, em minúsculas) e uma posição estável e única no script, com ao menos seis dígitos (`yst00123:000042`). Só depende do conteúdo do script, então duas extrações do mesmo arquivo dão os mesmos ids. O id é opaco, ninguém o decodifica. Decisão sobre a posição: o Core aceita qualquer número estável e único; a ordem da unidade no arquivo desloca todas as seguintes se a heurística de extração mudar (por exemplo, juntar `WORD` de outro jeito), enquanto o índice da instrução do primeiro comando da unidade não se mexe, então a E2.6 usa o índice da instrução e, quando duas unidades saem da mesma instrução, desempata sem reordenar as outras: no YU-RIS a posição é `índice da instrução * 256 + índice do argumento`. Se uma versão nova do jogo inserir texto no meio, a E3.5 percebe porque compara o `source` salvo com o novo.
- **`locator`** é um `JsonElement` opcional: JSON livre, definido por cada engine. Escolhido contra string ou base64 porque o `text/*.json` fica legível em diff, o valor é imutável e a engine não precisa de tipo no Core. O Core nunca interpreta o conteúdo, só o guarda e o devolve ao `Rebuild`. Depois de uma ida e volta o JSON é equivalente, não necessariamente idêntico em espaços. No YU-RIS (E2.6, `YstbTextExtractor`) guarda a versão do formato do locator, o comando, e a lista `parts` com o índice da instrução, o do argumento e o `size` e o `offset` originais de cada argumento (para a reconstrução conferir o que vai trocar), mais o falante separado (`speaker`: `name`, `open`, `close`) quando há; o exemplo do doc 06 mostra o formato completo.
- **Tradução e reescrita**: `HasTranslation` é verdadeiro com status diferente de `untranslated` e `translation` não vazia. `NeedsRewrite` exige ainda `translation` diferente de `source` (comparação ordinal). Só nesse caso o `Rebuild` grava texto novo; nos demais copia o trecho original cru. A tradução vazia conta como não traduzida (a regra de QA de tradução vazia a sinaliza). Um rascunho em status `untranslated` com `translation` preenchida é ignorado no build; a regra de QA "tradução ignorada" avisa esse caso, para o texto não ficar parado sem ninguém notar.

- **Edição manual (`VNForge.Core.Projects.LineEditor`, E10.3)**: o app (e qualquer cliente) edita uma linha de `text/<chave>.json` por `SetTranslation`, `SetNotes` e `Approve`. Cada operação lê o script, troca a unidade pelo `id` e grava com `SaveText` (atômico); `id` desconhecido é `KeyNotFoundException`. Tradução nula, vazia ou só de espaços deixa a linha `untranslated`, sem tradução e sem origem. Texto novo grava `translated` com origem `manual` no horário do relógio injetado, mesmo que a linha estivesse `reviewed` ou `approved`: editar desfaz a revisão. Texto igual ao gravado (e notas iguais, e aprovar o que já está aprovado) não regrava o arquivo. `Approve` exige texto de tradução na linha (`InvalidOperationException` sem ele), grava `approved` e mantém a origem; um rascunho `untranslated` com texto passa a valer ao ser aprovado.

Códigos de controle do jogo viram marcadores legíveis e estáveis. A IA e o QA tratam esses marcadores como intocáveis. Gramática, igual para `source` e `translation`:

| Marcador | Significado |
|---|---|
| `\n` | quebra de linha |
| `\p` | nova página |
| `\c` | esperar clique |
| `\u` | código desconhecido (no YU-RIS, `EF F5`) |
| `{NOME:valor}` | variável ou código genérico que preserva os bytes (`{VAR:...}`) |
| `{EF:xx}` | no YU-RIS, código `EF xx` sem atalho (`xx` em hexadecimal) |
| `{RAW:hex}` | no YU-RIS, bytes crus que não viram texto (`EF` no fim dos bytes, byte inválido em Shift-JIS) |
| `\\` | uma barra invertida literal |
| `\{` | uma chave literal |

Qualquer outra barra seguida de caractere é inválida: a engine recusa na reconstrução, citando o `id`. Na extração, uma barra ou chave que já existe nos bytes do jogo sai escapada (`\\`, `\{`), e a reconstrução desfaz o escape, então a conversão é reversível byte a byte. No JSON as barras dobram de novo (`\p` vira `"\\p"`).

### 3. Formatação (espaçamento)

Pipeline configurável aplicado **na hora do build**, não no texto salvo. Assim dá para mudar a regra e reconstruir sem retraduzir.

| Etapa | Exemplos de regra | Configurável |
|---|---|---|
| Normalização | Unicode NFC, espaços invisíveis, NBSP → espaço | sim |
| Espaços | colapsar espaços duplos, tirar espaço antes de `? ! : ;`, garantir espaço depois de pontuação | cada regra liga/desliga |
| Tipografia | `“ ”` → `" "` ou `「」`; `...` ↔ `…`; travessão `—` → `―` (existe no Shift-JIS) | sim |
| Largura | ASCII meia-largura ↔ largura cheia (Ｈｏｊｅ) | sim |
| Quebra de linha | quebra por palavra respeitando largura máxima em colunas e máximo de linhas por caixa | largura, linhas, largura por caractere |
| Regras do usuário | lista de substituições literais ou regex, antes e depois | sim |

A quebra de linha (E4.4) é `WordWrapper.Wrap(texto, WrapOptions)` em `VNForge.Core.Formatting`, uma função pura sobre a tradução com marcadores. A largura máxima (`MaxWidth`), o limite de linhas por caixa (`MaxLines`) e a largura por caractere (`CharWidth`, um delegate injetável) vêm de `WrapOptions`; o padrão `CharWidths.HalfWidthLatin` conta letra latina como 1 coluna e japonês como 2. Marcadores (`\c`, `\u`, `{...}`) não ocupam coluna e nunca são partidos; `\\` e `\{` contam como o caractere que representam; os `\n` já presentes ficam onde estão; `\p` zera a contagem de linhas da caixa. Espaços na posição da quebra são descartados, e palavra maior que a linha é cortada no limite (um caractere mais largo que a linha ocupa uma linha sozinho, sem laço). `WrapResult` devolve o texto, as linhas de cada caixa (`LinesPerBox`) e `ExceedsLineLimit`/`OverflowingBoxes`, que o QA do E7 usa. Marcador inválido lança `ArgumentException`. O `vnf build` a liga pelo `wrap` do `formatting.json`.

As regras ficam num **perfil de formatação** (`formatting.json`) na raiz do projeto. O app mostra uma prévia da caixa de texto do jogo com o resultado.

O perfil (E4.1) é `FormattingProfile` em `VNForge.Core.Formatting`, lido por `FormattingProfileSerializer` (JSON com comentários e vírgula final aceitos). Todo campo é opcional e o que falta usa o padrão de `FormattingProfile.Default`; campo desconhecido ou valor inválido é erro que cita o caminho (`formatting.json inválido em $.wrap.maxWidth: ...`). Sem o arquivo, vale o padrão, que não liga nenhuma regra e deixa o texto como está. Campos de hoje:

| Campo | Padrão | Efeito |
|---|---|---|
| `kinds` | `["dialogue", "narration"]` | Papéis de linha que o pipeline formata; as outras saem como estão |
| `normalization.nfc` | `false` | Unicode NFC: letra e acento soltos viram o caractere composto |
| `normalization.invisibleSpaces` | `false` | Remove U+200B, U+200C, U+200D, U+2060 e U+FEFF (inclui ZWJ e ZWNJ, que ligam emojis e certas escritas) |
| `normalization.nbsp` | `false` | U+00A0 e U+202F viram espaço comum |
| `spacing.collapseSpaces` | `false` | Dois ou mais espaços viram um |
| `spacing.spaceBeforePunctuation` | `false` | Tira o espaço antes de `? ! : ;` |
| `spacing.spaceAfterPunctuation` | `false` | Põe espaço depois de `, ; ? !` colados a uma letra (só dentro de um trecho: a pontuação colada a um marcador fica como está), de `.` e `:` entre letra e maiúscula, e depois de `...` antes de letra; números (`1,5`, `10:30`) e endereços (`site.com`) ficam como estão |
| `wrap.enabled` | `false` | Liga a quebra de linha |
| `wrap.maxWidth` | `54` | Colunas por linha |
| `wrap.maxLines` | `3` (`null` não limita) | Linhas por caixa; acima disso o build avisa, não recusa |
| `wrap.charWidth` | `"halfWidthLatin"` (ou `"uniform"`) | Tabela de largura por caractere |
| `typography.quotes` | `"none"` (`"ascii"`, `"corner"`) | Aspas curvas: `“ ” ‘ ’` para `" '`, ou `“ ”` para `「 」` (a aspa simples fica, porque `’` também é apóstrofo) |
| `typography.ellipsis` | `"none"` (`"character"`, `"dots"`) | `...` para `…` ou `…` para `...` |
| `typography.dash` | `null` (um caractere, por exemplo `"―"`) | Troca `—` em qualquer posição (o `–` de intervalos fica); não aceita `\`, `{`, `}` nem controle; `―` (U+2015) existe em Shift-JIS, `—` não |
| `typography.dialogueDash` | `null` (um caractere) | Fala que abre com `—`, `–`, `―` ou `-` seguido de espaço sai com esse caractere. Começo de linha é o começo do texto e o que vem depois de `\n` ou `\p`; vence `dash` ali |
| `width.mode` | `"none"` (`"full"`, `"half"`) | ASCII `!` a `~` para largura cheia (`Hoje` vira `Ｈｏｊｅ`) ou de volta. `\`, `{` e `}` nunca mudam |
| `width.spaces` | `false` | Troca também o espaço (`U+0020` e `U+3000`); desligado, a quebra de linha continua achando as palavras |
| `replacements.before` | `[]` | Substituições do usuário que rodam antes das regras do perfil (E4.5) |
| `replacements.after` | `[]` | Substituições do usuário que rodam depois das regras do perfil |

Cada item de `replacements.before` e `replacements.after` é `{ "pattern": "...", "with": "...", "regex": false }`: `pattern` (obrigatório, não vazio) e `with` (obrigatório, pode ser `""`) são texto; `regex` (padrão `false`) trata o `pattern` como expressão regular do .NET, e então `with` aceita `$1` e `${nome}`. A lista roda na ordem escrita, e cada substituição vê o resultado da anterior. As etapas (`ReplacementStage`, ids `replacements-before` e `replacements-after`) herdam `TextRunStage`: só veem o texto entre marcadores, então nunca alteram `\n`, `\c` ou `{...}`, e um padrão não casa através de um marcador (as âncoras `^` e `$` valem para cada trecho entre marcadores, não para a linha inteira); uma substituição que escreva um marcador (por exemplo `with` igual a `\n`) é recusada pelo pipeline. Regex inválida ou padrão vazio é erro de perfil que cita a regra (`formatting.json inválido em $.replacements.before[2].pattern: expressão regular inválida (...)`), no `Parse` e no `Serialize`. Cada casamento tem teto de 1 s (`ReplacementStage.RegexTimeout`); passar disso para o build com erro citando a regra, em vez de travá-lo.

`FormattingPipeline` aplica, na hora do build, as etapas (`IFormattingStage`) pela ordem de `FormattingPhase`: regras do usuário antes, normalização, espaços, tipografia, largura, quebra de linha e regras do usuário depois. O `ProjectBuilder` passa pelo pipeline a tradução de cada linha que será gravada e cujo papel está em `kinds`, antes de conferir a codificação, então o relatório de codificação (`vnf build --report` também) vale para o texto já formatado. O `text/*.json` em disco não muda. Aviso de uma etapa (caixa acima do limite de linhas) vai para os avisos do build com o id da linha.

Os marcadores de controle ficam fora do alcance das regras. Depois de cada etapa o pipeline confere que os marcadores são os mesmos, na mesma ordem; só a etapa que declara `MayChangeLineBreaks` (a quebra) pode acrescentar `
`. Uma etapa que violar isso lança `FormattingException` e o build para citando a linha. Regra de texto comum herda `TextRunStage`, que entrega a `ApplyToRun` só os trechos entre marcadores. Para acrescentar uma regra: escreva a etapa, dê a ela a fase e registre-a em `FormattingPipeline.Create` a partir de uma nova propriedade do perfil. Um perfil de exemplo fica em `samples/formatting.json`.

### 4. Codificação para português

O texto do YU-RIS é Shift-JIS (CP932), que **não tem** á, ã, ç, é, õ etc. O Core define uma interface `ITextEncoder` com estratégias trocáveis por projeto:

| Estratégia | Como funciona | Qualidade | Risco |
|---|---|---|---|
| `fold` | Remove acentos (ação → acao) e troca por equivalentes os sinais que o Shift-JIS não tem (`«»` → `"`, `ª º` → `a o`, travessões → `-`, `¿ ¡` → `? !`); o que sobra fora do Shift-JIS é recusado e vai para o QA | Perde todos os acentos; funciona em qualquer jogo | Nenhum |
| `remap` | Troca cada letra acentuada por um caractere Shift-JIS pouco usado (ex.: katakana de meia largura) e uma fonte modificada desenha a letra certa | Boa | Precisa editar/embutir fonte |
| `cp1252` | Patch no executável para tratar o texto como Latin-1 (Windows-1252) + troca do charset da fonte | Ótima, acentos nativos | Patch binário no .exe, depende da versão |

`Cp1252TextEncoder` (`VNForge.Core.Text`, id `cp1252`) implementa o lado do Core da estratégia `cp1252`: grava em Windows-1252 com fallback de exceção, então nada é trocado ou descartado em silêncio. O byte `0xEF` (`ï`) colide com o prefixo dos códigos de controle, por isso `ï` é recusado: `FindUnencodable` o aponta e `Encode` lança `EncoderFallbackException`. O patch no executável não faz parte do encoder.

O QA avisa qualquer caractere que a estratégia escolhida não consegue representar, e o `vnf build --report` lista cada um com a linha, a posição e o substituto (E5.1). A estratégia `sjis` (Shift-JIS pura, sem tratar acentos) existe como base e para texto sem acentos; o padrão do `vnf init` é `fold` (ver "Em aberto"). Detalhes técnicos em [06-yuris-notas-tecnicas.md](06-yuris-notas-tecnicas.md).

### 5. QA (conferência)

Regras automáticas, cada uma com severidade (erro bloqueia o build, aviso não):

- linha não traduzida, tradução vazia
- tradução ignorada: `translation` preenchida com status `untranslated` (o build a ignora)
- marcador de controle diferente do original (`\p`, `\n`, variáveis)
- caractere não codificável na estratégia atual
- texto passa do limite de linhas da caixa depois da quebra
- espaço duplo, espaço antes de pontuação, aspas ou parênteses desbalanceados
- japonês esquecido na tradução
- termo do glossário ou nome de personagem traduzido diferente do definido (`glossario-diferente`, `personagem-diferente`: aviso quando o original contém o termo e a tradução não contém o alvo, sem diferenciar maiúsculas)
- tamanho em bytes acima do limite do campo (nomes, escolhas)
- inconsistência: mesmo original traduzido de jeitos diferentes (`traducao-inconsistente`, aviso entre scripts: a tradução mais usada é a referência e cada linha que foge dela é apontada)

**Motor (E7.1, `VNForge.Core.Qa`)**: `IQaRule` tem `Id` (minúsculas e hífens, estável: é o que aparece na saída e em `flags`), `Description` e `Check(QaContext)`, que devolve `QaIssue` (regra, gravidade, id da linha, chave do script, mensagem). A regra vê o projeto inteiro pelo `QaContext` (textos de todos os scripts, glossário, personagens, perfil de formatação e encoder), então serve à regra entre linhas (inconsistência) tanto quanto à regra por linha; para esta última, derive de `UnitQaRule` e devolva só as mensagens de `CheckUnit`. A regra devolve toda ocorrência: quem filtra e silencia é o `QaEngine`, que valida ids únicos, aplica `QaOptions` (`Rules`: só estas regras; `MinimumSeverity`: `Warning` lista tudo, `Error` só os erros) e devolve um `QaReport` (`Issues` ativas, `Silenced`, `RulesRun`). As regras concretas entram em `QaEngine.Default` (E7.2 a E7.5). **Regras de texto (E7.4, `TextQaRules`)**: `espaco-duplo` (espaços comuns, NBSP e ideográficos), `espaco-antes-pontuacao` (`, ; : ? !`; reticências ficam fora), `aspas-desbalanceadas` (aspas retas, curvas, `«»`, `「」`, `『』` e parênteses; cala quando o original do mesmo grupo já está desbalanceado, pois a fala pode abrir numa linha e fechar na outra) e `japones-na-traducao` (hiragana, katakana e kanji; pontuação japonesa não conta). Todas são aviso, ignoram linha sem tradução utilizável e só olham texto fora dos marcadores. Cada uma expõe `Find(tradução)`, que devolve `QaSpan(Start, Length, Text)` com o trecho exato no índice da tradução inteira, para o `--fix`.

**Marcar como ok**: `QaFlags.MarkOk(projeto, engine, idDaLinha, idDaRegra)` grava o id da regra em `flags` da linha no `text/<chave>.json` (idempotente, só regrava se mudou, recusa regra desconhecida e linha inexistente); `Unmark` tira. O `QaEngine` move para `Silenced` o **aviso** cuja regra está em `flags` da linha. **Erro não se silencia**: marcado ou não, continua bloqueando. A re-extração (E3.5) mantém `flags` quando o original não mudou, então a marca vale até o original mudar.

**No build**: `ProjectBuilder.Build` roda o QA antes de qualquer outra conferência e lança `QaBlockedException` (com o `QaReport` completo; a CLI a converte em saída `2`) se houver erro, ou aviso sem `allowWarnings`. Ou seja, por padrão o build para em erro **e** em aviso ainda ativo; `--allow-warnings` libera só os avisos, e o jeito de silenciar um aviso para sempre é marcá-lo como ok na linha. Nada é gravado em `build/` quando o QA bloqueia. Os avisos liberados e a contagem de silenciados vão em `BuildResult.Qa`. Pontos em aberto para as regras concretas: o QA confere todas as linhas do projeto, não só as que o build grava (decisão da E7.2: `nao-traduzida` é aviso, não erro, para um build parcial continuar possível com `--allow-warnings`), e a marca de ok não expira quando a tradução muda.

**Regras de completude (E7.2, `CompletenessQaRules`)**: `nao-traduzida` (aviso): status `untranslated`, original não vazio e `translation` em branco; um rascunho em `untranslated` com texto é da regra de tradução ignorada. `traducao-vazia` (erro): status fora de `untranslated` com `translation` nula, vazia ou só espaços. `marcador-diferente` (erro): compara os marcadores do original e da tradução como multiconjunto (a ordem é livre) e lista o que falta e o que sobra; marcador inválido na tradução também é erro. As quebras `
` ficam fora da comparação, porque o `WordWrapper` do build as refaz; linha sem tradução e original com marcador inválido não são conferidos.

Além das regras, um passo opcional de **revisão por IA** gera sugestões (não altera nada sozinho).

### 6. IA

```csharp
public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct);
}
```

`TranslationBatch` traz as linhas (`TextUnit`) e o contexto: `PreviousLines`, `Glossary`, `Characters` e `StyleGuide`. `TranslationResult` é o par `Id` e `Translation`. Os tipos ficam em `VNForge.AI.Translation`, junto do `FakeTranslationProvider`, o provedor determinístico dos testes e do app sem chave (traduz `X` como `[PT] X`, registra os lotes e pode falhar numa chamada escolhida).

- **Lotes** de 20–50 linhas por chamada, com contexto: linhas anteriores já traduzidas, personagens presentes, glossário filtrado para o lote, guia de estilo do projeto (registro, tratamento, honoríficos).
- **Entrada e saída em JSON** com `id` por linha. Resposta validada: mesmos ids, mesmos marcadores de controle. Se falhar, reenvia só as linhas com problema.
- **Provedor na primeira versão: só Claude**, pelo SDK oficial, com o modelo selecionável pelo usuário:

  | Modelo | Id | Uso sugerido | US$ por milhão de tokens (entrada / saída) |
  |---|---|---|---|
  | Claude Opus 5.5 | `claude-opus-5-5` | Padrão: tradução e revisão | 4 / 20 |
  | Claude Sonnet 5.5 | `claude-sonnet-5-5` | Rascunho mais barato e rápido | 2 / 10 |
  | Claude Haiku 5.5 | `claude-haiku-5-5` | Menus, nomes, textos curtos | 0,10 / 0,50 |
  | Claude Fable 5.1 | `claude-fable-5-1` | Cenas difíceis, revisão fina | 10 / 50 |

  A interface `ITranslationProvider` já nasce pensada para vários provedores, mas a tela de configuração só mostra os outros (OpenAI, Gemini, Ollama/LM Studio) como "em breve" até o épico E12.
- **Provedor Claude (E6.2)**: `ClaudeTranslationProvider` (`VNForge.AI/Claude`) monta o pedido e confere a resposta; o SDK fica atrás de `IClaudeMessageClient` (`AnthropicMessageClient` é a implementação real), então os testes usam um cliente falso. JSON fora do contrato vira `ClaudeResponseException`; o retry e o reenvio de linhas ruins ficam para os próximos itens do E6. **Recusa e cache (E6.3)**: para Opus e Fable o cliente real manda `fallbacks` (beta `server-side-fallback-2026-06-01`, alvo `claude-opus-4-8`), então o servidor refaz a recusa de classificador na mesma chamada; Sonnet e Haiku seguem sem fallback no servidor. Se a recusa persiste, o provedor refaz o lote linha a linha e devolve só as linhas recusadas com `TranslationResult.Refused = true` e tradução vazia, sem derrubar o lote. O prompt de sistema (estilo, glossário, personagens) leva `cache_control` e `ClaudeTranslationProvider.Usage` acumula os tokens, incluindo os lidos do cache (`CacheReadInputTokens`). O teste de integração real só roda com `ANTHROPIC_API_KEY` definida.
- **Detalhes da API do Claude a respeitar**: thinking adaptativo é o padrão e não pode ser desligado no Opus 5.5 (o controle é o `effort`; para tradução em lote, `low` ou `medium`); saída em JSON via `output_config.format`; tratar `stop_reason: "refusal"` (conteúdo adulto de VN pode disparar) com o fallback do servidor; usar prompt caching no prompt de sistema + glossário, que se repetem em todo lote.
- **Chaves de API** nunca ficam no projeto: variável de ambiente ou cofre do Windows (DPAPI) nas configurações do usuário. **E6.8**: `ApiKeyResolver` (`VNForge.AI/Settings`) usa `ANTHROPIC_API_KEY` primeiro e depois `apikey.dat` em `%APPDATA%/VNForge`, cifrado com DPAPI (`CurrentUser`); `vnf config ai [--model id] [--set-key] [--clear-key]` grava o modelo em `ai.json` e lê a chave da entrada padrão, nunca de argumento, e nunca a imprime.
- **Custo**: estimativa antes de rodar (`--dry-run`), contagem real de tokens depois, cache por hash da linha para não pagar duas vezes.

### 7. Pipeline

```
 detect ─► extract ─► translate ─► check ─► build ─► inject ─► (testar no jogo) ─► restore se precisar
              │            ▲          │
              └─ text/*.json ◄────────┘  (edição humana no app ou em qualquer editor)
```

Cada etapa é idempotente e grava o estado em `.cache/state.json`, então a CLI e o app podem continuar de onde o outro parou. O `vnforge.json` guarda só a configuração, descrita em "Projeto de tradução no disco"; o estado fica em outro arquivo porque muda a cada execução, e o `vnforge.json` é escrito à mão e vai para o git.

## Projeto de tradução no disco

Um projeto é uma pasta. Ela vai para o git; os arquivos do jogo não.

```
minha-vn-ptbr/
├─ vnforge.json            # jogo, engine, idiomas, estratégia de codificação, provedor de IA
├─ formatting.json         # perfil de espaçamento e quebra de linha
├─ glossary.json           # termos fixos
├─ characters.json         # nomes de personagens: original → tradução, gênero, tom de fala
├─ style.md                # guia de estilo livre, vai junto no prompt da IA
├─ text/                   # UMA unidade de texto por linha, um arquivo por script
│  ├─ yst00012.json
│  └─ ...
├─ .cache/                 # (gitignored) game/ (pacotes descompactados pelo vnf extract, um por subpasta: game/ysbin.ypf/...), cache de IA, logs (logs/), state.json (estado do pipeline)
├─ build/                  # (gitignored) arquivos reconstruídos (ysbin.ypf, .exe patchado)
└─ backup/                 # (gitignored) cópia dos originais antes da injeção
```

`vnforge.json` de exemplo:

```jsonc
{
  "name": "Minha VN — PT-BR",
  "game": { "path": "D:/Jogos/MinhaVN", "exe": "MinhaVN.exe" },
  "engine": { "id": "yuris", "version": 500, "scriptKey": "0xD36FAC96" },
  "languages": { "source": "ja", "target": "pt-BR" },
  "encoding": { "strategy": "fold" },
  "ai": { "provider": "anthropic", "model": "claude-opus-5-5", "effort": "medium", "batchSize": 30 }
}
```

O Core (`VNForge.Core.Projects`) lê e valida o arquivo. Comentários e vírgula final são aceitos. `name`, `game`, `engine`, `languages` e `encoding` são obrigatórios; `engine.version`, `engine.scriptKey` e o bloco `ai` são opcionais (o padrão de `ai` é o do exemplo). Campo desconhecido é erro, para pegar erro de digitação. Todo erro de validação cita o caminho do campo e os valores aceitos, sem repetir o valor recebido (ele pode ser um segredo colado no campo errado), por exemplo `vnforge.json inválido em $.encoding.strategy: valor não aceito; use um destes: fold, remap, cp1252.` Chave de API no arquivo é recusada (ver "Chaves de API" na seção de IA). Salvar o `vnforge.json` pelo Core regrava o arquivo inteiro e descarta os comentários. O `Create` grava um `.gitignore` na pasta do projeto com `.cache/`, `build/` e `backup/`, para um commit em massa não levar os originais do jogo para o repositório. Apagar `.cache/` zera o estado do pipeline (`state.json`), e as etapas rodam de novo.

### Glossário e personagens (`VNForge.Core.Glossary`)

`glossary.json` e `characters.json` usam o mesmo envelope: `{ "version": 1, "entries": [...] }`, uma entrada por linha, UTF-8 sem BOM e LF, como o `text/*.json`. Arquivo ausente vale lista vazia; campo desconhecido, campo obrigatório ausente, versão desconhecida, texto original vazio ou repetido (comparação ordinal) são erros (`GlossaryFileException`).

| Arquivo | Entrada | Campos |
|---|---|---|
| `glossary.json` | `GlossaryTerm` | `source`, `target`, `notes` (opcional) |
| `characters.json` | `CharacterEntry` | `source`, `target`, `gender` (`unknown`, `female`, `male`, `other`; padrão `unknown`), `tone` (opcional) |

`CharacterEntry.Source` é o nome exatamente como a engine o extrai em `TextUnit.Speaker`. `CharacterBook.Find(speaker)` acha a entrada por comparação ordinal, ignorando espaços nas pontas; falante nulo, vazio ou desconhecido (por exemplo `？？？`, quando não há entrada) devolve `null`. `ProjectFolder` lê e grava os dois arquivos (`LoadGlossary`, `SaveGlossary`, `LoadCharacters`, `SaveCharacters`). Os comandos `vnf glossary` e `vnf chars` ficam na E9.1.

### Estado do pipeline (`.cache/state.json`)

`ProjectFolder.LoadState` e `SaveState` leem e gravam o `state.json` (`PipelineState`): `originals` (caminho relativo ao jogo com `/`, tamanho e SHA-256 de cada pacote que a extração leu) e `stages` (o estado de cada etapa; hoje só `extract: completed`). O arquivo não leva data nem outro valor que mude a cada execução, as listas saem ordenadas e `SaveState` não toca no disco quando o conteúdo já é o mesmo, então repetir uma etapa sem mudança deixa o arquivo idêntico. O `vnf extract` grava o estado depois do texto; extração que falha não deixa `extract: completed`. Uma nova extração substitui os hashes e zera as etapas seguintes. Arquivo malformado ou de outra versão é erro que manda apagar o `.cache/`. A E8.5 compara os hashes de `originals` com o jogo antes de injetar. O executável do jogo ainda não entra na lista.

### Injeção e backup (`VNForge.Core.Injection`)

`GameInjector` injeta o `build/` na pasta do jogo (`game.path`, relativo à pasta do projeto quando não é absoluto) e devolve os originais. `Plan` compara os hashes e diz o que seria gravado, sem escrever. `Inject` copia os originais que serão trocados para `backup/AAAA-MM-DD_HHmm/`, confere o SHA-256 das cópias, grava `vnforge-backup.json` (`BackupManifest`: caminho, se o arquivo existia, hash original e hash injetado) e só então escreve no jogo, conferindo o hash de cada arquivo. A gravação passa por `IInjectionFileSystem` (cópia atômica por temporário e `File.Move`), que os testes trocam para simular uma falha no meio. Falha na gravação desfaz o que foi trocado; se o desfazer também falha, o manifesto já gravado permite `vnf restore`. `PlanRestore` e `Restore` usam o backup mais recente que tenha manifesto.

## Alternativas consideradas para a interface gráfica

| Opção | Por que não (por enquanto) |
|---|---|
| WPF | Só Windows, e o app precisaria de um job Windows só para compilar. Tem `DataGrid` pronto, mas a grade do VNForge é somente leitura, de altura fixa, e um `ListBox` virtualizado a cobre |
| WinUI 3 | Mais moderno visualmente, mas empacotamento e DataGrid dão mais trabalho que WPF |
| MAUI | Pensado para mobile; desktop Windows é cidadão de segunda classe |

A escolha foi Avalonia (D3, #96). A lógica fica no Core e nos ViewModels, então uma troca futura de interface continua barata.

## Em aberto

- [x] WPF ou Avalonia: **Avalonia 12** sobre .NET 10, tema `FluentTheme`, CommunityToolkit.Mvvm (#96, 2026-10-24).
- [x] Provedor de IA: Claude primeiro, modelo selecionável; outros provedores depois (E12).
- [x] Estratégia de codificação padrão: `fold` (#63, 2026-10-23). O jogo lê Shift-JIS, e `cp1252` só funciona com o patch do executável (#65), ainda não validado. Projetos passam para `cp1252` à mão depois disso.
- [x] Formatos de exportação para revisores: CSV e XLSX; `.po` fica de fora (D1, #51, 2026-10-24). O XLSX usa o `DocumentFormat.OpenXml` 3.5.1 (MIT, mantido pela Microsoft), a mesma base do ClosedXML, sem a camada de planilha. O ClosedXML foi descartado porque depende do `SixLabors.Fonts`, cuja versão 2.x tem licença dividida (paga para uso comercial); o EPPlus é comercial. Fontes: [NuGet do ClosedXML 0.105.1](https://www.nuget.org/packages/closedxml), [discussão ClosedXML#2665](https://github.com/ClosedXML/ClosedXML/discussions/2665) e [licença do Open XML SDK](https://github.com/dotnet/Open-XML-SDK/blob/main/LICENSE). O formato novo implementa `IReviewFormat` e entra em `ReviewFormats.All` (E3.10, #52).
