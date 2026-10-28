# Interface v5: a IA como colega e os personagens com voz

A v4 deu largura ao texto e fixou a ação principal (ver `2026-10-27-interface-v4.md`). A v5 parte da main em 28/10/2026 (e69b85a), vista pelas 95 capturas dos testes de Rendering a 1440×1000 e a 1366×768. Os itens saíram da página de propostas "VNForge v5 · Propostas" (artefato no Claude.ai, privado), onde o Alessandro marcou Fazer, Talvez ou Não em cada um. Os ids `a1` a `b19` são os da página, para a escolha e a issue se acharem.

Ficaram de fora: **Nomes de cena pela IA** (`b5`, marcado Talvez) e **Ouvir a fala** (`b16`, marcado Não).

As regras das telas da v3 e da v4 continuam valendo. A v5 acrescenta estas:

- Toda ação em lote pode ser desfeita, inclusive depois de fechar o app. O "Desfazer" diz quantas linhas volta e quantas você editou depois e ficam como estão.
- A IA diz quando não tem certeza. Uma dúvida tem tipo e uma frase, nunca só um selo.
- Personagem tem cor e voz. A cor do Glossário aparece em todo lugar onde o nome aparece; a voz ("Como fala") entra em todo pedido que reescreve uma fala dele.
- A prévia mostra o que o jogo mostra. A placa da caixa do jogo usa o nome que o build grava.
- Marcador nunca aparece cru, também nos cartões e nas dicas novas.

## O que as capturas mostram

As capturas saíram de `dotnet test tests/VNForge.App.Tests --filter FullyQualifiedName~Rendering` com `VNFORGE_RENDER_DIR` apontando para uma pasta temporária (95 PNGs, 4 minutos, tudo passou).

