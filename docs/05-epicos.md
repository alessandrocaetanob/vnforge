# 05 — Épicos

Ordem pensada para chegar cedo a um ciclo completo (extrair → reinjetar sem tradução → ver o jogo rodando) e só depois investir em IA e interface. O primeiro risco a eliminar é "conseguimos remontar os arquivos e o jogo continua abrindo?".

## Marcos

| Marco | Resultado verificável |
|---|---|
| **M1 — Ida e volta** | `vnf extract` + `vnf build` + `vnf inject` sem nenhuma tradução gera um jogo que abre e roda igual ao original |
| **M2 — Primeira cena em PT-BR** | Uma cena traduzida à mão aparece no jogo com acentos e quebras de linha corretas |
| **M3 — Tradução assistida** | Um capítulo inteiro traduzido pela IA, conferido pelo QA e revisado no app |
| **M4 — Benchmark completo** | A VN YU-RIS inteira traduzida, jogável do início ao fim |

O M1 já usa `vnf extract`, `vnf build` e `vnf inject`, então as quatro primeiras tarefas do E3 e as três primeiras do E8 entram nele. A divisão em issues está em [plans/2026-10-22-backlog-github.md](plans/2026-10-22-backlog-github.md).

## E0 — Fundação

Objetivo: repositório pronto para trabalhar.

- Solução `.slnx`, `Directory.Build.props`, pacotes centralizados, .NET 10
- `.gitignore` que bloqueia arquivos de jogo (`*.ypf`, `*.ybn`, `*.exe`, `.cache/`, `build/`, `backup/`)
- CI no CircleCI: build + testes (Windows para o app, Linux para o resto)
- Logging, tratamento de erros, `--json` comum na CLI

Pronto quando: `dotnet build` e `dotnet test` passam no CI.

## E1 — YU-RIS: arquivos YPF

Objetivo: ler e reescrever `ysbin.ypf` e demais `.ypf`.

- Ler cabeçalho e índice em todas as variantes conhecidas (tabela de troca do tamanho do nome, chave do nome, offset 32/64 bits)
- Detectar hash de nome e de dados (CRC32, Adler32, MurmurHash2) comparando com o índice original
- Extrair com descompressão zlib
- **Reescrever preservando**: entradas não alteradas copiadas byte a byte, só as alteradas recomprimidas e com hash recalculado
- `vnf ypf info|unpack|pack`
- Testes: YPF sintético por variante, ida e volta byte a byte idêntica

Pronto quando: unpack + pack sem mudanças gera um arquivo idêntico ao original do seu jogo.

## E2 — YU-RIS: scripts YSTB

Objetivo: tirar e recolocar texto nos `.ybn`.

- Ler `ysc.ybn` (YSCM) para descobrir os opcodes `WORD`, `_`, `RETURNCODE`, `GOSUB`, `CG`
- Ler `yst_list.ybn` (YSTL) para dar nomes legíveis aos scripts
- Descobrir a chave XOR (pelo primeiro descritor de argumento, com chaves conhecidas como alternativa)
- Extrair: diálogos (`WORD`), nome do falante (`名前「...」`), escolhas (`ES.SEL.SET`), nomes (`ES.CHAR.NAME`), outras strings com japonês
- Converter códigos de controle (0xEF 0xF0 etc.) em marcadores legíveis e de volta
- Reconstruir: novos textos no fim da área de recursos, só os argumentos alterados apontam para eles
- `vnf ybn dump`
- Testes: script sintético; reconstrução sem mudanças = arquivo idêntico

Pronto quando: **M1** atingido no seu jogo.

## E3 — Modelo de projeto e formato de texto

- `vnforge.json`, `text/*.json`, glossário, personagens
- `vnf init`, `vnf extract` (re-extração preserva traduções e detecta original alterado), `vnf status`
- Ids estáveis por linha
- Export/import CSV e XLSX

## E4 — Formatação e espaçamento

- Perfil `formatting.json` com cada regra liga/desliga
- Normalização, espaços, pontuação, aspas, reticências, travessão, largura (meia/cheia)
- Quebra de linha por palavra com largura por caractere configurável e limite de linhas por caixa
- Substituições personalizadas (literal e regex)
- `vnf preview`
- Testes com frases reais em português (casos de ponta: "Ah...", "— Não!", "Ei, você?!")

