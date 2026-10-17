# 04 — Aplicativo Windows

**Protótipo visual:** [VNForge App Windows no Claude Design](https://claude.ai/artifact/36wMrNUfoMvXmhRNdJQhrn). São seis telas navegáveis (Início, Painel, Editor, Conferência, Build e injeção, Configurações). O Editor é interativo: selecionar linha, editar a tradução, aprovar, usar a sugestão da IA e ver a prévia da caixa de texto recalcular a quebra de linha. Os desenhos em texto abaixo são o rascunho que originou o protótipo; quando divergirem, vale o protótipo.

## Ideia geral

O app é uma "mesa de tradução": abre um projeto VNForge, mostra o pipeline como uma sequência de etapas e passa a maior parte do tempo no editor de linhas. Usa a mesma biblioteca da CLI, então um projeto pode ser extraído pela CLI e revisado no app, ou o contrário.

- WPF em .NET 10, tema Fluent (claro/escuro seguindo o Windows).
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

Cada etapa é um botão; o número mostra pendências.

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
- Busca e substituição em todo o projeto, com regex e prévia.
- Painel da direita: original, tradução editável, sugestão da IA lado a lado, notas, histórico.
- **Prévia da caixa de texto**: renderiza com a fonte, a largura em colunas e o limite de linhas do perfil de formatação. É aqui que ajustes de espaçamento ficam visíveis antes do build.
- Marcadores de controle (`\p`, `\n`) aparecem como "chips" não editáveis por engano.

### 5. Conferência (QA)

Lista de problemas agrupada por regra, com filtro por severidade. Clicar leva à linha no editor. Botões "corrigir automaticamente" para regras seguras e "marcar como ok" para falsos positivos (gravado no `flags` da linha).

### 6. Glossário e personagens

Duas tabelas editáveis. Personagens têm nome original, tradução, gênero e notas de estilo de fala (vai para o prompt da IA). O glossário avisa no editor quando um termo aparece na linha atual.

### 7. Configurações

- **IA**: provedor fixo em Claude na primeira versão (outros aparecem como "em breve"), escolha do modelo com preço e estimativa de custo, chave (guardada com DPAPI no perfil do usuário, nunca no projeto), tamanho do lote, concorrência, guia de estilo.
- **Formatação**: cada regra de espaçamento com liga/desliga, largura máxima, linhas por caixa, substituições personalizadas; prévia ao vivo com uma linha de exemplo.
- **Acentuação**: estratégia (`fold` / `remap` / `cp1252`), tabela de mapeamento editável no modo `remap`.

### 8. Build e injeção

Log em tempo real, resumo do que mudou (scripts reconstruídos, tamanho do YPF novo), botões **Injetar**, **Abrir jogo** e **Restaurar originais**. Mostra onde está o backup.

## Como fica rodando no dia a dia

1. Você abre o projeto; o app lembra o último script e a última linha.
2. Seleciona alguns scripts → "Traduzir com IA" → a tarefa aparece na barra inferior com progresso e custo; você continua revisando outro arquivo.
3. Revisa linha a linha com a prévia da caixa; `Ctrl+Enter` aprova.
4. "Conferir" antes do build; corrige os erros.
5. "Build + Injetar + Abrir jogo" num clique para testar no jogo. Achou algo estranho no jogo? Volta, corrige, repete.
6. Tudo que o app fez está nos arquivos do projeto: `git commit` a qualquer momento.

## Em aberto

- [ ] Prioridade das telas para a primeira versão (sugestão: Início, Editor, Build/Injeção; o resto via CLI no começo).
- [ ] A prévia deve usar a fonte real do jogo? (Dá para ler o nome da fonte da configuração do YU-RIS.)
