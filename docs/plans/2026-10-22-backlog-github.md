# Plano · backlog do VNForge no GitHub

Proposta de 2026-10-22 para aprovação. Transforma os marcos e épicos de [05-epicos.md](../05-epicos.md) em milestones, issues e um Project no repositório `alessandrocaetanob/vnforge`, que hoje não tem nenhuma issue.

Fontes: os documentos `docs/01` a `docs/06` e o rascunho anterior "Ferramenta de tradução de VNs · plano" (Claude Docs, 2026-10-17). O erro `ウィンドウが生成できませんでした` ao abrir o jogo original, que bloqueava a fase 0 daquele rascunho, já foi resolvido e não vira issue.

## Como fica no GitHub

| Item | Como |
|---|---|
| Marcos | Um milestone por marco (M1 a M4) e um `Pós-benchmark` para o E12 |
| Épicos | Uma issue com o rótulo `épico`, no milestone em que o épico termina |
| Tarefas e histórias | Sub-issues nativas do épico. Uma tarefa pode estar num milestone anterior ao do épico (E3 e E8 começam no M1) |
| Decisões | Perguntas em aberto dos documentos, com o rótulo `decisão`, como sub-issue do épico que elas bloqueiam |
| Dependências | Relação "blocked by" nativa, a partir da linha "Depende de" de cada item |
| Quadro | Project `VNForge` com as issues do repositório. O campo nativo "Parent issue" agrupa por épico; a área vem dos rótulos `área:`, então não há campo próprio para ela |

Issues do M1 e do M2 trazem escopo e critério de aceite. Do M3 em diante, o corpo é curto: o que é e quando está pronto. Elas ganham detalhe quando o M2 fechar.

### Milestones

| Chave | Título | Descrição |
|---|---|---|
| M1 | M1 · Ida e volta | `vnf extract` + `vnf build` + `vnf inject` sem nenhuma tradução gera um jogo que abre e roda igual ao original |
| M2 | M2 · Primeira cena em PT-BR | Uma cena traduzida à mão aparece no jogo com acentos e quebras de linha corretas |
| M3 | M3 · Tradução assistida | Um capítulo inteiro traduzido pela IA, conferido pelo QA e revisado no app |
| M4 | M4 · Benchmark completo | A VN YU-RIS inteira traduzida, jogável do início ao fim |
| Pós | Pós-benchmark | Provedores de IA, engines, imagens, memória de tradução e distribuição, depois do M4 |

### Rótulos

Os rótulos padrão do GitHub continuam. Entram estes:

| Rótulo | Cor | Descrição |
|---|---|---|
| `épico` | `5319e7` | Issue-pai que agrupa as tarefas de um épico |
| `decisão` | `fbca04` | Pergunta em aberto que precisa de resposta antes do trabalho seguir |
| `no jogo` | `b60205` | Validação com os arquivos do jogo, feita na máquina local |
| `área: core` | `0e8a16` | VNForge.Core |
| `área: yuris` | `1d76db` | VNForge.Engines.YuRis |
| `área: ia` | `5ebeff` | VNForge.AI |
| `área: cli` | `0052cc` | VNForge.Cli |
| `área: app` | `c2e0c6` | VNForge.App |
| `área: infra` | `bfd4f2` | Solução, CI e publicação |

O rótulo `no jogo` marca o que só pode ser validado com o jogo instalado. Um worker do `/distribuir` não tem o jogo, então essas issues ficam fora da distribuição.

### Resumo

| Marco | Épicos que fecham nele | Issues filhas |
|---|---|---|
| M1 | E0, E1, E2 (mais E3.1 a E3.4 e E8.1 a E8.3) | 31 |
| M2 | E3, E4, E5 | 22 |
| M3 | E6, E7, E8 (mais E9.1 e a fase 1 do E10) | 27 |
| M4 | E9, E10, E11 | 18 |
| Pós-benchmark | Cinco épicos do E12, sem filhas | 0 |

São 17 épicos e 98 filhas, 6 delas de decisão: 115 issues.

## Ajustes nos documentos

Este PR também corrige estes pontos:

- `05-epicos.md`: o M1 usa `vnf extract`, `vnf build` e `vnf inject`, que estão no E3 e no E8. As quatro primeiras tarefas do E3 e as três primeiras do E8 passam para o M1, e o doc passa a dizer isso.
- `03-cli.md`: o nome do executável (`vnf`) já está decidido no `README.md` e no `01-nome.md`; sai da lista "Em aberto".
- `README.md` de `docs/`: ganha a linha deste plano.

O `02-arquitetura.md` cita `project.json` para o estado do pipeline e `vnforge.json` na estrutura da pasta. A E3.1 resolve essa ambiguidade e corrige o doc.

## Pontos para revisar

- O M1 com arquivos idênticos prova o pipeline, mas não a reescrita: o jogo só lê um script reconstruído com mudança na E4.7. Para antecipar esse risco, a E8.3 pode incluir uma fala trocada por texto ASCII.
- A E5.6 (`remap`) só é feita se a E5.3 concluir que `cp1252` não funciona. Caso contrário, fecha como "not planned".
- Ficaram de fora, como você escolheu, estes itens do rascunho anterior: teste do pacote reempacotado como fase própria, segundo jogo YU-RIS como corpus de teste, auditoria de licenças e o tunneling com `sjis_ext.bin` e DLL proxy como quarta estratégia de acentuação.

## Como a criação roda

1. Você roda `gh auth refresh -s project`. O token atual tem `repo`, `workflow`, `gist` e `read:org`; o Project precisa do escopo `project`.
2. Um script no scratchpad lê este arquivo e chama o `gh`, nesta ordem: rótulos, milestones, épicos, filhas com `gh issue create --parent`, dependências pela API `issues/{n}/dependencies/blocked_by`, Project e itens. O `git-guard` bloqueia argumento dinâmico do `gh` direto no terminal, então o script só roda com a sua autorização.
3. O script grava o número de cada issue ao lado do título neste arquivo, e o commit desse resultado entra no mesmo PR.
4. As visões do Project (quadro por Status, tabela agrupada por "Parent issue") são configuradas na interface web.

Formato que o script lê: cada `##` com chave (`E0`, `E12a`) é um épico; cada `###` dentro dele é uma filha. As linhas `Marco`, `Rótulos` e `Depende de` logo abaixo do título viram metadados; o resto vira o corpo da issue. O título da filha é o texto depois de `·`, sem a chave.

## E0 · Fundação

- Marco: M1
- Rótulos: área: infra

Repositório pronto para trabalhar: solução .NET 10, CI e o esqueleto comum da CLI. O `.gitignore` que bloqueia arquivos de jogo já está no `main`.

Pronto quando: `dotnet build` e `dotnet test` passam no CI.

### E0.1 · Solução .NET 10 com os projetos vazios

- Marco: M1
- Rótulos: área: infra

Criar a solução descrita em `docs/02-arquitetura.md`, seção "Estrutura da solução".

