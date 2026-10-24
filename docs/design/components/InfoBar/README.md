# InfoBar

Mensagem persistente no topo de uma área, com severidade, título curto em 600, detalhe e uma ação opcional à direita. Para resultados de tarefa (build, injeção) e estados do ambiente (Mica, jogo alterado).

Título termina em ponto e diz o fato; o detalhe diz a consequência ou o próximo passo.

## Avalonia

O Fluent do Avalonia não traz `InfoBar`; criar `TemplatedControl` `InfoBar` com `Severity`, `Title`, `Message`, `ActionContent`, `IsClosable`. Fundo pelos tokens `*-bg` da severidade, ícone pela cor `*-fg`, texto em `text-primary`.