- **Grade do Editor** (`editor-v4-1440-escuro`): a 1440 px a coluna Original mostra cerca de oito caracteres e a Tradução cerca de vinte e dois; "Quem fala" ocupa 132 px fixos.
- **Lista de cenas** (`editor-v4-1440-escuro`): cada cena ocupa cerca de 66 px (nome, porcentagem, id em mono e barra), e cabem nove de 312.
- **Inspetor** (`editor-v4-1440-escuro`): "Usar em todos" fica cortado pela barra fixa de "Aprovar e próxima" do #403.
- **Cartão "Mesmo original"** (`conferir-v3-problemas-escuro`): `\p` cru no original e nas três variantes; o mesmo cartão está no detalhe da linha (#457).
- **Placa do nome** (`modo-revisao-1366-escuro`, `editor-v4-1440-escuro`, `inicio-v3-projetos-escuro`): a placa mostra 美咲 onde a grade diz Misaki. `TextboxPreviewViewModel` usa `TextUnit.EffectiveSpeaker`, o nome do jogo. O Alessandro confirmou no jogo, em 28/10/2026, que a caixa de nome mostra o nome traduzido.
- **Testar no jogo** (`testar-no-jogo-detalhes-escuro`): dois blocos "Opções", um no corpo e outro em Detalhes técnicos, e o botão "Ir para Testar no jogo" (`BuildView.axaml:144`) levando para a própria página.
- **Glossário** (`glossario-personagens-escuro`): 22 seletores de gênero em âmbar; o botão que resolve, "Sugerir voz e gênero pelas falas", está em cinza no canto.
- **Início** (`inicio-v3-projetos-escuro`): com um projeto, o conteúdo termina em cerca de 640 px numa janela de 1000.

## Ondas

As ondas definem a ordem dos merges, não a dos workers. Dentro de uma onda, as issues sem dependência escrita abaixo da tabela podem ir em paralelo; as outras esperam o merge da que vem antes. Cada issue tem um PR próprio, com teste de Rendering novo ou ajustado e as capturas antes e depois na descrição. Item que muda o Core entra primeiro no Core, com teste, e depois na tela.

### Onda 1 · Polimento das telas atuais

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| a1 | Editor: densidade "Roteiro" na grade | Ao lado da densidade atual, cada linha mostra o original em cima e a tradução embaixo, até duas linhas cada, com o nome de quem fala como rótulo acima do par. A escolha fica em `editor-layout.json` (`EditorLayoutMemory`) | `EditorView.axaml`, `LineRow`, `EditorViewModel.Layout.cs` |
| a2 | Editor: lista de cenas compacta | Itens de 36 px com o nome e a barra fina embaixo; o id vai para a dica; cena pronta mostra um visto no lugar de "100%" | `ScenesPanel.axaml`, `ScriptItem` |
| a3 | Inspetor: "Usar em todos" atrás da barra fixa | O conteúdo que rola ganha margem inferior igual à altura da barra; teste de Rendering mede o botão dentro da área visível | `LineDetailView.axaml` |
| a4 | Cartão "Mesmo original" com marcador cru | Original e variantes passam pelo `MarkedText`, mantendo a diferença do `DiffText` | `ConsistencyCardView.axaml`, `DiffText`, `MarkedText` |
| a5 | Placa do nome com o nome traduzido | A placa mostra o nome que o build grava na caixa de nome: a tradução do Glossário quando o personagem tem uma, o falante escolhido na linha (#437) quando há. Vale para Editor, Modo revisão, Início e Conferência. Um teste compara a placa com o nome que o injetor grava | `TextboxPreviewViewModel.cs`, `GameTextbox` |
| a6 | Testar no jogo: uma lista de opções | As opções ficam só no corpo da página; a faixa do #438 explica o botão do topo sem botão próprio; Detalhes técnicos fica com resultado, log e cópias de segurança | `BuildView.axaml`, `BuildViewModel.cs` |
| a7 | Glossário: gênero pela IA como ação principal | A faixa "N personagens estão sem gênero" ganha "Pedir à IA" (botão principal, com o preço). A sugestão aparece em cada linha como valor esmaecido com um visto para aceitar, e "Aceitar as N" no alto. O seletor vazio fica neutro | `GlossaryView.axaml`, `GlossaryViewModel.Suggestions.cs` |

O `a8` (Início com metade vazia) não vira issue própria: o Diário de tradução (`b13`) ocupa esse espaço.

### Onda 2 · Personagens com voz

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| b10 | Cor do personagem | `CharacterEntry` ganha uma cor opcional; o app escolhe uma de uma paleta fixa, com contraste nos dois temas, e a pessoa troca no Glossário. A cor aparece no nome da grade, na placa da caixa do jogo e no Modo revisão. Narração não tem cor | `CharacterEntry.cs`, `CharacterSerializer.cs`, `GlossaryView.axaml`, `LineRow`, `GameTextbox` |
| b8 | Revisar por personagem | O Modo revisão aceita como fonte as falas de um personagem em todas as cenas, na ordem da história; entra pelo Glossário, pelo menu do nome (`SpeakerMenu`) e pela paleta de comandos | `ReviewModeViewModel.cs`, `SpeakerMenu`, `CommandPaletteViewModel.cs` |
| b11 | Ficha do personagem | Clicar no nome no Glossário abre a ficha: gênero, como fala, cor, as cinco falas mais longas no original e na tradução (`SpeakerSamples`), quantas faltam revisar e "Revisar este personagem" (`b8`) | `GlossaryView.axaml`, `SpeakerSamples.cs` |
| b9 | Quem chama quem de quê | O glossário ganha o tratamento por par de personagens (como A chama B no japonês e na tradução). A IA preenche a partir das falas (Haiku, JSON), a pessoa ajusta numa tabela no Glossário, o tratamento vai no pedido de tradução, e uma regra nova do QA aponta a fala que foge dele | `Glossary/`, `VNForge.AI/Glossary`, `ClaudePromptParts.cs`, `Qa/` (regra nova), `GlossaryView.axaml` |

`b10` entra antes de `b11` e de `b9`, que mostram a cor. `b8` entra antes de `b11`.

### Onda 3 · A IA como colega de revisão

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| b2 | A IA diz onde ficou em dúvida | O schema do pedido de tradução (`ClaudeTranslationProvider.Schema`) ganha um campo opcional de dúvida com tipo (trocadilho, sujeito oculto, gênero incerto, referência cultural, tom) e uma frase. `TranslationResult` e `TextUnit` guardam a dúvida; aprovar a linha a encerra. A grade mostra o selo "Dúvida", a barra do Editor ganha o filtro, o inspetor mostra o cartão e o Modo revisão pode começar pelas dúvidas | `ClaudeTranslationProvider.cs`, `TranslationBatch.cs`, `TextUnit.cs`, `LineDetailView.axaml`, `ReviewModeViewModel.cs` |
| b3 | Reescrever com um toque | Chips no inspetor e no Modo revisão pedem uma variação dirigida: mais curta, mais natural, mais fiel, mais formal ou "como o personagem falaria" (o "Como fala" do Glossário). Vêm duas opções, cada uma medida contra a caixa do jogo. O encurtador (`ShortenInstruction`) vira o chip "Mais curta" | `ClaudeTranslationProvider.cs`, `LineDetailViewModel.Suggestion.cs`, `ReviewModeView.axaml` |
| b4 | A IA aprende com as suas correções | O Core compara a tradução da IA com a que a pessoa gravou e procura trocas que se repetem, por personagem ou no projeto. Passado um limite, o app oferece a regra para o `style.md` ou para o "Como fala" do personagem. Não chama a IA | `Core` (comparador novo), `StyleSettingsViewModel.cs`, `ToastService.cs` |
| b7 | Sugerir termo para o glossário | Quando o mesmo trecho japonês ganha a mesma tradução em várias falas corrigidas, o app oferece o par para o Glossário, ao lado dos "Termos que se repetem" | `RepeatedTerms.cs`, `GlossaryView.axaml` |

`b4` e `b7` precisam da tradução que a IA escreveu depois que a pessoa a edita. Hoje `TextUnit` guarda só a origem (`Origin`), então o `b4` acrescenta o campo com o texto da IA, com teste de ida e volta do JSON, e o `b7` entra depois dele.

### Onda 4 · Ler a história e confiar no que mudou

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| b1 | Ler como jogador | Uma cena inteira em tela cheia, uma fala por vez na caixa do jogo, avançando com Espaço e voltando com a seta. Nada para aprovar. `F` marca "soa estranho" (uma marca em `TextUnit.Flags`) e, ao sair, o Editor abre com o filtro dessas falas | `ReviewStage`, `GameTextbox`, `EditorViewModel.cs`, view nova |
| b12 | O que mudou desde o último teste | No teste no jogo, o Core guarda o retrato das traduções (hash por linha) junto do `GameLastTest`. A página Testar e o Painel listam as cenas que mudaram desde então, com quantas falas, na ordem da história, e "Abrir" leva ao Editor com o filtro | `GameStateFile.cs`, `GameTester`, `BuildView.axaml`, `DashboardView.axaml` |
| b13 | Diário de tradução no Início | Mapa de calor das aprovações por dia nas últimas 12 semanas (o `sessions.json` guarda 90 dias, `SessionsFile.Retention`), dias seguidos e o que foi fechado ontem. Ocupa o espaço vazio do Início (`a8`). Ao fechar o app, um resumo curto da sessão | `SessionsFile.cs`, `ReviewPace`, `StartView.axaml` |
| b14 | Desfazer qualquer lote | Antes de cada operação em lote (tradução da IA, substituição, corrigir com um clique, usar em todos), o Core guarda o estado das linhas que vão mudar em `.cache/`. A lista de tarefas ganha "Desfazer" em cada uma, mesmo dias depois; linha editada depois do lote fica como está, e o "Desfazer" diz quantas | Core (ponto de restauração por tarefa), `TaskEntry.cs`, `TaskBarViewModel.cs` |
| b6 | Pedido em lote em português | A paleta de comandos aceita um pedido escrito ("nas falas do Kenta, trocar você por tu"). A IA acha as falas e reescreve; a prévia é a do painel de busca e substituição, antes e depois por linha, nada gravado até Aplicar. Aplicar passa pelo ponto de restauração do `b14` | `CommandPaletteViewModel.cs`, `SearchReplaceViewModel.cs`, `ProjectSearch.cs`, IA (pedido novo) |

`b6` entra depois do `b14`.

### Onda 5 · Começar e ficar confortável

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| b19 | Tamanho do texto | `Ctrl` `+` e `Ctrl` `−` mudam o texto da grade, do inspetor e do Modo revisão; a aba Aparência ganha o tamanho e a fonte do japonês (a seção 7 do docs/04 lista os dois como faltando) | `SettingsView.axaml`, `Themes/` |
| b18 | Cena de exemplo para aprender | Na primeira vez, "Experimentar com uma cena de exemplo" ao lado de "Novo projeto" abre um projeto sintético de 30 falas com uma IA falsa que responde na hora: traduzir, revisar e conferir sem jogo, sem chave e sem gasto. O gerador hoje vive em `tests/VNForge.App.Tests/Demo/DemoProject.cs` e passa para o Core | `StartView.axaml`, `DemoProject.cs` (movido) |
| b15 | Corretor de português | Sublinhado no campo da tradução e uma regra de aviso na Conferência, com um dicionário do projeto que já conhece os nomes e termos do Glossário. A issue começa pela escolha do dicionário pt-BR e da biblioteca .NET, com a licença de cada um conferida por Context7 e Tavily e registrada no PR | `Qa/` (regra nova), `LineDetailView.axaml` |

### Onda 6 · Mapa das escolhas do jogo

O `b17` vira duas issues em sequência, porque o Core não lê os saltos dos scripts. Hoje a extração do YU-RIS lê as escolhas (`ES.SEL.SET`, docs/06) mas não para onde cada uma leva.

| Id | Issue | O que muda | Arquivos principais |
|---|---|---|---|
| b17a | YU-RIS: ler os saltos e o destino das escolhas | Pesquisa no formato YSTB dos comandos de salto e de chamada entre scripts e do destino de cada escolha, registrada no docs/06 com o que foi confirmado no jogo de referência. Termina num grafo de cenas no Core (`IEngine` devolve as arestas; engine sem suporte devolve nenhuma), com teste sobre script sintético | `Engines.YuRis/Ystb`, `docs/06-yuris-notas-tecnicas.md`, `Core` |
| b17b | Mapa das escolhas no Painel | As cenas ligadas pelas escolhas, como um mapa de rotas, com a parte pronta de cada cena. Clicar abre a cena no Editor | `DashboardView.axaml`, controle novo |

`b17b` só começa com o `b17a` mesclado. Se a pesquisa mostrar que o destino das escolhas não pode ser lido com segurança, o `b17b` sai do plano e o docs/06 registra o porquê.

## Issues

São 25 issues: 7 da onda 1, 4 da onda 2, 4 da onda 3, 5 da onda 4, 3 da onda 5 e 2 da onda 6 (`b17a` e `b17b`). Cada uma leva o id da página no título ("v5 · a1 Editor: densidade Roteiro na grade") e o label "área: app"; as que mudam o Core ou a IA levam também "área: core" ou "área: ia". Elas são abertas depois que este plano for aprovado.

## Fora deste plano

Nomes de cena pela IA (`b5`) e Ouvir a fala (`b16`). Outros provedores de IA, outras engines e a distribuição do patch continuam nos épicos E12. O canvas v3 não é refeito: como na v4, cada tela é conferida nas capturas do próprio app.
