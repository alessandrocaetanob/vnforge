# ScriptItem

Item da lista de cenas do jogo, compacto: 36 px com o nome da cena (o do `scripts.json` ou "Cena N" quando não há nome), o percentual concluído à direita e uma barra fina de 3 px embaixo.

- Nome em 13px, numa linha só; passando do espaço, vira reticências.
- O id do script não aparece no item: vai na dica do mouse, em duas linhas: o nome por inteiro e "yst00013 · IA 20%" (id e percentual de IA). O filtro da lista continua casando o id.
- Atual: `accent-soft` e nome em 600.
- Script com tarefa da IA em andamento: ícone Arrow Sync em `status-ai-fg` antes do percentual.
- 100%: um visto em `status-approved-fg` toma o lugar do texto "100%" e a barra fica em `seg-approved`. O nome de acessibilidade do item continua dizendo o percentual.

"Concluído" = linhas Revisadas + Aprovadas sobre o total.

## Avalonia

`ListBox` virtualizado com `ItemTemplate` de duas linhas (nome com percentual, barra); o item atual é o `SelectedItem` sincronizado com o editor. Agrupamento opcional por capítulo com `ItemsControl` + `Expander`.
