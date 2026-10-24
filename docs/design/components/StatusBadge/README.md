# StatusBadge

Status de uma linha de tradução. Sempre com o texto do status; a cor nunca aparece sozinha.

| Status (`TextUnit.status`) | Rótulo | Tokens |
|---|---|---|
| `untranslated` | Pendente | `status-pending-bg` / `status-pending-fg`, ponto vazado |
| `machine` | IA | `status-ai-bg` / `status-ai-fg` |
| `translated` | Traduzida | `status-translated-bg` / `status-translated-fg` |
| `reviewed` | Revisada | `status-reviewed-bg` / `status-reviewed-fg` |
| `approved` | Aprovada | `status-approved-bg` / `status-approved-fg` |

## Avalonia

`TemplatedControl` `StatusBadge` com `Status` (enum `LineStatus`). O `ControlTheme` troca `Background` e `Foreground` por `Style Selector="^[Status=Machine]"`. Altura 20, `CornerRadius` `radius-pill`, texto em `overline` sem caixa alta (11/600).

## Não fazer

- Usar a cor do status em outro significado (âmbar é aviso do QA, não IA).
- Abreviar o rótulo na grade; a coluna tem 96px para caber "Traduzida".
