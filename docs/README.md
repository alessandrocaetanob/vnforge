# VNForge — documentos de planejamento

Rascunho para o brainstorm antes de criar o repositório. Nada aqui é definitivo: cada documento termina com as decisões que ainda estão em aberto.

| Documento | Conteúdo |
|---|---|
| [01-nome.md](01-nome.md) | Opções de nome e recomendação |
| [02-arquitetura.md](02-arquitetura.md) | Solução .NET 10, camadas, formato do projeto de tradução, pipeline |
| [03-cli.md](03-cli.md) | Comandos da CLI, exemplos e fluxo de uso |
| [04-app-windows.md](04-app-windows.md) | Telas do aplicativo Windows e como ele funciona no dia a dia |
| [05-epicos.md](05-epicos.md) | Épicos, histórias e marcos até o benchmark YU-RIS |
| [06-yuris-notas-tecnicas.md](06-yuris-notas-tecnicas.md) | O que já levantei sobre os formatos do YU-RIS (YPF, YBN, codificação) |
| [plans/2026-10-22-backlog-github.md](plans/2026-10-22-backlog-github.md) | Marcos, épicos e issues no GitHub, com dependências |
| [plans/2026-10-27-roteiro-teste-v4.md](plans/2026-10-27-roteiro-teste-v4.md) | Roteiro do teste de tradução da interface v4, tela por tela, com a release teste.11 |
| [plans/2026-10-27-roteiro-teste-12.md](plans/2026-10-27-roteiro-teste-12.md) | Reteste da #417 com a teste.12: zero erros de codificação depois de um clique |
| [plans/2026-10-29-roteiro-teste-v5.md](plans/2026-10-29-roteiro-teste-v5.md) | Roteiro do teste de tradução da interface v5, com a release teste.18 e os três pontos nunca vistos no app real |

Design do app (telas, tokens e componentes): [design/](design/README.md).

## Decisões tomadas

| Tema | Decisão |
|---|---|
| Nome | VNForge, CLI `vnf` |
| Repositório | Privado no GitHub |
| Runtime | .NET 10 |
| IA | Só Claude na primeira versão, modelo selecionável; outros provedores depois |
| Interface do app | Avalonia 12, tema `FluentTheme`, design system "VNForge v2" (D3, #96) |
| Interface v3 | Vocabulário comum, custo em reais, modo revisão e testar no jogo, sobre o mesmo design system; [plano](plans/2026-10-25-interface-v3.md), termos e componentes em [design/](design/README-design-system.md) (U23, #315) |
| Telas do M3 | Início e Editor; o resto no M4 (D4, #97) |

## Visão em uma frase

Uma ferramenta em C# (.NET 10), com CLI e aplicativo Windows, que extrai o texto de uma visual novel, ajuda a traduzir com IA, confere a tradução, reconstrói os arquivos do jogo e injeta o resultado, tratando direito espaçamento e acentuação do português.

## Princípios

1. **Núcleo único, duas interfaces.** CLI e app Windows chamam a mesma biblioteca. Qualquer coisa que a GUI faz, a CLI também faz (e vice-versa).
2. **O jogo original nunca é alterado sem backup.** Toda injeção tem rollback.
3. **Reconstrução mínima.** Só os bytes que mudaram são reescritos. O resto dos arquivos do jogo é copiado byte a byte.
4. **Tradução é dado, não código.** O texto fica em arquivos JSON legíveis, versionáveis no git, um por script do jogo.
5. **A IA sugere, o humano decide.** Toda linha tem status (não traduzida, traduzida por IA, revisada, aprovada).
6. **Engines são plugins.** YU-RIS é o primeiro, mas o núcleo não sabe nada de YU-RIS.
7. **Arquivos do jogo nunca entram no repositório.** Nem extraídos, nem traduzidos em formato binário.
