# 03 — CLI (`vnf`)

## Como funciona

- Um executável, `vnf`, com subcomandos por etapa do pipeline.
- Todo comando roda dentro de uma pasta de projeto (a atual ou `--project <pasta>`).
- Saída legível por padrão; `--json` em qualquer comando para automação e para o app consumir.
- Códigos de saída: `0` ok, `1` erro, `2` QA encontrou erros bloqueantes, `3` cancelado.
- Nada destrutivo sem confirmação; `--yes` pula a confirmação em scripts. Gravar na pasta do jogo (`inject`, `restore`) e gravar por cima de algo (`ypf unpack` em pasta com arquivos, `ypf pack` sobre um arquivo que já existe) perguntam `[s/N]`. A resposta vem da entrada padrão; recusar sai com `3` e não grava nada. Sem `--yes` e sem como perguntar (`--json` ou entrada encerrada) o comando sai com `1` e pede o `--yes`.
- `--verbose` / `--quiet` controlam o log; o log completo vai para `.cache/logs/`.
- Opções globais, válidas em qualquer posição da linha: `--project <pasta>`, `--json`, `--verbose`, `--quiet`, `--yes`, `--no-color`. `--verbose` e `--quiet` não combinam. `--version` existe só na raiz; `--help` mostra a ajuda em português em qualquer comando.
- Ctrl+C cancela o comando em andamento e sai com `3`. Um segundo Ctrl+C encerra o processo na hora, também com `3`, para o caso de um comando que não respeita o cancelamento.
- Só conta como cancelamento a exceção de cancelamento com o token do Ctrl+C ativo. Um timeout de rede (`HttpClient`) é erro e sai com `1`.
- Erro de uso (opção desconhecida, opção obrigatória ausente, valor inválido, `--project` apontando para pasta que não existe) sai com `1` e em português. Em texto, a segunda linha traz a dica (veja "Erros e dicas").
- `--project` sem valor consome a opção seguinte como se fosse a pasta: `vnf --project --json` procura uma pasta chamada `--json` e sai com `1`.

### Onde vai o log

- A raiz é a pasta de `--project` ou, sem a opção, a pasta atual. Se a raiz tem `vnforge.json`, o log vai para `<raiz>\.cache\logs\`.
- Sem `vnforge.json` na raiz (inclusive com `--project` apontando para uma pasta qualquer): `%LOCALAPPDATA%\VNForge\logs\`, ou a pasta temporária se `LocalApplicationData` vier vazio ou relativo. A CLI nunca cria `.cache` em pasta qualquer, que pode ser a pasta do jogo. A busca de `vnforge.json` olha só a raiz, não sobe diretórios.

- Se o log não puder ser gravado (pasta sem permissão, disco cheio), a CLI segue sem ele. Erro previsto sai igual. Erro inesperado sai então completo no erro padrão, com o stack, em vez de apontar o arquivo de log; com `--json`, `error.detail` traz o tipo e a mensagem da exceção e não há `logDirectory`.
- O log guarda o que não cabe na tela: o stack do erro inesperado, o aviso do erro previsto (nível Warning, que o `--quiet` mantém) e o texto original em inglês de uma mensagem de uso que o `vnf` não sabe traduzir.

### Erros e dicas

Um erro previsto sai no erro padrão como `Erro: <mensagem>` e, quando há o que fazer, uma segunda linha `→ <dica>`:

```
Erro: A pasta do projeto não existe: C:\Projetos\minha-vn
→ Confira o caminho de --project <pasta> ou crie o projeto com 'vnf init <pasta> --game <pasta>'.
```

A dica aparece só no texto: o `--json` continua com `error.kind` e `error.message`, sem a linha da dica. Têm dica o erro de uso (aponta `vnf <comando> --help`), a pasta do projeto que não existe, a pasta que não é projeto (sem `vnforge.json`), a pasta do jogo que não existe no `detect` e o arquivo YPF ou YBN inválido. Erro inesperado e cancelamento não têm. Mensagem de uso que o `vnf` não reconhece sai como `Uso inválido.` e o original em inglês vai só para o log.

### Formato do `--json`

Uma linha de JSON por documento, sempre em stdout (nada vai para stderr). `exitCode` repete o código de saída do processo. Os erros saem no formato abaixo por conta da CLI; cada comando escreve o próprio resultado com `JsonEnvelope.Result` (Core), inclusive quando termina com `2`.

```json
{"ok":true,"exitCode":0,"data":{"name":"vnf","version":"0.1.0"}}
{"ok":false,"exitCode":1,"error":{"kind":"expected","message":"O arquivo 'a.ypf' é inválido: truncado"}}
```

`error.kind` é `expected` (erro previsto, a mensagem está em português), `unexpected` (bug; `message` genérica e `logDirectory` aponta o log com o stack) ou `cancelled` (código `3`). O stack trace nunca aparece. Cada comando define o conteúdo de `data`.

### Cor e texto simples

A saída usa Spectre.Console 0.57.2 e as cores do app: aprovada `#5FBF7F`, revisada `#3E9AAE`, traduzida `#6E8FCB`, IA `#9A7FD6`, pendente `#3A4250`, aviso `#F2C46D`, erro `#F59AB2` e destaque `#8DB0EA`. Cor e animação só aparecem quando a saída vai para um terminal interativo.

A saída vira texto simples, sem ANSI e sem animação, quando vale qualquer um destes:

