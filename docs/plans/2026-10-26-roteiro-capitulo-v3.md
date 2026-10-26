# Roteiro do teste de um capítulo · interface v3 e CLI

Este roteiro testa um capítulo do jogo de referência numa cópia limpa, do zero: primeiro pela interface v3 (parte A), depois pela CLI (parte B), cada uma com o seu projeto e a sua cópia do jogo. Ele substitui as etapas 5 e 6 do [roteiro de testes no jogo](2026-10-24-plano-de-testes-no-jogo.md), escritas antes da v3, quando gerar, injetar e restaurar ficavam na tela Build. As etapas 0 a 4 daquele roteiro já rodaram (resultados no [doc 06](../06-yuris-notas-tecnicas.md#testes-da-v2-no-jogo-de-referência)) e não se repetem aqui; a etapa 7 (jogo inteiro) continua lá.

O que este roteiro fecha ou alimenta:

| Issue | O que o roteiro mostra |
|---|---|
| #67 (M2) | Uma cena traduzida à mão, com acentos em fala, nome e escolha, e o jogo voltando ao original |
| #85 (M3) | Um capítulo traduzido pela IA, revisado e jogado |
| #65 | O patch da fonte no exe do jogo, que até agora só rodou em executáveis sintéticos |
| #342 | Quantas vezes o patch precisa ser aplicado à mão durante um teste real |

Precisa do jogo instalado, da chave da Anthropic e de alguém jogando, então nenhum agente fecha essas issues sozinho.

## Antes de começar

### Versão

Use a release [`v0.0.0-teste.7`](https://github.com/alessandrocaetanob/vnforge/releases/tag/v0.0.0-teste.7), gerada da `main` em `a4aa0c5` (interface v3 completa). Baixe os dois zips para `H:\Projetos\vnforge-builds\v0.0.0-teste.7\` e extraia o app em `app\` e a CLI em `cli\`:

| Pacote | Conteúdo |
|---|---|
| `vnforge-app-0.0.0-teste.7-win-x64.zip` | `VNForge.App.exe` |
| `vnf-0.0.0-teste.7-win-x64.zip` | `vnf.exe` |

Confira os dois arquivos contra o `SHA256SUMS.txt` da release. No app, a versão aparece na barra de status do Início; na CLI, `vnf --version` mostra `vnf 0.0.0-teste.7`.

### Pastas

Use uma raiz nova, fora de qualquer repositório e separada da raiz do roteiro antigo. O roteiro chama essa raiz de `$raiz`.

| Pasta | Para quê |
|---|---|
| `$raiz\jogo-original` | Cópia intacta do jogo. Nunca é aberta pelo app nem pela CLI. Serve de referência nas comparações. |
| `$raiz\jogo-app` | Cópia de trabalho da parte A. Só o projeto do app aponta para ela. |
| `$raiz\jogo-cli` | Cópia de trabalho da parte B. Só o projeto da CLI aponta para ela. |
| `$raiz\projeto-app` | Projeto criado pelo Novo projeto do app. |
| `$raiz\projeto-cli` | Projeto criado pelo `vnf init`. |
| `$raiz\tmp` | Arquivos de passagem (CSV do export). Pode ser apagada no fim. |
| `$raiz\evidencias` | Uma subpasta por etapa. |

Cada projeto fica fora da pasta do jogo: o build, o teste e o patch recusam um projeto com o `.cache` dentro do jogo. Duas cópias do jogo evitam que os backups e o estado do jogo de um projeto se misturem com os do outro. Não mova nem renomeie as pastas dos projetos durante o teste: a #327 ainda está aberta e uma pasta movida recria o `.cache`.

### Sessão do PowerShell

Abra um PowerShell 7 e rode este bloco no começo de cada sessão. A parte A só usa o `Compare-Pasta` e o `Diff-Exe`; a parte B usa todos.

```powershell
$raiz = 'H:\VNForge-capitulo'
$vnf  = 'H:\Projetos\vnforge-builds\v0.0.0-teste.7\cli\vnf.exe'
$proj = Join-Path $raiz 'projeto-cli'
$exe  = 'jogo.exe'   # troque pelo nome real do executável do jogo
$env:NO_COLOR = '1'

# Roda o vnf no projeto da CLI e grava a saída na pasta de evidências da etapa.
function Vnf-Ev([string]$etapa, [string]$arquivo) {
  $dest = Join-Path $raiz "evidencias\$etapa\$arquivo"
  $p = if (Test-Path $proj) { @('--project', $proj) } else { @() }
  & $vnf @args @p 2>&1 | Tee-Object -FilePath $dest
  "saída: $LASTEXITCODE" | Tee-Object -FilePath $dest -Append
}

# Compara duas pastas por SHA-256 e lista só o que difere.
function Compare-Pasta([string]$a, [string]$b) {
  $ha = Get-ChildItem $a -Recurse -File | ForEach-Object { [pscustomobject]@{ Rel = $_.FullName.Substring($a.Length); Hash = (Get-FileHash $_.FullName).Hash } }
  $hb = Get-ChildItem $b -Recurse -File | ForEach-Object { [pscustomobject]@{ Rel = $_.FullName.Substring($b.Length); Hash = (Get-FileHash $_.FullName).Hash } }
  Compare-Object $ha $hb -Property Rel, Hash | Sort-Object Rel
}

# Lista os bytes do executável que diferem do original (fc /b imprime um por linha).
function Diff-Exe([string]$copia, [string]$saida) {
  fc.exe /b (Join-Path $raiz "jogo-original\$exe") (Join-Path $raiz "$copia\$exe") | Tee-Object -FilePath $saida
}
```

O jogo abre sem o Locale Emulator em todo o roteiro, como o jogador vai abrir a tradução. Falas ainda em japonês aparecem com caracteres trocados, e isso é esperado.

### Pasta de evidências

```powershell
'A0-preparacao','A1-novo-projeto','A2-painel','A3-acentos-e-fonte','A4-cena','A5-capitulo','A6-volta','A7-interface',
'B1-projeto','B2-acentos-e-fonte','B3-mesmo-build','B4-ia','B5-teste-e-volta' |
  ForEach-Object { New-Item -ItemType Directory -Force (Join-Path $raiz "evidencias\$_") | Out-Null }
```

Cada subpasta tem um `registro.md` com o modelo do roteiro antigo (data, versão, resultado, issues, o que fiz, o que vi, problemas). Saída de comando vai em `.txt`, captura de tela em `.png` com o número do passo (`A4.5-dialogo.png`). Texto do jogo, arquivos do jogo e pastas do projeto não entram nas evidências: cite a linha pelo id (`yst00012:000034`), nunca pelo texto. Quando terminar uma etapa, me diga o caminho do `registro.md`.

### Quando algo falha

Abra uma issue com o passo, o que você fez, o que viu, a captura de tela e o log do dia (`.cache\logs` do projeto), sem texto do jogo, e anote o número no `registro.md`. Problema de tela (texto cortado, botão que não faz o que diz, layout quebrado) leva o label "área: app"; da CLI, "área: cli". Se o passo disser "pare aqui", não siga: o próximo depende dele.

## Parte A · Interface

### A0 · Preparação

1. Copie o jogo instalado para as três pastas e confira que saíram iguais:

   ```powershell
   robocopy '<pasta do jogo instalado>' "$raiz\jogo-original" /E /NFL /NDL
   robocopy "$raiz\jogo-original" "$raiz\jogo-app" /E /NFL /NDL
   robocopy "$raiz\jogo-original" "$raiz\jogo-cli" /E /NFL /NDL
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo-app" | Tee-Object "$raiz\evidencias\A0-preparacao\copia-app.txt"
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo-cli" | Tee-Object "$raiz\evidencias\A0-preparacao\copia-cli.txt"
   ```

   Esperado: os dois `.txt` vazios.

2. Abra o `VNForge.App.exe`. Se você nunca abriu a v3 nesta máquina, o Início mostra "Como começar" com quatro passos e "O que você precisa". Se já tem projetos na lista, eles aparecem; os do roteiro antigo podem ficar. Captura: `A0.2-inicio.png`.
3. Abra Ajustes › IA e gasto e confira:
   - a chave do Claude aparece como guardada (se não, informe a chave e use "Testar conexão");
   - a cotação do dólar é a de hoje;
   - o limite de gasto é um valor que você aceita gastar no teste inteiro;
   - "avisar acima de" está abaixo do limite.

   A CLI lê o mesmo cofre da chave e o mesmo limite, então a parte B usa o que você definir aqui. Captura: `A0.3-ia-e-gasto.png`.

### A1 · Novo projeto

1. Arraste a pasta `$raiz\jogo-app` do Explorer para a área "Traduzir outro jogo" do Início (ou use "Escolher a pasta").
2. Passo 1, o jogo. Esperado: "Feito com o motor YU-RIS" e o que o app achou em linguagem comum (texto para traduzir, arquivo que abre o jogo, cópia de segurança). Abra "Detalhes técnicos" e confira o pacote `ysbin.ypf`, a chave `0xD36FAC96` e a página de código 932. Fora de "Detalhes técnicos" não pode aparecer YPF, XOR, 932 nem caminho de pasta. Captura: `A1.2-jogo.png`.
3. Passo 2, nome e idiomas. Japonês para Português do Brasil. Aponte a pasta do projeto para `$raiz\projeto-app`. Captura: `A1.3-nome.png`.
4. Passo 3, acentos. Esperado: três cartões com a frase de teste desenhada na caixa do jogo; "Com acentos, ajuste no jogo" com o selo "Recomendada para este jogo"; "Com acentos, fonte modificada" com "Em breve" e desligado. Escolha "Com acentos, ajuste no jogo". Captura: `A1.4-acentos.png`.
5. Passo 4, IA e gasto. Esperado: as três qualidades com o gasto do jogo inteiro em reais e o tempo, a cotação e o limite já preenchidos com os valores do A0. Anote no registro o gasto que cada qualidade mostra para o jogo inteiro. Escolha a qualidade do teste (a Caprichada é o padrão). Captura: `A1.5-ia.png`.
6. Crie o projeto e aceite rodar a extração. A extração roda na barra de tarefas, com andamento e cancelar. Esperado: 131 cenas e cerca de 6.850 linhas (a teste.4 extraiu 6.855; a #288 tirou a unidade do comando `FONT`). Anote os números. Captura do fim: `A1.6-extracao.png`.

Se a extração falhar, pare aqui.

### A2 · Painel

1. Abra o projeto. Esperado no Painel: o nome do projeto, um botão de próximo passo ("Traduzir o capítulo 1"), a jornada em cinco passos, o anel quase todo em japonês e o sumário de capítulos. Captura: `A2.1-painel.png`.
2. Confira os capítulos. O app agrupa as cenas pelas pastas do jogo; sem pastas numeradas, por faixas de 40 cenas ("Parte 1", "Parte 2"). Anote no registro quantos capítulos apareceram, o nome de cada um e se batem com a história do jogo. Agrupamento que não faz sentido vira issue "área: core".
3. Escolha o capítulo do teste (um capítulo curto do começo do jogo) e anote no registro o nome e as cenas dele. As etapas A4 e A5 usam esse capítulo.
4. "Precisa de você": anote os itens. Esperado, num projeto novo: personagens sem gênero, teste dos acentos no jogo, narrador e guia de estilo.
5. Ajustes › Formatação. O projeto novo não tem `formatting.json`, e sem ele a caixa do jogo não tem limite: o Editor não avisa quando uma fala passa de 3 linhas e o build não quebra as linhas. Ligue a quebra de linha com largura máxima 73 e 3 linhas por caixa, as medidas do jogo de referência (doc 06, #59). Esperado: a barra de status passa a mostrar esses limites. Captura: `A2.5-formatacao.png`.

### A3 · Patch da fonte e teste de acentos

Esta etapa valida o patch no exe real pela primeira vez. Sem o patch, as falas saem na fonte `System`, com um vão depois de cada letra estreita (doc 06).

1. Abra Testar no trilho. Esperado: a faixa "O jogo está como veio" e o botão "Testar no jogo".
2. Abra "Detalhes técnicos" e ache o cartão "Patch da fonte no executável". Esperado: estado disponível. Captura: `A3.2-patch-disponivel.png`.
3. Clique em "Aplicar patch da fonte". A confirmação lista o executável e o local do backup. Confirme. Esperado: patch aplicado, hash conferido, e o cartão passa para aplicado. Captura: `A3.3-patch-aplicado.png`.
4. Confira os bytes trocados:

   ```powershell
   Diff-Exe 'jogo-app' "$raiz\evidencias\A3-acentos-e-fonte\diff-exe.txt"
   ```

   Esperado: 13 linhas de diferença, entre os offsets `000D5008` e `000D5014`. Outro número, ou offsets fora dessa faixa, vira issue com o `diff-exe.txt` e para aqui.

5. Ainda em Testar, marque "Testar só os acentos" e clique em "Testar no jogo". Esperado: os cinco passos (conferir, preparar os arquivos, guardar a cópia de segurança, copiar para o jogo, abrir o jogo) e o jogo abrindo.
6. No jogo, avance até a primeira fala. Esperado: `Teste de acentos: ação, coração, é, avô, não.` com os acentos certos, em MS Gothic, sem vão depois de `i`, `l` e `t`, e o falante `Teste`. Captura: `A3.6-acentos-no-jogo.png`. Feche o jogo.
7. Em Testar, anote o "Último teste" com o tempo de cada passo. Captura: `A3.7-ultimo-teste.png`.
8. Não use "Voltar ao jogo original" agora. O teste da cena (A4) injeta por cima, e o patch continua no exe.

Se os acentos ou a fonte saírem errados, pare aqui: o resto do roteiro depende deles.

### A4 · Uma cena à mão (#67)

1. Escolha, no capítulo do A2, uma cena curta com diálogo, nome de personagem e uma escolha. Anote a chave da cena (`yst000NN`) e os ids das linhas que vai traduzir.
2. Abra Glossário › Personagens. Para os personagens da cena, preencha o nome na tradução e o gênero. A página grava sozinha ("Salvo automaticamente"). Captura: `A4.2-personagens.png`.
3. Abra a cena no Editor (clique no capítulo no Painel, ou em Cenas). Traduza à mão as falas da cena, com acento em pelo menos uma fala, um nome e a escolha. Em cada linha, confira a caixa do jogo abaixo do campo ("Cabe · N de 3 linhas") e aprove com `Ctrl+Enter`. Numa fala, use "Entender o japonês" uma vez (o preço aparece no botão, menos de R$ 0,01) e anote se ajudou. Captura: `A4.3-editor.png`.
4. Abra Conferir. Esperado: a faixa diz se há algo que trava o teste. Resolva os erros da cena com "Corrigir e ir para o próximo" ou "Abrir no editor". Avisos de linhas sem tradução das outras cenas não travam. Captura: `A4.4-conferir.png`.
5. Com a cena aberta no Editor, vá a Testar, marque "Só a cena aberta no editor", desmarque "Testar só os acentos" e clique em "Testar no jogo". Esperado: os cinco passos e o jogo abrindo. Linhas sem tradução não travam o teste, mas avisos da Conferência nas linhas traduzidas travam: se o botão ficar desligado só por avisos que você já conferiu, marque "Testar mesmo com avisos" e anote quais eram.
6. Jogue até a cena e passe por ela inteira. Esperado: acentos certos nos três tipos de texto, quebras de linha onde a caixa do Editor mostrou e a escolha clicável, com a cena seguindo normalmente. Capturas: `A4.6-dialogo.png`, `A4.6-nome.png`, `A4.6-escolha.png`.
7. Anote no registro o estado que o Painel e a barra de status mostram depois do teste ("Jogo com a tradução de ...").

Com os três tipos certos no jogo, o M2 está validado; a volta ao original é conferida no A6.

### A5 · O capítulo pela IA (#85)

Esta é a primeira etapa com gasto de API.

1. Ajustes › Estilo: escolha honoríficos, tratamento e narrador, e escreva duas ou três linhas no guia de estilo. Anote as escolhas. Captura: `A5.1-estilo.png`.
2. Glossário › Personagens: "Sugerir voz e gênero pelas falas" (o preço aparece no botão). Confira as sugestões e corrija o que estiver errado. Glossário › Termos: em "Termos que se repetem", use "Traduzir com a IA" e adicione os termos que fazem sentido. Anote o gasto de cada botão. Captura: `A5.2-glossario.png`.
3. No Editor, abra uma cena do capítulo e clique em "Traduzir com IA". Na janela, escolha "O capítulo", confira a qualidade e o valor em "Quanto custa". Anote quantas linhas o app chama de capítulo e se são as mesmas cenas do capítulo do Painel: enquanto você não define os grupos, a janela usa faixas de 20 cenas na ordem numérica, e o Painel usa as pastas do jogo. Diferença entre os dois vira issue "área: app". Captura: `A5.3-janela.png`.
4. Clique em "Traduzir". A tarefa roda na barra inferior, com o andamento e o gasto em reais. Esperado no fim: um aviso com as linhas traduzidas e, se houver, as que falharam ou não passaram na validação. Anote o gasto final e o tempo. Captura: `A5.4-fim.png`.
5. Revise cena por cena com "Modo revisão": `Enter` (Está boa), `E` (Corrigir), `M` (Depois), seta para a direita (Pular). Quando a caixa do jogo disser "Passa", teste "Dividir em 2 páginas" numa fala e "Encurtar com a IA" em outra. Anote quantas linhas você corrigiu e por quê (tom, gênero, nome, termo). Captura de uma fala com "Passa": `A5.5-passa.png`.
6. Conferir: use "Corrigir N com um clique" e resolva o que travar o teste. Anote os números do cabeçalho antes e depois. Captura: `A5.6-conferir.png`.
7. Testar: desmarque "Só a cena aberta no editor" e clique em "Testar no jogo". Jogue o capítulo inteiro. Para cada problema no jogo (texto cortado, quebra errada, acento trocado, nome errado, tom estranho), anote o id da linha no registro.
8. Volte ao Painel. Anote o gasto com a IA, a parte pronta do capítulo e a frase do ritmo. Captura: `A5.8-painel-depois.png`.

Com o capítulo jogado e sem erro da Conferência pendente, o M3 está validado.

### A6 · Voltar ao jogo original

1. Em Testar, clique em "Voltar ao jogo original". Esperado: a faixa volta a "O jogo está como veio", sem pedir confirmação, e a tradução continua no projeto.
2. Compare a cópia com o original:

   ```powershell
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo-app" | Tee-Object "$raiz\evidencias\A6-volta\diff-volta.txt"
   Diff-Exe 'jogo-app' "$raiz\evidencias\A6-volta\diff-exe.txt"
   ```

3. Esperado: os dois arquivos sem diferenças. Se só o executável continuar diferente, "Voltar ao jogo original" devolveu os pacotes mas não desfez o patch. Abra issue com os dois `.txt` e use "Restaurar originais" em "Detalhes técnicos" até o exe voltar; anote quantos cliques foram precisos. Esse dado vai para a #342.

### A7 · Uso da interface

Enquanto as etapas acima rodam, anote no `registro.md` do A7 o que atrapalhou: texto cortado, palavra técnica fora de "Detalhes técnicos", botão que levou a um lugar inesperado, tela que travou, valor em reais que não bateu com o gasto real. Cada item com a tela, a captura e, se possível, a issue.

## Parte B · CLI

A parte B repete o caminho pela linha de comando, com o projeto `$raiz\projeto-cli` e a cópia `$raiz\jogo-cli`. Ela confere que a CLI e o app geram o mesmo jogo a partir do mesmo texto e gasta pouco com a IA. Rode cada bloco e leia a última linha: `saída: 0` é sucesso. `vnf test`, `patch-exe` e `restore` perguntam `[s/N]` antes de gravar no jogo.

### B1 · Projeto

```powershell
Vnf-Ev B1-projeto version.txt --version
Vnf-Ev B1-projeto detect.txt detect --game "$raiz\jogo-cli"
Vnf-Ev B1-projeto init.txt init $proj --game "$raiz\jogo-cli" --encoding cp1252
Vnf-Ev B1-projeto extract.txt extract
Vnf-Ev B1-projeto status.txt status
```

Esperado: `vnf 0.0.0-teste.7`; o `detect` com YU-RIS, YSTB v485 e a chave `0xD36FAC96`; o `extract` com as mesmas 131 cenas e o mesmo total de linhas do A1.

### B2 · Patch da fonte e acentos

```powershell
Vnf-Ev B2-acentos-e-fonte patch-exe.txt patch-exe
Diff-Exe 'jogo-cli' "$raiz\evidencias\B2-acentos-e-fonte\diff-exe.txt"
Vnf-Ev B2-acentos-e-fonte test-acentos.txt test --accent-test
```

Esperado: o `patch-exe` diz o que muda e grava com backup; o `diff-exe.txt` igual ao do A3; o `test --accent-test` lista os cinco passos e abre o jogo com a frase de teste igual à do A3.6. Feche o jogo.

### B3 · Mesmo texto, mesmo jogo

Este bloco leva o texto do projeto do app para o projeto da CLI e confere que o build sai idêntico ao do app. O `formatting.json` vai junto, porque o app pode ter mudado a formatação nos Ajustes.

```powershell
& $vnf export --output "$raiz\tmp\app.csv" --project "$raiz\projeto-app" 2>&1 |
  Tee-Object "$raiz\evidencias\B3-mesmo-build\export.txt"
if (Test-Path "$raiz\projeto-app\formatting.json") { Copy-Item "$raiz\projeto-app\formatting.json" "$proj\formatting.json" -Force }
Vnf-Ev B3-mesmo-build import.txt import "$raiz\tmp\app.csv" --yes
Vnf-Ev B3-mesmo-build build.txt build --full
Get-FileHash "$raiz\projeto-app\build\ysbin.ypf", "$proj\build\ysbin.ypf" |
  Tee-Object "$raiz\evidencias\B3-mesmo-build\hashes.txt"
```

O `export` roda direto, sem o `Vnf-Ev`, porque lê o projeto do app e o `Vnf-Ev` acrescentaria o `--project` da CLI. Esperado: os dois hashes iguais. Se forem diferentes, rode `vnf build --full` no projeto do app (`& $vnf build --full --project "$raiz\projeto-app"`) e compare de novo, porque o build do app pode ser de antes da última edição. Se continuarem diferentes, abra issue "área: core" com o `hashes.txt`.

### B4 · Um pouco de IA

Use a cena seguinte ao capítulo do A5, para não traduzir de novo o que o app já traduziu (troque `yst000NN` pela chave dela).

```powershell
Vnf-Ev B4-ia dry-run.txt translate --files yst000NN --dry-run
Vnf-Ev B4-ia translate.txt translate --files yst000NN --limit 30
Vnf-Ev B4-ia review.txt review --files yst000NN --limit 30
Vnf-Ev B4-ia check.txt check
Vnf-Ev B4-ia check-fix.txt check --fix
Vnf-Ev B4-ia status.txt status
Vnf-Ev B4-ia chars.txt chars list
Vnf-Ev B4-ia glossary.txt glossary list
```

Esperado: o `--dry-run` estima tokens e custo sem gravar; o `translate` traduz até 30 linhas e para se o gasto do projeto chegar ao limite do A0; o `review` grava sugestões em `notes` sem trocar traduções; o `check` sai com `2` se houver erro e `0` só com avisos. Escolha um id traduzido e veja a caixa do jogo:

```powershell
Vnf-Ev B4-ia preview.txt preview yst000NN:000NNN
```

Compare o `preview` com a caixa do Editor do app para a mesma linha, se ela existir lá. As aprovações feitas pela CLI não entram no ritmo do Painel: hoje só o app grava o registro de aprovações.

### B5 · Teste no jogo e volta

```powershell
Vnf-Ev B5-teste-e-volta test-cena.txt test --only-script yst000NN
Vnf-Ev B5-teste-e-volta test.txt test
```

O primeiro testa só a cena do B4; o segundo, tudo. Esperado nos dois: os cinco passos com o tempo de cada um e o jogo abrindo. Se um deles sair com `2` só por avisos da Conferência (erro trava sempre), confira os avisos no `check.txt` do B4 e repita com `--allow-warnings`. Jogue a cena do B4 e anote problemas pelo id. Feche o jogo e volte ao original:

```powershell
Vnf-Ev B5-teste-e-volta restore.txt restore
Compare-Pasta "$raiz\jogo-original" "$raiz\jogo-cli" | Tee-Object "$raiz\evidencias\B5-teste-e-volta\diff-volta.txt"
Diff-Exe 'jogo-cli' "$raiz\evidencias\B5-teste-e-volta\diff-exe.txt"
```

O `vnf restore` volta o backup mais recente, que é o do último teste; o patch do B2 está num backup anterior. Se só o exe continuar diferente, rode `vnf restore` de novo, compare outra vez e anote quantas vezes foi preciso, como no A6. Esperado no fim: os dois `.txt` sem diferenças.

## Depois do teste

Me passe o caminho de `$raiz\evidencias`. Eu leio os registros, fecho a #67 e a #85 se passaram, registro o resultado do patch no doc 06 e levo o que apareceu para a #342 e para issues novas. As pastas `jogo-app`, `jogo-cli` e `tmp` podem ser apagadas depois que os `diff-volta.txt` saírem vazios; `jogo-original` e os dois projetos servem para a etapa 7 do roteiro antigo.
