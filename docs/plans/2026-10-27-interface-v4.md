# Interface v4: o texto em primeiro lugar

A v3 deu ao app a linguagem e o fluxo de quem nunca traduziu uma visual novel (ver `2026-10-25-interface-v3.md`). A v4 parte do app como ele está na main em 27/10/2026 (7e1e227), visto pelos 78 testes de Rendering a 1440×1000 e a 1366×768, nos dois temas. Não troca telas nem fluxo. Corrige o que a v3 prometeu e o app não entregou, e muda três telas onde o desenho atual atrapalha o trabalho: o Editor esconde o texto, o modo revisão deixa a caixa do jogo pequena num canto e o Início trata um projeto em andamento como uma lista vazia.

As regras das telas da v3 continuam valendo. A v4 acrescenta estas:

- O texto ganha a largura. Na grade do Editor, Original e Tradução dividem o espaço que sobra; Linha, Situação e Quem fala têm largura fixa e pequena.
- O botão principal fica sempre à vista. Um painel que rola mantém a ação principal numa barra fixa no rodapé, fora da rolagem.
- Uma cena se chama pelo nome. O id do script (`yst00013`) aparece em letra pequena ao lado do nome, nunca no lugar dele, e nunca como título de página.
- Nada truncado com reticências em texto que a pessoa precisa ler para decidir. Se não cabe, a frase encurta ou quebra em duas linhas.
- Fonte monoespaçada só para id, atalho de teclado e a caixa do jogo. Números, preços, rótulos de passo e sobrancelhas de página usam a fonte da interface com algarismos tabulares.
- O selo vermelho no trilho conta só o que trava o teste no jogo. Avisos não pintam o trilho.
- Contraste mínimo de 4,5:1 em todo texto de botão, nos dois temas.

## O que as capturas mostram

As capturas saíram de `dotnet test tests/VNForge.App.Tests --filter FullyQualifiedName~Rendering` com `VNFORGE_RENDER_DIR` apontando para uma pasta temporária. Cada item cita o arquivo da captura.

**Base visual (todas as telas).**
- O botão principal no tema escuro tem texto branco sobre `#8DB0EA`, abaixo de 2:1 (`inicio-v3-projetos-escuro`, `painel-v3-escuro`, `novo-projeto-v3-4-ia-e-gasto-escuro`). O `Button.accent` de `Themes/Controls/Buttons.axaml` define `Foreground` no botão, mas o FluentTheme pinta o `ContentPresenter` do modelo; só o estado `pointerover` chega lá. Ativo, ele parece desligado: "Criar projeto" e "Avançar" têm a mesma cara de "Voltar" desligado. O `Segmented` selecionado ("Primeira", "Neutro" em `ajustes-estilo-escuro`) repete o defeito.
- Os títulos `title-lg` cortam as descendentes: o "j" de "projeto", "Ajustes" e "jogo" e o "p" de "Capítulo" (`novo-projeto-v3-1-jogo-escuro`, `ajustes-escuro`, `testar-no-jogo-traduzido-escuro`). `LineHeight` 32 com fonte de 26 px não comporta a fonte de exibição.
- O trilho mostra "99+" em vermelho no Conferir com 2 erros e 10.122 avisos (`painel-v3-escuro`). A pessoa lê "99 coisas quebradas".
- Sobrancelhas de página em monoespaçada ("Painel", "Passo 1 de 4", o nome do projeto em Testar no jogo, "yst00013" no Editor) destoam do resto e, no Editor, mostram o id do script.
- A barra de status termina num triângulo de aviso que aparece até sem aviso nenhum (`inicio-v3-primeira-vez-claro`, `novo-projeto-v3-1-jogo-escuro`).

**Início** (`inicio-v3-projetos-escuro`). Com um projeto só, a tela é uma tabela de uma linha, uma área de soltar pasta e dois botões "Abrir projeto" (no topo e no rodapé do painel direito), além de dois botões principais disputando o olhar ("Novo projeto" e "Revisar 'A promessa'"). Metade de baixo vazia.

**Painel** (`painel-v3-escuro`). O stepper trunca três dos cinco passos ("faltam 18.320 li…", "sem con…", "tradução de 2…"). As notas dos capítulos truncam ("2 erros travam o …", "Você está aqui · 1…"). As linhas de cada capítulo estão em monoespaçada.

