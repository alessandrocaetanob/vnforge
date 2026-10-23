# 03 — CLI (`vnf`)

## Como funciona

- Um executável, `vnf`, com subcomandos por etapa do pipeline.
- Todo comando roda dentro de uma pasta de projeto (a atual ou `--project <pasta>`).
- Saída legível por padrão; `--json` em qualquer comando para automação e para o app consumir.
- Códigos de saída: `0` ok, `1` erro, `2` QA encontrou erros bloqueantes, `3` cancelado.
- Nada destrutivo sem confirmação; `--yes` pula a confirmação em scripts.
- `--verbose` / `--quiet` controlam o log; o log completo vai para `.cache/logs/`.
- Opções globais, válidas em qualquer posição da linha: `--project <pasta>`, `--json`, `--verbose`, `--quiet`, `--yes`. `--verbose` e `--quiet` não combinam. `--version` existe só na raiz; `--help` mostra a ajuda em português em qualquer comando.
- Ctrl+C cancela o comando em andamento e sai com `3`. Um segundo Ctrl+C encerra o processo na hora, também com `3`, para o caso de um comando que não respeita o cancelamento.
- Só conta como cancelamento a exceção de cancelamento com o token do Ctrl+C ativo. Um timeout de rede (`HttpClient`) é erro e sai com `1`.
- Erro de uso (opção desconhecida, opção obrigatória ausente, valor inválido, `--project` apontando para pasta que não existe) sai com `1`, em português, e a mensagem aponta `--help`.
- `--project` sem valor consome a opção seguinte como se fosse a pasta: `vnf --project --json` procura uma pasta chamada `--json` e sai com `1`.

### Onde vai o log

