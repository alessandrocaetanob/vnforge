# StatusDistribution

Barra empilhada com a distribuição de status das linhas, sempre acompanhada da legenda com percentuais. A barra fina de baixo (`vnf-progress`) é o progresso simples de uma tarefa ou script.

Ordem fixa dos segmentos: Aprovadas, Revisadas, Traduzidas, IA; Pendentes é o trilho (`seg-pending`). Percentuais arredondados para inteiro e somando 100.

## Avalonia

`ItemsControl` com `StackPanel` horizontal de `Border` cujas larguras vêm de um conversor de proporção (ou um `Control` próprio que desenha em `Render`). A legenda é um `WrapPanel`. Para progresso simples use `ProgressBar` com `Height=4` e `CornerRadius=2`; cheio usa `seg-approved`.
