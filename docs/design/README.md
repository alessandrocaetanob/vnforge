# Design do app

Cópia local do design do aplicativo, para a implementação em Avalonia ter a fonte ao lado do código. Copiado em 24/10/2026 (issue #98).

| Origem | Link |
|---|---|
| Canvas das telas "VNForge v2 · App Windows" | https://claude.ai/artifact/97EhvQiUBs3pcHUqUFBkEc |
| Canvas das telas "VNForge v3 · App Windows" (versão 30, doze artboards; plano [2026-10-25-interface-v3.md](../plans/2026-10-25-interface-v3.md)). As capturas do app renderizado estão em `telas/v3/` (ver [Telas da v3](#telas-da-v3)) | https://claude.ai/artifact/X4z2JK2pfKH9zZSeBGkfUW |
| Design system "VNForge v2" | https://claude.ai/artifact/TvsKGRUc1BFYezZdfydrh6 |

## Conteúdo

| Arquivo | O que é |
|---|---|
| `README-design-system.md` | README do design system: conteúdo e termos da v3, regras das telas, cores, tipografia, espaço, estados, ícones, marca, componentes da v3 |
| `avalonia.md` | Como o design vira recursos e controles Avalonia |
| `tokens.json` | Fonte dos tokens. `tools/Generate-Tokens.ps1` gera `src/VNForge.App/Themes/Tokens.axaml` a partir dele |
| `components/*/README.md` | Um README por componente |
| `telas/*.dc.html` | As telas desenhadas no canvas (Início, Painel, Editor escuro em `Main`, Editor claro, Conferir, Build, Ajustes) e, desenhadas na #248 na mesma linguagem e ainda fora do canvas do claude.ai, `Glossario` e `NovoProjeto`. Dependem do `support.js` e do CSS do design system, que não vêm junto; servem de leitura |
| `telas/v3/*.png` | Capturas do app da interface v3, renderizadas sem janela (ver [Telas da v3](#telas-da-v3)) |

Quando o design mudar: atualize `tokens.json` aqui, rode `pwsh tools/Generate-Tokens.ps1` e confira o diff do `Tokens.axaml`. O teste `TokensTests` falha se os dois divergirem.

## Ícones

Os ícones são os Fluent UI System Icons da Microsoft (MIT), estilo Regular 20. Os traçados em `src/VNForge.App/Assets/Icons.axaml` vêm do repositório oficial `microsoft/fluentui-system-icons`, por `pwsh tools/Update-Icons.ps1`, e não dos blobs do canvas.

O ícone do app (`src/VNForge.App/Assets/VNForge.ico`, 16, 24, 32, 48 e 256 px) é o selo cheio desenhado do `Brand.Seal` por `pwsh tools/New-AppIcon.ps1`: placa em `brand-shu` (tema claro) inclinada -4° com o 訳 em tom de papel. Refaça o arquivo com o script quando a marca mudar.

## Fontes

Noto Sans (400, 500, 600), Noto Sans JP (400, 500), Zen Kaku Gothic New (500, 700), JetBrains Mono (400, 500) e BIZ UDGothic (400, a reserva da prévia da caixa de texto quando a MS Gothic do sistema não existe), todas sob a SIL OFL 1.1, embutidas em `src/VNForge.App/Assets/Fonts` com as licenças em `licenses/`. O Noto Sans JP vai completo, sem subconjunto de glifos, porque o texto vem do jogo.

## Telas da v3

As imagens de `telas/v3/` saem do próprio app: os testes de renderização (`tests/VNForge.App.Tests/Rendering`, Avalonia.Headless com Skia) desenham cada tela com o projeto de demonstração e gravam o PNG na pasta de `VNFORGE_RENDER_DIR`. Para refazer:

```
$env:VNFORGE_RENDER_DIR = "<pasta>"
dotnet test --project tests/VNForge.App.Tests --filter-namespace "VNForge.App.Tests.Rendering"
```

O tamanho é o do canvas, **1440×1000** (`ScreenRenderer.Width` e `Height`), e não 1440×900: é o tamanho que os artboards e todos os testes de renderização do repositório usam. As telas compactas ficam em 1366×768. Cada tela existe nos dois temas (`-escuro` e `-claro`). O texto das capturas vem dos dados de demonstração, não de jogo comercial.

| Artboard do canvas | Arquivos em `telas/v3/` | Observação |
|---|---|---|
| 1 · Início | | Ainda no desenho da v2 (`docs/design/capturas/inicio`); o Início v3 vem na #313 |
| 2 · Novo projeto | `novo-projeto-v3-1-jogo`, `novo-projeto-v3-3-acentos`, `novo-projeto-v3-4-ia-e-gasto` | Os passos 2 (idiomas) e 5 (pronto) não mudaram: `docs/design/telas/prints/novo-projeto-2-idiomas-*` e `-5-pronto-*` |
| 3 · Painel | | Ainda no desenho da v2 (`docs/design/prints/painel-*`); o Painel v3 vem na #309 |
| 4, 5 · Editor | `editor-1440`, `editor-inspetor` (com "Entender o japonês" aberto), `editor-caixa-nao-cabe`, `editor-caixa-dividida` | |
| 6 · Editor em 1366×768 | `editor-compacto-traducao`, `editor-compacto-sugestao`, `editor-compacto-contexto` | Abas do inspetor de 360 px |
| 7 · Modo revisão | `modo-revisao-cabe`, `modo-revisao-passa`, `modo-revisao-fim`, `modo-revisao-1366` | |
| 8 · Traduzir com IA | `traduzir-com-ia`, `traduzir-com-ia-acima-do-limite` | |
| 9 · Conferir | `conferir-caixa-nao-cabe` | Tela Conferir a tradução (#334); `conferir-caixa-nao-cabe` mostra a caixa do jogo e o aviso, `conferir-v3-problemas` e `conferir-v3-um-clique` a lista com o painel |
| 10 · Testar no jogo | `testar-no-jogo-traduzido`, `testar-no-jogo-travado`, `testar-no-jogo-detalhes` | |
| 11 · Ajustes | `ajustes-ia`, `ajustes-estilo`, `ajustes-formatacao`, `ajustes-acentuacao` | A aba Aparência não tem captura |
| 12 · Glossário | `glossario-v3-personagens`, `glossario-v3-termos` | |
| Componentes | `galeria-v3` | Galeria de controles (800×5200) |

As capturas em `docs/design/telas/prints`, `docs/design/telas/render`, `docs/design/renders`, `docs/design/render`, `docs/design/prints`, `docs/design/capturas` e `docs/design/u9` são de etapas anteriores (v2 e PRs de acabamento) e ficam como registro.
