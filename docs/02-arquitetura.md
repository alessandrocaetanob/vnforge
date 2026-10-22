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
    // IReadOnlyList<IScriptFormat> Scripts { get; }    // YSTB: entra na E2.1
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
    ScriptExtraction Extract(ScriptFile file, EngineContext ctx);          // bytes -> unidades de texto
    byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units,          // unidades -> bytes
                   ITextEncoder encoder, EngineContext ctx);
}
```

Uma engine nova (KiriKiri, Ren'Py, SiglusEngine, Artemis...) é só outro projeto `VNForge.Engines.X` que implementa essas interfaces.

Decisões dos contratos (E1.2, no namespace `VNForge.Core.Engines`):

- As coleções são `IReadOnlyList<T>` em vez de array e `IEnumerable`, para o chamador poder indexar e contar sem enumerar duas vezes.
- Os pacotes trabalham sobre `Stream` com busca, não sobre caminho: os testes usam `MemoryStream` sem tocar o disco, e a reescrita grava num fluxo de destino que o fluxo de injeção controla.
- Tudo é síncrono. Os arquivos do jogo são locais e o gargalo não está na leitura; o `async` fica para a IA, que faz chamadas de rede.
- O doc original só citava `IArchiveFormat`; ele passa a abrir um `IArchive` (índice, leitura de entrada, `WriteTo` com reescrita mínima), que é o que a regra 3 do `CLAUDE.md` exige do escritor.
- `IGamePatch` não faz backup: declara `AffectedFiles` e o fluxo de injeção faz o backup antes de `Apply` (regra 2 do `CLAUDE.md`). `GamePaths.Resolve` valida cada caminho (vazio, enraizado, `C:foo`, UNC, `:` e `..` são recusados; `\` vale como separador em qualquer plataforma).
- `WriteTo` trata uma troca com conteúdo idêntico ao atual como entrada não trocada, porque o zlib não reproduz os bytes comprimidos do original; sem isso, um `pack` que passa todos os arquivos quebraria a regra 3.
- `ArchiveEntry` não é selada: cada formato deriva dela (`YpfEntry`) para guardar os campos internos da reescrita. `Path` é único no pacote.
- `OpenEntry` falha na hora quando o hash diverge. A verificação é do formato: o algoritmo de hash por versão está como não testado no doc 06, com ambiguidade nas versões 477 e 478.
- `CanApply` devolve verdadeiro com o patch já aplicado, e `Apply` é idempotente.
- `IScriptFormat` e `Scripts` entram na E2.1; até lá `IEngine` não tem `Scripts`.
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

Códigos de controle do jogo viram marcadores legíveis e estáveis: `\n` (quebra de linha), `\p` (nova página), `\c` (esperar clique), `{VAR:...}` (variáveis). A IA e o QA tratam esses marcadores como intocáveis.

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

As regras ficam num **perfil de formatação** (`formatting.json`) dentro do projeto. O app mostra uma prévia da caixa de texto do jogo com o resultado.

### 4. Codificação para português

O texto do YU-RIS é Shift-JIS (CP932), que **não tem** á, ã, ç, é, õ etc. O Core define uma interface `ITextEncoder` com estratégias trocáveis por projeto:

| Estratégia | Como funciona | Qualidade | Risco |
|---|---|---|---|
| `fold` | Remove acentos (ação → acao) | Ruim, mas sempre funciona | Nenhum |
| `remap` | Troca cada letra acentuada por um caractere Shift-JIS pouco usado (ex.: katakana de meia largura) e uma fonte modificada desenha a letra certa | Boa | Precisa editar/embutir fonte |
| `cp1252` | Patch no executável para tratar o texto como Latin-1 (Windows-1252) + troca do charset da fonte | Ótima, acentos nativos | Patch binário no .exe, depende da versão |

O QA avisa qualquer caractere que a estratégia escolhida não consegue representar. Detalhes técnicos em [06-yuris-notas-tecnicas.md](06-yuris-notas-tecnicas.md).

### 5. QA (conferência)

Regras automáticas, cada uma com severidade (erro bloqueia o build, aviso não):

- linha não traduzida, tradução vazia
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

Cada etapa é idempotente e grava o estado em `project.json`, então a CLI e o app podem continuar de onde o outro parou.

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
├─ .cache/                 # (gitignored) arquivos extraídos do jogo, cache de IA, logs (logs/)
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