## E5 — Acentuação e codificação PT-BR

- Interface `ITextEncoder` com `fold`, `remap`, `cp1252`
- Relatório de caracteres não codificáveis
- **Pesquisa no seu jogo**: o patch da tabela de lead bytes funciona? A fonte precisa de troca de charset?
- `vnf patch-exe` (experimental), com backup e verificação
- Se `cp1252` não der certo: `remap` com geração de fonte modificada

Pronto quando: **M2** atingido.

## E6 — IA

- `ITranslationProvider` + provedor falso para testes
- Claude pelo SDK oficial, com modelo selecionável (Opus 5.5 padrão, Sonnet 5.5, Haiku 5.5, Fable 5.1) e `effort` configurável
- Tratamento de recusa (`stop_reason: "refusal"`) com fallback, e prompt caching do prompt de sistema e do glossário
- Montagem de prompt: guia de estilo, glossário filtrado, personagens, contexto anterior
- Saída JSON validada (ids, marcadores); reenvio só das linhas com problema
- Lotes, concorrência, retry com backoff, cancelamento
- Estimativa de custo (`--dry-run`), contagem real de tokens, cache por hash
- `vnf translate`, `vnf review`

## E7 — QA (conferência)

- Motor de regras com severidade
- Regras listadas em [02-arquitetura.md](02-arquitetura.md#5-qa-conferência)
- `--fix` para correções seguras; "marcar como ok" persistente
- `vnf check` com código de saída 2 em erro bloqueante

Pronto quando, junto com E6: **M3** atingido.

## E8 — Build e injeção

- `vnf build` incremental (só scripts alterados)
- `vnf inject` com backup datado e verificação de hash
- `vnf restore`, `vnf run`
- Proteção: recusar injetar se o jogo mudou desde a extração (ex.: atualização)

## E9 — CLI completa

- Todos os comandos de [03-cli.md](03-cli.md), ajuda em português, `--json`
- Publicação como executável único

## E10 — App Windows

- Interface em Avalonia 12 (D3, #96). Telas e design em [design/](design/README.md)
- Fase 1 (M3, D4 em #97): janela principal e Início (#98), Editor com grade e painel de detalhe (#99, #100), salvar
- Fase 2 (M4): Painel (#102, E10.5: página `DashboardViewModel` sobre `ProjectDashboard` do Core, que reaproveita `ProjectStatusReporter` do `vnf status`; o custo de IA vem de `.cache/ai-usage.json`, onde `vnf translate` e `vnf review` somam cada execução), Build e injeção (#103), tarefas em segundo plano (extrair, traduzir, build, injetar) com progresso
- Fase 3 (M4): prévia da caixa de texto, Conferência (#106) integrada ao QA, glossário e personagens (#108: tabelas editáveis na página Glossário; o aviso no editor usa `GlossaryMatcher` do Core e `GlossaryViewModel.FindTerms`, ligado ao painel da linha quando o #100 entrar)
- Fase 4 (M4): assistente de novo projeto, Ajustes (#110), busca e substituição global

## E11 — Benchmark: a VN YU-RIS completa

- Tradução e revisão do jogo inteiro
- Ajuste fino de formatação olhando no jogo
- Imagens com texto (menus, títulos) ficam fora do escopo inicial; levantar quantas são
- Lista de problemas encontrados vira backlog

Pronto quando: **M4** atingido.

## E12 — Depois do benchmark

- Outros provedores de IA: cliente compatível com OpenAI (OpenAI, Gemini, OpenRouter, DeepSeek) e locais (Ollama, LM Studio), com tela de escolha de provedor
- Outras engines (KiriKiri/KAG, Ren'Py, SiglusEngine, Artemis), uma por vez
- Edição de imagens com texto
- Memória de tradução entre projetos
- Distribuição do patch para jogadores (instalador que aplica só a diferença)

## Sugestão de ordem

```
E0 ─► E1 ─► E2 ─► (M1) ─► E3 ─► E4 ─► E5 ─► (M2) ─► E6 ─► E7 ─► E8 ─► (M3)
                                                  └─► E9 e E10 em paralelo a partir daqui
                                                                         └─► E11 (M4) ─► E12
```