- a saída foi redirecionada para arquivo ou pipe;
- `--json`;
- `--quiet`;
- `--no-color`;
- a variável de ambiente `NO_COLOR` está definida e não vazia (https://no-color.org).

Em texto simples a barra de distribuição continua legível: cada status tem um glifo próprio (`█` aprovadas, `▓` revisadas, `▒` traduzidas, `░` IA, `·` pendentes). `--no-color` vale em qualquer comando e em qualquer posição.

### Progresso ao vivo (terminal interativo)

Num terminal interativo com ANSI, as operações longas desenham uma barra que se atualiza no lugar. Fora dele (saída redirecionada, `--json`, `--quiet`, `--no-color`, `NO_COLOR`, terminal sem ANSI, erro padrão redirecionado como em `vnf translate 2> progresso.log`) nada disso aparece: o log de sempre continua, inclusive o progresso de `translate` e `review` no erro padrão, e o `--json` não muda.

| Comando | No terminal interativo |
|---|---|
| `translate`, `review` | Uma linha `Traduzindo com <modelo> · esforço <nível>` e uma barra por lote com o número do lote, as linhas feitas, os tokens, o custo acumulado e o tempo restante: `lote 7/12  210/360 linhas  18.430 tokens  US$ 0,84  ~1 min`. O tempo restante vem do ritmo até o momento e fica "calculando" até a primeira linha voltar. Tokens e custo aparecem quando o provedor informa o uso. A barra substitui as linhas de progresso do erro padrão |
| `extract`, `build` | Uma barra com o script em andamento (`yst00012.ybn  14/312 scripts`), indeterminada enquanto os pacotes são descompactados (`extract`) ou as traduções conferidas (`build`). No fim, o painel "Resultado da extração" ou "Resultado do build" no lugar do resumo em linhas: scripts, linhas, tamanho de cada YPF (no build, com o do original), acentuação (estratégia e caracteres trocados, só no build) e duração. Avisos e linhas de QA seguem depois do painel |
| `inject`, `restore` | Antes da pergunta de confirmação, uma tabela com o arquivo, o que acontece (`substitui o original`, `arquivo novo`, `volta ao original`, `removido (criado pela injeção)`) e o tamanho atual e o de depois. Com `--yes` a tabela aparece e o comando segue |


### Como escrever um comando

- Registre o comando em `VnfCommands.Register` (`src/VNForge.Cli/VnfCommands.cs`), uma linha por comando.
- Escreva o resultado em `parseResult.InvocationConfiguration.Output`, nunca em `Console.Out`, para a saída poder ser capturada nos testes. Para saída com cor, tabela, painel ou barra, use `cli.Ansi` (um `IAnsiConsole` sobre o mesmo writer) e os helpers `CliViews.NewTable`, `CliViews.NewPanel` e `DistributionBar`; `cli.RichOutput` diz se a invocação pode usar cor e animação. Dado do projeto vai por `CliViews.Escape` antes de entrar num markup.
- Nos testes, `CliHarness` roda a CLI com terminal de texto simples e largura 100; `ConsoleHarness.Plain(width)` e `ConsoleHarness.Rich(width)` criam o console para testar um helper sozinho.
- Com `--json`, escreva `JsonEnvelope.Result(data, exitCode)` passando o mesmo `exitCode` que o comando devolve (por exemplo `2` no QA bloqueante).
- Lance `VNForgeException` para erro previsto; use o `CancellationToken` da ação, que o Ctrl+C cancela.

## Comandos

`vnf --help` agrupa os comandos em três seções, na ordem abaixo, e `vnf <comando> --help` traz a descrição de uma linha e a seção "Exemplos:" do comando. Comando que não entrou em nenhuma seção aparece em "Outros comandos:". As seções ficam em `VnfCommands.Sections` e os exemplos em `CommandExamples`; um teste confere que cada exemplo passa pelo parser.

## Comandos do pipeline

Seção "Pipeline" da ajuda: `init`, `extract`, `translate`, `review`, `check`, `build`, `inject`, `run`, `restore`.

| Comando | O que faz |
|---|---|
| `vnf init <pasta> --game <pasta> [--name <nome>] [--exe <arquivo>] [--source <idioma>] [--target <idioma>] [--encoding <estratégia>] [--model <modelo>]` | Cria o projeto, detecta a engine e grava engine, versão do pacote e chave XOR no `vnforge.json`. Idiomas (`ja` para `pt-BR`), codificação (`fold`) e IA (Claude Opus 5.5) começam com os padrões do exemplo de `02-arquitetura.md`; `--source`, `--target`, `--encoding` e `--model` trocam cada um. O assistente de novo projeto do app chama o mesmo `ProjectInitializer` e grava o mesmo `vnforge.json` para as mesmas escolhas. O nome padrão é o da pasta; o executável padrão é o único `.exe` da pasta do jogo, sem os `unins*.exe` (`--exe` resolve zero ou vários). Pasta que já tem `vnforge.json`, engine não reconhecida, jogo inexistente e projeto dentro da pasta do jogo saem com `1`, sem criar nem sobrescrever nada. Não grava nada na pasta do jogo. Com `--json`, `data` traz `folder`, `config`, `engine`, `version`, `scriptKey`, `summary` e `warnings` Com `--encoding remap` o comando avisa que o `vnf build` ainda não tem o encoder da estratégia (#66); o projeto é criado e o aviso vai também em `warnings` no `--json` |
| `vnf extract` | Descompacta os pacotes do jogo para `.cache/game/` e gera `text/*.json` (detalhes abaixo). Repetido, mantém as traduções (E3.5) |
| `vnf translate` | Traduz com IA as linhas pendentes de `text/*.json` (E6.9). Cada lote volta, é validado (reenvia só as linhas ruins), passa pelo cache por hash (`.cache/translations.json`) e é gravado na hora com `status: machine` e `origin` (`anthropic`, modelo, data UTC); cancelar com Ctrl+C mantém o que já voltou e sai com `3`. `--status` aceita `untranslated` (padrão), `machine` e `all`, que reúne sem tradução e máquina: linhas traduzidas, revisadas ou aprovadas nunca são tocadas. `--files` filtra pela chave do script (`*` e `?`, vários separados por vírgula). O modelo vem de `--model`, depois de `vnf config ai`, depois do `vnforge.json`; esforço e `--batch-size` (20 a 50) caem no `vnforge.json`. Glossário, personagens e `style.md` do projeto entram no prompt. A chave vem de `ANTHROPIC_API_KEY` ou do cofre; `--dry-run` não precisa dela, estima tokens e custo e não grava nada. O progresso (`[####------] 20%  30/150  yst00087.json`) vai para o erro padrão, uma linha por lote, e não aparece com `--json`; num terminal interativo ele vira a barra de "Progresso ao vivo". Linha recusada pelo modelo ou fora das regras de validação fica sem tradução e sai em `refused`; lote que falha depois das tentativas sai em `failed` e o comando sai com `1`. Com `--json`, `data` traz `model`, `effort`, `batchSize`, `concurrency`, `lines`, `translated`, `cached`, `refused`, `failed` (`lines`, `error`, `attempts`), `usage` e `dollars`; no `--dry-run`, `dryRun` e `estimate` (`lines`, `cachedLines`, `inputTokens`, `outputTokens`, `dollars`) |
| `vnf review` | Revisão por IA (E6.10): pede ao modelo uma sugestão por linha já traduzida e grava em `notes`, na linha `[IA] ...`; `translation`, `status`, `origin` e `source` nunca mudam. Uma nova revisão troca só a linha `[IA]` e preserva as notas humanas. Linha sem ressalva não ganha nota. `--status` aceita `machine` (padrão), `translated`, `reviewed`, `approved` e `all` (todas com tradução); `--files`, `--model`, `--effort`, `--batch-size`, `--concurrency`, `--dry-run` e `--limit` valem como em `vnf translate`. Lotes, retry, cancelamento (`3`), progresso no erro padrão, chave de API e glossário, personagens e `style.md` no prompt seguem o `vnf translate`; não há cache. Lote que falha sai em `failed` e o comando sai com `1`; linha recusada sai em `refused`. Com `--json`, `data` traz `model`, `effort`, `batchSize`, `concurrency`, `lines`, `reviewed`, `suggestions`, `clean`, `refused`, `failed` (`lines`, `error`, `attempts`), `usage` e `dollars`; no `--dry-run`, `dryRun` e `estimate` (`lines`, `inputTokens`, `outputTokens`, `dollars`) |
| `vnf check` | Roda o QA (E7.7) e lista os problemas agrupados por categoria, com as categorias que têm erro primeiro (`ERRO`/`AVISO`, id da linha, mensagem, id da regra), e o resumo `2 erros, 37 avisos`. "Linha não traduzida" sai resumida em uma linha por script; `--rules nao-traduzida` lista cada linha (exemplos em "Saída dos comandos de leitura"); avisos que a linha marcou como ok não contam. Sai com `2` se houver erro e com `0` só com avisos. `--rules` filtra por id (vários separados por vírgula; id desconhecido sai com `1`), `--severity error|warn` filtra a gravidade e `--fix` corrige antes o que é seguro (`QaFixer`), grava em `text/*.json`, lista o que mudou e confere de novo: o código de saída vale depois da correção. Com `--json`, `data` traz `errors`, `warnings`, `silenced`, `fixed`, `fixes` e `issues` (`rule`, `severity`, `unit`, `script`, `message`) |
| `vnf build [--full]` | Reconstrói os scripts com as traduções de `text/*.json` e reempacota os YPF em `build/` (E8.1, E5.1 e E4.1: estratégia do `encoding.strategy` e perfil `formatting.json`; detalhes abaixo) |
| `vnf inject` | Faz backup dos originais e copia o build para a pasta do jogo (E8.2) |
| `vnf run` | Abre o `game.exe` do `vnforge.json` (atalho para testar depois do inject). `game.path` tem de ser a pasta que o projeto registrou na extração (ou numa injeção), e `game.exe` um `.exe` relativo a ela e dentro dela (caminho absoluto, `..` e `.bat`/atalhos são recusados). Se o executável não existe, o erro mostra o caminho procurado (E8.6) |
| `vnf restore` | Volta os arquivos originais do backup mais recente (E8.2) |

## Comandos do projeto

Seção "Projeto" da ajuda: `status`, `preview`, `export`, `import`, `glossary`, `chars`, `config`, `detect`.

| Comando | O que faz |
|---|---|
| `vnf status` | Progresso lido de `text/*.json`: total de linhas por status (sem tradução, máquina, traduzida, revisada, aprovada) e percentual com tradução, depois a mesma contagem por arquivo (chave do script) e por personagem (campo `speaker`, mais linhas primeiro; as linhas sem falante vêm por último). Só lê, não grava nada. Em texto: barra de distribuição, legenda e duas tabelas, com o nome traduzido do personagem de `characters.json`. Com `--json`, `data` traz `total`, `files` (`key`) e `characters` (`speaker`, `null` para sem falante); cada item tem `lines`, `untranslated`, `machine`, `translated`, `reviewed`, `approved` e `percent` |
| `vnf preview <id>` | Mostra a tradução da linha como o build a grava: passa pelo mesmo pipeline do `formatting.json` (`FormattingPipeline.Create` sobre o perfil do projeto) e desenha, por caixa (`\p` abre outra), a caixa de texto do jogo com as linhas e as colunas de cada uma, medidas com o `wrap.charWidth` do perfil, mais os avisos do pipeline (caixa acima de `wrap.maxLines`). Só lê, não grava nada. Linha cujo papel não está em `kinds` aparece sem formatação, com um aviso. Id inexistente ou linha sem tradução saem com `1` e mensagem em português. Com `--json`, `data` traz `id`, `source`, `translation`, `text`, `formatted`, `lines`, `maxWidth`, `maxLines`, `wrapEnabled`, `boxes` (`lines`, `columns`) e `warnings` |
| `vnf export [--format csv\|xlsx]` | Exporta o texto para revisão externa em `revisao.<formato>` ou no `--output`. Sem `--format`, vale a extensão do `--output` (`.csv` ou `.xlsx`); sem nenhum dos dois, `csv`. O CSV é UTF-8 com BOM; o XLSX tem uma aba `Revisao` com todas as células em texto. As colunas são `id,kind,speaker,source,translation,status,notes` nos dois. Não troca arquivo existente sem `--force`. O formato `.po` não entra (decisão D1, #51) |
| `vnf import <arquivo> [--format csv\|xlsx]` | Importa de volta (formato pela extensão, ou `--format`), casando por `id`. Só `translation`, `status` e `notes` mudam; id desconhecido ou repetido e status inválido são erros e, com qualquer erro, nada é importado. Só regrava o `text/<chave>.json` que teve linha alterada |
| `vnf glossary add "先輩" "veterano" [--notes] [--force]`, `vnf glossary list` | Gerencia o glossário (`glossary.json`); termo repetido só é trocado com `--force`. O `list` mostra uma tabela. Aceitam `--json` |
| `vnf chars list`, `vnf chars set <original> <tradução> [--gender valor] [--tone texto]` | Gerencia os personagens (`characters.json`); `set` cria ou atualiza e mantém o que não foi informado. Gênero: `unknown`, `female`, `male` ou `other`. O `list` mostra uma tabela. Aceitam `--json` |
| `vnf config ai` | Configura modelo do Claude e chave (salva fora do projeto) |
| `vnf detect [--game <pasta>]` | Só diagnostica, sem gravar: engine, versão, arquivos YPF, versão do YSTB, chave XOR (a codificação ainda não é detectada). Com `--json`, `data.details` usa chaves em inglês: `ypf:<caminho>` (um por pacote), `scriptsArchive`, `ypfVersion`, `scripts`, `ystbVersion`, `scriptKey`, `scriptKeyOrigin` e `warnings`; as que não se aplicam ao jogo não aparecem. Sem `--game` usa a pasta atual; pasta sem engine conhecida responde "Engine não reconhecida" e sai com 0. O texto sai em árvore |

## Comandos de baixo nível

Seção "Baixo nível" da ajuda: `ypf` e `ybn`. O `patch-exe` está planejado e não aparece na ajuda.

| Comando | O que faz |
|---|---|
| `vnf ypf info\|unpack\|pack` | Ferramentas de baixo nível sobre arquivos YPF, úteis para pesquisa. O `info` mostra uma árvore. `unpack` e `pack` perguntam antes de gravar por cima de algo, como o `inject` (detalhes abaixo) |
| `vnf ybn dump <arquivo> [--ysc <arquivo>]` | Despeja um script YSTB legível (comandos, argumentos, textos com marcadores), só leitura (E2.9) |
| `vnf patch-exe --encoding cp1252` | **Planejado (#65), ainda não existe**: aplicaria o patch de codepage no executável. Hoje o comando não está registrado e `vnf patch-exe` sai com `1` ("não é um comando, opção ou argumento conhecido") |

## Saída dos comandos de leitura

Estes comandos só leem. Em terminal interativo usam as cores do app; redirecionados, com `--no-color` ou com `NO_COLOR`, saem como texto simples com a mesma estrutura. O `--json` de cada um não muda.

`vnf status`: título do projeto, total, barra de distribuição, legenda e duas tabelas. A coluna "Nome" do script entra quando o Core passar a guardar o nome do capítulo.

```
Minha VN · PT-BR
102 linhas · 80,3 % com tradução
████████████▓▓▓▓▓▓▓▓▓▓▓▓▒▒▒▒▒▒▒▒▒▒▒▒░░░░░░░░░░░░············
█ Aprovadas 21  ▓ Revisadas 21  ▒ Traduzidas 20  ░ IA 20  · Pendentes 20

Script   Linhas Progresso                 %
yst00001     27 ████▓▓▓▓▒▒▒▒░░░░···· 81,4 %
yst00002     34 ████▓▓▓▓▒▒▒▒░░░░···· 79,4 %

Personagem    Linhas Progresso                 %
美咲 → Misaki     69 ████▓▓▓▓▒▒▒▒░░░░···· 81,1 %
(sem falante)     33 ████▓▓▓▓▒▒▒▒░░░░···· 78,7 %
```

`vnf check`: problemas agrupados pelas categorias da tela Conferir do app (Marcadores, Caixa de texto, Espaços, Pontuação, Acentuação, Glossário, Consistência, Tradução, Outras). Categorias com erro vêm primeiro e, dentro de cada uma, os erros antes dos avisos. "Linha não traduzida" vira uma linha por script; `vnf check --rules nao-traduzida` lista cada linha. A categoria de cada regra vem de `QaCategories` no Core.

```
Marcadores · 1 erro
  ERRO  yst00001:000002  marcadores diferentes do original: falta \c  [marcador-diferente]

Espaços · 2 avisos
  AVISO yst00001:000001  2 espaços seguidos na posição 8.  [espaco-duplo]
  AVISO yst00002:000004  2 espaços seguidos na posição 8.  [espaco-duplo]

Tradução · 20 avisos
  AVISO yst00001  5 linhas sem tradução  [nao-traduzida]
  AVISO yst00002  7 linhas sem tradução  [nao-traduzida]
  Para listar uma a uma: vnf check --rules nao-traduzida

1 erro, 22 avisos
```

`vnf preview <id>`: a caixa de texto do jogo como no app. A placa traz o falante, cada caixa é um painel, `┊` marca a coluna limite (`wrap.maxWidth`) e o que passa dela aparece depois da régua, em coral no terminal colorido. Linhas além de `wrap.maxLines` ficam abaixo de um separador `┄` e saem em coral. À direita de cada linha vão as colunas usadas e o limite.

```
Linha a001:000001

 美咲 
╭─Caixa 1 · 3 linhas───────────────────────╮
│  um dois   ┊   7/10 col                  │
│  tres      ┊   4/10 col                  │
│ ┄┄┄┄┄┄┄┄┄┄┄┄┄  limite de 2 linhas        │
│  quatro    ┊   6/10 col  acima do limite │
╰──────────────────────────────────────────╯

Total: 3 linhas; largura máxima 10 colunas.
Aviso: caixa 1 com 3 linhas (máximo 2).
```

`vnf detect` e `vnf ypf info` saem em árvore. No `ypf info` os arquivos ficam sob a pasta deles, com tamanho, compressão e hashes.

```
Engine detectada: yuris
├── parece YU-RIS, pacote de scripts ysbin.ypf v500
└── Pacotes
    └── ysbin.ypf: v500, 41 arquivos

YPF · versão 500 · 3 arquivos
├── Estrutura
│   ├── Tamanho do arquivo: 2.048 bytes
│   └── Hash do nome: 0x1F3A9C52
└── Arquivos (3)
    ├── ysbin
    │   └── yst00001.ybn  1.024 bytes · zlib, 512 armazenados · nome 0x1A2B3C4D · dados 0x5E6F7A8B
    └── bg
        └── fundo.png  512 bytes · nome 0x7C21D0AE · dados 0x33B4F9E1
```

`vnf glossary list` e `vnf chars list` saem em tabela, com as colunas Original, Tradução e Notas (glossário) ou Gênero e Tom (personagens).

## Opções principais

`vnf translate`
```
  --files <padrão>        só alguns scripts (glob: "yst001*")
  --status <lista>        quais linhas: untranslated (padrão), machine, all
  --model <id>            claude-opus-5-5 (padrão), claude-sonnet-5-5, claude-haiku-5-5, claude-fable-5-1
  --effort <nível>        low | medium | high
  --batch-size <n>        linhas por chamada (padrão 30)
  --concurrency <n>       chamadas em paralelo (padrão 2)
  --dry-run               só estima tokens e custo
  --limit <n>             para depois de n linhas (bom para testar o prompt)
```

`vnf review`
```
  --files <padrão>        só alguns scripts (glob: "yst001*")
  --status <lista>        quais linhas: machine (padrão), translated, reviewed, approved, all
  --model <id>            igual ao vnf translate
  --effort <nível>        low | medium | high
  --batch-size <n>        linhas por chamada (padrão 30)
  --concurrency <n>       chamadas em paralelo (padrão 2)
  --dry-run               só estima tokens e custo
  --limit <n>             para depois de n linhas
```

`vnf check`
```
  --rules <lista>         só algumas regras
  --severity error|warn   filtro
  --fix                   corrige o que é seguro corrigir (espaços duplos etc.)
```

`vnf build` (E8.1, E5.1; aceita `--json`)
```
  --report                 só lista os caracteres que a estratégia troca ou não representa; não reconstrói nada (sai com 2 se algum não for codificável)
  --allow-warnings         deixa o build seguir com avisos de QA; erros de QA continuam bloqueando (E7.1). Os avisos `nao-traduzida` nunca bloqueiam: o build copia o original nessas linhas e o resumo traz a contagem ("QA: 6.853 linhas sem tradução; o build copiou o original nelas."; no `--json`, `qa.untranslated`) (#232)
  --full                   reconstrói todos os scripts; sem a opção o build reaproveita os que não mudaram desde o build anterior (E8.4)
  --encoding <estratégia>  estratégia de codificação só deste build (sjis, fold, cp1252); o vnforge.json não muda (E8.4)
```
O build é incremental por padrão: reaproveita o resultado do build anterior dos scripts cuja tradução não mudou, e `--full` reconstrói tudo. A opção `--only-changed` saiu: sem `--full` já vale o incremental. `--encoding` troca a estratégia de codificação só naquele build. O build reconstrói só os scripts com tradução (`status` diferente de `untranslated`, `translation` não vazia e diferente do `source`) e codifica com a estratégia do `encoding.strategy` do `vnforge.json`. Estratégias com encoder: `sjis` (Shift-JIS pura), `fold` e `cp1252`; `remap` dá erro claro até a issue dele entrar. Os detalhes estão em "`vnf build`" abaixo.

`vnf ypf` (E1.8; os três aceitam `--json`; saídas: `0` ok, `1` erro, `3` recusou a confirmação)
```
  info <arquivo>                       versão, variante (hashes, offset, tabela de troca, chave do nome) e a lista de arquivos com tamanhos e hashes
  unpack <arquivo> <pasta>             extrai tudo, já descomprimido; pasta com arquivos pergunta antes (ou --yes)
  pack <pasta> --original <arquivo> --output <arquivo>
                                       remonta com reescrita mínima; só o arquivo que difere do original vira troca
```
O `pack` nunca grava sobre o original. Saída que já existe e pasta de destino com arquivos (`unpack`) perguntam `[s/N]` antes de gravar, como o `inject`: recusar sai com `3` e não grava nada (antes saía com `1`); sem como perguntar, ou com `--json`, o comando sai com `1` e pede `--yes`. YPF ou YBN inválido, truncado ou com entrada corrompida sai com `1` e uma mensagem em português que cita o arquivo (e a entrada, no `unpack`); nunca "Erro inesperado". Ele não acrescenta entradas: um arquivo da pasta que o original não tem é recusado. Entrada sem arquivo na pasta é mantida do original. Sem mudanças na pasta, a saída é idêntica ao original; o `--json` informa isso em `identical`. O `unpack` recusa, antes de gravar, nome interno que sai da pasta (`..`, caminho absoluto, nome reservado do Windows) ou que colide com outro neste sistema de arquivos. Também recusa, antes de gravar, o caminho que passa por link simbólico ou junção já existente no destino (a própria pasta de destino pode ser um link) e a entrada cujo tamanho no índice passa do que cabe num array. Um erro no meio da extração, como hash divergente ou disco cheio, deixa a extração parcial: os arquivos já gravados ficam na pasta. No Windows o `pack` compara os caminhos sem diferenciar caixa e recusa o original que tem duas entradas só com a caixa diferente. Arquivo da pasta que o original não tem, como `Thumbs.db` ou `desktop.ini`, é recusado com o nome citado; remova-o da pasta.

`vnf ybn dump` (E2.9; aceita `--json`)
```
  dump <arquivo> [--ysc <arquivo>]     comandos, argumentos e textos de um yst*.ybn
  --ysc <arquivo>                      ysc.ybn do mesmo jogo; sem a opção, procura ysc.ybn na pasta do script e na pasta acima
```
O comando só lê: não grava nada, nem no script nem no `ysc.ybn`. O `ysc.ybn` é obrigatório porque o opcode de cada instrução só ganha nome com ele; para um script extraído com `vnf ypf unpack`, o `ysc.ybn` fica na mesma pasta ou na de cima e a opção não é necessária. Se o YSCM não for achado, o erro pede a `--ysc`. Cada instrução sai com o índice, a linha do `.yst` original e o comando; cada argumento, com o nome do parâmetro no YSCM, o operador de atribuição e o valor conforme o tipo (`kind`):

| `kind` | Texto | O que mostra |
|---|---|---|
| `text` | `texto` | Argumento do `WORD`, decodificado em Shift-JIS (página 932) com os códigos de controle como marcadores (`\n`, `\p`, `\c`, `\u`, `{EF:xx}`, `{RAW:hex}`), os mesmos de `ControlMarkers` |
| `string` | `string` | Expressão que é só uma string literal, sem as aspas e com os escapes desfeitos, também com marcadores |
| `expression` | `expressão` | Qualquer outra expressão, em bytes hexadecimais |
| `value` | `valor` | Argumento cujo `size` e `offset` não apontam para os recursos (`RETURNCODE`, 2º e 3º de `IF`/`ELSE` com 3 argumentos, 2º de `LOOP`); mostra só `size` e `offset` |
| `empty` | `vazio` | Argumento sem bytes nos recursos |

```console
$ vnf ybn dump yst00001.ybn
Arquivo: D:\jogo\ysbin\yst00001.ybn
YSCM: D:\jogo\ysc.ybn
Versão: 500
Chave: 0xD36FAC96 (derivada do arquivo)
Instruções: 3
Argumentos: 3

#0000  linha 1  WORD
    [0] TEXT (texto) = 美咲「やあ\nまたね」\p

#0001  linha 2  RETURNCODE
    [0] CODE (valor) = tamanho 0, offset 0

#0002  linha 3  GOSUB
    [0] LABEL (string) = ES.SEL.SET
```
Com `--json`, `data` traz `file`, `ysc`, `version`, `key`, `keyOrigin` (`none`, `derived` ou `known`), `instructionCount`, `argumentCount` e `instructions`. Cada instrução tem `index`, `line`, `opcode`, `command`, `extra` e `arguments`; cada argumento tem `index`, `id`, `name` (ausente se o `id` passa da lista do comando), `type`, `assignOp`, `assignOperator`, `size`, `offset`, `kind` e `value` (ausente em `value` e `empty`). O despejo é para pesquisa e depuração: o texto para traduzir sai de `vnf extract`.

`vnf inject` e `vnf restore` (E8.2; ambos aceitam `--json`, `--project` e `--yes`)
```
  restore                  devolve os originais: o backup mais recente e, para arquivos que só um backup mais velho guardou, o deles
  restore                  devolve os originais do backup mais recente
  --yes                    pula a confirmação "Gravar N arquivo(s)?" (obrigatório com --json)
```
O `inject` lista o que vai gravar e pede confirmação antes de tocar no jogo. Recusar sai com código 3 e não grava nada. Sem `--yes` e sem entrada (script, `--json`), o comando pede o `--yes` em vez de seguir. A ordem é: copia para `backup/AAAA-MM-DD_HHmm/` só os originais que o build substitui, confere o SHA-256 de cada cópia, grava `vnforge-backup.json` na pasta do backup e só então escreve no jogo, conferindo o SHA-256 de cada arquivo gravado. Arquivo do build igual ao do jogo é ignorado; sem nada a mudar não há backup. Dois backups no mesmo minuto ganham o sufixo `-02`, `-03`.

Antes de tudo, o `inject` confere (E8.5) cada arquivo que o `vnf extract` leu contra o SHA-256 gravado em `.cache/state.json`. Se algum mudou ou sumiu, por exemplo depois de uma atualização do jogo, ele recusa, não grava nada, lista os arquivos alterados e manda rodar `vnf extract` de novo. O build de um `inject` anterior não conta como mudança. Projeto sem estado de extração não é verificado.

Se uma gravação falha no meio, o `inject` devolve na hora os arquivos já trocados e diz que o jogo voltou ao estado original. Se nem isso for possível, a mensagem manda rodar `vnf restore`, que lê o manifesto (já gravado antes da primeira escrita no jogo) e termina o serviço. Antes de copiar, o `inject` confere de novo que cada arquivo do jogo continua como no plano; se o jogo mudou nesse intervalo, aborta sem gravar. Se o jogo ainda está com o build de um `inject` anterior, o original vem do backup mais recente que mexeu naquele arquivo, então todo backup guarda os arquivos originais e nunca uma versão já traduzida. O `restore` grava na pasta do jogo registrada no manifesto (a do `inject`, mesmo que o `game.path` tenha mudado; se ela não existe mais, usa o `game.path` atual), confere o hash de todas as cópias do backup antes de escrever, apaga os arquivos que a injeção criou (os que mudaram depois dela ficam, listados como mantidos) e pode ser repetido. `build/` ausente ou vazia, pasta do jogo inexistente e backup inexistente são erros em português. Com `--json`, o `inject` devolve `gameDirectory`, `backupDirectory` (nulo sem mudanças), `written` e `unchanged`; o `restore` devolve `gameDirectory`, `backupDirectory`, `restored`, `removed` e `kept`. O `restore` não segue links simbólicos além do que o sistema de arquivos já faz: não aponte subpastas do jogo para fora dele.

`vnf extract` (E3.4; aceita `--json` e `--project`)
```
  extract                  descompacta os pacotes com scripts para .cache/game/ e grava um text/<chave>.json por script
```
O comando só lê a pasta do jogo (`game.path` do `vnforge.json`, relativo à pasta do projeto quando não é absoluto); um teste compara o hash de todos os arquivos do jogo antes e depois. No YU-RIS ele descompacta só os pacotes `.ypf` que têm scripts (`yst*.ybn`), o `ysc.ybn` ou o `yst_list.ybn`, cada um para `.cache/game/<caminho do pacote no jogo>/` com os caminhos internos (`.cache/game/ysbin.ypf/ysc.ybn`, `.cache/game/pac/ysbin.ypf/...`). Pacotes de imagem e áudio não são tocados (pesam gigabytes e não têm texto); use `vnf ypf unpack` se precisar de um deles. Cada rodada apaga `.cache/game/` antes de descompactar, então sobras de uma extração anterior não ficam.

Cada script vira `text/<chave>.json`, onde a chave é o nome do `.ybn` sem extensão, em minúsculas (`yst00012.ybn` vira `text/yst00012.json`), mesmo quando o script não tem texto (o arquivo traz `units` vazio). Os ids são os de `ScriptFile.UnitId`. O nome legível do `yst_list.ybn` (`01_prologo`) aparece só no `--json` (`texts[].name`), porque o arquivo de texto não tem campo para ele.

Um script ilegível aborta a extração com o nome do script na mensagem e nada é gravado em `text/`; se a gravação de `text/` falhar no meio, os arquivos voltam ao que eram. Com `text/*.json` já na pasta, o `extract` mantém as traduções (E3.5): casa cada linha pelo `id` e pelo original. Jogo sem mudança deixa `text/*.json` intacto. Linha cujo original mudou volta para `untranslated`, guarda a tradução antiga e o original anterior em `previousSource`, e entra na contagem "desatualizadas"; linha nova entra sem tradução; linha ou script que sumiu do jogo sai de `text/`, e o arquivo anterior fica em `.cache/text-anterior/<data-hora>/`. A saída traz uma linha `Re-extração: ...` (e `reextraction` com `kept`, `outdated`, `added` e `removed` no `--json`). Um `text/*.json` inválido interrompe tudo antes de qualquer gravação. Para extrair do zero, apague a pasta `text/` (as traduções nela se perdem). `.cache/` dentro da pasta do jogo, também por link simbólico ou junção, é recusado antes de gravar, e um jogo sem nenhum script depois de descompactar é erro.

No texto, a saída é uma linha por pacote descompactado e o resumo, com o tipo de cada linha na ordem diálogos, narração, escolhas, nomes, sistema e outros (tipos com zero ficam de fora), seguidos de até 10 avisos dos scripts. Com `--json`, `data` traz `gameDirectory`, `cacheDirectory`, `archives` (`path`, `directory`, `files`, `bytes`), `scripts` (quantidade), `lines`, `kinds` (`dialogue`, `narration`, `choice`, `name`, `system`, `other`, todos presentes), `texts` (`key`, `name`, `lines` por script) e `warnings`.

`vnf build` (E8.1; aceita `--json`)

Reabre o jogo descompactado em `.cache/game/` (o `vnf extract` precisa ter rodado), lê `text/*.json` e reconstrói cada script que tem tradução com a reescrita mínima do E2: só os argumentos traduzidos mudam, o resto do script sai byte a byte igual. Depois grava os pacotes em `build/`, com o mesmo caminho relativo à pasta do jogo (`build/ysbin.ypf`, `build/pac/x.ypf`), que é o que o `vnf inject` espera: cada arquivo de `build/` substitui o de mesmo caminho no jogo.

- **Pacotes sem alteração**: saem em `build/` idênticos ao original (cópia byte a byte), e o `vnf inject` ignora o arquivo igual ao do jogo. Assim `build/` sempre tem todos os pacotes de scripts, e um build sem nenhuma tradução gera `build/ysbin.ypf` idêntico ao original. Pacotes que o `extract` não descompactou (imagens, áudio) não entram.
- **Não altera o jogo nem o `.cache/`**: só lê a pasta do jogo e `.cache/game/`. O `build/` anterior é apagado por inteiro no fim, não em cada etapa: o novo é montado em `build.tmp/`, ao lado de `build/`, e só então toma o lugar do anterior. `build/` dentro da pasta do jogo é recusado, como o `.cache/` no `extract`.
- **Estratégia de codificação (E5.1)**: o encoder vem do `encoding.strategy`. Estratégia sem encoder nesta versão sai com `1` e a mensagem diz quais existem; o build nunca cai em Shift-JIS em silêncio. Antes de reconstruir, o build confere todas as traduções a gravar e, se a estratégia não representa algum caractere, aborta com a lista completa (linha, posição e caractere com o código `U+`), não só o primeiro. Estratégia que troca caracteres (o `fold`) mostra o substituto no `--report`; a troca não impede o build.
- **Formatação (E4.1)**: se existe `formatting.json` na raiz do projeto, a tradução de cada linha a gravar passa pelo pipeline do perfil antes da conferência de codificação (ver doc 02, "Formatação"). O `text/*.json` não muda. Perfil inválido sai com `1` citando o campo; caixa acima do limite de linhas vira aviso com o id da linha. Sem o arquivo, o texto vai como está.
- **QA (E7.1)**: antes de qualquer outra conferência o build roda as regras de QA (doc 02, "QA"). Erro de QA, ou aviso (exceto `nao-traduzida`) que a linha não marcou como ok, sai com `2` sem gravar nada em `build/`; no texto, uma linha por ocorrência (`ERRO  yst00012:000042  [regra] mensagem`, `AVISO ...`) e o resumo `1 erro, 3 avisos`. Com `--allow-warnings` só os avisos passam (erro continua saindo com `2`) e a lista deles aparece depois do resumo do build. Com `--json`, `data.qa` traz `errors`, `warnings`, `untranslated`, `silenced` e `issues` (`rule`, `severity` `error`|`warn`, `unit`, `script`, `message`); `warnings` e `issues` não contam `nao-traduzida`, que só aparece em `untranslated`; no bloqueio o `data` é esse mesmo objeto e o `exitCode` do envelope é `2`.
- **Build incremental (E8.4)**: o estado do último build fica em `.cache/build-state.json`, e os bytes de cada script reconstruído em `.cache/build-scripts/`. Para cada script com tradução o build calcula um hash do script original, da estratégia de codificação e das unidades a gravar (já formatadas). Hash igual ao do build anterior reaproveita o resultado guardado, sem chamar o `Rebuild`; hash diferente reconstrói só aquele script. Se nenhum script mudou e os pacotes de `build/` estão como o build anterior os deixou (mesmo tamanho), o build nem regrava `build/` e diz `Nada mudou desde o build anterior`. Trocar `--encoding` muda o hash e reconstrói. O QA e a conferência de codificação rodam em todo build, incremental ou não. Estado ausente, corrompido ou de outra versão cai num build completo. Mudança na pasta do jogo depois da extração é assunto da proteção contra jogo alterado (E8.5), não do estado incremental. Com `--json`, `data.reusedScripts` conta os scripts reaproveitados.
- **Aborta sem deixar `build/` pela metade**: caractere que a estratégia não representa, marcador inválido na tradução, trecho que não é mais o do `source` (script de outra versão), `text/*.json` inválido ou jogo que mudou depois da extração saem com `1`, com a mensagem em português citando o script e o `id` da linha. O `build/` anterior continua como estava e o `build.tmp/` é removido.
- **Avisos**: um `text/<chave>.json` sem script correspondente em `.cache/game/` é ignorado com aviso.
- **Saída**: no texto, uma linha por pacote gravado (com a quantidade de scripts alterados, ou "sem alterações, idêntico ao original") e o resumo `Reconstruídos 1 script (2 linhas trocadas); 1 pacote gravado em build/`, seguidos de até 10 avisos. Com `--json`, `data` traz `gameDirectory`, `buildDirectory`, `archives` (`path`, `replacedFiles`), `scripts` (reconstruídos), `totalScripts` (lidos de `.cache/`), `lines` (unidades trocadas), `rebuilt` (`key`, `name`, `lines` por script) e `warnings`, mais `encoding` (o relatório de codificação, igual ao `data` do `--report`).

`vnf build --report` não toca em `.cache/`, em `build/` nem no jogo: lê o `vnforge.json` e os `text/*.json`. No texto, uma linha por ocorrência (`yst00012:000034 posição 4: '«' (U+00AB) não codificável`, ou `vira 'a'` quando a estratégia troca o caractere) e o resumo. Com `--json`, `data` traz `strategy`, `checkedLines`, `unencodable`, `replaced` e `issues` (`unit`, `script`, `position`, `character`, `codePoint`, `replacement`, ausente quando a estratégia não representa o caractere). Sai com `0` sem caractere bloqueante e com `2` com algum; o `exitCode` do envelope repete o código.

```console
$ vnf build
Gravado: minha-vn-ptbr\build\ysbin.ypf (12 scripts alterados)
Reconstruídos 12 scripts (1.204 linhas trocadas); 1 pacote gravado em build/
```

## Fluxo típico

```console
$ vnf init minha-vn-ptbr --game "D:\Jogos\MinhaVN"
Engine detectada: YU-RIS (YPF v500, YSTB v480)
Arquivo de scripts: ysbin.ypf (312 scripts)
Chave XOR dos scripts: 0xD36FAC96 (padrão)
Projeto criado em minha-vn-ptbr\

$ cd minha-vn-ptbr
$ vnf extract
Descompactado: ysbin.ypf (314 arquivos) em .cache\game\ysbin.ypf
Extraídos 312 scripts, 48.211 linhas (41.977 diálogos, 5.902 narração, 211 escolhas, 121 nomes)

$ vnf translate --limit 50          # testa o prompt antes de gastar
$ vnf translate --dry-run
Estimativa: ~3,1 M tokens de entrada, ~1,4 M de saída

$ vnf translate
[#########---------] 52%  25.069/48.211  yst00087.json

$ vnf check
ERRO  yst00012:0042  marcador \p ausente na tradução
AVISO yst00031:0007  3 linhas na caixa (máximo 3) com 61 colunas na última (máximo 54)
AVISO yst00031:0102  caractere "«" não existe em CP1252 → será trocado por "\""
2 erros, 37 avisos

$ vnf build && vnf inject
Backup: backup\2026-10-17_1820\ysbin.ypf
Injetado: D:\Jogos\MinhaVN\ysbin.ypf

$ vnf run
```

## Em aberto

- [x] Nome do executável: `vnf` (decidido em [01-nome.md](01-nome.md)).
- [x] Distribuição (D5, decidido em 24/10/2026): **executável único autocontido**, gerado com `dotnet publish -p:PublishSingleFile=true --self-contained`. Quem traduz baixa o `vnf` e roda, sem instalar o SDK nem o runtime do .NET; `dotnet tool install -g` exigiria o SDK. Detalhes:
  - **RIDs**: `win-x64` (público principal, o app e os jogos são Windows) e `linux-x64` (custo zero: o mesmo `dotnet publish` roda no job do CI). `win-arm64`, `osx-*` e `linux-arm64` ficam de fora até alguém pedir.
  - **Sem trimming** (`PublishTrimmed` desligado). O `System.CommandLine` e o SDK `Anthropic` usam reflexão e serialização por reflexão; o trimming exigiria anotar e testar os dois, e o ganho é só de tamanho. O pacote sai com compressão do single-file (`EnableCompressionInSingleFile`), cerca de 41 MB no `win-x64`.
  - **Pacote**: `vnf-<versão>-win-x64.zip` e `vnf-<versão>-linux-x64.tar.gz`, cada um com o executável e o `README.md`, mais `SHA256SUMS.txt`. A versão vem da tag (`v1.2.3` vira `1.2.3`) e aparece em `vnf --version`.
  - **Publicação (E9.3)**: a cada tag `vX.Y.Z` (ou `vX.Y.Z-rc1`), o job `publicar` do CircleCI gera o pacote e o anexa à release do GitHub dessa tag, que é o único lugar onde ele sai. Ver "CI" em [02-arquitetura.md](02-arquitetura.md).
