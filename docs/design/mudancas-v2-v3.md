Comparação entre o `docs/design/tokens.json` da v2 e o da v3. O esquema é o mesmo e `tools/Generate-Tokens.ps1` aceita o arquivo sem mudança: ele gera `Tokens.axaml` com os recursos `Vnf.*` de sempre. A única chave nova de topo é `duration`, que o gerador ignora (as durações entram como constantes de transição no estilo dos controles).

## Entra

- **Cores (14):** `cover-chip-bg`, `cover-chip-ink`, `cover-chip-warn`, `cover-veil-bg`, `cover-scrim`, `speaker-1`, `speaker-2`, `speaker-3`, `speaker-4`, `speaker-5`, `speaker-6`, `speaker-7`, `speaker-8`, `dialog-scrim`.
- **Espaço (1):** `space-10` (40).
- **Tamanhos (11):** `drawer-width`, `cover-card-min`, `cover-drawer-width`, `cover-thumb-width`, `portrait-sm`, `portrait-md`, `portrait-lg`, `cover-state-size`, `cover-state-clearance`, `tag-height`, `progress-height`.
- **Raios (2):** `radius-lg` (16) e `radius-xl` (20).
- **Sombra (1):** `shadow-cover`.
- **Durações (2):** `duration-fast` (167ms) e `duration-normal` (250ms).
- **Estilos de texto (3):** `title-xl`, `title-sm`, `column-header`.

## Sai

Nenhum token sai. Nada foi renomeado: manter os nomes deixa o `Tokens.axaml` compatível e a migração das telas (e4) só troca valores e usos.

## Muda de valor

- **Cores (35 de 45):** todo o miolo troca de azul-ai para verde-água e de azul-acinzentado para grafite esverdeado. Ficam iguais: `brand-shu`, `textbox-bg`, `textbox-ink`, `textbox-overflow`, `textbox-plate`, `stage-top`, `stage-bottom`, `stage-vignette`, `stage-card`, `stage-stroke`.
- **`status-translated-*`:** no v2 era alias de `accent-soft` e `accent-text`. Como o acento agora é verde-água e o estado "Sua" precisa continuar distinto de "Pronta", vira par próprio azul-lavanda, com valores explícitos.
- **Tamanhos:** `row-height` 36px → 48px; `navrail-width` 72px → 80px; `inspector-width` 400px → 384px; `scripts-width` 190px → 220px.
- **Raios:** `radius-sm` 4 → 8 e `radius-md` 8 → 12. Os painéis de hoje usam `radius-md` e passam a `radius-lg`.
- **Sombra:** `shadow-flyout` no claro ganha tinta esverdeada (`rgba(20,28,26,0.16)`), no escuro continua igual.
- **Texto:** `jp-body` de 15/24 para 19/30; `overline` de 12 para 11 px, agora em caixa alta com 0,07 em; `title-lg` de 26/38 para 24/32 (o 34/40 virou `title-xl`); `title-md` continua 17/24.

## Cuidados na implementação

- Os tokens `stage-*`, `cover-scrim` e `dialog-scrim` seguem a convenção do v2: valor com alfa em AARRGGBB, como o Avalonia lê. O design system os mostra como cor sem alfa.
- `TokensTests` compara `tokens.json` com o `Tokens.axaml` gerado: rode o gerador antes de rodar os testes.
- Os seis ícones novos entraram em `Icons.axaml` por `tools/Update-Icons.ps1` (nomes `Library`, `ArrowLeft`, `Eye`, `EyeOff`, `Link`, `Filter`).
