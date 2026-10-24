# 04 — Aplicativo Windows

**Design:** as telas e o design system estão copiados em [design/](design/README.md) (canvas "VNForge v2 · App Windows" e design system "VNForge v2", no Claude.ai). São seis telas (Início, Painel, Editor, Conferir, Build, Ajustes). Os desenhos em texto abaixo são o rascunho que originou o design; quando divergirem, vale o design. A implementação segue [design/avalonia.md](design/avalonia.md).

## Ideia geral

O app é uma "mesa de tradução": abre um projeto VNForge, mostra o pipeline como uma sequência de etapas e passa a maior parte do tempo no editor de linhas. Usa a mesma biblioteca da CLI, então um projeto pode ser extraído pela CLI e revisado no app, ou o contrário.

- Avalonia 12 em .NET 10, tema `FluentTheme` (claro ou escuro, seguindo o Windows).
- MVVM com CommunityToolkit.Mvvm; nenhuma regra de negócio nas telas.
- Tarefas longas (extrair, traduzir, build) rodam em segundo plano com barra de progresso e botão cancelar. Dá para continuar editando enquanto a IA traduz outro arquivo.
- Salvamento automático a cada edição (arquivo `text/*.json` do script atual).
- Atalhos de teclado para o fluxo de revisão: `Ctrl+Enter` aprova e vai para a próxima, `Ctrl+Espaço` pede sugestão da IA, `Alt+↑/↓` navega.

## Telas

### 1. Início

```
┌──────────────────────────────────────────────────────────────┐
│  VNForge                                                     │
│                                                              │
│  [ + Novo projeto ]   [ Abrir projeto ]                      │
│                                                              │
│  Recentes                                                    │
│  ▸ Minha VN — PT-BR      YU-RIS   62% traduzido   ontem      │
│  ▸ Teste YPF             YU-RIS    0%             3 dias     │
└──────────────────────────────────────────────────────────────┘
```

### 2. Assistente de novo projeto

Quatro passos: (1) escolher a pasta do jogo → detecção automática mostra engine, versão e arquivos encontrados; (2) idiomas e nome; (3) estratégia de acentuação, com explicação curta de cada opção; (4) provedor de IA (pode pular). No fim, oferece rodar a extração.

