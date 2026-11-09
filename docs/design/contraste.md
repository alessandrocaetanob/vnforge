Razão de contraste WCAG 2 calculada sobre os valores hexadecimais de `tokens.json`: 172 pares texto/fundo e acento/fundo, nos dois temas. Mínimos: 4,5:1 para texto e 3:1 para borda de controle, anel de foco, acento e barras. A divisória `stroke-subtle` é decorativa e fica fora da exigência.

Com os valores do quadro e1, **32 pares** ficavam abaixo do mínimo. Depois dos ajustes abaixo, **0**. Os fundos de selo foram derivados do tom base do estado misturado ao cartão (18% no escuro, 14% no claro); só o texto foi escurecido ou clareado.

## O que foi ajustado

| Token | Tema | Quadro e1 | v3 | Razão sobre bg-card |
|---|---|---|---|---|
| `stroke-control` | Escuro | `#5E6A66` | `#636F6B` | 2,83 para 3,05 |
| `stroke-control` | Claro | `#B7BEB9` | `#7F8B82` | 1,90 para 3,55 |
| `text-tertiary` | Claro | `#6A736E` | `#656E69` | 4,89 para 5,27 |
| `status-approved-fg` | Claro | `#2E9E62` | `#247A4C` | 3,40 para 5,30 |
| `status-reviewed-fg` | Claro | `#1F8FA6` | `#197588` | 3,79 para 5,33 |
| `status-translated-fg` | Claro | `#4E7BD8` | `#3165D2` | 4,08 para 5,35 |
| `status-ai-fg` | Escuro | `#A58BF2` | `#AC94F3` | 5,74 para 6,28 |
| `status-ai-fg` | Claro | `#7B5BD6` | `#714FD3` | 4,87 para 5,58 |
| `status-pending-fg` | Escuro | `#7C8492` | `#959CA7` | 4,23 para 5,76 |
| `status-pending-fg` | Claro | `#8A8F98` | `#666B74` | 3,25 para 5,36 |
| `signal-warning-fg` | Claro | `#B8700A` | `#965B08` | 3,92 para 5,53 |
| `seg-approved` | Claro | `#2E9E62` | `#2D9A60` | 3,40 para 3,56 |
| `seg-pending` | Claro | `#8A8F98` | `#828791` | 3,25 para 3,61 |
| `speaker-1` | Claro | `#298E6D` | `#22765B` | 4,05 para 5,51 |
| `speaker-4` | Claro | `#8E6329` | `#8A6028` | 5,29 para 5,55 |
| `speaker-5` | Escuro | `#B581DA` | `#B785DB` | 5,40 para 5,60 |
| `speaker-6` | Claro | `#538E29` | `#437221` | 3,99 para 5,73 |
| `speaker-7` | Claro | `#297D8E` | `#246F7E` | 4,75 para 5,75 |

A mudança de maior efeito é a **borda de controle no tema claro** (1,90 para 3,55): a do quadro era uma divisória, não uma borda de controle. Botão, campo e caixa de seleção ficam mais definidos. Os textos de selo no tema claro escureceram para passar 4,5:1 sobre o próprio fundo e sobre o cartão.

## Tabela completa

Cada linha mostra a razão final no tema escuro e no claro. "Mín." é o mínimo exigido.

