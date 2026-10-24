# StatusBar

Rodapé da janela com a tarefa em segundo plano mais recente (progresso, contagem e custo acumulado), o atalho para a lista de tarefas e o estado do projeto: último salvamento, estratégia de acentuação, perfil da caixa.

Uma tarefa por vez no rodapé; as demais ficam no flyout "Tarefas (n)", cada uma com Pausar e Cancelar.

## Avalonia

`Border` na linha 2 do layout raiz (`statusbar-height`). As tarefas são `IBackgroundJob` publicados pelo Core via `IProgress<JobProgress>`; o ViewModel agrega e expõe a mais recente. Valores monetários com `CultureInfo("pt-BR")`.