**Editor** (`editor-1440-escuro`, `editor-caixa-nao-cabe-escuro`). A 1440 px, Original e Tradução mostram cerca de 12 caracteres cada, enquanto Situação e Personagem ocupam mais que as duas juntas; a 1366 px, sem a lista de cenas, a grade mostra mais texto do que a 1440. A lista de cenas tem `yst00001` como rótulo principal. O título vira `yst00013` quando a cena não tem capítulo. No inspetor, o botão "Aprovar e próxima" fica abaixo da dobra: a pessoa precisa rolar para achar a ação principal da tela. O original passa da borda do cartão ("Narraçã", "Misak") e a frase "caixa 1 com 4 linhas (máximo 3)." repete o "Passa 1 linha" logo acima, em linguagem técnica. Os filtros dizem "Pendentes" e "IA"; a v3 pedia "Em japonês" e "Para revisar".

**Modo revisão** (`modo-revisao-cabe-escuro`, `modo-revisao-fim-escuro`). A tela que responde "esta fala está boa?" ocupa o terço de cima: a caixa do jogo é pequena, os botões ficam no meio da altura e 55% da janela é fundo vazio. O fim da revisão anuncia "Próxima cena: yst00002".

**Traduzir com IA** (`traduzir-com-ia-escuro`). Os cartões mostram Haiku, Sonnet e Opus, contra a regra "qualidade em vez de modelo". "Esta cena" pode ser escolhida com "nenhuma linha pendente".

**Conferir** (`conferir-v3-problemas-escuro`). Metade dos problemas diz onde está pelo id ("yst00012 · linha 1"), metade pelo nome da cena. Mensagens trazem o marcador escrito por extenso entre parênteses ("'Beleza.(virar a página)'") em vez do selo. O painel do problema só tem "Abrir no editor"; faltam "Corrigir e ir para o próximo" e "Está certo assim".

**Testar no jogo** (`testar-no-jogo-traduzido-escuro`, `testar-no-jogo-travado-escuro`). Dois terços da tela vazios. Com erros, "Ver os erros" vira um segundo botão de destaque ao lado do principal desligado.

**Ajustes** (`ajustes-escuro`). A sobrancelha fala em `vnforge.json`; a barra de status diz "Alterações salvas automaticamente em vnforge.json". "(cotação de exemplo)" aparece quatro vezes. Sem limite de gasto, a barra do orçamento aparece cheia.

**Glossário** (`glossario-v3-personagens-escuro`). O preço do botão "Sugerir voz e gênero pelas falas" sai em monoespaçada, com "(cotação de exemplo)" dentro do botão.

## O que muda, tela a tela

**Base.** `Button.accent` e o `Segmented` selecionado pintam o `ContentPresenter` com `Vnf.OnAccent` em todos os estados, e o estado desligado fica visivelmente diferente (fundo de controle, texto desligado). Os títulos ganham altura de linha que comporta a fonte. O selo do trilho conta só os erros que travam o teste; com zero erros e avisos abertos, o ícone ganha um ponto neutro, sem número. Sobrancelhas na fonte da interface. O triângulo da barra de status só aparece com aviso, e com dica explicando qual.

**Início: "Continuar de onde parou".** Com projeto recente, o topo da tela é um cartão largo com o nome do projeto, o anel, a cena onde a pessoa parou ("A promessa · capítulo 5"), a última fala aprovada desenhada na caixa do jogo pequena e um botão só, "Continuar: revisar 'A promessa'", com as horas que faltam. A lista de projetos vem abaixo, compacta, com "Abrir" por linha; "Abrir projeto" e "Novo projeto" ficam no cabeçalho, ambos neutros. A área de soltar pasta fica no fim da lista. A primeira vez não muda.

**Painel.** Os passos do stepper usam frases que cabem: "48.211 linhas lidas", "62% traduzido", "18.802 revisadas", "2 erros travam", "Testado em 22/10". A nota do capítulo quebra em duas linhas em vez de truncar, e a coluna de linhas sai da monoespaçada.

**Editor.** A grade passa a `56 · 92 · 132 · * · 1.2* · 32`: Original e Tradução levam o resto, a tradução um pouco mais porque o português é mais longo. A lista de cenas mostra o nome da cena (ou "Cena 13" quando não há nome) com o id em letra pequena à direita. O título da página segue o mesmo nome; a sobrancelha diz "Capítulo 1 · cena 13 de 45". O inspetor divide-se em conteúdo que rola e uma barra fixa no rodapé com "Aprovar e próxima" (`Ctrl+Enter`) e "Usar sugestão". O original quebra linha dentro do cartão. A mensagem técnica da caixa sai; fica "Passa 1 linha" com o aviso e as duas saídas. Filtros: Todas, Para revisar, Em japonês, Com aviso, Não traduzir.

