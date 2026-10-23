# 02 — Arquitetura inicial

## Stack

| Item | Escolha | Motivo |
|---|---|---|
| Runtime | **.NET 10** (SDK 10.0.x, LTS) | Versão mais recente, suporte longo |
| Linguagem | C# 14 | Vem com o .NET 10 |
| CLI | `System.CommandLine` 2.x | Parser oficial da Microsoft, estável desde o .NET 10 |
| App Windows | **WPF** (`net10.0-windows`) + CommunityToolkit.Mvvm | Maduro, tema Fluent nativo desde o .NET 9, DataGrid pronto para milhares de linhas. Ver alternativas no fim |
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
├─ VNForge.NoApp.slnf             # filtro de solução sem o app: compila e testa no Linux
├─ global.json                    # SDK 10.0.x e runner de testes (Microsoft.Testing.Platform)
├─ Directory.Build.props          # net10.0, nullable, warnings as errors, versão
├─ Directory.Packages.props       # versões centralizadas de pacotes
├─ src/
│  ├─ VNForge.Core/               # modelo do projeto, pipeline, formatação, codificação, QA
│  ├─ VNForge.Engines.YuRis/      # YPF, YSTB, YSCM, YSTL, patch do executável
│  ├─ VNForge.AI/                 # provedores de IA, prompts, orquestração em lote
│  ├─ VNForge.Cli/                # executável "vnf"
│  └─ VNForge.App/                # WPF (só compila/roda no Windows)
├─ tests/
│  ├─ VNForge.Core.Tests/
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

Os testes usam xUnit v3 (`xunit.v3.mtp-v2`) sobre o Microsoft.Testing.Platform. No SDK 10, o `dotnet test` precisa de `global.json` com `test.runner = Microsoft.Testing.Platform`, e a solução passa por `--solution` (`dotnet test --solution VNForge.NoApp.slnf`; sem argumento, ele acha a solução da pasta).

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
- **`ProjectExtractor.Extract(projeto, engines, ct)` (Core, `VNForge.Core.Projects`)** faz a primeira extração: acha a engine do `vnforge.json`, recusa se `text/` já tem `*.json` (a re-extração é a E3.5) ou se `.cache/` está dentro do jogo, apaga `.cache/game/`, chama `Unpack` e `Open`, extrai cada script com o `IScriptFormat` e grava `text/<chave>.json` com `ProjectFolder.SaveText`. Só grava `text/` depois que todos os scripts foram lidos; falha ou cancelamento na gravação remove o que já saiu. Devolve `ExtractionResult` (pacotes, scripts com quantidade de linhas, linhas por `TextUnitKind`, avisos).
- **Build (E8.1, `VNForge.Core.Engines.IGameRepacker`)**: a engine implementa a interface além de `IEngine` e `IGameUnpacker`. `Repack(gameDir, pastaDescompactada, rebuilt, destino, ct)` grava no destino, no mesmo caminho relativo ao jogo, cada pacote que o `Unpack` descompactou, com os scripts de `rebuilt` (`RebuiltScript`: caminho na pasta descompactada, conteúdo original e conteúdo novo) no lugar dos originais. O que não mudou sai byte a byte igual; pacote sem script alterado é copiado idêntico. Cada script a trocar confere antes que a entrada do pacote do jogo ainda é igual ao `Original` (o `vnf extract` descompactou aquele conteúdo); se o jogo mudou depois da extração, o build recusa e pede um novo `vnf extract`. No YU-RIS, `YuRisEngine` a implementa com `YuRisRepacker`, que percorre os mesmos `.ypf` do `YuRisUnpacker` e grava com `YpfArchive.WriteTo` direto no destino, sem montar pasta intermediária nem escrever no `.cache/`.
- **`ProjectBuilder.Build(projeto, engines, ct)` (Core, `VNForge.Core.Projects`)** faz o `vnf build`: acha a engine do `vnforge.json` (precisa ser `IGameUnpacker` e `IGameRepacker`), reabre `.cache/game/` com `Open`, lê `text/*.json` e reconstrói com `IScriptFormat.Rebuild` e `ShiftJisTextEncoder` só os scripts que têm alguma unidade com `NeedsRewrite`; script sem tradução não é reconstruído. Entrega os que mudaram ao `Repack`, que grava em `build.tmp/` (irmã de `build/`); só no fim move o `build/` anterior para `build.old/`, renomeia `build.tmp/` para `build/` e apaga o `build.old/` (se a troca falha, o anterior volta). Recusa um `build/` que fique dentro da pasta do jogo, como o `extract` faz com o `.cache/`. Erro, tradução inválida ou cancelamento removem o `build.tmp/` e deixam o `build/` anterior como estava. Todo pacote descompactado sai em `build/` com o caminho relativo ao jogo (`build/ysbin.ypf`, `build/pac/x.ypf`), que é o que o `GameInjector` espera; o pacote sem alteração sai idêntico e o `inject` o ignora por hash. Erros de reconstrução citam o script e o `id` da linha. O `ITextEncoder` vem do `encoding.strategy` do `vnforge.json` (E5.1, abaixo) e antes de reconstruir o build confere todas as traduções e recusa o conjunto inteiro, com todos os casos, se a estratégia não representa algum caractere. Esta versão não formata o texto; o perfil de formatação entra no M2. Devolve `BuildResult` (pacotes com a quantidade de scripts trocados, scripts reconstruídos com linhas, avisos).
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
| `wrap.enabled` | `false` | Liga a quebra de linha |
| `wrap.maxWidth` | `54` | Colunas por linha |
| `wrap.maxLines` | `3` (`null` não limita) | Linhas por caixa; acima disso o build avisa, não recusa |
| `wrap.charWidth` | `"halfWidthLatin"` (ou `"uniform"`) | Tabela de largura por caractere |