O assistente chama o mesmo `ProjectInitializer` do `vnf init` (`vnf init` ganhou `--source`, `--target`, `--encoding` e `--model` para as mesmas escolhas), então o `vnforge.json` e as pastas saem iguais; um teste compara os dois com um jogo sintético. O passo 1 usa `ProjectInitializer.Inspect`, que só lê a pasta do jogo. "Pular" no passo 4 grava o padrão do `vnf init` (Claude Opus 5.5). A extração do passo final roda pela barra de tarefas (#101), com andamento e cancelar.

### 3. Painel do projeto

```
┌──────────────────────────────────────────────────────────────────────┐
│ Minha VN — PT-BR                                    YU-RIS · CP1252  │
├──────────────────────────────────────────────────────────────────────┤
│  ① Extrair ✔   ② Traduzir 62%   ③ Conferir ⚠ 37   ④ Build   ⑤ Injetar│
├──────────────────────────────────────────────────────────────────────┤
│  Linhas      48.211                                                  │
│  ████████████░░░░░░░  aprovadas 21%  revisadas 18%  IA 23%  vazias 38%│
│                                                                      │
│  Por arquivo                         Por personagem                  │
│  yst00012  prólogo        100%       美咲 → Misaki     8.120 linhas   │
│  yst00013  cap. 1          74%       健太 → Kenta      6.431 linhas   │
│  ...                                 ...                             │
│                                                                      │
│  Custo de IA até agora: 2,1 M tokens                                 │
└──────────────────────────────────────────────────────────────────────┘
```

Cada etapa é um botão; o texto mostra as pendências. A página lê `ProjectDashboard` (Core): o progresso é o `ProjectStatusReporter` que o `vnf status` imprime, então total, percentual, arquivos e personagens são os mesmos números. Etapas:

- **Extrair**: concluída quando o `.cache/state.json` registra a extração.
- **Traduzir**: pendentes são as linhas sem tradução.
- **Conferir**: pendentes são as linhas de máquina e traduzidas, que ninguém revisou. A contagem de avisos do QA entra com a tela de Conferência (#106).
- **Build**: concluída quando os pacotes de `build/` conferem em tamanho e o último build foi feito com os mesmos textos, perfil de formatação e estratégia de hoje. O `vnf build` grava no `.cache/build-state.json` uma impressão digital do que ele lê de `text/*.json` (só o que entra nos bytes: original, tradução, tipo e posição das linhas que o build grava); mudar só status ou notas, ou copiar o projeto sem preservar datas, não a altera. Um build de versão anterior, sem impressão digital, aparece como pendente até o próximo build.
- **Injetar**: concluída quando o Build está concluído e o backup mais recente lista arquivos que estão no jogo com o tamanho do arquivo de `build/`. A comparação é por tamanho, porque hashear os pacotes do jogo a cada abertura do Painel seria lento; depois de um `vnf restore` o jogo volta aos originais, o tamanho muda e a etapa volta a pendente. Dois arquivos de mesmo tamanho e conteúdo diferente passariam, o que o `vnf inject` (que compara hash) corrige.

Traduzir abre o Editor, Conferir abre a Conferência, e Build e Injetar abrem a tela Build (a injeção mora nela); Build e Injetar só ficam acionáveis enquanto pendentes. Extrair não tem tela. O custo de IA é o total de `.cache/ai-usage.json` (tokens e dólares), que `vnf translate` e `vnf review` atualizam ao fim de cada execução; sem o arquivo, o Painel diz que não há chamada registrada. Execução de modelo sem preço na tabela não soma dólar: o Painel mostra o custo como parcial ou desconhecido, nunca US$ 0,00. Um `ai-usage.json` inválido vai para `ai-usage.json.bad` e a conta recomeça; um arquivo que não abre naquele instante deixa a execução sem registro, sem apagar o histórico. A página recarrega a cada vez que é aberta.

### 4. Editor de tradução (tela principal)

```
┌───────────────┬──────────────────────────────────────────┬─────────────────────────┐
│ Scripts       │ #    Personagem  Original        Tradução │ Linha yst00013:0042     │
│ 🔍 filtrar     │ 40   美咲        おはよう！      Bom dia! │                         │
│               │ 41   —           窓の外は…       Lá fora… │ Original                │
│ ▸ yst00012 ✔  │▶42   美咲        今日は、いい…   Hoje est…│ 今日は、いい天気だね。\p │
│ ▸ yst00013 74%│ 43   健太        そうだな。      É mesmo. │                         │
│ ▸ yst00014 0% │ 44   —           ……             …        │ Tradução                │
│               │                                          │ ┌─────────────────────┐ │
│ Filtros       │                                          │ │Hoje está um dia     │ │
│ ☐ só pendentes│                                          │ │bonito, né?\p        │ │
│ ☐ com aviso   │                                          │ └─────────────────────┘ │
│ ☐ personagem  │                                          │ [Sugestão IA] [Aprovar] │
│               │                                          │                         │
│               │                                          │ Prévia da caixa de texto│
│               │                                          │ ┌─────────────────────┐ │
│               │                                          │ │Misaki               │ │
│               │                                          │ │Hoje está um dia     │ │
│               │                                          │ │bonito, né?          │ │
│               │                                          │ └─────────────────────┘ │
│               │                                          │ 2/3 linhas · 22/54 col. │
│               │                                          │ ⚠ nenhum aviso          │
└───────────────┴──────────────────────────────────────────┴─────────────────────────┘
```

- Grade virtualizada (aguenta dezenas de milhares de linhas).
- Cores por status; ícone de aviso do QA ao lado da linha.
- Busca e substituição em todo o projeto, com regex e prévia. Fica na página **Busca** do trilho (serviço `ProjectSearch` no Core). A busca procura na tradução ou no original; a substituição só altera a tradução. A prévia lista cada linha com o antes e o depois e não grava nada; **Aplicar** grava exatamente a prévia mostrada, e mudar qualquer campo a descarta. Ocorrência que encosta num marcador de controle (`\n`, `\p`, `{...}`) não conta, nem na busca nem na troca, e a linha cuja troca deixaria um marcador malformado (ou que já tem um) não é alterada: a prévia lista essas linhas com o motivo. "Palavra inteira" trata o marcador vizinho como separador e só vale na tradução (no original em japonês kana e kanji são todos letras). Cada busca tem limite de 2 s por linha, para uma regex catastrófica dar erro em vez de travar, e o botão Cancelar interrompe busca e prévia. Um padrão que só casa texto vazio (`^`, `$`) é avisado e ignorado. A linha gravada vira `translated` com origem `manual`, como uma edição no Editor; a prévia diz quantas linhas aprovadas ou revisadas perdem o status.
- Painel da direita: original, tradução editável, sugestão da IA lado a lado, notas, histórico.
- **Prévia da caixa de texto**: renderiza com a fonte, a largura em colunas e o limite de linhas do perfil de formatação. É aqui que ajustes de espaçamento ficam visíveis antes do build.
- Marcadores de controle (`\p`, `\n`) aparecem como "chips" não editáveis por engano.

### 5. Conferência (QA)

Três colunas, como o artboard "Conferir" do canvas v2. O cabeçalho diz quando foi a última conferência, quantas linhas e quantas regras ativas, tem os chips Todos, Erros e Avisos com a contagem e os botões "Corrigir N automaticamente" e "Conferir de novo". Com erro aberto, uma faixa vermelha avisa "N erros bloqueiam o build". A coluna **Regras** lista as categorias de `QaCategories` (Marcadores, Caixa de texto, Espaços, Pontuação, Acentuação, Glossário, Consistência; Tradução e Outras só quando têm problema) com a contagem e o ícone do pior sinal. A lista de **problemas** mostra linha em mono, título da regra, o trecho que a correção muda e "Corrigível"; erros primeiro, depois por script e linha, e "não traduzida" por último, resumida numa linha por script. O **detalhe** do selecionado traz o `SeverityTag`, a explicação da regra, o original, a tradução atual, a correção proposta, "Abrir no editor", "Aplicar correção" e "Marcar como ok" (só aviso de uma linha).

Implementação (#106, #245): a tela usa o mesmo `QaEngine` do `vnf check`, então os números batem com a CLI. As contagens dos chips e das regras são do relatório inteiro; o filtro de gravidade e a categoria só mudam a lista. A correção proposta vem de `QaFixer.Propose`, que roda o `IQaFixer` da regra sobre a tradução gravada de cada linha apontada (uma proposta por linha e regra, sem encadear regras); "Aplicar correção" chama `QaFixer.ApplyOne` e grava só aquela linha, e "Corrigir N automaticamente" chama `QaFixer.Apply`. Só `espaco-duplo` e `espaco-antes-pontuacao` têm corretor hoje. "Marcar como ok" chama `QaFlags.MarkOk`: erro não se silencia. Duplo clique na ocorrência (ou "Abrir no editor") leva à linha no Editor. A lista é lida ao abrir a página, e o Editor relê ao voltar, na mesma linha, se a Conferência mudou algum texto. Enquanto a Conferência lê ou grava, ela fica ocupada: não troca de página e desabilita os botões.

### 6. Glossário e personagens

Duas tabelas editáveis. Personagens têm nome original, tradução, gênero e notas de estilo de fala (vai para o prompt da IA). O glossário avisa no editor quando um termo aparece na linha atual.

### 7. Configurações

- **IA**: provedor fixo em Claude na primeira versão (outros aparecem como "em breve"), escolha do modelo com preço e estimativa de custo, chave (guardada com DPAPI no perfil do usuário, nunca no projeto), tamanho do lote, concorrência, guia de estilo.
- **Formatação**: cada regra de espaçamento com liga/desliga, largura máxima, linhas por caixa, substituições personalizadas; prévia ao vivo com uma linha de exemplo.
- **Acentuação**: estratégia (`fold` / `remap` / `cp1252`), tabela de mapeamento editável no modo `remap`.

### 8. Build e injeção

Log em tempo real, resumo do que mudou (scripts reconstruídos, tamanho do YPF novo), botões **Injetar**, **Abrir jogo** e **Restaurar originais**. Mostra onde está o backup.

**Linhas sem tradução não bloqueiam o build** (decisão de 2026-10-24, #232). O build copia o original nelas e o resumo mostra a contagem ("6.853 linhas sem tradução; o build copiou o original nelas."), então um projeto recém-extraído constrói sem ligar nada. Os outros avisos do QA continuam exigindo "Permitir avisos do QA"; erros bloqueiam sempre.

## Como fica rodando no dia a dia

1. Você abre o projeto; o app lembra o último script e a última linha.
2. Seleciona alguns scripts → "Traduzir com IA" → a tarefa aparece na barra inferior com progresso e custo; você continua revisando outro arquivo.
3. Revisa linha a linha com a prévia da caixa; `Ctrl+Enter` aprova.
4. "Conferir" antes do build; corrige os erros.
5. "Build + Injetar + Abrir jogo" num clique para testar no jogo. Achou algo estranho no jogo? Volta, corrige, repete.
6. Tudo que o app fez está nos arquivos do projeto: `git commit` a qualquer momento.

## Em aberto

- [x] Telas da primeira versão (D4, #97, 2026-10-24): **Início e Editor** entram no M3 (#98, #99, #100). Painel (#102), Conferência (#106), Build e injeção (#103) e Ajustes (#110) ficam no M4; até lá, o resto vai pela CLI. O trilho de navegação mostra só Início e Editor no M3.
- [x] Ajustes (#110, 2026-10-24): provedor Claude com os outros em "em breve", modelo com preço, chave só no cofre DPAPI (`apikey.dat` em `%APPDATA%/VNForge`), esforço, lote, concorrência (`app.json`, do usuário), guia de estilo (`style.md`), acentuação e as regras de normalização, espaçamento, aspas, reticências e quebra de linha com prévia ao vivo. Modelo, esforço, lote e acentuação gravam em `vnforge.json`; as regras em `formatting.json`. Ficam para depois: estimativa de custo das linhas pendentes, "Testar conexão", substituições personalizadas e a tabela do `remap`.
- [x] Build e injeção (#103, 2026-10-24): a página **Build** do trilho usa o `ProjectBuilder`, o `GameInjector` e o `GameLauncher` do Core, os mesmos de `vnf build`, `vnf inject`, `vnf restore` e `vnf run`. Cada operação é uma tarefa da barra inferior. Injetar e Restaurar originais pedem confirmação na própria página antes de escrever no jogo, mostram o plano (arquivos que substituem original ou são novos) e o local do backup. O log do Core hoje só tem as etapas, não uma linha por script, porque o `ProjectBuilder` não reporta progresso. Ficam para depois: "Build + Injetar + Abrir jogo" num clique e o patch de codepage no executável.
- [ ] A prévia deve usar a fonte real do jogo? (Dá para ler o nome da fonte da configuração do YU-RIS.)