Escopo:

- `VNForge.slnx` e `global.json` com o SDK 10.0.x
- `Directory.Build.props`: `net10.0`, C# 14, nullable habilitado, warnings como erro, versão
- `Directory.Packages.props` com as versões centralizadas
- projetos `src/VNForge.Core`, `src/VNForge.Engines.YuRis`, `src/VNForge.AI`, `src/VNForge.Cli`, `src/VNForge.App` (`net10.0-windows`) e os três de `tests/`, com xUnit
- referências entre projetos como no diagrama de dependências do doc 02

Pronto quando:

- [ ] `dotnet build` e `dotnet test` passam no Windows
- [ ] Um filtro de solução (`.slnf`) sem o app compila e testa no Linux

### E0.2 · CI no GitHub Actions

- Marco: M1
- Rótulos: área: infra
- Depende de: E0.1

Workflow em PR e em push no `main` com dois jobs: Linux (tudo menos o app) e Windows (a solução inteira), cada um com build e testes.

Pronto quando:

- [ ] Um PR mostra os dois jobs verdes
- [ ] Um teste que falha deixa o PR vermelho

### E0.3 · Logging e erros comuns

- Marco: M1
- Rótulos: área: core
- Depende de: E0.1

`Microsoft.Extensions.Logging` configurado num só lugar para a CLI e o app. Erro esperado (arquivo inválido, versão desconhecida) vira uma exceção do Core com mensagem em português; erro inesperado vai completo para o log.

Pronto quando:

- [ ] O log completo vai para `.cache/logs/` do projeto
- [ ] Uma exceção do Core chega ao console como mensagem curta em português, sem stack trace

### E0.4 · Esqueleto da CLI `vnf`

- Marco: M1
- Rótulos: área: cli
- Depende de: E0.1, E0.3

Executável `vnf` com `System.CommandLine` 2.x e as convenções de `docs/03-cli.md`, seção "Como funciona".

Escopo:

- opções globais `--project`, `--json`, `--verbose`, `--quiet` e `--yes`
- códigos de saída `0` ok, `1` erro, `2` QA bloqueante, `3` cancelado
- formato comum de `--json` para resultado e para erro
- Ctrl+C cancela o comando e sai com `3`

Pronto quando:

- [ ] `vnf --help` em português
- [ ] Testes cobrem os códigos de saída e o formato `--json` de `vnf --version`

### E0.5 · Codificação Shift-JIS registrada uma vez

- Marco: M1
- Rótulos: área: core
- Depende de: E0.1

Registrar `CodePagesEncodingProvider` num ponto único de inicialização e expor Shift-JIS (página 932) e Windows-1252 para o resto do código.

Pronto quando:

- [ ] Teste converte texto japonês de e para Shift-JIS e confere os bytes
- [ ] Nenhum outro ponto do código registra o provedor

## E1 · YU-RIS: arquivos YPF

- Marco: M1
- Rótulos: área: yuris

Ler e reescrever `ysbin.ypf` e os demais `.ypf` com reescrita mínima. Formato em `docs/06-yuris-notas-tecnicas.md`, seção "Arquivo YPF".

Pronto quando: unpack + pack sem mudanças gera um arquivo idêntico ao `ysbin.ypf` original do jogo.

### E1.1 · Conferir a versão do YPF no jogo

- Marco: M1
- Rótulos: área: yuris, no jogo

A versão define hash, tamanho do offset e tabela de nomes, e diz qual variante testar primeiro. Os bytes 4 a 7 de `ysbin.ypf` guardam a versão em little-endian (`Format-Hex ysbin.ypf | Select-Object -First 2`).

Pronto quando:

- [ ] Versão do `ysbin.ypf` e dos outros `.ypf` do jogo registrada no doc 06

### E1.2 · Contratos de engine e de arquivo no Core

- Marco: M1
- Rótulos: área: core
- Depende de: E0.1

Interfaces de `docs/02-arquitetura.md`, seção "Abstração de engine", sem implementação: `IEngine`, `EngineDetection`, `IArchiveFormat` e `IGamePatch`. `IScriptFormat` fica para a E2.1.

Pronto quando:

- [ ] As interfaces compilam no Core sem referência a YU-RIS
- [ ] Um teste de arquitetura falha se o Core referenciar `VNForge.Engines.*` ou `VNForge.AI`

### E1.3 · Hashes Adler32, CRC32 e MurmurHash2

- Marco: M1
- Rótulos: área: yuris
- Depende de: E0.1

CRC32 vem de `System.IO.Hashing`. Adler32 e MurmurHash2 (seed 0) são implementação própria.

Pronto quando:

- [ ] Testes com vetores conhecidos para os três algoritmos

### E1.4 · Gerador de YPF sintético para testes

- Marco: M1
- Rótulos: área: yuris
- Depende de: E1.3

Os testes não podem usar arquivos do jogo, então cada variante precisa de um YPF montado em código.

Escopo:

- versões abaixo de 265, de 265 a 478 e a partir de 479 (offset de 32 e de 64 bits)
- tabela de troca do tamanho do nome padrão e a da v500
- chave XOR do nome configurável, entradas com e sem zlib

Pronto quando:

- [ ] O gerador monta um YPF de cada variante com arquivos de conteúdo conhecido

### E1.5 · Leitor do cabeçalho e do índice YPF

- Marco: M1
- Rótulos: área: yuris
- Depende de: E1.2, E1.4

Escopo:

- cabeçalho, contagem de arquivos e tamanho do cabeçalho
- tamanho do nome decodificado (`~b` e tabela de troca), tentando as tabelas conhecidas e validando pelo hash do nome
- chave XOR do nome deduzida pelo `.` da extensão
- offset de 32 ou 64 bits conforme a versão
- algoritmo de hash de nome e de dados detectado pela comparação com o índice

Pronto quando:

- [ ] Testes leem o índice de cada variante do gerador sintético
- [ ] Versão desconhecida ou índice inconsistente gera erro em português que diz o que falhou

### E1.6 · Extração das entradas com zlib

- Marco: M1
- Rótulos: área: yuris
- Depende de: E1.5

Descomprimir com `ZLibStream` as entradas marcadas como comprimidas e conferir tamanho e hash dos dados.

Pronto quando:

- [ ] Testes extraem todas as entradas do YPF sintético e comparam com o conteúdo original
- [ ] Hash de dados divergente gera erro, nunca um arquivo corrompido

### E1.7 · Escritor YPF com reescrita mínima

- Marco: M1
- Rótulos: área: yuris
- Depende de: E1.6

Regra 3 do `CLAUDE.md`: entradas que não mudaram são copiadas byte a byte.

Escopo:

- manter versão, chave, tabela e ordem das entradas do original
- recomprimir só as entradas alteradas e recalcular o hash com o algoritmo detectado
- recalcular offsets e cabeçalho

Pronto quando:

- [ ] Ida e volta sem mudanças gera arquivo idêntico, para cada variante
- [ ] Com uma entrada trocada, as outras saem idênticas e a trocada é lida de volta com o conteúdo novo

### E1.8 · Comandos `vnf ypf info`, `unpack` e `pack`

- Marco: M1
- Rótulos: área: cli
- Depende de: E0.4, E1.7

`info` mostra versão, variante, hashes e a lista de arquivos. `unpack` extrai para uma pasta. `pack` remonta a partir da pasta e do YPF original, com a reescrita mínima.

Pronto quando:

- [ ] Os três comandos aceitam `--json`
- [ ] `pack` sem mudanças na pasta gera arquivo idêntico ao original

### E1.9 · Ida e volta do YPF do jogo

- Marco: M1
- Rótulos: área: yuris, no jogo
- Depende de: E1.1, E1.8

O `unpack` lê a pasta do jogo e o `pack` grava em outra pasta; nada é escrito no jogo.

Pronto quando:

- [ ] `vnf ypf unpack` + `vnf ypf pack` sem mudanças gera arquivo idêntico ao `ysbin.ypf` do jogo
- [ ] Os outros `.ypf` do jogo passam pela mesma ida e volta
- [ ] Variante encontrada registrada no doc 06

## E2 · YU-RIS: scripts YSTB

- Marco: M1
- Rótulos: área: yuris

Tirar e recolocar texto nos `.ybn`. Formato em `docs/06-yuris-notas-tecnicas.md`, seção "Scripts YSTB".

Pronto quando: extrair e reconstruir sem mudanças gera scripts idênticos em todo o jogo. Com as tarefas do E3 e do E8 marcadas para o M1, isso fecha o M1.

### E2.1 · Contratos de script e `TextUnit` no Core

- Marco: M1
- Rótulos: área: core
- Depende de: E1.2

Tipos de `docs/02-arquitetura.md`, seções "Abstração de engine" e "Unidade de texto": `IScriptFormat`, `ScriptFile`, `ScriptExtraction`, `EngineContext` e `TextUnit` com `id`, `kind`, `speaker`, `source`, `translation`, `status`, `origin`, `notes`, `flags` e `locator` opaco.

Pronto quando:

- [ ] Os tipos compilam no Core sem referência a YU-RIS
- [ ] `kind` e `status` cobrem os valores listados no doc 02

### E2.2 · `ITextEncoder` com a estratégia Shift-JIS pura

- Marco: M1
- Rótulos: área: core
- Depende de: E0.5, E2.1

A reconstrução precisa de um codificador antes do E5. Esta tarefa cria a interface e a estratégia que só converte para Shift-JIS; `fold`, `remap` e `cp1252` entram no E5.

Pronto quando:

- [ ] A interface informa os caracteres que a estratégia não consegue representar
- [ ] Texto japonês sai byte a byte igual à conversão direta para a página 932

### E2.3 · Marcadores de controle

- Marco: M1
- Rótulos: área: yuris
- Depende de: E2.1

Converter os códigos de controle do texto em marcadores legíveis e de volta: `EF F0` vira `\n`, `EF F2` vira `\p`, `EF F3` vira `\c` e `EF F5` vira `\u`.

Pronto quando:

- [ ] Ida e volta de bytes com todos os códigos gera os mesmos bytes
- [ ] Código `EF` desconhecido vira um marcador genérico que preserva o byte
- [ ] Barra invertida que já existe no texto não vira marcador por engano

### E2.4 · Leitores YSCM e YSTL

- Marco: M1
- Rótulos: área: yuris
- Depende de: E2.1

`ysc.ybn` (YSCM) lista os comandos na ordem do id; dele saem os opcodes de `WORD`, `_`, `RETURNCODE`, `GOSUB` e `CG`. `yst_list.ybn` (YSTL) dá o caminho original de cada script, usado para nomes legíveis.

Pronto quando:

- [ ] Testes com YSCM e YSTL sintéticos
- [ ] Falta de um comando esperado gera erro que nomeia o comando

### E2.5 · Leitor YSTB a partir da versão 300

- Marco: M1
- Rótulos: área: yuris
- Depende de: E2.4

Escopo:

- cabeçalho e as quatro seções
- chave XOR pelos bytes 8 a 11 da seção de argumentos, com `0xD36FAC96` e `0x07B4024A` como alternativas validadas
- instruções e argumentos decodificados, com o opcode resolvido pelo YSCM
- gerador de YSTB sintético para os testes

Pronto quando:

- [ ] Testes leem scripts sintéticos com cada chave conhecida e com uma chave fora da lista
- [ ] Versão abaixo de 300 gera erro dizendo que esse layout não é suportado

### E2.6 · Extração de texto dos scripts

- Marco: M1
- Rótulos: área: yuris
- Depende de: E2.3, E2.5

Escopo:

- diálogos de `WORD`, juntando `WORD` seguidos numa mensagem
- falante no formato `名前「fala」` separado em `speaker`
- escolhas (`ES.SEL.SET`) e nomes (`ES.CHAR.NAME`) em chamadas `GOSUB`
- outras strings de expressão com japonês, com `kind: other`
- `locator` com instrução e argumentos para a reinjeção

Pronto quando:

- [ ] Teste com script sintético extrai cada tipo com o `kind` certo
- [ ] Os ids das unidades são os mesmos em duas extrações do mesmo arquivo

### E2.7 · Reconstrução dos scripts

- Marco: M1
- Rótulos: área: yuris
- Depende de: E2.2, E2.6

Textos novos vão para o fim da seção de recursos, e só os argumentos alterados ganham `size` e `offset` novos. Os demais argumentos, inclusive os de `RETURNCODE`, ficam intactos.

Escopo:

- mensagem de vários `WORD`: tradução inteira no primeiro, os outros vazios
- strings de expressão delimitadas com crase quando o texto tem aspas
- seções cifradas de novo com a mesma chave

Pronto quando:

- [ ] Reconstrução sem mudanças gera arquivo idêntico
- [ ] Com uma linha trocada, os outros argumentos saem idênticos e a linha nova é lida de volta

### E2.8 · Detecção YU-RIS e `vnf detect`

- Marco: M1
- Rótulos: área: yuris, área: cli
- Depende de: E0.4, E1.5, E2.5

`IEngine.Detect` reconhece uma pasta de jogo YU-RIS e informa versão do YPF, arquivos YPF, versão do YSTB e chave XOR. `vnf detect --game <dir>` mostra o diagnóstico sem gravar nada.

Pronto quando:

- [ ] Pasta sem YU-RIS gera a resposta "engine não reconhecida", sem exceção
- [ ] Teste com uma pasta de jogo sintética

### E2.9 · Comando `vnf ybn dump`

- Marco: M1
- Rótulos: área: cli
- Depende de: E0.4, E2.6

Despeja um script YSTB legível: comandos, argumentos e textos com marcadores.

Pronto quando:

- [ ] Saída em texto e em `--json`, testada com um script sintético

