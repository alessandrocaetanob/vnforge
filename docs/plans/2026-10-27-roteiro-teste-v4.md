# Roteiro do teste de tradução · interface v4

Este roteiro traduz e joga um capítulo novo do jogo de referência com a interface v4 (#402 a #407 e #415). A v4 não muda o Core, a extração, o build nem a injeção; ela muda como cada tela mostra o trabalho. Por isso o roteiro reaproveita o projeto do [roteiro da v3](2026-10-26-roteiro-capitulo-v3.md) quando ele existe e olha cada tela com a pergunta de quem traduz: dá para decidir sem adivinhar?

| Issue | O que o roteiro mostra |
|---|---|
| #402 | Botão principal legível nos dois temas, títulos sem corte, selo do trilho só com erros |
| #403 | Editor com a cena pelo nome, o texto com mais largura e a barra fixa com Aprovar e próxima |
| #404 | Modo revisão como roteiro e palco, com a fala anterior e a seguinte |
| #405 | Início com "Continuar de onde parou" e Painel sem reticências |
| #406 | Conferir e Traduzir com IA |
| #407 | Testar no jogo, Ajustes e Glossário |
| #415 | Números com separador de milhar, mensagens da Conferência em linguagem de tradutor |

Precisa do jogo instalado, da chave da Anthropic e de alguém jogando.

## Antes de começar

### Versão

Use a release [`v0.0.0-teste.11`](https://github.com/alessandrocaetanob/vnforge/releases/tag/v0.0.0-teste.11), gerada da `main` com a interface v4 completa. Baixe os dois zips para `H:\Projetos\vnforge-builds\v0.0.0-teste.11\` e extraia o app em `app\` e a CLI em `cli\`. Confira os dois contra o `SHA256SUMS.txt` da release. A versão aparece na barra de status do app e em `vnf --version`.

### Projeto e pastas

- Se a raiz do roteiro da v3 (`H:\VNForge-capitulo`) ainda existe, use o `projeto-app` e o `jogo-app` dela. Antes, em Testar no jogo, use "Voltar ao jogo original" e confira com o `Compare-Pasta` do roteiro da v3 que `jogo-app` voltou igual a `jogo-original`.
- Se ela não existe, rode as etapas A0, A1 e A2 do roteiro da v3 com a teste.11 e volte para cá.

Crie as pastas de evidências:

```powershell
$raiz = 'H:\VNForge-capitulo'
'V0-versao','V1-inicio-painel','V2-traduzir','V3-editor','V4-revisao','V5-conferir','V6-jogo','V7-impressoes' |
  ForEach-Object { New-Item -ItemType Directory -Force (Join-Path $raiz "evidencias-v4\$_") | Out-Null }
```

Cada subpasta tem um `registro.md` (data, versão, resultado, issues, o que fiz, o que vi, problemas). Captura de tela em `.png` com o número do passo. Texto do jogo não entra nas evidências: cite a linha pelo id (`yst00012:000034`).

### Quando algo falha

Abra uma issue com o passo, o que fez, o que viu, a captura e o log do dia (`.cache\logs` do projeto), sem texto do jogo, com o label "área: app". Se o passo disser "pare aqui", não siga.

## V0 · Versão e tema

1. Abra o `VNForge.App.exe` da teste.11. A barra de status, no pé da janela, mostra "VNForge 0.0.0-teste.11" (a versão sem o sufixo do commit). Captura: `V0.1-inicio.png`.
2. Troque o tema em Ajustes › Aparência › Tema (Claro e Escuro; "Sistema" segue o Windows). Esperado nos dois: o texto do botão principal ("Continuar", "Traduzir", "Aprovar e próxima") lê sem esforço; os títulos grandes mostram inteiras as letras que descem ("projeto", "Ajustes", "jogo"). Capturas: `V0.2-claro.png`, `V0.2-escuro.png`.

## V1 · Início e Painel

1. Início: o cartão "Continuar de onde parou" mostra o anel, "Capítulo N · nome do capítulo" (o número antes do nome, como no Editor), as horas de revisão e a próxima fala na caixa do jogo. Captura: `V1.1-continuar.png`.
2. Painel: nenhuma frase do stepper termina em reticências; a nota de cada capítulo quebra em duas linhas quando é longa. Números grandes têm ponto de milhar ("10.514 linhas"). Captura: `V1.2-painel.png`.
3. Trilho: o selo vermelho de Conferir conta só erros. Com só avisos, ele vira um ponto neutro, e a dica diz que nenhum é erro. Anote o que o trilho mostra. Captura do trilho: `V1.3-trilho.png`.
4. Escolha um capítulo curto que o roteiro da v3 não traduziu e anote no registro o nome e as cenas dele.

## V2 · Traduzir com IA

1. No Editor, abra uma cena do capítulo escolhido e use **Traduzir com IA** › "O capítulo". Esperado: o número de linhas e de cenas com ponto de milhar, as três qualidades com o gasto em reais e o tempo, o aviso de personagens sem gênero com "Definir agora" e a frase do que vai junto no pedido (glossário, voz, falas em volta). Captura: `V2.1-pedido.png`.
2. Traduza. A barra de tarefas mostra o andamento e o custo da IA. Anote o custo final e o tempo. Se falhar, pare aqui.

## V3 · Editor

1. O título do cabeçalho é o nome da cena, ou "Cena N" sem nome; o id do script (`yst00013`) só aparece pequeno, na lista de cenas e no inspetor. A sobrancelha diz "Capítulo 1 · cena 13 de 45". Captura: `V3.1-cabecalho.png`.
2. A grade dá a maior parte da largura para Original e Tradução. Anote se dá para entender a fala pela grade sem abrir o inspetor.
3. Corrija à mão cinco falas do capítulo. Com uma tradução longa, o aviso "Não cabe na caixa do jogo" aparece e **Aprovar e próxima** continua à vista no rodapé do inspetor, sem rolar. Captura: `V3.3-nao-cabe.png`.
4. Numa fala que se repete pelo jogo, o inspetor diz "Esta fala aparece em mais N linhas", com ponto de milhar quando N passa de 999, e "Usar em todos" funciona. Anote o id e o N.
5. O rodapé da grade diz "N linhas nesta cena".

## V4 · Modo revisão

1. Abra **Modo revisão** na primeira cena do capítulo. A esquerda é o roteiro (original e tradução); a direita é o palco, com a fala na caixa do jogo. Na primeira linha não há "Fala anterior". Captura: `V4.1-primeira.png`.
2. Revise a cena inteira com o teclado: `Enter` aprova, `E` corrige, `M` deixa para depois, as setas andam. A partir da segunda linha aparece "Fala anterior" acima do original. Anote se a fala anterior ajudou a decidir alguma tradução e cite o id.
3. Teste numa janela de 1366×768 (ou com a janela reduzida a esse tamanho). Com uma tradução longa, a "Fala seguinte" pode sumir para o palco e a barra de botões caberem; ela volta quando a linha seguinte é curta. Nada pode ficar cortado na borda. Captura: `V4.3-1366.png`.
4. No fim da cena, a tela final oferece a próxima cena. Captura: `V4.4-fim.png`.

## V5 · Conferir

1. Abra Conferir. A faixa do topo diz quantos problemas travam o teste no jogo e que os avisos não travam nada. Os filtros têm números com ponto de milhar. Captura: `V5.1-conferir.png`.
2. Leia as mensagens como tradutor. Esperado: cada mensagem começa com maiúscula, cita o texto entre aspas tipográficas (“veterano”) e fala em cena, não em script ("2 linhas sem tradução nesta cena"). Anote no registro qualquer mensagem que você teve que ler duas vezes para entender.
3. Use "Corrigir N com um clique" e "Conferir de novo". Corrija os erros que restarem pelo "Abrir no editor".

## V6 · Testar no jogo

1. Em Testar no jogo, rode o teste do capítulo. Os passos aparecem com tempo; um passo que falha fica vermelho e diz por quê. Captura: `V6.1-passos.png`.
2. Jogue o capítulo do começo ao fim. Anote no registro: acentos (ç, ã, é, ô) na fala, no nome e nas escolhas; falas que passam da caixa; falas que ficaram em japonês. Capturas do jogo vão só para a pasta de evidências, nunca para o repositório.
3. Use "Voltar ao jogo original" e confira com o `Compare-Pasta` que `jogo-app` voltou igual a `jogo-original`.

## V7 · Impressões

Escreva no `registro.md` de `V7-impressoes`, em poucas linhas:

- a tela em que você passou mais tempo e se ela ajudou ou atrapalhou;
- o que você procurou e não achou;
- qualquer texto do app que soou estranho em português.

Cada item vira uma issue "área: app" se for defeito, ou uma nota para a v5 se for ideia.
