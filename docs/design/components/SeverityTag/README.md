# SeverityTag

Resultado do QA para uma linha, um arquivo ou o projeto. Ícone + contagem + palavra.

- **Erro** (`signal-error-*`, ícone Error Circle): bloqueia o build.
- **Aviso** (`signal-warning-*`, ícone Warning): não bloqueia; pode ser marcado como ok.
- **Sem problemas** (`status-approved-*`, ícone Checkmark Circle).

Contagens no formato pt-BR (`1.204 avisos`). Plural correto ("1 aviso", "2 avisos").

## Avalonia

`Border Classes="sev error|warning|ok"` com `PathIcon` 16 e `TextBlock` 12/600. Em listas do QA, o mesmo par de cores vai no ícone da linha da grade (coluna de 24px).
