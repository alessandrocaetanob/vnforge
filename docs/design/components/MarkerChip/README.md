# MarkerChip

Marcador de controle do jogo dentro do texto: `\n` quebra de linha, `\p` nova página, `\c` esperar clique, `{VAR:nome}` variável. É um bloco atômico: o cursor pula o chip inteiro e Backspace remove o chip todo, nunca metade.

- Na v3 o selo é uma pílula com o nome em português ("virar a página", "quebra de linha", "nome do jogador"), nunca o `\p` cru. O triângulo abre o selo de virar a página, `↵` a quebra de linha e `✱` a variável.
- `missing`: marcador presente no original e ausente na tradução, em `signal-error-*`, mostrado abaixo do campo.

## Avalonia

No editor de tradução (AvaloniaEdit), um `VisualLineElementGenerator` reconhece `\\[npcu]` e `\{VAR:[^}]+\}` e devolve um `InlineObjectElement` com um `MarkerChip`; nos textos só de leitura o mesmo `MarkerChip` entra num `InlineUIContainer`. A mesma regex alimenta a regra do QA "marcadores diferentes do original". Na grade (somente leitura), os marcadores aparecem como texto `mono-xs` em `accent-text`, sem caixa, para não pesar a linha.