- A raiz é a pasta de `--project` ou, sem a opção, a pasta atual. Se a raiz tem `vnforge.json`, o log vai para `<raiz>\.cache\logs\`.
- Sem `vnforge.json` na raiz (inclusive com `--project` apontando para uma pasta qualquer): `%LOCALAPPDATA%\VNForge\logs\`, ou a pasta temporária se `LocalApplicationData` vier vazio ou relativo. A CLI nunca cria `.cache` em pasta qualquer, que pode ser a pasta do jogo. A busca de `vnforge.json` olha só a raiz, não sobe diretórios.

### Formato do `--json`

Uma linha de JSON por documento, sempre em stdout (nada vai para stderr). `exitCode` repete o código de saída do processo. Os erros saem no formato abaixo por conta da CLI; cada comando escreve o próprio resultado com `JsonEnvelope.Result` (Core), inclusive quando termina com `2`.

```json
{"ok":true,"exitCode":0,"data":{"name":"vnf","version":"0.1.0"}}
{"ok":false,"exitCode":1,"error":{"kind":"expected","message":"O arquivo 'a.ypf' é inválido: truncado"}}
```

`error.kind` é `expected` (erro previsto, a mensagem está em português), `unexpected` (bug; `message` genérica e `logDirectory` aponta o log com o stack) ou `cancelled` (código `3`). O stack trace nunca aparece. Cada comando define o conteúdo de `data`.

### Como escrever um comando

- Registre o comando em `VnfCommands.Register` (`src/VNForge.Cli/VnfCommands.cs`), uma linha por comando.
- Escreva o resultado em `parseResult.InvocationConfiguration.Output`, nunca em `Console.Out`, para a saída poder ser capturada nos testes.
- Com `--json`, escreva `JsonEnvelope.Result(data, exitCode)` passando o mesmo `exitCode` que o comando devolve (por exemplo `2` no QA bloqueante).
- Lance `VNForgeException` para erro previsto; use o `CancellationToken` da ação, que o Ctrl+C cancela.

## Comandos do pipeline

| Comando | O que faz |
|---|---|
| `vnf init <pasta> --game <dir> [--name <nome>] [--exe <arquivo>]` | Cria o projeto, detecta a engine e grava engine, versão do pacote e chave XOR no `vnforge.json`. Idiomas (`ja` para `pt-BR`), codificação (`cp1252`) e IA começam com os padrões do exemplo de `02-arquitetura.md`. O nome padrão é o da pasta; o executável padrão é o único `.exe` da pasta do jogo, sem os `unins*.exe` (`--exe` resolve zero ou vários). Pasta que já tem `vnforge.json`, engine não reconhecida, jogo inexistente e projeto dentro da pasta do jogo saem com `1`, sem criar nem sobrescrever nada. Não grava nada na pasta do jogo. Com `--json`, `data` traz `folder`, `config`, `engine`, `version`, `scriptKey`, `summary` e `warnings` |
| `vnf detect [--game <dir>]` | Só diagnostica, sem gravar: engine, versão, arquivos YPF, versão do YSTB, chave XOR (a codificação ainda não é detectada). Com `--json`, `data.details` usa chaves em inglês: `ypf:<caminho>` (um por pacote), `scriptsArchive`, `ypfVersion`, `scripts`, `ystbVersion`, `scriptKey`, `scriptKeyOrigin` e `warnings`; as que não se aplicam ao jogo não aparecem. Sem `--game` usa a pasta atual; pasta sem engine conhecida responde "Engine não reconhecida" e sai com 0 |
| `vnf extract` | Descompacta os pacotes do jogo para `.cache/game/` e gera `text/*.json` (E3.4: só a primeira extração; detalhes abaixo). Re-extrair preservando traduções é a E3.5 |
| `vnf status` | Progresso: linhas por status, por arquivo, por personagem |
| `vnf translate` | Traduz com IA as linhas pendentes |
| `vnf check` | Roda o QA e lista problemas |
| `vnf review` | Revisão por IA: gera sugestões nas notas, não altera traduções |
| `vnf preview <id>` | Mostra como a linha fica depois da formatação e quebra de linha |
| `vnf build` | Aplica formatação e codificação, reconstrói scripts e arquivos YPF em `build/` |
| `vnf inject` | Faz backup dos originais e copia o build para a pasta do jogo (E8.2) |
| `vnf restore` | Volta os arquivos originais do backup mais recente (E8.2) |
| `vnf run` | Abre o jogo (atalho para testar depois do inject) |

## Comandos de apoio

| Comando | O que faz |
|---|---|
| `vnf export --format xlsx\|csv\|po` | Exporta o texto para revisão externa |
| `vnf import <arquivo>` | Importa de volta, casando por `id` |
| `vnf glossary add "先輩" "veterano"` | Gerencia o glossário |
| `vnf chars list\|set` | Gerencia nomes de personagens |
| `vnf config ai` | Configura modelo do Claude e chave (salva fora do projeto) |
| `vnf ypf info\|unpack\|pack` | Ferramentas baixas de arquivo YPF, úteis para pesquisa |
| `vnf ybn dump <arquivo> [--ysc <arquivo>]` | Despeja um script YSTB legível (comandos, argumentos, textos com marcadores), só leitura (E2.9) |
| `vnf patch-exe --encoding cp1252` | Aplica o patch de codepage no executável (experimental) |

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

`vnf check`
```
  --rules <lista>         só algumas regras
  --severity error|warn   filtro
  --fix                   corrige o que é seguro corrigir (espaços duplos etc.)
```

`vnf build`
```
  --only-changed          reconstrói só scripts com tradução alterada (padrão)
  --allow-warnings        builda mesmo com avisos (erros sempre bloqueiam)
  --encoding <estratégia> sobrescreve a do projeto, útil para comparar
```

`vnf ypf` (E1.8; os três aceitam `--json`)
```
  info <arquivo>                       versão, variante (hashes, offset, tabela de troca, chave do nome) e a lista de arquivos com tamanhos e hashes
  unpack <arquivo> <pasta>             extrai tudo, já descomprimido; pasta com arquivos pede --yes
  pack <pasta> --original <arquivo> --output <arquivo>
                                       remonta com reescrita mínima; só o arquivo que difere do original vira troca
```
O `pack` nunca grava sobre o original e pede `--yes` para sobrescrever a saída. Ele não acrescenta entradas: um arquivo da pasta que o original não tem é recusado. Entrada sem arquivo na pasta é mantida do original. Sem mudanças na pasta, a saída é idêntica ao original; o `--json` informa isso em `identical`. O `unpack` recusa, antes de gravar, nome interno que sai da pasta (`..`, caminho absoluto, nome reservado do Windows) ou que colide com outro neste sistema de arquivos. Também recusa, antes de gravar, o caminho que passa por link simbólico ou junção já existente no destino (a própria pasta de destino pode ser um link) e a entrada cujo tamanho no índice passa do que cabe num array. Um erro no meio da extração, como hash divergente ou disco cheio, deixa a extração parcial: os arquivos já gravados ficam na pasta. No Windows o `pack` compara os caminhos sem diferenciar caixa e recusa o original que tem duas entradas só com a caixa diferente. Arquivo da pasta que o original não tem, como `Thumbs.db` ou `desktop.ini`, é recusado com o nome citado; remova-o da pasta.

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
  inject                   cada arquivo de build/ substitui o de mesmo caminho na pasta do jogo (game.path do vnforge.json)
  restore                  devolve os originais do backup mais recente
  --yes                    pula a confirmação "Gravar N arquivo(s)?" (obrigatório com --json)
```
O `inject` lista o que vai gravar e pede confirmação antes de tocar no jogo. Recusar sai com código 3 e não grava nada. Sem `--yes` e sem entrada (script, `--json`), o comando pede o `--yes` em vez de seguir. A ordem é: copia para `backup/AAAA-MM-DD_HHmm/` só os originais que o build substitui, confere o SHA-256 de cada cópia, grava `vnforge-backup.json` na pasta do backup e só então escreve no jogo, conferindo o SHA-256 de cada arquivo gravado. Arquivo do build igual ao do jogo é ignorado; sem nada a mudar não há backup. Dois backups no mesmo minuto ganham o sufixo `-02`, `-03`.

Se uma gravação falha no meio, o `inject` devolve na hora os arquivos já trocados e diz que o jogo voltou ao estado original. Se nem isso for possível, a mensagem manda rodar `vnf restore`, que lê o manifesto (já gravado antes da primeira escrita no jogo) e termina o serviço. Antes de copiar, o `inject` confere de novo que cada arquivo do jogo continua como no plano; se o jogo mudou nesse intervalo, aborta sem gravar. Se o jogo ainda está com o build de um `inject` anterior, o original vem do backup mais recente que mexeu naquele arquivo, então todo backup guarda os arquivos originais e nunca uma versão já traduzida. O `restore` grava na pasta do jogo registrada no manifesto (a do `inject`, mesmo que o `game.path` tenha mudado; se ela não existe mais, usa o `game.path` atual), confere o hash de todas as cópias do backup antes de escrever, apaga os arquivos que a injeção criou (os que mudaram depois dela ficam, listados como mantidos) e pode ser repetido. `build/` ausente ou vazia, pasta do jogo inexistente e backup inexistente são erros em português. Com `--json`, o `inject` devolve `gameDirectory`, `backupDirectory` (nulo sem mudanças), `written` e `unchanged`; o `restore` devolve `gameDirectory`, `backupDirectory`, `restored`, `removed` e `kept`. O `restore` não segue links simbólicos além do que o sistema de arquivos já faz: não aponte subpastas do jogo para fora dele.

`vnf extract` (E3.4; aceita `--json` e `--project`)
```
  extract                  descompacta os pacotes com scripts para .cache/game/ e grava um text/<chave>.json por script
```
O comando só lê a pasta do jogo (`game.path` do `vnforge.json`, relativo à pasta do projeto quando não é absoluto); um teste compara o hash de todos os arquivos do jogo antes e depois. No YU-RIS ele descompacta só os pacotes `.ypf` que têm scripts (`yst*.ybn`), o `ysc.ybn` ou o `yst_list.ybn`, cada um para `.cache/game/<caminho do pacote no jogo>/` com os caminhos internos (`.cache/game/ysbin.ypf/ysc.ybn`, `.cache/game/pac/ysbin.ypf/...`). Pacotes de imagem e áudio não são tocados (pesam gigabytes e não têm texto); use `vnf ypf unpack` se precisar de um deles. Cada rodada apaga `.cache/game/` antes de descompactar, então sobras de uma extração anterior não ficam.

Cada script vira `text/<chave>.json`, onde a chave é o nome do `.ybn` sem extensão, em minúsculas (`yst00012.ybn` vira `text/yst00012.json`), mesmo quando o script não tem texto (o arquivo traz `units` vazio). Os ids são os de `ScriptFile.UnitId`. O nome legível do `yst_list.ybn` (`01_prologo`) aparece só no `--json` (`texts[].name`), porque o arquivo de texto não tem campo para ele.

Um script ilegível aborta a extração com o nome do script na mensagem e nada é gravado em `text/`; se a gravação de `text/` falhar no meio, os arquivos já gravados são removidos. A re-extração que preserva traduções é a E3.5: com qualquer `text/*.json` na pasta, o `extract` recusa, sai com `1` e não altera nada, nem o `.cache/`. Para extrair do zero, mova ou apague a pasta `text/` (as traduções nela se perdem). `.cache/` dentro da pasta do jogo, também por link simbólico ou junção, é recusado antes de gravar, e um jogo sem nenhum script depois de descompactar é erro.

No texto, a saída é uma linha por pacote descompactado e o resumo, com o tipo de cada linha na ordem diálogos, narração, escolhas, nomes, sistema e outros (tipos com zero ficam de fora), seguidos de até 10 avisos dos scripts. Com `--json`, `data` traz `gameDirectory`, `cacheDirectory`, `archives` (`path`, `directory`, `files`, `bytes`), `scripts` (quantidade), `lines`, `kinds` (`dialogue`, `narration`, `choice`, `name`, `system`, `other`, todos presentes), `texts` (`key`, `name`, `lines` por script) e `warnings`.

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
- [ ] Distribuição: executável único autocontido (`dotnet publish -p:PublishSingleFile=true`) ou `dotnet tool install -g`?
