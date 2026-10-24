# TextboxPreview

Simulação da caixa de texto do jogo com o perfil de formatação aplicado: placa do falante, quebra por palavra na largura em colunas e limite de linhas.

- Cores fixas `textbox-*` nos dois temas: representa o jogo.
- Linhas além do limite em `textbox-overflow`, com a mensagem abaixo em `signal-warning-fg`.
- A largura em colunas e o máximo de linhas vêm de `formatting.json`; a fonte é a MS Gothic do sistema e, sem ela, a BIZ UDGothic embutida (`TextboxPreview.GameFont`); a largura continua medida em colunas.

## Avalonia

`Control` próprio (`TextboxPreview`) com a propriedade `Layout` (placa, colunas, limite e linhas já quebradas) e `FontFamily`, que desenha em `Render` com `FormattedText`. Quem quebra é o `LinePreviewer` do `VNForge.Core`, o mesmo serviço do build e do `vnf preview`, chamado pelo `TextboxPreviewViewModel`; nada de reimplementar a quebra na interface. A fonte padrão é `TextboxPreview.GameFont` (MS Gothic do sistema e, sem ela, BIZ UDGothic embutida, decisão D6, #105); `FontFamily` continua sendo o ponto de troca.
