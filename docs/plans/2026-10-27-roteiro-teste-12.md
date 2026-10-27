# Roteiro do reteste · teste.12

Este roteiro confere a correção da issue #417 no projeto de teste da teste.11: o ajuste Reticências = "Caractere …" gerava 4.114 erros `caractere-nao-codificavel` na estratégia `cp1252`, e o `♪` da `yst00010` não tinha troca. Os PRs #418 (Core), #419 (CLI) e #420 (App) entram na `v0.0.0-teste.12`.

Não precisa do jogo nem da chave da Anthropic: o reteste é só na Conferência, em Ajustes e na CLI.

## Antes de começar

Use a release [`v0.0.0-teste.12`](https://github.com/alessandrocaetanob/vnforge/releases/tag/v0.0.0-teste.12). Baixe os dois zips para `H:\Projetos\vnforge-builds\v0.0.0-teste.12\`, extraia o app em `app\` e a CLI em `cli\` e confira os dois contra o `SHA256SUMS.txt`. A barra de status do app mostra "VNForge 0.0.0-teste.12".

O projeto é `H:\vnforge\projeto`. Se você já trocou Reticências para "Três pontos" como contorno, volte para "Caractere …" em Ajustes antes do passo R1, para o reteste partir do erro original. Faça uma cópia do `formatting.json` do projeto antes de começar.

Evidências em `H:\vnforge\evidencias-teste12\`, com um `registro.md` (data, versão, resultado, o que fiz, o que vi). Texto do jogo não entra nas evidências: cite a linha pelo id (`yst00010:000012`).

## R1 · CLI antes da correção

```powershell
$vnf = 'H:\Projetos\vnforge-builds\v0.0.0-teste.12\cli\vnf.exe'
& $vnf check --project H:\vnforge\projeto
```

Esperado: uma linha só para o ajuste, no lugar das 4.114: "Reticências (Caractere …): 1.748 linhas com erro" (a regra nova conta um erro por linha, não por reticência), dizendo que a `cp1252` não grava o `…` e que `vnf check --fix` troca para Três pontos. O `♪` aparece como erro de caractere com a sugestão `'~'`. Os dois `falante-nao-codificavel` (nomes de personagem) continuam: o corretor não mexe em nome. Captura da saída: `R1-check.png`.

## R2 · Conferência

1. Abra o projeto no app e vá em Conferir. Esperado: um card no topo dos erros, "O ajuste Reticências gera 1.748 erros", com o texto "A estratégia cp1252 não grava as reticências (…). As traduções têm três pontos; o ajuste é que troca." e o botão "Usar três pontos". A lista não tem mais uma linha por reticência. Captura: `R2.1-card.png`.
2. O cabeçalho "Corrigir N com um clique" e o filtro "Um clique" não mostram 0: contam as linhas que o ajuste resolve mais o `♪`. Anote os dois números.
3. Abra o erro do `♪`. A dica diz qual caractere usar por extenso e não cola o ponto final no caractere citado. Captura: `R2.3-dica.png`.
4. Clique em "Usar três pontos". Esperado: o aviso "O ajuste Reticências passou de Caractere … para Três pontos.", a Conferência confere de novo e os 1.748 erros somem. Sobra o `♪` e os dois nomes.
5. Clique em "Corrigir N com um clique". Esperado: o `♪` vira `~` e o aviso diz o que mudou. Sobram só os dois `falante-nao-codificavel`. Captura: `R2.5-depois.png`.

## R3 · Ajustes

1. Em Ajustes › Formatação, com a estratégia `cp1252`, a opção "Caractere …" de Reticências e a de aspas `「 」` ficam desligadas. Passe o mouse: a dica diz que a estratégia não grava o caractere. Captura: `R3.1-ajustes.png`.
2. Feche o app, restaure a cópia do `formatting.json` (Reticências = "Caractere …"), abra o app de novo e vá em Ajustes › Formatação. Esperado: "Caractere …" aparece marcada e legível, e uma faixa abaixo diz "A estratégia cp1252 não grava as reticências (…)." com o botão "Usar três pontos". Captura: `R3.2-faixa.png`.
3. Troque a estratégia para `sjis` (sem salvar outras mudanças). As duas opções voltam a ficar ligadas e a faixa some. Volte para `cp1252`.

## R4 · CLI depois da correção

Feche o app. O `formatting.json` está com Reticências = "Caractere …" desde o R3.2; rode:

```powershell
& $vnf check --project H:\vnforge\projeto --fix
& $vnf check --project H:\vnforge\projeto --json
```

Esperado: o `--fix` escreve "formatting.json: Reticências passou de Caractere … para Três pontos." O segundo comando não tem nenhum `caractere-nao-codificavel` nem `ajuste-nao-codificavel` em `data.issues[].rule`. Salve o JSON em `R4-check.json`.

## Resultado

O reteste passa quando, depois de um clique, a Conferência do projeto não tem erro de codificação além dos dois nomes de personagem. Cada desvio vira issue com o label "área: app" ou "área: cli", o passo, a captura e o log do dia (`.cache\logs` do projeto).
