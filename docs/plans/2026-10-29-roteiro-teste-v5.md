# Roteiro do teste de tradução · interface v5

Este roteiro traduz e joga mais um capítulo do jogo de referência com a interface v5 ([plano](2026-10-28-interface-v5.md), #460 a #483) e as pendências que vieram depois dela (#509, #511, #512, #514). A v5 muda o Core em três pontos que o teste precisa exercitar no jogo: o tratamento entre personagens vai no pedido de tradução, a IA devolve dúvidas por linha, e cada lote guarda um ponto de restauração.

Três comportamentos nunca foram vistos no app real, só em teste automatizado. Eles têm passo próprio e captura obrigatória:

- "Usar em todos" num projeto grande, com a fala repetida em centenas de linhas (V5.4);
- o menu do corretor aberto com o ponteiro, no campo da tradução (V5.6);
- o aviso de regra aprendida com as correções, que precisa ficar tempo bastante para ser lido (V5.8).

Precisa do jogo instalado, da chave da Anthropic e de alguém jogando.

## Antes de começar

### Versão

Use a release `v0.0.0-teste.18`, gerada da `main` depois da #510. Baixe os dois zips para `H:\Projetos\vnforge-builds\v0.0.0-teste.18\`, extraia o app em `app\` e a CLI em `cli\`, e confira os dois contra o `SHA256SUMS.txt` da release. A versão aparece na barra de status do app e em `vnf --version`.

### Projeto e pastas

Reaproveite o `projeto-app` e o `jogo-app` de `H:\VNForge-capitulo`, do [roteiro da v4](2026-10-27-roteiro-teste-v4.md). Antes, em Testar no jogo, use "Voltar ao jogo original" e confira com o `Compare-Pasta` do [roteiro da v3](2026-10-26-roteiro-capitulo-v3.md) que `jogo-app` voltou igual a `jogo-original`. Se a raiz não existe mais, rode as etapas A0, A1 e A2 do roteiro da v3 com a teste.18.

Crie as pastas de evidências:

```powershell
$raiz = 'H:\VNForge-capitulo'
'V0-versao','V1-exemplo','V2-inicio','V3-glossario','V4-traduzir','V5-editor','V6-revisao','V7-ler','V8-lote','V9-conferir','V10-jogo','V11-impressoes' |
  ForEach-Object { New-Item -ItemType Directory -Force (Join-Path $raiz "evidencias-v5\$_") | Out-Null }
```

Cada subpasta tem um `registro.md` (data, versão, resultado, issues, o que fiz, o que vi, problemas). Captura em `.png` com o número do passo. Texto do jogo não entra nas evidências: cite a linha pelo id (`yst00012:000034`).

### Quando algo falha

Abra uma issue com o passo, o que fez, o que viu, a captura e o log do dia (`.cache\logs` do projeto), sem texto do jogo, com o label "área: app". Se o passo disser "pare aqui", não siga.

## V0 · Versão e tamanho do texto

1. Abra o `VNForge.App.exe` da teste.18. A barra de status mostra "VNForge 0.0.0-teste.18". Captura: `V0.1-versao.png`.
2. Em Ajustes › Aparência, mude o Tamanho do texto e a fonte do japonês. No Editor, `Ctrl` `+` e `Ctrl` `−` mudam o texto da grade, do inspetor e do Modo revisão, e o tamanho volta igual depois de fechar e abrir o app (#480). Nada pode ficar cortado no maior tamanho. Captura: `V0.2-texto-grande.png`.

## V1 · Cena de exemplo

1. Com nenhum projeto aberto, use "Experimentar com uma cena de exemplo" no Início (#481). Esperado: um projeto de 30 falas abre sem pedir jogo nem chave.
2. Traduza, revise três falas e rode Conferir. A IA falsa responde na hora e a barra de tarefas mostra gasto zero. Anote se em algum momento o app pediu a chave. Captura: `V1.2-exemplo.png`.
3. Feche o exemplo e abra o `projeto-app`.

## V2 · Início

1. O Diário de tradução ocupa a metade que estava vazia (#477): o mapa das últimas 12 semanas, os dias seguidos e o que foi fechado ontem. Confira os números contra o que você lembra dos testes anteriores. Captura: `V2.1-diario.png`.
2. Na caixa do jogo de "Continuar de onde parou", a placa do nome mostra o nome traduzido do Glossário, não o japonês (#464). Captura: `V2.2-placa.png`.

## V3 · Glossário e personagens

1. Se houver personagens sem gênero, a faixa do topo oferece "Pedir à IA" com o preço (#466). Peça, confira as sugestões esmaecidas linha a linha e use "Aceitar as N" ou o visto de cada uma. Captura: `V3.1-genero.png`.
2. Troque a cor de dois personagens (#467). A cor aparece no nome da grade do Editor, na placa da caixa do jogo e no Modo revisão, nos dois temas. Narração fica sem cor. Captura: `V3.2-cor.png`.
3. Abra a ficha de um personagem principal pelo ícone de pessoa (#469): gênero, como fala, cor, as cinco falas mais longas e quantas faltam revisar. Captura: `V3.3-ficha.png`.
4. Em "Quem chama quem" (#470), peça o preenchimento à IA. Ajuste dois tratamentos que vieram errados e anote no registro os pares e o que mudou. Captura: `V3.4-tratamentos.png`.
5. Renomeie a tradução de um personagem que aparece em algum tratamento (#514). Esperado: os tratamentos que o citam mudam junto. Tente também um nome que já existe no glossário: o app recusa e diz por quê. Anote o que aconteceu.

## V4 · Traduzir com IA

1. Escolha um capítulo curto que os roteiros anteriores não traduziram e anote o nome e as cenas.
2. No Editor, use **Traduzir com IA** › "O capítulo". O pedido mostra o que vai junto, com o tratamento entre personagens. Anote o custo previsto. Captura: `V4.2-pedido.png`.
3. Traduza. Anote custo final e tempo. Se falhar, pare aqui.
4. Algumas linhas ganham o selo "Dúvida" (#471). Use o filtro de dúvidas na barra do Editor, abra três e leia o cartão "Dúvida da IA" no inspetor. Anote para cada uma o tipo, o id e se a dúvida fazia sentido. Captura: `V4.4-duvida.png`.

## V5 · Editor

1. Troque a densidade da grade para "Roteiro" (#460): o nome de quem fala acima do par, original em cima e tradução embaixo. Feche e abra o app: a escolha continua. Captura: `V5.1-roteiro.png`.
2. A lista de cenas é compacta (#461): nome e barra fina, o id só na dica, um visto na cena pronta. Captura: `V5.2-cenas.png`.
3. Abra uma linha com "Mesmo original" (#463). O cartão mostra os marcadores do jogo formatados, nunca o código cru, e a diferença entre as variantes continua marcada. Captura: `V5.3-mesmo-original.png`.
4. **Usar em todos com projeto grande** (#462, #509). Ache uma fala curta que se repete em muitas linhas (o inspetor diz "Esta fala aparece em mais N linhas"; procure N acima de 200). Use "Usar em todos" e, enquanto o lote roda, continue editando outra linha. Esperado: o botão aparece inteiro acima da barra fixa, o app não trava, a barra de tarefas mostra o andamento, e a linha que você editou durante o lote fica com a sua edição. Anote o id, o N e o tempo. Captura: `V5.4-usar-em-todos.png`.
5. Use os chips de reescrita no inspetor (#472): "Mais curta", "Mais natural" e "como o personagem falaria". Vêm duas opções, cada uma medida contra a caixa do jogo. Com uma tradução longa, a barra de reescrita acompanha o tamanho do texto sem cortar (#512). Captura: `V5.5-reescrita.png`.
6. **Corretor com o ponteiro** (#482, #511). Escreva uma palavra errada numa tradução. Ela ganha o sublinhado. Clique com o botão direito sobre ela: o menu abre na palavra, com sugestões e "Ignorar esta palavra no projeto". Ignore uma palavra que é gíria do personagem e confira que o sublinhado some nela em outras linhas. Nomes do Glossário nunca são sublinhados. Captura: `V5.6-corretor.png`.
7. "Sugerir (Ctrl+Espaço)" no campo da tradução oferece termos do Glossário.
8. **Aviso de regra aprendida** (#473, #474). Corrija à mão a mesma troca em várias falas (por exemplo, sempre "você" para "tu" nas falas de um personagem). Passado o limite, o app oferece a regra para o `style.md` ou para o "Como fala" do personagem. Anote quantas correções foram precisas, quanto tempo o aviso ficou na tela e se deu para ler e decidir antes de ele sumir. Quando o mesmo trecho japonês ganha a mesma tradução em várias falas, o Glossário oferece o par com "Adicionar ao glossário". Captura: `V5.8-aprendeu.png`.

## V6 · Modo revisão

1. Pela ficha do personagem, use "Revisar este personagem" (#468). O Modo revisão percorre as falas dele em todas as cenas, na ordem da história. Revise dez. Captura: `V6.1-por-personagem.png`.
2. Abra o Modo revisão começando pelas dúvidas (#471). Aprovar a linha encerra a dúvida, e o selo some da grade.
3. Os chips de reescrita também aparecem aqui (#472).

## V7 · Ler como jogador

1. Abra uma cena traduzida com "Ler como jogador" (#475). Tela cheia, uma fala por vez, Espaço avança e a seta volta. A placa e a cor seguem o Glossário. Captura: `V7.1-ler.png`.
2. Marque com `F` três falas que soam estranhas. Ao sair, o Editor abre filtrado nessas falas. Anote os ids.

## V8 · Lote e Desfazer

1. Na paleta de comandos, escreva um pedido em português, como "nas falas do Kenta, trocar você por tu" (#479). A prévia mostra antes e depois por linha e nada é gravado antes de Aplicar. Aplique. Captura: `V8.1-pedido.png`.
2. Edite à mão uma das linhas que o lote mudou.
3. Na lista de tarefas, use "Desfazer" no lote (#478). Esperado: as linhas voltam, a que você editou depois fica como está, e o aviso diz quantas ficaram. Desfaça também o "Usar em todos" de V5.4. Captura: `V8.3-desfazer.png`.

## V9 · Conferir

1. Rode Conferir. A regra "Tratamento fora do combinado" (#470) aponta falas que fogem dos tratamentos de V3.4, e o corretor aparece como aviso. Abra duas pelo "Abrir no editor" e anote se o aviso estava certo. Captura: `V9.1-conferir.png`.
2. Corrija os erros que restarem.

## V10 · Testar no jogo

1. Em Testar no jogo, as opções ficam numa lista só, no corpo da página (#465). Captura: `V10.1-opcoes.png`.
2. A seção "O que mudou desde o último teste" (#476) lista as cenas mudadas desde o teste da v4, com quantas falas, na ordem da história. "Abrir" leva ao Editor filtrado. Confira o mesmo no Painel. Captura: `V10.2-mudou.png`.
3. Rode o teste do capítulo e jogue do começo ao fim. Anote acentos na fala, no nome e nas escolhas; falas que passam da caixa; falas em japonês; se os tratamentos de V3.4 aparecem como combinado; se o nome na caixa do jogo é o mesmo da placa do app.
4. Rode o teste de novo: a lista de V10.2 fica vazia.
5. Use "Voltar ao jogo original" e confira com o `Compare-Pasta` que `jogo-app` voltou igual a `jogo-original`.

## V11 · Impressões

Escreva no `registro.md` de `V11-impressoes`, em poucas linhas:

- se as dúvidas da IA ajudaram ou viraram ruído;
- se a cor e a ficha mudaram como você revisa um personagem;
- o que você procurou e não achou;
- qualquer texto do app que soou estranho em português.

Cada item vira uma issue "área: app" se for defeito, ou uma nota para a v6 se for ideia.