### E2.10 · Ida e volta dos scripts do jogo

- Marco: M1
- Rótulos: área: yuris, no jogo
- Depende de: E1.9, E2.7, E2.8

Pronto quando:

- [ ] Extrair e reconstruir sem mudanças gera arquivos idênticos para todos os `yst*.ybn`
- [ ] Versão do YSTB, chave XOR e contagem de unidades por `kind` registradas no doc 06

## E3 · Modelo de projeto e formato de texto

- Marco: M2
- Rótulos: área: core

Pasta de projeto e formato de texto de `docs/02-arquitetura.md`, seção "Projeto de tradução no disco". As quatro primeiras tarefas entram no M1, porque o M1 já usa `vnf extract`.

Pronto quando: re-extrair preserva traduções, e `vnf status` e a exportação funcionam.

### E3.1 · Pasta de projeto e `vnforge.json`

- Marco: M1
- Rótulos: área: core
- Depende de: E0.1

Escopo:

- leitura, validação e gravação de `vnforge.json` com os campos do exemplo do doc 02
- pastas `text/`, `.cache/`, `build/` e `backup/`
- o doc 02 cita `project.json` para o estado do pipeline e só `vnforge.json` na estrutura da pasta: decidir se o estado fica num arquivo separado e corrigir o doc

Pronto quando:

- [ ] `vnforge.json` inválido gera erro que aponta o campo
- [ ] Doc 02 sem a ambiguidade entre `project.json` e `vnforge.json`

### E3.2 · Arquivos `text/*.json`

- Marco: M1
- Rótulos: área: core
- Depende de: E2.1, E3.1

Um arquivo por script com as `TextUnit` dele, legível em diff e versionável no git.

Escopo:

- ids estáveis no formato `<script>:<nnnn>`
- gravação estável: mesma ordem, mesma indentação, UTF-8 sem BOM, fim de linha fixo
- marcadores de controle preservados como texto

Pronto quando:

- [ ] Gravar duas vezes o mesmo conteúdo gera arquivos idênticos
- [ ] Ida e volta de JSON para `TextUnit` e de volta para JSON sem perda

### E3.3 · Comando `vnf init`

- Marco: M1
- Rótulos: área: cli
- Depende de: E2.8, E3.1

`vnf init <pasta> --game <dir>` cria o projeto, roda a detecção e grava engine, versão e chave no `vnforge.json`.

Pronto quando:

- [ ] Pasta que já tem projeto gera erro, sem sobrescrever
- [ ] Saída como no exemplo de `docs/03-cli.md`, seção "Fluxo típico"

### E3.4 · Comando `vnf extract`

- Marco: M1
- Rótulos: área: core, área: cli
- Depende de: E1.6, E2.6, E3.2, E3.3

Descompacta os arquivos do jogo para `.cache/` e gera `text/*.json`. Esta tarefa cobre a primeira extração; a re-extração que preserva traduções é a E3.5.

Pronto quando:

- [ ] A pasta do jogo não é alterada
- [ ] Resumo com scripts e linhas por `kind`, como no doc 03

### E3.5 · Re-extração que preserva traduções

- Marco: M2
- Rótulos: área: core
- Depende de: E3.4

Rodar `vnf extract` de novo mantém as traduções, casando por id e pelo hash do original. Linha cujo original mudou fica marcada para revisão e guarda a tradução antiga; a tarefa decide se a marca é um status ou uma flag.

Pronto quando:

- [ ] Re-extração sem mudança no jogo não altera `text/*.json`
- [ ] Original alterado fica marcado e a tradução antiga não se perde

### E3.6 · Hashes dos originais e estado do pipeline

- Marco: M2
- Rótulos: área: core
- Depende de: E3.4

A extração grava o hash dos arquivos originais do jogo, que a E8.5 usa para recusar a injeção quando o jogo mudou. Cada etapa grava o próprio estado, para a CLI e o app continuarem de onde o outro parou.

Pronto quando:

- [ ] Hashes dos originais gravados no projeto na extração
- [ ] Rodar uma etapa duas vezes sem mudança não altera nada

### E3.7 · Glossário e personagens

- Marco: M2
- Rótulos: área: core
- Depende de: E3.1

Modelo, leitura e gravação de `glossary.json` (termo original e tradução) e `characters.json` (nome original, tradução, gênero, tom de fala). Os comandos `vnf glossary` e `vnf chars` ficam na E9.1.

Pronto quando:

- [ ] Ida e volta dos dois arquivos sem perda
- [ ] O `speaker` extraído encontra a entrada certa em `characters.json`

### E3.8 · Comando `vnf status`

- Marco: M2
- Rótulos: área: cli
- Depende de: E3.4

Progresso por status, por arquivo e por personagem, com `--json`.

Pronto quando:

- [ ] Os números batem com a contagem feita direto nos `text/*.json` num teste

### E3.9 · Exportação e importação CSV

- Marco: M2
- Rótulos: área: core, área: cli
- Depende de: E3.2

`vnf export --format csv` e `vnf import <arquivo>`, casando por id. A importação só altera tradução, status e notas.

Pronto quando:

- [ ] Exportar e importar sem edição não altera `text/*.json`
- [ ] Id desconhecido no arquivo importado aparece como erro, e nada é importado pela metade

### D1 · Decisão: formatos de exportação para revisores

- Marco: M2
- Rótulos: decisão, área: core

O E3 cita CSV e XLSX; `docs/03-cli.md` cita também `.po`. Decidir quais formatos entram além do CSV e qual biblioteca lê e grava XLSX.

Pronto quando:

- [ ] Decisão registrada no doc 02, seção "Em aberto"

### E3.10 · Exportação e importação nos formatos decididos

- Marco: M2
- Rótulos: área: core, área: cli
- Depende de: E3.9, D1

Os formatos escolhidos na D1, com o mesmo contrato da E3.9: casar por id e só alterar tradução, status e notas.

Pronto quando:

- [ ] Ida e volta sem edição não altera `text/*.json`, em cada formato

## E4 · Formatação e espaçamento

- Marco: M2
- Rótulos: área: core

Regras aplicadas no build, não no texto salvo, para mudar uma regra e reconstruir sem retraduzir. Regras em `docs/02-arquitetura.md`, seção "Formatação (espaçamento)". Os testes usam frases reais em português, com os casos de ponta do doc 05.

Pronto quando: `vnf build` aplica o perfil e `vnf preview` mostra o resultado de cada linha.

### E4.1 · Perfil `formatting.json` e pipeline de regras

- Marco: M2
- Rótulos: área: core
- Depende de: E3.1, E8.1

Escopo:

- modelo do perfil, com cada regra liga/desliga e valores padrão
- pipeline que aplica as etapas na ordem do doc 02 e preserva os marcadores de controle
- `vnf build` aplica o perfil antes de codificar
- perfil de exemplo em `samples/`

Pronto quando:

- [ ] Perfil sem um campo usa o valor padrão
- [ ] Nenhuma regra altera marcadores de controle (teste com todos os marcadores)

### E4.2 · Regras de normalização e espaços

- Marco: M2
- Rótulos: área: core
- Depende de: E4.1

NFC, espaços invisíveis, NBSP para espaço, espaços duplos, espaço antes de `? ! : ;` e espaço depois de pontuação.

Pronto quando:

- [ ] Cada regra tem teste ligada e desligada
- [ ] `Ei, você?!` e `Ah...` saem sem espaço a mais

### E4.3 · Regras de tipografia e largura

- Marco: M2
- Rótulos: área: core
- Depende de: E4.1

Aspas `“ ”` para `" "` ou `「」`, `...` e `…` nos dois sentidos, travessão para `―` (que existe em Shift-JIS) e ASCII de meia largura para largura cheia e de volta.

Pronto quando:

- [ ] Cada regra tem teste ligada e desligada
- [ ] Fala que abre com travessão sai com o caractere escolhido no perfil

### E4.4 · Quebra de linha por palavra

- Marco: M2
- Rótulos: área: core
- Depende de: E4.1

Quebra que respeita a largura máxima em colunas e o máximo de linhas por caixa, com largura por caractere configurável. Os `\n` que já estão no texto são respeitados.

Pronto quando:

- [ ] Palavra maior que a largura é quebrada no limite, sem laço infinito
- [ ] Texto acima do limite de linhas é informado ao chamador, para o QA do E7

### E4.5 · Substituições do usuário

- Marco: M2
- Rótulos: área: core
- Depende de: E4.1

Lista de substituições literais e regex, antes e depois das regras do perfil.

Pronto quando:

- [ ] Regex inválida gera erro que aponta a regra no `formatting.json`
- [ ] Teste com uma substituição antes e outra depois das regras

### E4.6 · Comando `vnf preview`

- Marco: M2
- Rótulos: área: cli
- Depende de: E0.4, E4.4

`vnf preview <id>` mostra a linha depois da formatação e da quebra, com a contagem de linhas e colunas.

Pronto quando:

- [ ] Saída em texto e em `--json`

### E4.7 · Largura e linhas da caixa de texto medidas no jogo

- Marco: M2
- Rótulos: área: core, no jogo
- Depende de: E4.6, E8.3

Descobrir quantas colunas e linhas cabem na caixa de diálogo do jogo, com uma fala longa de teste em ASCII, e gravar os valores no perfil de exemplo. É a primeira vez que o jogo lê um script reconstruído com mudança.

Pronto quando:

- [ ] A fala de teste quebra no jogo onde `vnf preview` mostra
- [ ] O jogo aceita o script e o YPF alterados

## E5 · Acentuação e codificação PT-BR

- Marco: M2
- Rótulos: área: core

Shift-JIS não tem as letras acentuadas do português. As estratégias `fold`, `remap` e `cp1252` estão em `docs/02-arquitetura.md`, seção "Codificação para português"; os detalhes do executável estão em `docs/06-yuris-notas-tecnicas.md`, seção "Acentuação: o problema central".

Pronto quando: M2 atingido.

### E5.1 · Escolha da estratégia e relatório de caracteres não codificáveis

- Marco: M2
- Rótulos: área: core
- Depende de: E2.2, E3.1

Escopo:

- estratégia escolhida no `vnforge.json`
- relatório com cada caractere que a estratégia não representa, a linha onde aparece e o substituto usado

Pronto quando:

- [ ] Teste com `«`, `ª` e `ã` em cada estratégia disponível

### E5.2 · Estratégia `fold`

- Marco: M2
- Rótulos: área: core
- Depende de: E5.1

Remove acentos (ação vira acao) e troca os sinais que não existem em Shift-JIS por equivalentes.

Pronto quando:

- [ ] Teste com todas as letras acentuadas do português, maiúsculas e minúsculas
- [ ] Nenhum caractere fora do Shift-JIS sobra depois do `fold`

### E5.3 · Pesquisa: lead bytes e charset da fonte no executável

- Marco: M2
- Rótulos: área: yuris, no jogo
- Depende de: E8.2

Responder no executável do jogo, sempre numa cópia ou pelo fluxo de injeção com backup:

- onde está a tabela de 256 bytes de lead bytes, e se zerá-la faz o motor tratar todo byte como caractere simples
- se a fonte precisa de `ANSI_CHARSET` em `CreateFontIndirectA`, e se a troca sai com patch direto ou só com uma DLL de hook
- o que quebra junto (nomes de arquivo e strings internas continuam em Shift-JIS)

Pronto quando:

- [ ] Resultado no doc 06, com o endereço da tabela e o caminho escolhido para a fonte
- [ ] Conclusão registrada: seguir com `cp1252` ou partir para `remap`

### D2 · Decisão: estratégia de codificação padrão

- Marco: M2
- Rótulos: decisão, área: core
- Depende de: E5.3

Decidir se projetos novos começam em `fold`, que funciona em qualquer jogo, e passam para `cp1252` quando o patch do executável estiver validado. Pergunta aberta em `docs/02-arquitetura.md`.

Pronto quando:

- [ ] Decisão registrada no doc 02, seção "Em aberto"

### E5.4 · Estratégia `cp1252`

- Marco: M2
- Rótulos: área: core
- Depende de: E5.1, E5.3

Codifica o texto exibido em Windows-1252. O byte `0xEF` (`ï`) colide com o prefixo dos códigos de controle, então `ï` é recusado.

Pronto quando:

- [ ] Teste com todas as letras acentuadas do português
- [ ] `ï` aparece no relatório de não codificáveis

### E5.5 · Comando `vnf patch-exe` (experimental)

- Marco: M2
- Rótulos: área: yuris, área: cli
- Depende de: E5.3, E8.2

Aplica no executável o patch descoberto na E5.3, pelo mesmo fluxo de backup e restauração da injeção.

Pronto quando:

- [ ] Executável com hash diferente do esperado é recusado, sem patch
- [ ] `vnf restore` devolve o executável original

### E5.6 · Estratégia `remap` com fonte modificada

- Marco: M2
- Rótulos: área: core
- Depende de: E5.1, E5.3

Só entra se a E5.3 concluir que `cp1252` não funciona; senão, fecha como "not planned". Mapeia as letras acentuadas para katakanas de meia largura (`0xA1` a `0xDF`) e gera a fonte com os glifos trocados. A tabela de mapeamento é editável no projeto.

Pronto quando:

- [ ] Teste de ida e volta do mapeamento
- [ ] A fonte gerada mostra as letras acentuadas no jogo

### E5.7 · Validação do M2 no jogo

- Marco: M2
- Rótulos: no jogo
- Depende de: E4.7, E5.4, D2

Traduzir à mão uma cena curta em `text/*.json`, rodar `vnf build` e `vnf inject` e conferir no jogo.

Pronto quando:

- [ ] Acentos certos em diálogo, nome de personagem e escolha
- [ ] Quebras de linha onde `vnf preview` mostra
- [ ] `vnf restore` volta o jogo ao original

## E6 · IA

- Marco: M3
- Rótulos: área: ia

Tradução com Claude pelo SDK oficial `Anthropic`, com toda chamada trocável por um provedor falso nos testes. Antes de escrever código que chama a API, conferir o formato atual com a skill `claude-api`, como manda o `CLAUDE.md`. Detalhes em `docs/02-arquitetura.md`, seção "IA".

Pronto quando: `vnf translate` traduz um capítulo e `vnf review` gera sugestões.

### E6.1 · `ITranslationProvider` e provedor falso

- Marco: M3
- Rótulos: área: ia
- Depende de: E2.1

Interface do doc 02 e um provedor falso determinístico, usado nos testes do E6 e do app.

Pronto quando:

- [ ] Os testes de `VNForge.AI` rodam sem rede e sem chave

### E6.2 · Provedor Claude

- Marco: M3
- Rótulos: área: ia
- Depende de: E6.1

SDK oficial `Anthropic` para C#, com os modelos `claude-opus-5-5` (padrão), `claude-sonnet-5-5`, `claude-haiku-5-5` e `claude-fable-5-1`, `effort` configurável e saída JSON por `output_config.format`.

Pronto quando:

- [ ] Teste de integração opcional, que só roda com `ANTHROPIC_API_KEY` definida

### E6.3 · Recusa e prompt caching

- Marco: M3
- Rótulos: área: ia
- Depende de: E6.2

Tratar `stop_reason: "refusal"` com o fallback do servidor e marcar a linha quando a recusa persistir. Prompt caching no prompt de sistema e no glossário, que se repetem em todo lote.

Pronto quando:

- [ ] Recusa simulada deixa a linha marcada sem derrubar o lote
- [ ] Tokens lidos do cache aparecem na contagem

### E6.4 · Montagem do prompt

- Marco: M3
- Rótulos: área: ia
- Depende de: E3.7, E6.1

Guia de estilo (`style.md`), glossário filtrado para o lote, personagens presentes e linhas anteriores já traduzidas.

Pronto quando:

- [ ] Teste confere que só os termos do glossário presentes no lote entram no prompt

### E6.5 · Validação da resposta e reenvio

- Marco: M3
- Rótulos: área: ia
- Depende de: E6.1

A resposta precisa ter os mesmos ids e os mesmos marcadores de controle do lote. Linhas com problema são reenviadas sozinhas.

Pronto quando:

- [ ] Id ausente ou `\p` perdido gera reenvio só daquela linha

### E6.6 · Lotes, concorrência, retry e cancelamento

- Marco: M3
- Rótulos: área: ia
- Depende de: E6.5

Lotes de 20 a 50 linhas (padrão 30), chamadas em paralelo (padrão 2), retry com backoff e cancelamento que grava o que já voltou.

Pronto quando:

- [ ] Cancelar no meio não perde as linhas já traduzidas

### E6.7 · Custo: estimativa, contagem e cache por hash

- Marco: M3
- Rótulos: área: ia
- Depende de: E6.6

`--dry-run` estima tokens e custo, a execução registra a contagem real de tokens, e um cache por hash da linha evita pagar duas vezes.

Pronto quando:

- [ ] A mesma tradução rodada duas vezes não chama a API na segunda

### E6.8 · Chave de API e `vnf config ai`

- Marco: M3
- Rótulos: área: ia, área: cli
- Depende de: E6.2

A chave fica só em `ANTHROPIC_API_KEY` ou no cofre do usuário com DPAPI (regra 4 do `CLAUDE.md`). `vnf config ai` grava modelo e chave fora do projeto.

Pronto quando:

- [ ] Teste confere que nenhum arquivo do projeto recebe a chave

### E6.9 · Comando `vnf translate`

- Marco: M3
- Rótulos: área: cli
- Depende de: E6.3, E6.4, E6.7, E6.8

Opções de `docs/03-cli.md`: `--files`, `--status`, `--model`, `--effort`, `--batch-size`, `--concurrency`, `--dry-run` e `--limit`. Grava `status: machine` e `origin` em cada linha traduzida.

Pronto quando:

- [ ] Progresso no console e resultado em `--json`

### E6.10 · Comando `vnf review`

- Marco: M3
- Rótulos: área: cli, área: ia
- Depende de: E6.9

Revisão por IA que grava sugestões em `notes`, sem alterar traduções.

Pronto quando:

- [ ] Teste confere que `translation` e `status` não mudam

## E7 · QA (conferência)

- Marco: M3
- Rótulos: área: core

Motor de regras com severidade: erro bloqueia o build, aviso não. Regras em `docs/02-arquitetura.md`, seção "QA (conferência)".

Pronto quando: M3 atingido, junto com o E6.

### E7.1 · Motor de regras e "marcar como ok"

- Marco: M3
- Rótulos: área: core
- Depende de: E3.2

Regras com id e severidade, filtro por regra e por severidade, e "marcar como ok" gravado em `flags` da linha, que silencia aquele aviso dali em diante.

Pronto quando:

- [ ] `vnf build` para em erro de QA, e com `--allow-warnings` só os avisos passam

### E7.2 · Regras de completude e marcadores

- Marco: M3
- Rótulos: área: core
- Depende de: E7.1

Linha não traduzida, tradução vazia e marcador de controle diferente do original.

Pronto quando:

- [ ] Cada regra tem teste com um caso que dispara e um que não dispara

### E7.3 · Regras de codificação e de caixa

- Marco: M3
- Rótulos: área: core
- Depende de: E4.4, E5.1, E7.1

Caractere não codificável na estratégia atual, texto acima do limite de linhas da caixa e tamanho em bytes acima do limite do campo (nomes, escolhas).

Pronto quando:

- [ ] Cada regra tem teste com um caso que dispara e um que não dispara

### E7.4 · Regras de texto

- Marco: M3
- Rótulos: área: core
- Depende de: E7.1

Espaço duplo, espaço antes de pontuação, aspas ou parênteses desbalanceados e japonês esquecido na tradução.

Pronto quando:

- [ ] Cada regra tem teste com um caso que dispara e um que não dispara

### E7.5 · Regras de consistência

- Marco: M3
- Rótulos: área: core
- Depende de: E3.7, E7.1

Termo do glossário ou nome de personagem traduzido diferente do definido, e o mesmo original traduzido de jeitos diferentes.

Pronto quando:

- [ ] Cada regra tem teste com um caso que dispara e um que não dispara

### E7.6 · Correção automática com `--fix`

- Marco: M3
- Rótulos: área: core
- Depende de: E7.4

Corrige só o que é seguro corrigir, como espaços duplos, e lista o que mudou.

Pronto quando:

- [ ] Rodar `--fix` duas vezes seguidas não muda nada na segunda

### E7.7 · Comando `vnf check`

