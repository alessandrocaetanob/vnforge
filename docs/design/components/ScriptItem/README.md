# ScriptItem

Item da lista de scripts do jogo: id, nome legível (vindo do `yst_list.ybn`), percentual concluído e barra de progresso.

- Atual: `accent-soft` e id em 600.
- Script com tarefa da IA em andamento: ícone Arrow Sync em `status-ai-fg` antes do percentual.
- 100%: barra em `seg-approved`.

"Concluído" = linhas Revisadas + Aprovadas sobre o total. O percentual de IA aparece só no tooltip.

## Avalonia

`ListBox` virtualizado com `ItemTemplate` de três linhas; o item atual é o `SelectedItem` sincronizado com o editor. Agrupamento opcional por capítulo com `ItemsControl` + `Expander`.
