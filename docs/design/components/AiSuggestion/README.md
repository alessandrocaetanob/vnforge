# AiSuggestion

Sugestão da IA para a linha atual, mostrada como diferença em relação à tradução existente. A sugestão nunca substitui o texto sozinha.

- Cabeçalho: ícone Sparkle, modelo em `status-ai-fg`, esforço e quando foi gerada.
- Diferença por palavra: inserções em `status-approved-*`, remoções riscadas em `text-secondary`. Sem tradução existente, mostra o texto inteiro sem marcação.
- "Usar sugestão" copia para o editor e muda o status para Traduzida (ainda exige aprovação).

## Avalonia

`Border` com `SelectableTextBlock` cujos `Inlines` são montados no ViewModel a partir de um diff de palavras (`Run` com classes `ins` e `del`). Enquanto a chamada roda, o corpo mostra `ProgressBar IsIndeterminate` de 4px e o botão vira "Cancelar".
