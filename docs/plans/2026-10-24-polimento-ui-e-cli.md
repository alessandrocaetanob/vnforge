# Interface no nível do design v2 e CLI com saída rica

O app hoje implementa a estrutura das telas, mas não o design. A referência é o canvas "VNForge v2 · App Windows" (https://claude.ai/artifact/97EhvQiUBs3pcHUqUFBkEc, cópia em `docs/design/telas/`) com o design system "VNForge v2" (https://claude.ai/artifact/TvsKGRUc1BFYezZdfydrh6). A meta mínima é cada tela do app ficar igual ao seu artboard, com dados reais no lugar dos de exemplo. A meta de verdade é passar dele: movimento, estados vazios, paleta de comandos e um tema que se ajusta ao Windows.

A CLI ganha o mesmo cuidado: saída com cor, tabelas, barras de distribuição nas cores do app e progresso ao vivo nas operações longas, sem perder o `--json` nem a saída limpa quando redirecionada.

Este plano substitui a versão anterior (só ajustes de margem e token), recusada por ficar longe do canvas.

## Como a comparação foi feita

Os sete artboards foram renderizados a 1440×1000 com o runtime e o CSS do próprio canvas, e o app foi aberto com um projeto sintético de 353 linhas (6 scripts, 4 personagens, 153 avisos de QA), gerado pelos builders de teste. As diferenças abaixo saem da comparação lado a lado.

## O que separa o app do design

**Moldura.** O design tem barra de título com o selo 訳 em vermelho shu, o projeto como migalha ("Minha VN · PT-BR") e uma busca central "Buscar linhas, scripts e comandos" com `Ctrl+K`. O app mostra só "VNForge" e o nome. O trilho do design tem Início, Painel, Editor, Conferir (com contador de problemas) e Build no alto e Ajustes no pé; o app empilha oito itens, Busca incluída, sem contador. A barra de status do design mostra a tarefa em andamento ("Traduzindo yst00014 · Claude Opus 5.5", barra, 312/890, US$ 0,84, "Tarefas (2)") e à direita "Salvo às 00:41 · Acentuação: CP1252 · 40 colunas · 3 linhas"; a do app mostra a versão e o estado da chave.

**Início.** É a tela mais próxima. Faltam o "Último build" no detalhe, o botão "Continuar em yst00013", o rodapé com a pasta padrão e o tratamento de projeto sem suporte.

**Painel.** O design tem um stepper numerado de cinco etapas ligadas (concluída com ✓ verde, ativa com sublinhado de destaque, resumo em cada uma), três cartões de número grande (Linhas 48.211, Uso da IA US$ 26,40 com tokens e estimativa restante, Conferência "39 problemas" com barra erros/avisos), cada um com link de ação, e duas tabelas, "Por script" (id, nome do capítulo, linhas, barra, %) e "Por personagem" (美咲 → Misaki). O app tem cinco botões cinza, uma barra de linhas e duas listas simples.

**Editor.** O design tem cabeçalho com caminho do script e título do capítulo, filtros em chip com contagem, "Conferir arquivo" e "Traduzir pendentes". A lista de scripts tem campo de filtro, nome do capítulo, barra por status e ícone girando no script que está sendo traduzido. A grade usa número da linha em mono com zeros, badge de status com ponto, nome traduzido do personagem e ícone de problema. O inspetor tem cabeçalho com id, badge, falante, histórico e menu; original em fundo rebaixado com chips de marcador; tradução com borda inferior de destaque e linha de medidas ("66 caracteres · 2 de 3 linhas · 38/40 colunas", "Marcadores iguais ao original" em verde); "Aprovar e próxima" largo com `Ctrl+Enter`; "Usar sugestão"; cartão da IA com diff (inserções em verde, remoções riscadas); prévia da caixa de texto; contexto do glossário. No app, os botões quebram linha, a caixa de tradução não tem borda e não há diff nem contexto. O tema claro do design é o mesmo layout com a paleta clara.

**Conferência.** O design tem três colunas: regras agrupadas por categoria com contagem (Marcadores, Caixa de texto, Espaços, Pontuação, Acentuação, Glossário, Consistência), lista de problemas (linha em mono, título, trecho, "Corrigível") e detalhe com severidade, explicação, original, tradução atual, correção proposta, "Abrir no editor" e "Aplicar correção". No alto, faixa vermelha "2 erros bloqueiam o build" e "Corrigir 3 automaticamente". O app tem uma lista única agrupada pelo id da regra, com 124 linhas de "não traduzida" antes de qualquer aviso útil.

**Build.** O design tem faixa de aviso com "Ver erros", opções como linhas com interruptor e descrição, cartão Resultado com InfoBar e números (scripts reconstruídos, tamanho do YPF, acentuação), log com horário em mono, lista de backups e "Novo build" com `Ctrl+B`. O app tem a estrutura, mas sem log, sem backups e com botões de larguras diferentes.

**Ajustes.** O design tem abas (IA, Formatação, Acentuação), provedores em chip, cartão da chave com "Trocar chave" e "Testar conexão", modelos como cartões de opção com preço e estimativa para as linhas pendentes, esforço em controle segmentado e "Alterações salvas automaticamente" na barra de status. O app é um formulário longo com caixas de seleção.

**Fora do canvas.** Glossário e assistente de novo projeto não têm artboard. Os dois são desenhados no canvas, na mesma linguagem, antes do código.

## Além do canvas

- **Paleta de comandos** (`Ctrl+K`): a busca da barra de título abre uma lista que acha linhas por texto, scripts por id ou nome, telas e comandos ("Traduzir pendentes", "Novo build", "Abrir pasta do projeto"). Cobre o que a página Busca faz hoje, e a página sai do trilho; buscar e substituir dentro do Editor continua no #234.
- **Movimento**: troca de página com fade e deslocamento de 8 px em 150 ms, barras de progresso e de distribuição animadas ao mudar, linha aprovada com um pulso verde curto antes de avançar, ícone girando nas tarefas. Tudo desligado quando o Windows pede menos animação.
- **Estados vazios com o selo**: o 訳 em marca d'água com uma frase e uma ação ("Nenhum projeto ainda" com "Novo projeto"; "Nada para conferir" com "Conferir de novo"; script sem linhas).
- **Carregamento em esqueleto** na grade e nas tabelas do Painel no lugar de tela vazia enquanto o projeto abre.
- **Avisos curtos** (toasts) para gravação, build pronto e injeção, com "Desfazer" quando houver.
- **Tema**: Sistema, Claro ou Escuro nos Ajustes, seguindo o Windows por padrão; Mica na moldura, como hoje.
- **Mapa de atalhos** com `?`, no estilo do componente Kbd.

## Issues

Uma issue por item, criadas antes de qualquer branch. U0 vem primeiro porque as outras usam os controles dele; depois de U0 e U1, as telas andam em paralelo.

| # | Issue | Depende de | Core novo |
|---|---|---|---|
| U0 | Base visual: controles que faltam e galeria | | |
| U1 | Moldura: barra de título, paleta de comandos, trilho, barra de status | U0 | |
| U2 | Início no nível do design | U0, U1 | último build por projeto |
| U3 | Painel no nível do design | U0, U1 | nome do script |
| U4 | Editor no nível do design (escuro e claro) | U0, U1 | nome do script |
| U5 | Conferência em três colunas | U0, U1 | categoria da regra, correção proposta por problema |
| U6 | Build e injeção no nível do design | U0, U1 | eventos do build, lista de backups |
| U7 | Ajustes em abas | U0, U1 | estimativa por modelo |
| U8 | Glossário e assistente: desenho no canvas e implementação | U0, U1 | |
| U9 | Além do canvas: movimento, vazios, esqueleto, avisos, tema, atalhos | U0 | |
| C1 | CLI: base de saída rica | | |
| C2 | CLI: comandos de leitura | C1 | |
| C3 | CLI: progresso ao vivo nas operações longas | C1 | |
| C4 | CLI: ajuda, erros e correções de comportamento | C1 | |

### U0 · Base visual

- Controles e estilos que o design system define e o app não tem: `PipelineStepper`, `SeverityTag`, `LineRow`, `ScriptItem`, `ToggleRule`, controle segmentado, abas, chip de filtro com contagem, `AiSuggestion` com diff, legenda de distribuição, cartão de número (KPI) e cabeçalho de página (migalha em mono, título em Zen Kaku Gothic New 22/28, ações à direita). Cada um segue o seu `docs/design/components/*/README.md` e o `bundle.css` do canvas.
- Títulos em Zen Kaku Gothic New, como o design; hoje saem em Noto Sans.
- Galeria de componentes: janela só no build Debug com cada controle em todos os estados, nos dois temas, para conferir contra o design system.
- Projeto de demonstração gerado em código (`tests/VNForge.App.Tests/Demo`), com os nomes, números e estados dos artboards, usado nos testes e nos prints.
- Render das telas para PNG pelo Avalonia.Headless com Skia, a 1440×1000, nos dois temas, para pôr ao lado do artboard em cada PR.

### U1 · Moldura

- Barra de título: selo, migalha do projeto, busca central com `Ctrl+K` que abre a paleta de comandos.
- Trilho: Início, Painel, Editor, Conferir com contador, Build no alto; Ajustes no pé. Glossário entra no trilho só com projeto aberto, como no artboard do Editor. Busca sai.
- Barra de status: tarefa em andamento com barra, contagem, custo e "Tarefas (n)" com a lista das tarefas; à direita, hora da última gravação, acentuação e limites da caixa.
- Fecha o #235 no que toca ao trilho; o fluxo do Início fica no U2.

### U2 a U8 · Telas

Cada issue lista, como caixas de seleção, os elementos do artboard descritos acima. Pronto quando o PNG da tela ao lado do artboard não mostra diferença de layout, cor, tipografia nem componente, com dados do projeto de demonstração; quando cada ação leva ao lugar certo; e quando há testes de ViewModel para os dados novos. O que pede Core novo entra no Core primeiro, com teste, e a CLI ganha o mesmo dado quando fizer sentido (nome do script no `vnf status`, categoria no `vnf check`).

### U9 · Além do canvas

Os itens da seção "Além do canvas", menos a paleta (U1). Cada animação tem teste que a desliga com a preferência de acessibilidade.

## CLI

A saída passa a usar Spectre.Console 0.57.2 (versão estável atual no NuGet). Um `IAnsiConsole` criado na `VnfCli` escreve no mesmo `TextWriter` de hoje, então os testes continuam capturando a saída; neles a console é criada sem ANSI e com largura fixa. Cor e animação só aparecem em terminal interativo; com `--json`, `--quiet`, saída redirecionada ou `NO_COLOR`, a saída é texto simples. Uma opção global `--no-color` força o mesmo.

As cores são as do app: aprovada `#5FBF7F`, revisada `#3E9AAE`, traduzida `#6E8FCB`, IA `#9A7FD6`, pendente `#3A4250`, aviso `#F2C46D`, erro `#F59AB2`, destaque `#8DB0EA`.

### C1 · Base

`IAnsiConsole` na `VnfCli`, paleta, detecção de terminal, `--no-color`, helpers de tabela, painel e barra de distribuição, e o harness de teste com console sem ANSI.

### C2 · Comandos de leitura

`vnf status` com barra de distribuição colorida e tabelas por script e por personagem:

```
 Minha VN · PT-BR                                  353 linhas · 64,8 % com tradução
 ████████████▓▓▓▓▓▓▒▒▒▒▒▒▒░░░░░░░░░░···········
 ■ Aprovadas 34  ■ Revisadas 36  ■ Traduzidas 70  ■ IA 89  ■ Pendentes 124

 Script     Nome                    Linhas  Progresso             %
 yst00001   Prólogo                     49  ██████████▓▓▓▒▒░░···  65,3
 yst00002   Capítulo 1 · Manhã          58  █████████▓▓▓▒▒░░····  63,7
```

`vnf check` agrupado por categoria, com "não traduzida" resumida em uma linha por script e erros antes de avisos; `vnf preview` desenha a caixa de texto como no app (fundo escuro, régua da coluna limite, excesso em vermelho); `detect` e `ypf info` em árvore; `glossary list` e `chars list` em tabela.

### C3 · Progresso ao vivo

`translate` e `review` com barra por lote, linhas, tokens, custo acumulado e tempo restante; `extract` e `build` com progresso por script e um painel de resumo no fim, como o cartão Resultado do Build; `inject` e `restore` mostram em tabela o que vai mudar antes da confirmação.

```
 Traduzindo com Claude Opus 5.5 · esforço médio
 ⠼ lote 7/12  ██████████████████░░░░░░░░  58 %  210/360 linhas  US$ 0,84  ~1 min
```

### C4 · Ajuda, erros e comportamento

- `--help` em seções (Pipeline, Projeto, Baixo nível), na ordem do pipeline: `init`, `extract`, `translate`, `review`, `check`, `build`, `inject`, `run`, `restore`; depois `status`, `preview`, `export`, `import`, `glossary`, `chars`, `config`, `detect`; por fim `ypf` e `ybn`. Descrições de uma linha e um exemplo por comando.
- Erros com uma segunda linha de dica ("→ Use --project <pasta> ou rode dentro da pasta do projeto.").
- As correções da revisão anterior: `--full` no lugar de `--only-changed false`; recusa de confirmação no `ypf pack` e `ypf unpack` saindo com 3; `ConfirmationPrompt` lendo `cli.Input`; YPF e YBN inválidos com mensagem em português; fallback de parse sem texto em inglês; nomes de valor iguais (`<pasta>`, `<modelo>`).
- `docs/03-cli.md` atualizado com a saída nova, `--no-color`, `--full`, códigos de saída e `patch-exe` marcado como planejado (#65).

## Execução

Um worker por issue (`/distribuir`), cada um no seu worktree, abrindo o próprio PR depois do `oc-review`. U0 e C1 começam juntos; o resto entra quando a dependência estiver na `main`. Cada PR de tela traz o PNG do app ao lado do artboard. Antes do merge, `/code-review` e o gate do Sonar.

## Decisões em aberto

- A paleta de comandos tira a página Busca do trilho; buscar e substituir passa a viver só no Editor (#234). Confirmar.
- U8: desenhar Glossário e assistente no canvas antes do código (proposto) ou implementar direto na linguagem dos outros artboards.
- Spectre.Console como dependência nova da CLI, fixada em 0.57.2 no `Directory.Packages.props`.