**Modo revisão: o palco.** A coluna da direita vira a tela do jogo: um retângulo 16:9 com fundo escuro em degradê, o nome da cena no canto como cartão de capítulo e a caixa do jogo em tamanho grande encostada na base, como no jogo de verdade. Abaixo do palco, "Cabe · 1 de 3 linhas" ou o aviso com as saídas. A coluna da esquerda lê como um roteiro: a fala anterior em cinza, o original e a tradução da fala atual em destaque (a tradução editável, com "Outra sugestão"), a fala seguinte em cinza. A barra de ações fica fixa no rodapé da tela, de ponta a ponta: "Está boa" (`Enter`) e "Corrigir" (`E`) grandes à esquerda, "Depois" (`M`) e "Pular" discretos à direita, e o progresso "Linha 1 de 3 · 1 revisada hoje" no centro. A tela de fim chama a próxima cena pelo nome e mostra quantas linhas a pessoa aprovou hoje.

**Traduzir com IA.** O nome do modelo sai dos cartões de qualidade (fica em Detalhes técnicos dos Ajustes). Escopo sem linhas pendentes fica desligado, com "Nada para traduzir aqui".

**Conferir.** Todo problema diz onde está pelo nome da cena. Marcadores nas mensagens viram selo (`MarkerChip`), nunca texto entre parênteses. O painel do problema ganha "Corrigir e ir para o próximo" (`Enter`, só quando há correção automática) e "Está certo assim"; "Abrir no editor" fica como terceira ação. A faixa vermelha do topo vira uma faixa neutra com o ícone de erro e o número em vermelho.

**Testar no jogo.** Um cartão principal no topo junta o estado do jogo, o botão "Testar no jogo" e os cinco passos numa linha horizontal com o tempo de cada um, em vez do botão no canto e da lista à direita. Com erros, o passo "Conferir" fica vermelho e a faixa leva a "Ver os erros" como botão neutro. Opções e Detalhes técnicos abaixo.

**Ajustes e Glossário.** Sobrancelha sem nome de arquivo ("Valem só para este projeto. A chave do Claude fica guardada no Windows."); a barra de status diz "Salvo". "(cotação de exemplo)" uma vez, no título do bloco de qualidade. Sem limite de gasto, a barra do orçamento some e fica só o gasto. No Glossário, o preço do botão sai da monoespaçada e o "(cotação de exemplo)" vai para a dica.

## Issues

Cada issue tem um PR próprio, com teste de Rendering novo ou ajustado e as capturas antes e depois na descrição. Nenhuma depende de outra para compilar; a base visual muda a cara das outras, por isso entra primeiro.

| Id | Issue | Telas | Arquivos principais |
|---|---|---|---|
| V1 | Base visual: contraste do botão principal, títulos cortados, selo do trilho, sobrancelhas | todas | `Themes/Controls/Buttons.axaml`, `Segmented.axaml`, `Typography.axaml`, `NavRail.axaml`, `PageHeader.axaml`, `MainWindow.axaml` |
| V2 | Editor: texto com largura, cenas pelo nome, ação fixa no inspetor | Editor | `EditorView.axaml`, `LineDetailView.axaml`, `ScenesPanel.axaml`, `ScriptItem.axaml` |
| V3 | Modo revisão: o palco | Modo revisão | `ReviewModeView.axaml` |
| V4 | Início e Painel: continuar de onde parou, stepper sem corte | Início, Painel | `StartView.axaml`, `DashboardView.axaml`, `PipelineStepper.axaml` |
| V5 | Conferir e Traduzir com IA: nome da cena, selos, ações do problema, sem nome de modelo | Conferir, Traduzir com IA | `ConferenceView.axaml`, `TranslateDialogView.axaml`, `QualityCard.axaml` |
| V6 | Testar no jogo, Ajustes e Glossário: cartão principal, sem jargão | Testar, Ajustes, Glossário | `BuildView.axaml`, `SettingsView.axaml`, `GlossaryView.axaml` |

## Fora deste plano

Tela nova, mudança de fluxo, mudança no Core além do que a tela precisa ler (o nome da cena e o capítulo já vêm do Core desde o #374). O canvas v3 não é refeito: a v4 é conferida nas capturas do próprio app.