`FormattingPipeline` aplica, na hora do build, as etapas (`IFormattingStage`) pela ordem de `FormattingPhase`: regras do usuário antes, normalização, espaços, tipografia, largura, quebra de linha e regras do usuário depois. O `ProjectBuilder` passa pelo pipeline a tradução de cada linha que será gravada e cujo papel está em `kinds`, antes de conferir a codificação, então o relatório de codificação (`vnf build --report` também) vale para o texto já formatado. O `text/*.json` em disco não muda. Aviso de uma etapa (caixa acima do limite de linhas) vai para os avisos do build com o id da linha.

Os marcadores de controle ficam fora do alcance das regras. Depois de cada etapa o pipeline confere que os marcadores são os mesmos, na mesma ordem; só a etapa que declara `MayChangeLineBreaks` (a quebra) pode acrescentar `
`. Uma etapa que violar isso lança `FormattingException` e o build para citando a linha. Regra de texto comum herda `TextRunStage`, que entrega a `ApplyToRun` só os trechos entre marcadores. Para acrescentar uma regra (E4.2, E4.3, E4.5): escreva a etapa, dê a ela a fase e registre-a em `FormattingPipeline.Create` a partir de uma nova propriedade do perfil. Um perfil de exemplo fica em `samples/formatting.json`.

### 4. Codificação para português

O texto do YU-RIS é Shift-JIS (CP932), que **não tem** á, ã, ç, é, õ etc. O Core define uma interface `ITextEncoder` com estratégias trocáveis por projeto:

| Estratégia | Como funciona | Qualidade | Risco |
|---|---|---|---|
| `fold` | Remove acentos (ação → acao) e troca por equivalentes os sinais que o Shift-JIS não tem (`«»` → `"`, `ª º` → `a o`, travessões → `-`, `¿ ¡` → `? !`); o que sobra fora do Shift-JIS é recusado e vai para o QA | Perde todos os acentos; funciona em qualquer jogo | Nenhum |
| `remap` | Troca cada letra acentuada por um caractere Shift-JIS pouco usado (ex.: katakana de meia largura) e uma fonte modificada desenha a letra certa | Boa | Precisa editar/embutir fonte |
| `cp1252` | Patch no executável para tratar o texto como Latin-1 (Windows-1252) + troca do charset da fonte | Ótima, acentos nativos | Patch binário no .exe, depende da versão |

`Cp1252TextEncoder` (`VNForge.Core.Text`, id `cp1252`) implementa o lado do Core da estratégia `cp1252`: grava em Windows-1252 com fallback de exceção, então nada é trocado ou descartado em silêncio. O byte `0xEF` (`ï`) colide com o prefixo dos códigos de controle, por isso `ï` é recusado: `FindUnencodable` o aponta e `Encode` lança `EncoderFallbackException`. O patch no executável não faz parte do encoder.

O QA avisa qualquer caractere que a estratégia escolhida não consegue representar, e o `vnf build --report` lista cada um com a linha, a posição e o substituto (E5.1). A estratégia `sjis` (Shift-JIS pura, sem tratar acentos) existe como base e para texto sem acentos; o padrão do `vnf init` continua `cp1252`. Detalhes técnicos em [06-yuris-notas-tecnicas.md](06-yuris-notas-tecnicas.md).

### 5. QA (conferência)

