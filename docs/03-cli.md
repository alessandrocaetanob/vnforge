# 03 — CLI (`vnf`)

## Como funciona

- Um executável, `vnf`, com subcomandos por etapa do pipeline.
- Todo comando roda dentro de uma pasta de projeto (a atual ou `--project <pasta>`).
- Saída legível por padrão; `--json` em qualquer comando para automação e para o app consumir.
- Códigos de saída: `0` ok, `1` erro, `2` QA encontrou erros bloqueantes, `3` cancelado.
- Nada destrutivo sem confirmação; `--yes` pula a confirmação em scripts.
- `--verbose` / `--quiet` controlam o log; o log completo vai para `.cache/logs/`.
- Opções globais, válidas em qualquer posição da linha: `--project <pasta>`, `--json`, `--verbose`, `--quiet`, `--yes`. `--verbose` e `--quiet` não combinam. `--version` existe só na raiz; `--help` mostra a ajuda em português em qualquer comando.
- Ctrl+C cancela o comando em andamento e sai com `3`.
- Erro de uso (opção desconhecida, `--project` apontando para pasta que não existe) sai com `1` e a mensagem aponta `--help`.

### Onde vai o log

- Com `--project <pasta>` e a pasta existindo: `<pasta>\.cache\logs\`.
- Sem `--project`, com `vnforge.json` na pasta atual: `.cache\logs\` da pasta atual.
- Sem `--project` e fora de um projeto: `%LOCALAPPDATA%\VNForge\logs\`. A CLI nunca cria `.cache` em pasta qualquer, que pode ser a pasta do jogo. A busca de `vnforge.json` olha só a pasta atual, não sobe diretórios.

### Formato do `--json`

Uma linha de JSON por execução, sempre em stdout (nada vai para stderr). `exitCode` repete o código de saída do processo.

```json
{"ok":true,"exitCode":0,"data":{"name":"vnf","version":"0.1.0"}}
{"ok":false,"exitCode":1,"error":{"kind":"expected","message":"O arquivo 'a.ypf' é inválido: truncado"}}
```

`error.kind` é `expected` (erro previsto, a mensagem está em português), `unexpected` (bug; `message` genérica e `logDirectory` aponta o log com o stack) ou `cancelled` (código `3`). O stack trace nunca aparece. Cada comando define o conteúdo de `data`.

## Comandos do pipeline

| Comando | O que faz |
|---|---|
| `vnf init <pasta> --game <dir>` | Cria o projeto, detecta a engine e a versão, sugere configurações |
| `vnf detect [--game <dir>]` | Só diagnostica: engine, versão, arquivos YPF, chave XOR, codificação |
| `vnf extract` | Descompacta os arquivos do jogo para `.cache/` e gera `text/*.json`. Re-extrair preserva traduções existentes |
| `vnf status` | Progresso: linhas por status, por arquivo, por personagem |
| `vnf translate` | Traduz com IA as linhas pendentes |
| `vnf check` | Roda o QA e lista problemas |
| `vnf review` | Revisão por IA: gera sugestões nas notas, não altera traduções |
| `vnf preview <id>` | Mostra como a linha fica depois da formatação e quebra de linha |
| `vnf build` | Aplica formatação e codificação, reconstrói scripts e arquivos YPF em `build/` |
| `vnf inject` | Faz backup dos originais e copia o build para a pasta do jogo |
| `vnf restore` | Volta os arquivos originais do backup |
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
| `vnf ybn dump <arquivo>` | Despeja um script YSTB legível (comandos, argumentos, textos) |
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

## Fluxo típico

```console
$ vnf init minha-vn-ptbr --game "D:\Jogos\MinhaVN"
Engine detectada: YU-RIS (YPF v500, YSTB v4xx)
Arquivo de scripts: ysbin.ypf (1.284 arquivos)
Chave XOR dos scripts: 0xD36FAC96 (padrão)
Projeto criado em minha-vn-ptbr\

$ cd minha-vn-ptbr
$ vnf extract
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