| Par | Tipo | Mín. | Escuro | Claro |
|---|---|---|---|---|
| Texto principal: `text-primary` sobre `bg-base` | texto | 4,5 | 16,25 ok | 15,52 ok |
| Texto principal: `text-primary` sobre `bg-layer` | texto | 4,5 | 15,27 ok | 16,50 ok |
| Texto principal: `text-primary` sobre `bg-card` | texto | 4,5 | 13,85 ok | 17,43 ok |
| Texto principal: `text-primary` sobre `bg-card-hover` | texto | 4,5 | 12,21 ok | 15,31 ok |
| Texto principal: `text-primary` sobre `bg-inset` | texto | 4,5 | 15,86 ok | 14,94 ok |
| Texto principal: `text-primary` sobre `accent-soft` | texto | 4,5 | 11,51 ok | 14,32 ok |
| Texto secundário: `text-secondary` sobre `bg-base` | texto | 4,5 | 8,17 ok | 5,81 ok |
| Texto secundário: `text-secondary` sobre `bg-layer` | texto | 4,5 | 7,68 ok | 6,18 ok |
| Texto secundário: `text-secondary` sobre `bg-card` | texto | 4,5 | 6,96 ok | 6,52 ok |
| Texto secundário: `text-secondary` sobre `bg-card-hover` | texto | 4,5 | 6,14 ok | 5,73 ok |
| Texto secundário: `text-secondary` sobre `bg-inset` | texto | 4,5 | 7,98 ok | 5,59 ok |
| Texto secundário: `text-secondary` sobre `accent-soft` | texto | 4,5 | 5,79 ok | 5,36 ok |
| Placeholder e dica: `text-tertiary` sobre `bg-layer` | texto | 4,5 | 5,30 ok | 4,99 ok |
| Placeholder e dica: `text-tertiary` sobre `bg-card` | texto | 4,5 | 4,81 ok | 5,27 ok |
| Placeholder e dica: `text-tertiary` sobre `bg-inset` | texto | 4,5 | 5,51 ok | 4,51 ok (era 4,19) |
| Texto de destaque: `accent-text` sobre `bg-layer` | texto | 4,5 | 11,52 ok | 7,62 ok |
| Texto de destaque: `accent-text` sobre `bg-card` | texto | 4,5 | 10,44 ok | 8,05 ok |
| Texto de destaque: `accent-text` sobre `accent-soft` | texto | 4,5 | 8,68 ok | 6,61 ok |
| Texto de destaque: `accent-text` sobre `bg-card-hover` | texto | 4,5 | 9,20 ok | 7,07 ok |
| Texto sobre o acento: `on-accent` sobre `accent` | texto | 4,5 | 9,05 ok | 5,26 ok |
| Texto sobre o acento: `on-accent` sobre `accent-hover` | texto | 4,5 | 10,86 ok | 7,19 ok |
| Selo approved: `status-approved-fg` sobre `status-approved-bg` | texto | 4,5 | 5,45 ok | 4,54 ok (era 2,91) |
| Selo reviewed: `status-reviewed-fg` sobre `status-reviewed-bg` | texto | 4,5 | 4,94 ok | 4,52 ok (era 3,22) |
| Selo translated: `status-translated-fg` sobre `status-translated-bg` | texto | 4,5 | 4,56 ok | 4,55 ok (era 3,47) |
| Selo ai: `status-ai-fg` sobre `status-ai-bg` | texto | 4,5 | 4,67 ok (era 4,27) | 4,66 ok (era 4,06) |
| Selo pending: `status-pending-fg` sobre `status-pending-bg` | texto | 4,5 | 4,56 ok (era 3,35) | 4,66 ok (era 2,83) |
| Sinal warning: `signal-warning-fg` sobre `signal-warning-bg` | texto | 4,5 | 5,62 ok | 4,69 ok (era 3,32) |
| Sinal error: `signal-error-fg` sobre `signal-error-bg` | texto | 4,5 | 5,39 ok | 4,90 ok |
| Selo approved sobre o cartão: `status-approved-fg` sobre `bg-card` | texto | 4,5 | 7,86 ok | 5,30 ok (era 3,40) |
| Selo reviewed sobre o cartão: `status-reviewed-fg` sobre `bg-card` | texto | 4,5 | 6,92 ok | 5,33 ok (era 3,79) |
| Selo translated sobre o cartão: `status-translated-fg` sobre `bg-card` | texto | 4,5 | 6,28 ok | 5,35 ok (era 4,08) |
| Selo ai sobre o cartão: `status-ai-fg` sobre `bg-card` | texto | 4,5 | 6,28 ok | 5,58 ok |
| Selo pending sobre o cartão: `status-pending-fg` sobre `bg-card` | texto | 4,5 | 5,76 ok (era 4,23) | 5,36 ok (era 3,25) |
| Selo warning sobre o cartão: `signal-warning-fg` sobre `bg-card` | texto | 4,5 | 8,18 ok | 5,53 ok (era 3,92) |
| Selo error sobre o cartão: `signal-error-fg` sobre `bg-card` | texto | 4,5 | 7,70 ok | 6,17 ok |
| Chip da capa: `cover-chip-ink` sobre `cover-chip-bg` | texto | 4,5 | 18,86 ok | 18,86 ok |
| Chip da capa sem suporte: `cover-chip-warn` sobre `cover-chip-bg` | texto | 4,5 | 11,21 ok | 11,21 ok |
| Texto sobre o véu adulto: `text-primary` sobre `cover-veil-bg` | texto | 4,5 | 13,04 ok | 13,98 ok |
| Texto secundário sobre o véu: `text-secondary` sobre `cover-veil-bg` | texto | 4,5 | 6,56 ok | 5,23 ok |
| Texto da caixa do jogo: `textbox-ink` sobre `textbox-bg` | texto | 4,5 | 15,60 ok | 15,60 ok |
| Excesso na caixa do jogo: `textbox-overflow` sobre `textbox-bg` | texto | 4,5 | 8,85 ok | 8,85 ok |
| Nome do personagem 1: `speaker-1` sobre `bg-card` | texto | 4,5 | 9,62 ok | 5,51 ok (era 4,05) |
| Nome do personagem 1: `speaker-1` sobre `accent-soft` | texto | 4,5 | 8,00 ok | 4,53 ok (era 3,33) |
| Nome do personagem 1: `speaker-1` sobre `bg-inset` | texto | 4,5 | 11,02 ok | 4,72 ok (era 3,47) |
| Nome do personagem 2: `speaker-2` sobre `bg-card` | texto | 4,5 | 6,59 ok | 7,29 ok |
| Nome do personagem 2: `speaker-2` sobre `accent-soft` | texto | 4,5 | 5,48 ok | 5,99 ok |
| Nome do personagem 2: `speaker-2` sobre `bg-inset` | texto | 4,5 | 7,55 ok | 6,25 ok |
| Nome do personagem 3: `speaker-3` sobre `bg-card` | texto | 4,5 | 5,77 ok | 8,17 ok |
| Nome do personagem 3: `speaker-3` sobre `accent-soft` | texto | 4,5 | 4,80 ok | 6,72 ok |
| Nome do personagem 3: `speaker-3` sobre `bg-inset` | texto | 4,5 | 6,61 ok | 7,01 ok |
| Nome do personagem 4: `speaker-4` sobre `bg-card` | texto | 4,5 | 8,15 ok | 5,55 ok |
| Nome do personagem 4: `speaker-4` sobre `accent-soft` | texto | 4,5 | 6,77 ok | 4,56 ok (era 4,35) |
| Nome do personagem 4: `speaker-4` sobre `bg-inset` | texto | 4,5 | 9,33 ok | 4,76 ok |
| Nome do personagem 5: `speaker-5` sobre `bg-card` | texto | 4,5 | 5,60 ok | 9,33 ok |
| Nome do personagem 5: `speaker-5` sobre `accent-soft` | texto | 4,5 | 4,66 ok (era 4,49) | 7,67 ok |
| Nome do personagem 5: `speaker-5` sobre `bg-inset` | texto | 4,5 | 6,42 ok | 8,00 ok |
| Nome do personagem 6: `speaker-6` sobre `bg-card` | texto | 4,5 | 9,83 ok | 5,73 ok (era 3,99) |
| Nome do personagem 6: `speaker-6` sobre `accent-soft` | texto | 4,5 | 8,17 ok | 4,70 ok (era 3,27) |
| Nome do personagem 6: `speaker-6` sobre `bg-inset` | texto | 4,5 | 11,26 ok | 4,91 ok (era 3,42) |
| Nome do personagem 7: `speaker-7` sobre `bg-card` | texto | 4,5 | 8,71 ok | 5,75 ok |
| Nome do personagem 7: `speaker-7` sobre `accent-soft` | texto | 4,5 | 7,24 ok | 4,73 ok (era 3,91) |
| Nome do personagem 7: `speaker-7` sobre `bg-inset` | texto | 4,5 | 9,98 ok | 4,93 ok (era 4,07) |
| Nome do personagem 8: `speaker-8` sobre `bg-card` | texto | 4,5 | 5,93 ok | 7,96 ok |
| Nome do personagem 8: `speaker-8` sobre `accent-soft` | texto | 4,5 | 4,93 ok | 6,54 ok |
| Nome do personagem 8: `speaker-8` sobre `bg-inset` | texto | 4,5 | 6,79 ok | 6,82 ok |
| Borda de controle: `stroke-control` sobre `bg-base` | elemento | 3 | 3,57 ok | 3,16 ok (era 1,69) |
| Borda de controle: `stroke-control` sobre `bg-layer` | elemento | 3 | 3,36 ok | 3,36 ok (era 1,79) |
| Borda de controle: `stroke-control` sobre `bg-card` | elemento | 3 | 3,05 ok (era 2,83) | 3,55 ok (era 1,90) |
| Borda de controle: `stroke-control` sobre `bg-inset` | elemento | 3 | 3,49 ok | 3,04 ok (era 1,62) |
| Anel de foco: `focus-ring` sobre `bg-base` | elemento | 3 | 12,25 ok | 7,16 ok |
| Anel de foco: `focus-ring` sobre `bg-layer` | elemento | 3 | 11,52 ok | 7,62 ok |
| Anel de foco: `focus-ring` sobre `bg-card` | elemento | 3 | 10,44 ok | 8,05 ok |
| Anel de foco: `focus-ring` sobre `accent-soft` | elemento | 3 | 8,68 ok | 6,61 ok |
| Acento (seleção, progresso, botão): `accent` sobre `bg-layer` | elemento | 3 | 9,30 ok | 4,98 ok |
| Acento (seleção, progresso, botão): `accent` sobre `bg-card` | elemento | 3 | 8,43 ok | 5,26 ok |
| Barra approved: `seg-approved` sobre `bg-card` | elemento | 3 | 7,86 ok | 3,56 ok |
| Barra approved: `seg-approved` sobre `bg-inset` | elemento | 3 | 9,00 ok | 3,05 ok (era 2,91) |
| Barra reviewed: `seg-reviewed` sobre `bg-card` | elemento | 3 | 6,92 ok | 3,79 ok |
| Barra reviewed: `seg-reviewed` sobre `bg-inset` | elemento | 3 | 7,92 ok | 3,25 ok |
| Barra translated: `seg-translated` sobre `bg-card` | elemento | 3 | 6,28 ok | 4,08 ok |
| Barra translated: `seg-translated` sobre `bg-inset` | elemento | 3 | 7,19 ok | 3,50 ok |
| Barra ai: `seg-ai` sobre `bg-card` | elemento | 3 | 5,74 ok | 4,87 ok |
| Barra ai: `seg-ai` sobre `bg-inset` | elemento | 3 | 6,58 ok | 4,18 ok |
| Barra pending: `seg-pending` sobre `bg-card` | elemento | 3 | 4,23 ok | 3,61 ok |
| Barra pending: `seg-pending` sobre `bg-inset` | elemento | 3 | 4,84 ok | 3,09 ok (era 2,78) |
| Divisória (decorativa, sem exigência): `stroke-subtle` sobre `bg-card` | decorativo | 1 | 1,21 ok | 1,32 ok |

Fora da tabela: o texto de `cover-chip-ink` e `cover-chip-warn` é medido só sobre o selo (`cover-chip-bg`), não sobre a imagem; `textbox-*` e `stage-*` são do jogo e iguais nos dois temas.