Regras automáticas, cada uma com severidade (erro bloqueia o build, aviso não):

- linha não traduzida, tradução vazia
- tradução ignorada: `translation` preenchida com status `untranslated` (o build a ignora)
- marcador de controle diferente do original (`\p`, `\n`, variáveis)
- caractere não codificável na estratégia atual
- texto passa do limite de linhas da caixa depois da quebra
- espaço duplo, espaço antes de pontuação, aspas ou parênteses desbalanceados
- japonês esquecido na tradução
- termo do glossário ou nome de personagem traduzido diferente do definido
- tamanho em bytes acima do limite do campo (nomes, escolhas)
- inconsistência: mesmo original traduzido de jeitos diferentes

Além das regras, um passo opcional de **revisão por IA** gera sugestões (não altera nada sozinho).

### 6. IA

```csharp
public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct);
}
```

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
- **Detalhes da API do Claude a respeitar**: thinking adaptativo é o padrão e não pode ser desligado no Opus 5.5 (o controle é o `effort`; para tradução em lote, `low` ou `medium`); saída em JSON via `output_config.format`; tratar `stop_reason: "refusal"` (conteúdo adulto de VN pode disparar) com o fallback do servidor; usar prompt caching no prompt de sistema + glossário, que se repetem em todo lote.
- **Chaves de API** nunca ficam no projeto: variável de ambiente ou cofre do Windows (DPAPI) nas configurações do usuário.
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
  "encoding": { "strategy": "cp1252" },
  "ai": { "provider": "anthropic", "model": "claude-opus-5-5", "effort": "medium", "batchSize": 30 }
}
```

O Core (`VNForge.Core.Projects`) lê e valida o arquivo. Comentários e vírgula final são aceitos. `name`, `game`, `engine`, `languages` e `encoding` são obrigatórios; `engine.version`, `engine.scriptKey` e o bloco `ai` são opcionais (o padrão de `ai` é o do exemplo). Campo desconhecido é erro, para pegar erro de digitação. Todo erro de validação cita o caminho do campo e os valores aceitos, sem repetir o valor recebido (ele pode ser um segredo colado no campo errado), por exemplo `vnforge.json inválido em $.encoding.strategy: valor não aceito; use um destes: fold, remap, cp1252.` Chave de API no arquivo é recusada (ver "Chaves de API" na seção de IA). Salvar o `vnforge.json` pelo Core regrava o arquivo inteiro e descarta os comentários. O `Create` grava um `.gitignore` na pasta do projeto com `.cache/`, `build/` e `backup/`, para um commit em massa não levar os originais do jogo para o repositório. Apagar `.cache/` zera o estado do pipeline (`state.json`), e as etapas rodam de novo.

### Injeção e backup (`VNForge.Core.Injection`)

`GameInjector` injeta o `build/` na pasta do jogo (`game.path`, relativo à pasta do projeto quando não é absoluto) e devolve os originais. `Plan` compara os hashes e diz o que seria gravado, sem escrever. `Inject` copia os originais que serão trocados para `backup/AAAA-MM-DD_HHmm/`, confere o SHA-256 das cópias, grava `vnforge-backup.json` (`BackupManifest`: caminho, se o arquivo existia, hash original e hash injetado) e só então escreve no jogo, conferindo o hash de cada arquivo. A gravação passa por `IInjectionFileSystem` (cópia atômica por temporário e `File.Move`), que os testes trocam para simular uma falha no meio. Falha na gravação desfaz o que foi trocado; se o desfazer também falha, o manifesto já gravado permite `vnf restore`. `PlanRestore` e `Restore` usam o backup mais recente que tenha manifesto.

## Alternativas consideradas para a interface gráfica

| Opção | Por que não (por enquanto) |
|---|---|
| WinUI 3 | Mais moderno visualmente, mas empacotamento e DataGrid ainda dão mais trabalho que WPF |
| Avalonia | Rodaria em Linux/macOS também. Boa opção se quisermos multiplataforma; a troca custa pouco se o ViewModel ficar separado |
| MAUI | Pensado para mobile; desktop Windows é cidadão de segunda classe |

Como a lógica fica toda no Core e nos ViewModels, trocar WPF por Avalonia depois é viável.

## Em aberto

- [ ] WPF (só Windows) ou Avalonia (multiplataforma)?
- [x] Provedor de IA: Claude primeiro, modelo selecionável; outros provedores depois (E12).
- [ ] Estratégia de codificação padrão: começar com `fold` (garantido) e evoluir para `cp1252`?
- [ ] Formato de exportação extra para revisores (planilha .xlsx? .po?).