- Marco: M3
- Rótulos: área: cli
- Depende de: E7.2

Opções `--rules`, `--severity` e `--fix`, código de saída `2` em erro bloqueante e saída como no exemplo do doc 03.

Pronto quando:

- [ ] Teste confere o código de saída `2` com um erro e `0` só com avisos

### E7.8 · Validação do M3

- Marco: M3
- Rótulos: no jogo
- Depende de: E6.9, E7.7, E10.3

Um capítulo inteiro traduzido por `vnf translate`, conferido por `vnf check`, revisado no app e testado no jogo.

Pronto quando:

- [ ] O capítulo roda no jogo sem erro de QA pendente

## E8 · Build e injeção

- Marco: M3
- Rótulos: área: core

`vnf build`, `vnf inject`, `vnf restore` e `vnf run`. As três primeiras tarefas entram no M1, porque o M1 já roda build e inject. Regra 2 do `CLAUDE.md`: nada escreve na pasta do jogo sem backup.

Pronto quando: build incremental, proteção contra jogo alterado e `vnf run` funcionam.

### E8.1 · Comando `vnf build`

- Marco: M1
- Rótulos: área: core, área: cli
- Depende de: E1.7, E2.7, E3.4

Reconstrói todos os scripts a partir de `text/*.json` e reempacota o YPF em `build/`, com a reescrita mínima do E1 e do E2. Nesta versão não há formatação e a codificação é a Shift-JIS pura; o perfil de formatação e as outras estratégias chegam no M2.

Pronto quando:

- [ ] Build sem nenhuma tradução gera `build/ysbin.ypf` idêntico ao original
- [ ] A pasta do jogo não é alterada

### E8.2 · Comandos `vnf inject` e `vnf restore`

- Marco: M1
- Rótulos: área: core, área: cli
- Depende de: E8.1

`inject` copia os originais para `backup/AAAA-MM-DD_HHmm/`, confere o hash da cópia e só então grava o build na pasta do jogo, conferindo o hash depois. `restore` volta os originais do backup mais recente.

Pronto quando:

- [ ] Falha no meio da injeção deixa o jogo como estava ou restaurável por `vnf restore`
- [ ] Confirmação antes de gravar, pulada com `--yes`
- [ ] Testes com uma pasta de jogo sintética

### E8.3 · Validação do M1 no jogo

- Marco: M1
- Rótulos: no jogo
- Depende de: E2.10, E8.2

`vnf init`, `vnf extract`, `vnf build` e `vnf inject` sem tradução, como no doc 05.

Pronto quando:

- [ ] O jogo abre e roda igual ao original
- [ ] `vnf restore` deixa a pasta do jogo com os hashes originais

### E8.4 · Build incremental e opções

- Marco: M3
- Rótulos: área: core, área: cli
- Depende de: E8.1

`--only-changed` como padrão (só scripts com tradução alterada), `--allow-warnings` e `--encoding <estratégia>`.

Pronto quando:

- [ ] Build sem mudanças desde o anterior não reconstrói nenhum script

### E8.5 · Proteção contra jogo alterado

- Marco: M3
- Rótulos: área: core
- Depende de: E3.6, E8.2

`vnf inject` recusa rodar quando os arquivos do jogo mudaram desde a extração, por exemplo depois de uma atualização, e diz quais mudaram.

Pronto quando:

- [ ] Teste com um arquivo do jogo sintético alterado depois da extração

### E8.6 · Comando `vnf run`

- Marco: M3
- Rótulos: área: cli
- Depende de: E3.1

Abre o executável do jogo registrado no `vnforge.json`.

Pronto quando:

- [ ] Executável ausente gera erro que mostra o caminho procurado

## E9 · CLI completa

- Marco: M4
- Rótulos: área: cli

Todos os comandos de `docs/03-cli.md`, ajuda em português e `--json` em tudo. Os comandos de cada etapa nascem nos épicos da etapa; este épico fecha o que falta e publica o executável.

Pronto quando: todos os comandos do doc 03 existem e a CLI é publicada pelo CI.

### E9.1 · Comandos `vnf glossary` e `vnf chars`

- Marco: M3
- Rótulos: área: cli
- Depende de: E3.7

`vnf glossary add "先輩" "veterano"` e `vnf chars list|set`, gravando nos arquivos da E3.7.

Pronto quando:

- [ ] Os dois comandos aceitam `--json`

### E9.2 · Ajuda, `--json` e confirmações em todos os comandos

- Marco: M4
- Rótulos: área: cli

Revisão final: ajuda em português, `--json` no mesmo formato e confirmação antes de ação destrutiva, pulada com `--yes`.

Pronto quando:

- [ ] Teste percorre todos os comandos e confere ajuda e `--json`

### D5 · Decisão: distribuição da CLI

- Marco: M4
- Rótulos: decisão, área: cli

Executável único autocontido (`dotnet publish -p:PublishSingleFile=true`) ou `dotnet tool install -g`. Pergunta aberta em `docs/03-cli.md`.

Pronto quando:

- [ ] Decisão registrada no doc 03, seção "Em aberto"

### E9.3 · Publicação da CLI

- Marco: M4
- Rótulos: área: infra
- Depende de: D5

Publicação no formato decidido na D5, gerada pelo CI a cada tag de versão.

Pronto quando:

- [ ] Uma tag de teste gera o pacote da CLI no CI

## E10 · App Windows

- Marco: M4
- Rótulos: área: app

