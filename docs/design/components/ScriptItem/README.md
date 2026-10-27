# ScriptItem

Item da lista de cenas do jogo: nome da cena como rótulo principal (o do `scripts.json` ou "Cena N" quando não há nome), id do script pequeno embaixo, percentual concluído e barra de progresso.

- Nome em 13px, quebra em até duas linhas; o id em `mono` 11px e `text-secondary`, nunca no lugar do nome.
- Atual: `accent-soft` e nome em 600.
- Script com tarefa da IA em andamento: ícone Arrow Sync em `status-ai-fg` antes do percentual.
- 100%: barra em `seg-approved`.

"Concluído" = linhas Revisadas + Aprovadas sobre o total. O percentual de IA aparece só no tooltip.

## Avalonia

`ListBox` virtualizado com `ItemTemplate` de três linhas (nome com percentual, id, barra); o item atual é o `SelectedItem` sincronizado com o editor. Agrupamento opcional por capítulo com `ItemsControl` + `Expander`.
