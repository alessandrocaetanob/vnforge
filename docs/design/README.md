# Design do app

Cópia local do design do aplicativo, para a implementação em Avalonia ter a fonte ao lado do código. Copiado em 24/10/2026 (issue #98).

| Origem | Link |
|---|---|
| Canvas das telas "VNForge v2 · App Windows" | https://claude.ai/artifact/97EhvQiUBs3pcHUqUFBkEc |
| Design system "VNForge v2" | https://claude.ai/artifact/TvsKGRUc1BFYezZdfydrh6 |

## Conteúdo

| Arquivo | O que é |
|---|---|
| `README-design-system.md` | README do design system: conteúdo, cores, tipografia, espaço, estados, ícones, marca |
| `avalonia.md` | Como o design vira recursos e controles Avalonia |
| `tokens.json` | Fonte dos tokens. `tools/Generate-Tokens.ps1` gera `src/VNForge.App/Themes/Tokens.axaml` a partir dele |
| `components/*/README.md` | Um README por componente |
| `telas/*.dc.html` | As telas desenhadas no canvas (Início, Painel, Editor escuro em `Main`, Editor claro, Conferir, Build, Ajustes) e, desenhadas na #248 na mesma linguagem e ainda fora do canvas do claude.ai, `Glossario` e `NovoProjeto`. Dependem do `support.js` e do CSS do design system, que não vêm junto; servem de leitura |

Quando o design mudar: atualize `tokens.json` aqui, rode `pwsh tools/Generate-Tokens.ps1` e confira o diff do `Tokens.axaml`. O teste `TokensTests` falha se os dois divergirem.

## Ícones

Os ícones são os Fluent UI System Icons da Microsoft (MIT), estilo Regular 20. Os traçados em `src/VNForge.App/Assets/Icons.axaml` vêm do repositório oficial `microsoft/fluentui-system-icons`, por `pwsh tools/Update-Icons.ps1`, e não dos blobs do canvas.

## Fontes

Noto Sans (400, 500, 600), Noto Sans JP (400, 500), Zen Kaku Gothic New (500, 700) e JetBrains Mono (400, 500) e BIZ UDGothic (400, a reserva da prévia da caixa de texto quando a MS Gothic do sistema não existe), todas sob a SIL OFL 1.1, embutidas em `src/VNForge.App/Assets/Fonts` com as licenças em `licenses/`. O Noto Sans JP vai completo, sem subconjunto de glifos, porque o texto vem do jogo.