MVVM com CommunityToolkit.Mvvm e nenhuma regra de negócio nas telas. Telas em `docs/04-app-windows.md`; quando o texto divergir do [protótipo](https://claude.ai/artifact/36wMrNUfoMvXmhRNdJQhrn), vale o protótipo. A fase 1 entra no M3, porque o M3 inclui revisão no app.

Pronto quando: as quatro fases do doc 05 estão no app.

### D3 · Decisão: WPF ou Avalonia

- Marco: M3
- Rótulos: decisão, área: app

Pergunta aberta em `docs/02-arquitetura.md`. WPF é só Windows e já tem DataGrid para milhares de linhas; Avalonia roda também em Linux e macOS. Precisa de resposta antes da E10.1.

Pronto quando:

- [ ] Decisão registrada no doc 02 e, se mudar, no doc 04

### D4 · Decisão: telas da primeira versão do app

- Marco: M3
- Rótulos: decisão, área: app

O doc 04 sugere Início, Editor e Build e injeção para a primeira versão; o doc 05 põe Build e injeção na fase 2. Decidir o que entra no M3.

Pronto quando:

- [ ] Decisão registrada no doc 04, seção "Em aberto"

### E10.1 · Janela principal e tela Início

- Marco: M3
- Rótulos: área: app
- Depende de: D3, D4, E3.1

Tema Fluent claro ou escuro seguindo o Windows, navegação entre telas e a tela Início com novo projeto, abrir projeto e recentes.

Pronto quando:

- [ ] Abrir um projeto criado pela CLI mostra o nome e o progresso

### E10.2 · Editor: scripts e grade de linhas

- Marco: M3
- Rótulos: área: app
- Depende de: E3.2, E10.1

Lista de scripts com progresso, grade virtualizada para dezenas de milhares de linhas, cores por status e filtros (pendentes, com aviso, personagem).

Pronto quando:

- [ ] A grade rola sem travar com 50 mil linhas sintéticas

### E10.3 · Editor: painel de detalhe e aprovação

- Marco: M3
- Rótulos: área: app
- Depende de: E10.2

Original, tradução editável, aprovar e notas. Marcadores de controle aparecem como chips que não se editam por engano. Salvamento automático a cada edição e atalhos `Ctrl+Enter` (aprova e vai para a próxima) e `Alt+↑/↓`.

Pronto quando:

- [ ] Editar no app e rodar `vnf status` mostra a mudança

### E10.4 · Tarefas em segundo plano

- Marco: M4
- Rótulos: área: app
- Depende de: E6.9, E10.1

Extrair, traduzir, build e injetar em segundo plano, com progresso, custo e cancelar na barra inferior, sem travar a edição.

Pronto quando:

- [ ] Dá para editar outro script enquanto uma tradução roda

### E10.5 · Painel do projeto

- Marco: M4
- Rótulos: área: app
- Depende de: E3.8, E10.1

Etapas do pipeline com pendências, progresso por status, arquivo e personagem, e custo de IA até o momento.

Pronto quando:

- [ ] Os números batem com `vnf status`

### E10.6 · Tela Build e injeção

- Marco: M4
- Rótulos: área: app
- Depende de: E8.2, E10.4

Log em tempo real, resumo do que mudou, botões Injetar, Abrir jogo e Restaurar originais, e o local do backup.

Pronto quando:

- [ ] Build, injeção e restauração rodam pelo app numa pasta de jogo sintética

### E10.7 · Prévia da caixa de texto

- Marco: M4
- Rótulos: área: app
- Depende de: E4.4, E10.3

Desenha a linha selecionada com a fonte, a largura em colunas e o limite de linhas do perfil, recalculando enquanto a tradução é editada.

Pronto quando:

- [ ] A prévia quebra nos mesmos pontos que `vnf preview`

### D6 · Decisão: prévia com a fonte real do jogo

- Marco: M4
- Rótulos: decisão, área: app

Pergunta aberta em `docs/04-app-windows.md`. O nome da fonte pode ser lido da configuração do YU-RIS.

Pronto quando:

- [ ] Decisão registrada no doc 04, seção "Em aberto"

### E10.8 · Tela Conferência

- Marco: M4
- Rótulos: área: app
- Depende de: E7.1, E10.3

Problemas agrupados por regra, filtro por severidade, clique que leva à linha no editor, "corrigir automaticamente" e "marcar como ok".

Pronto quando:

- [ ] A lista bate com `vnf check`

### E10.9 · Sugestão da IA no editor

- Marco: M4
- Rótulos: área: app
- Depende de: E6.2, E10.3

`Ctrl+Espaço` pede uma sugestão para a linha atual e mostra ao lado da tradução.

Pronto quando:

- [ ] Funciona com o provedor falso, sem chave

### E10.10 · Glossário e personagens

- Marco: M4
- Rótulos: área: app
- Depende de: E3.7, E10.1

Duas tabelas editáveis. O editor avisa quando um termo do glossário aparece na linha atual.

Pronto quando:

- [ ] Editar no app altera `glossary.json` e `characters.json`

### E10.11 · Assistente de novo projeto

- Marco: M4
- Rótulos: área: app
- Depende de: E3.3, E10.1

Os quatro passos do doc 04: pasta do jogo com detecção, idiomas e nome, estratégia de acentuação e provedor de IA. No fim, oferece rodar a extração.

Pronto quando:

- [ ] O projeto criado pelo assistente é igual ao de `vnf init` com as mesmas escolhas

### E10.12 · Configurações

- Marco: M4
- Rótulos: área: app
- Depende de: E6.8, E10.1

IA (modelo com preço, chave com DPAPI, lote, concorrência, guia de estilo; outros provedores aparecem como "em breve"), formatação com prévia ao vivo e acentuação.

Pronto quando:

- [ ] A chave salva pelo app não aparece em nenhum arquivo do projeto

### E10.13 · Busca e substituição global

- Marco: M4
- Rótulos: área: app
- Depende de: E10.2

Em todo o projeto, com regex e prévia antes de aplicar.

Pronto quando:

- [ ] A prévia lista cada linha alterada antes de gravar

## E11 · Benchmark: a VN YU-RIS completa

- Marco: M4
- Rótulos: no jogo

O jogo inteiro traduzido, revisado e jogável do início ao fim.

Pronto quando: M4 atingido.

### E11.1 · Traduzir e revisar o jogo inteiro

- Marco: M4
- Rótulos: no jogo
- Depende de: E7.8

Tradução pela IA e revisão no app de todos os scripts.

Pronto quando:

- [ ] `vnf status` mostra todas as linhas aprovadas

### E11.2 · Ajuste fino de formatação no jogo

- Marco: M4
- Rótulos: no jogo
- Depende de: E11.1

Ajustar o perfil de formatação olhando o resultado no jogo.

Pronto quando:

- [ ] Nenhuma fala estoura a caixa de texto

### E11.3 · Levantar as imagens com texto

- Marco: M4
- Rótulos: no jogo

Menus, títulos e outras imagens com texto ficam fora do escopo inicial. Contar quantas são e onde estão, como entrada para o E12.

Pronto quando:

- [ ] Lista de imagens com texto registrada no doc 06

### E11.4 · Validação do M4: jogar do início ao fim

- Marco: M4
- Rótulos: no jogo
- Depende de: E11.2

Cada problema encontrado vira uma issue.

Pronto quando:

- [ ] O jogo foi jogado do início ao fim com a tradução

## E12a · E12 · Outros provedores de IA

- Marco: Pós
- Rótulos: área: ia

Cliente compatível com OpenAI (OpenAI, Gemini, OpenRouter, DeepSeek) e provedores locais (Ollama, LM Studio), com tela de escolha de provedor.

## E12b · E12 · Outras engines

- Marco: Pós
- Rótulos: área: core

KiriKiri/KAG, Ren'Py, SiglusEngine e Artemis, uma por vez, cada uma como um projeto `VNForge.Engines.X`.

## E12c · E12 · Edição de imagens com texto

- Marco: Pós
- Rótulos: área: core

Traduzir menus, títulos e outras imagens levantadas na E11.3.

## E12d · E12 · Memória de tradução entre projetos

- Marco: Pós
- Rótulos: área: core

Reaproveitar traduções aprovadas de um projeto em outro.

## E12e · E12 · Distribuição do patch para jogadores

- Marco: Pós
- Rótulos: área: infra

Instalador que aplica só a diferença sobre os arquivos do jogo.
