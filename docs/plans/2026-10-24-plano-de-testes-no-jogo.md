# Roteiro de testes no jogo

Este roteiro fecha as issues com o label "no jogo" (#31, #41, #59, #62, #67, #85, #112 a #115 e o épico #12). Ele precisa do jogo YU-RIS instalado, da chave da Anthropic e de alguém jogando, por isso nenhum agente fecha essas issues sozinho. Cada etapa diz o que rodar, o que esperar, o que guardar na pasta de evidências e o que fazer se falhar. As etapas vão em ordem: cada uma usa o resultado da anterior.

Versão testada: `v0.0.0-teste.4` (commit `02ce4da` da `main`). Os comandos foram conferidos no `vnf --help` dessa versão.

## Antes de começar

### Programas

| O quê | Onde |
|---|---|
| CLI `vnf` | `H:\Projetos\vnforge-builds\v0.0.0-teste.4\cli\vnf.exe`, ou o zip `vnf-0.0.0-teste.4-win-x64.zip` da [release](https://github.com/alessandrocaetanob/vnforge/releases/tag/v0.0.0-teste.4) |
| App | `H:\Projetos\vnforge-builds\v0.0.0-teste.4\app\VNForge.App.exe` |
| Editor hexadecimal | HxD ou outro, só na etapa 4 |
| Depurador | x32dbg (executável de 32 bits) ou x64dbg (64 bits), só na etapa 4 |

Confira a versão antes de tudo: `vnf --version` tem que mostrar `vnf 0.0.0-teste.4`.

### Pastas

Use uma raiz fora de qualquer repositório. O roteiro chama essa raiz de `$raiz`.

| Pasta | Para quê |
|---|---|
| `$raiz\jogo-original` | Cópia intacta do jogo. Nunca é aberta, alterada nem apontada pelo `vnf`. Serve de referência nos hashes. |
| `$raiz\jogo` | Cópia de trabalho. É esta que o projeto aponta e que o `vnf inject` altera. |
| `$raiz\projeto` | Projeto de tradução criado pelo `vnf init`. |
| `$raiz\tmp` | Saídas de `unpack` e `pack` das etapas 1 e 2. Pode ser apagada no fim. |
| `$raiz\evidencias` | Uma subpasta por etapa, descrita abaixo. |

O jogo instalado continua onde está. Toda escrita acontece na cópia `$raiz\jogo`, e o `vnf inject` ainda faz backup em `$raiz\projeto\backup` antes de gravar.

### Sessão do PowerShell

Abra um PowerShell 7 e rode este bloco no começo de cada sessão. Ele define as variáveis e duas funções usadas nas etapas.

```powershell
$raiz = 'H:\VNForge-testes'
$vnf  = 'H:\Projetos\vnforge-builds\v0.0.0-teste.4\cli\vnf.exe'
$proj = Join-Path $raiz 'projeto'
$env:NO_COLOR = '1'

# Roda o vnf no projeto e grava a saída na pasta de evidências da etapa.
# Antes do vnf init a pasta do projeto não existe, e o vnf recusa um --project inexistente.
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

# Grava traduções de teste pelo vnf import. Cada item é 'id|tradução'; tradução vazia volta a linha para untranslated.
function Traduzir([string]$etapa, [string]$nome, [string[]]$linhas) {
  $csv = Join-Path $raiz "tmp\$nome.csv"
  $rows = @('id,translation,status,notes') + @($linhas | ForEach-Object {
    $id, $texto = $_ -split '\|', 2
    $status = if ($texto) { 'translated' } else { 'untranslated' }
    '{0},"{1}",{2},' -f $id, ($texto -replace '"', '""'), $status
  })
  Set-Content $csv $rows -Encoding utf8
  Vnf-Ev $etapa "$nome.txt" import $csv --yes
}
```

`Vnf-Ev 03-caixa status.txt status` roda `vnf status --project $proj` e grava a saída e o código de saída em `evidencias\03-caixa\status.txt`. `Traduzir 03-caixa 3.2-reguas @('yst00112:000768|texto')` grava a tradução dessa linha sem abrir o app.

O `inject` e o `restore` pedem confirmação. Pelo `Vnf-Ev`, a pergunta `[s/N]` só aparece depois da resposta (#287): quando o comando parecer parado, digite `s` e Enter.

### Locale Emulator

As etapas 0 a 2 não dependem dele. Da etapa 3 em diante, abra o jogo sem o Locale Emulator, como o jogador vai abrir a tradução; o `vnf run` já abre assim. As falas em japonês ainda sem tradução aparecem com caracteres trocados, e isso é esperado.

Rode um bloco por vez e leia a última linha: `saída: 0` é sucesso. Com outro código, pare e leia a evidência antes do próximo comando. Onde um comando depende do anterior (um `inject` depois do `build`), o bloco só segue com `saída: 0`.

### Chave da Anthropic

A chave só é usada a partir da etapa 6. Grave-a no cofre do Windows uma vez, digitando-a na entrada padrão:

```powershell
& $vnf config ai --set-key
```

Ou defina `$env:ANTHROPIC_API_KEY` só na sessão. Ela nunca vai para arquivo do projeto, nem para a pasta de evidências.

## Pasta de evidências

Crie a estrutura de uma vez:

```powershell
'00-preparacao','01-ypf','02-scripts','03-caixa','04-fonte','05-m2','06-m3','07-m4' |
  ForEach-Object { New-Item -ItemType Directory -Force (Join-Path $raiz "evidencias\$_") | Out-Null }
```

Cada subpasta tem um `registro.md` com este modelo:

```markdown
# Etapa NN · nome

Data:
vnf --version:
Resultado: passou | falhou | parcial
Issues: #NN

## O que fiz
Os comandos na ordem, ou "conforme o roteiro".

## O que vi
O resultado de cada passo, com o nome do arquivo de evidência.

## Problemas
Cada problema com o número da issue aberta.
```

Regras da pasta:

- Saída de comando vai em `.txt` com o nome do passo (`info.txt`, `pack.txt`). Captura de tela vai em `.png` com o número do passo (`3.4-caixa.png`).
- Texto extraído do jogo, arquivos do jogo e pastas do projeto não entram na pasta de evidências: ela guarda saídas, hashes e capturas de tela. Se algo precisar de uma linha específica, cite o id (`yst00012:000034`), nunca o texto.
- A pasta fica fora do repositório. Quando terminar uma etapa, me diga o caminho: eu leio o `registro.md`, fecho a issue e registro o resultado no doc 06.

### Quando algo falha

Abra uma issue com o passo do roteiro, o comando, a saída (o `.txt` da evidência), o código de saída e a captura de tela, sem texto do jogo. Anote o número no `registro.md`. Se a etapa disser "pare aqui", não siga para a próxima: a seguinte depende dela.

Os logs do `vnf` e do app ficam em `$raiz\projeto\.cache\logs`. Anexe o log do dia à issue quando a falha não for óbvia pela saída.

## Etapa 0 · Preparação

1. Copie o jogo instalado para `$raiz\jogo-original` e para `$raiz\jogo`. Grave os hashes da cópia intacta:

   ```powershell
   Get-ChildItem "$raiz\jogo-original" -Recurse -File | Get-FileHash |
     Select-Object Hash, Path | Export-Csv "$raiz\evidencias\00-preparacao\hashes-original.csv" -NoTypeInformation
   ```

2. Confira a versão e diagnostique o jogo, sem gravar nada:

   ```powershell
   & $vnf --version | Tee-Object "$raiz\evidencias\00-preparacao\versao.txt"
   Vnf-Ev 00-preparacao detect.txt detect --game "$raiz\jogo"
   ```

   Esperado: engine YU-RIS, a versão do YSTB, a chave XOR e a lista de `.ypf`.

3. Crie o projeto e extraia. O `--exe` só é preciso se a pasta do jogo tiver mais de um `.exe`:

   ```powershell
   & $vnf init $proj --game "$raiz\jogo" 2>&1 | Tee-Object "$raiz\evidencias\00-preparacao\init.txt"
   Vnf-Ev 00-preparacao extract.txt extract
   Vnf-Ev 00-preparacao status.txt status
   ```

   Esperado: `init` cria `vnforge.json` com a estratégia `fold` e o modelo `claude-opus-5-5`; `extract` grava `.cache\game` e `text\*.json` e não altera `$raiz\jogo`; `status` mostra todas as linhas como `untranslated`.

4. Confirme que a cópia de trabalho não mudou com a extração:

   ```powershell
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo" | Tee-Object "$raiz\evidencias\00-preparacao\diff-jogo.txt"
   ```

   Esperado: nenhuma linha. Qualquer diferença aqui é bug do `extract`: pare aqui.

5. Abra o app, escolha "Abrir projeto" no Início e aponte `$raiz\projeto`. O trilho libera Painel, Editor, Conferir e Build. Tire uma captura do Painel (`0.5-painel.png`).

## Etapa 1 · Ida e volta dos pacotes YPF (#31)

Nada é gravado no jogo nesta etapa: o `unpack` lê da cópia intacta e o `pack` grava em `$raiz\tmp`.

1. Para cada `.ypf` do jogo, rode `info`, `unpack`, `pack` e compare o hash:

   ```powershell
   $ev = "$raiz\evidencias\01-ypf"
   Get-ChildItem "$raiz\jogo-original" -Recurse -Filter *.ypf | ForEach-Object {
     $nome = $_.BaseName
     $pasta = "$raiz\tmp\ypf\$nome"
     $novo  = "$raiz\tmp\ypf\$nome.novo.ypf"
     & $vnf ypf info $_.FullName 2>&1 | Out-File "$ev\info-$nome.txt"
     & $vnf ypf unpack $_.FullName $pasta 2>&1 | Out-File "$ev\unpack-$nome.txt"
     & $vnf ypf pack $pasta --original $_.FullName --output $novo 2>&1 | Out-File "$ev\pack-$nome.txt"
     [pscustomobject]@{
       Pacote   = $_.Name
       Original = (Get-FileHash $_.FullName).Hash
       Novo     = (Get-FileHash $novo).Hash
     }
   } | Tee-Object -Variable r | Format-Table -AutoSize | Out-File "$ev\hashes.txt"
   $r | Where-Object { $_.Original -ne $_.Novo }
   ```

2. Esperado: a última linha não mostra nada, porque todo pacote remontado tem o mesmo SHA-256 do original.
3. Anote no `registro.md` a variante que o `info` mostra para o `ysbin.ypf` (versão, compressão, hash de nome e de dados). Esse é o dado que vai para o doc 06.

Se algum pacote sair diferente, pare aqui. A regra de reescrita mínima do `CLAUDE.md` vem antes de qualquer teste com tradução. Na issue, inclua o `info-<nome>.txt` do pacote que diferiu.

## Etapa 2 · Ida e volta dos scripts (#41)

A etapa tem duas partes. Na primeira o build roda sem nenhuma tradução; na segunda, com uma linha trocada.

### 2A · Build sem tradução

Sem nenhuma linha traduzida, o `vnf build` não reconstrói script nenhum e copia cada pacote original para `build\`. O teste confirma esse comportamento, mas não exercita a reconstrução: quem faz isso é a parte 2B.

1. Rode o build e compare:

   ```powershell
   Vnf-Ev 02-scripts build-vazio.txt build --full
   Compare-Pasta "$raiz\jogo-original" "$proj\build" | Where-Object SideIndicator -eq '=>' |
     Tee-Object "$raiz\evidencias\02-scripts\diff-build-vazio.txt"
   ```

2. Esperado: o build termina com saída 0, e todo arquivo em `build\` tem o mesmo hash do arquivo de mesmo caminho em `jogo-original`.

### 2B · Uma linha trocada

1. No app, abra o Editor, escolha uma fala de diálogo do primeiro script e digite como tradução `teste de ida e volta`. Anote o id da linha (aparece no painel da linha) no `registro.md`.
2. Reconstrua e desempacote o `ysbin.ypf` do build:

   ```powershell
   Vnf-Ev 02-scripts build-uma-linha.txt build --full
   $ysbin = Get-ChildItem "$proj\build" -Recurse -Filter ysbin.ypf | Select-Object -First 1
   & $vnf ypf unpack $ysbin.FullName "$raiz\tmp\build-ysbin" 2>&1 | Out-File "$raiz\evidencias\02-scripts\unpack-build.txt"
   Compare-Pasta "$raiz\tmp\ypf\ysbin" "$raiz\tmp\build-ysbin" | Tee-Object "$raiz\evidencias\02-scripts\diff-scripts.txt"
   ```

3. Esperado: só o `.ybn` do script editado aparece na comparação (duas linhas, uma de cada lado). Todo outro `yst*.ybn` é idêntico, byte a byte.
4. Conte as unidades por tipo, para o doc 06:

   ```powershell
   Get-ChildItem "$proj\text" -Filter *.json | ForEach-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).units } |
     Group-Object kind | Select-Object Name, Count | Tee-Object "$raiz\evidencias\02-scripts\unidades-por-tipo.txt"
   ```

5. Copie para o `registro.md` a versão do YSTB e a chave XOR do `detect.txt` da etapa 0.
6. No Editor, apague a tradução de teste (campo vazio volta a linha para `untranslated`).

Se outro script além do editado diferir, pare aqui e abra a issue com o `diff-scripts.txt`.

## Etapa 3 · Largura e linhas da caixa de texto (#59)

É a primeira vez que o jogo lê um script reconstruído com mudança. O jogo quebra a fala sozinho pela contagem de células, então a medida não depende da fonte: no jogo de referência deram 73 colunas e 3 linhas, com a fonte `System` e com a MS Gothic.

1. Escolha duas falas seguidas que apareçam logo no começo do jogo e anote os ids. No jogo de referência foram `yst00112:000768` e `yst00112:001280`.
2. Grave duas réguas. A primeira tem 520 caracteres sem espaço, com uma letra a cada 10 colunas (`A234567890B234567890…`), e mostra a coluna de cada quebra. A segunda tem 6 linhas curtas separadas por `\n` e mostra quantas linhas cabem:

   ```powershell
   $f1 = 'yst00112:000768'; $f2 = 'yst00112:001280'
   $regua = -join ((65..90 + 97..122) | ForEach-Object { [char]$_ + '234567890' })
   Traduzir 03-caixa 3.2-reguas @("$f1|$regua", "$f2|L1 primeira\nL2 segunda\nL3 terceira\nL4 quarta\nL5 quinta\nL6 sexta")
   Vnf-Ev 03-caixa 3.2-preview.txt preview $f1
   ```

   Sem `formatting.json` no projeto a quebra fica desligada, e o preview mostra a régua numa linha só: quem quebra é o jogo.

3. Construa, injete e abra:

   ```powershell
   Vnf-Ev 03-caixa 3.3-build.txt build --encoding cp1252
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 03-caixa 3.3-inject.txt inject }
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 03-caixa 3.3-run.txt run }
   ```

4. Comece um jogo novo, vá até as duas falas e tire as capturas `3.4-regua.png` e `3.4-linhas.png`. Na régua, o fim de cada linha dá a coluna: a letra é a dezena (A vai de 1 a 10, B de 11 a 20, Z de 251 a 260, a minúscula `a` de 261 a 270) e o dígito é a posição dentro dela (`0` é a décima). Na segunda fala, conte as linhas que aparecem inteiras dentro da caixa, sem nada por cima.
5. Feche o jogo e restaure (`Vnf-Ev 03-caixa 3.5-restore.txt restore`).
6. Valide os números com a quebra do VNForge. Crie o perfil do projeto com a largura e as linhas medidas e grave uma fala de três linhas em que as duas primeiras têm exatamente a largura máxima (a frase abaixo é para 73 colunas), e uma de cinco linhas:

   ```powershell
   Set-Content "$proj\formatting.json" -Encoding utf8 -Value @(
     '{',
     '  "kinds": ["dialogue", "narration"],',
     '  "wrap": { "enabled": true, "maxWidth": 73, "maxLines": 3, "charWidth": "halfWidthLatin" }',
     '}'
   )
   $t1 = 'Quando cheguei na estação, o trem já tinha partido, e ninguém soube dizer para onde tinham levado as malas, e fiquei ali parado, olhando os trilhos vazios, sem saber se voltava para casa ou se esperava o próximo.'
   $t2 = $t1 + ' Depois de meia hora, um funcionário apareceu e disse que o último trem já tinha saído.'
   Traduzir 03-caixa 3.6-validar @("$f1|$t1", "$f2|$t2")
   Vnf-Ev 03-caixa 3.6-preview-1.txt preview $f1
   Vnf-Ev 03-caixa 3.6-preview-2.txt preview $f2
   Vnf-Ev 03-caixa 3.6-build.txt build --full --encoding cp1252
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 03-caixa 3.6-inject.txt inject }
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 03-caixa 3.6-run.txt run }
   ```

   Esperado: o preview da primeira fala mostra 3 linhas (73, 73 e 64 colunas), o da segunda mostra 5 linhas com o aviso de limite, e o build repete o aviso `caixa-acima-do-limite`. No jogo, tire `3.6-fala1.png` e `3.6-fala2.png`: cada linha tem que terminar onde o preview mostra, sem palavra partida no meio. Se o jogo partir uma palavra, a largura está uma coluna acima do real.

7. Grave a largura e as linhas finais no `registro.md`: elas vão para o perfil de exemplo (`samples/formatting.json`). O `formatting.json` fica no projeto para as etapas seguintes.
8. Restaure, confira e limpe as falas de teste:

   ```powershell
   Vnf-Ev 03-caixa 3.8-restore.txt restore
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo" | Tee-Object "$raiz\evidencias\03-caixa\diff-restore.txt"
   Traduzir 03-caixa 3.8-limpeza @("$f1|", "$f2|")
   ```

   Esperado: nenhuma diferença, exceto arquivos que o próprio jogo cria ao rodar (no jogo de referência, `save\gsd.sd` e `save\kidoku.sd`). Liste esses arquivos no registro.

Se o jogo não abrir ou travar na fala, pare aqui: o script reconstruído não é aceito.

## Etapa 4 · Lead bytes e fonte no executável (#62)

Esta etapa decide se os acentos saem com `cp1252` e com qual fonte. Trabalhe só em cópias do executável, em `$raiz\tmp\exe`. O roteiro chama o executável do jogo de `jogo.exe`; troque pelo nome real.

No jogo de referência o resultado foi: acentos certos com o exe original, a tabela de lead bytes intocada e um patch de 13 bytes no nome da fonte (doc 06, "Testes da V2 no jogo de referência").

### 4A · Onde está o nome da fonte

O jogo pede a fonte `ＭＳ ゴシック`. Procure o nome em Shift-JIS e em UTF-16 nos arquivos soltos do jogo e nos pacotes já desempacotados na etapa 1 (dentro de um `.ypf` o conteúdo está comprimido, e a busca não o acharia):

```powershell
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
$latin1 = [System.Text.Encoding]::Latin1
$alvos = @{
  'sjis'  = $latin1.GetString([System.Text.Encoding]::GetEncoding(932).GetBytes('ＭＳ ゴシック'))
  'utf16' = $latin1.GetString([System.Text.Encoding]::Unicode.GetBytes('ＭＳ ゴシック'))
}
@(Get-ChildItem "$raiz\jogo-original" -Recurse -File | Where-Object Extension -ne '.ypf') +
@(Get-ChildItem "$raiz\tmp\ypf" -Recurse -File) | ForEach-Object {
  $s = $latin1.GetString([IO.File]::ReadAllBytes($_.FullName))
  foreach ($k in $alvos.Keys) {
    $i = $s.IndexOf($alvos[$k], [StringComparison]::Ordinal)
    while ($i -ge 0) { '{0} {1} 0x{2:X}' -f $_.FullName, $k, $i; $i = $s.IndexOf($alvos[$k], $i + 1, [StringComparison]::Ordinal) }
  }
} | Tee-Object "$raiz\evidencias\04-fonte\nome-da-fonte.txt"
```

Em Shift-JIS o nome é `82 6C 82 72 20 83 53 83 56 83 62 83 4E`. No jogo de referência ele aparece duas vezes no exe: em Shift-JIS em 0xD5008, numa lista com `標準ゴシック`, `ＭＳ Ｐゴシック`, `ＭＳ 明朝` e `標準明朝` (é o nome que as falas usam), e em UTF-16 em 0x11EF02, a fonte de uma caixa de diálogo do Windows. Os `yst*.ybn` guardam o texto cifrado com a chave XOR, então um nome que só exista ali não aparece nesta busca; no jogo de referência o `yst00011` tem o nome num comando `FONT`, mas trocá-lo não muda a fonte das falas.

### 4B · Tabela de lead bytes

O YU-RIS marca numa tabela de 256 bytes quais bytes iniciam caractere duplo do Shift-JIS (`0x81` a `0x9F` e `0xE0` a `0xFC`). O bloco abaixo procura no executável janelas de 256 bytes em que só essas posições são diferentes de zero:

```powershell
New-Item -ItemType Directory -Force "$raiz\tmp\exe" | Out-Null
$b = [IO.File]::ReadAllBytes("$raiz\jogo-original\jogo.exe")
& {
  for ($o = 0; $o -le $b.Length - 256; $o++) {
    if ($b[$o + 0x80] -ne 0 -or $b[$o + 0x81] -eq 0 -or $b[$o + 0xA0] -ne 0 -or $b[$o + 0xE0] -eq 0) { continue }
    $ok = $true
    for ($i = 0; $i -lt 256; $i++) {
      $lead = ($i -ge 0x81 -and $i -le 0x9F) -or ($i -ge 0xE0 -and $i -le 0xFC)
      if ($lead -ne ($b[$o + $i] -ne 0)) { $ok = $false; break }
    }
    if ($ok) { '0x{0:X}' -f $o }
  }
} | Tee-Object "$raiz\evidencias\04-fonte\tabela-lead-bytes.txt"
```

A busca leva até um minuto num executável de alguns MB. No jogo de referência a tabela está em 0xC20A0. Anote o endereço e não mexa na tabela: com ela inteira zerada o jogo não abre e mostra `ルビ設定開始文字が不正です。` (caractere inicial de ruby inválido).

### 4C · Fonte e charset pedidos pelo jogo

O x32dbg mostra o que o jogo pede ao Windows quando cria a fonte. Use o x32dbg para executável de 32 bits e o x64dbg para 64 bits.

1. Abra o x32dbg, use **Arquivo › Abrir** (F3) e escolha `$raiz\jogo\jogo.exe`. A pasta de trabalho está igual ao original nesta etapa.
2. Aperte F9 até a barra de baixo, à esquerda, mostrar "Pausado" com a mensagem de "entry breakpoint". Costuma levar uma ou duas vezes.
3. Clique na caixa **Comando:**, na base da janela, digite `bp CreateFontIndirectA` e tecle Enter. Se der erro, use `bp gdi32full.CreateFontIndirectA`.
4. Aperte F9. Quando parar, digite `dump [esp+4]` na caixa Comando e tecle Enter. O painel **Despejo 1**, embaixo à esquerda, mostra a estrutura `LOGFONTA`.
5. Leia o painel: os 4 primeiros bytes são a altura; na segunda linha, o 8º byte é o charset (`80` é Shift-JIS, `00` é ANSI, `01` é `DEFAULT_CHARSET`) e o nome da fonte começa no 13º byte, na coluna de texto à direita. Tire a captura (`4C-logfont-1.png`).
6. Aperte F9 de novo e repita os passos 4 e 5 em cada parada, até a primeira fala aparecer. Depois digite `bc` e Enter para apagar o breakpoint e feche o x32dbg.

No jogo de referência, sem o Locale Emulator, as duas paradas mostraram a face `System`, charset `01`, peso 700 e alturas 92 e 84: o Windows não acha `ＭＳ ゴシック` pelo nome japonês.

### 4D · Teste dos acentos

1. Grave nas duas falas da etapa 3 uma frase com os acentos do português e uma que termina em acento:

   ```powershell
   $f1 = 'yst00112:000768'; $f2 = 'yst00112:001280'  # as falas da etapa 3
   Traduzir 04-fonte 4D-texto @("$f1|Ação, coração, você é ótimo! Àquela ponte, pôr o pé.", "$f2|Até já, café")
   Vnf-Ev 04-fonte 4D-build-report.txt build --report --encoding cp1252
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 04-fonte 4D-build.txt build --encoding cp1252 }
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 04-fonte 4D-inject.txt inject }
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 04-fonte 4D-run.txt run }
   ```

   O `--report` sai com 2 se algum caractere não existir em `cp1252`; nesse caso nada é construído nem injetado.

2. Vá até as falas e tire `4D-fala1.png` e `4D-fala2.png`. Esperado: os acentos certos e o `é` final de `café` inteiro, sem caractere estranho depois. Com a fonte `System` as letras estreitas ficam com um vão depois delas; a 4E corrige isso.
3. Feche o jogo e restaure (`Vnf-Ev 04-fonte 4D-restore.txt restore`).

Se os acentos aparecerem como outro caractere, o caminho é `remap` (#66): anote no registro e pule a 4E.

### 4E · Nome da fonte no executável

Troque, numa cópia do exe, o nome em Shift-JIS achado na 4A por `MS Gothic` em ASCII, completando com zeros. O espaço do nome no jogo de referência tem 16 bytes (13 do nome e 3 zeros), e `MS Gothic` usa 9.

1. Gere a cópia alterada. O bloco confere os bytes antes de mexer e conta o que mudou:

   ```powershell
   $exeFonte = "$raiz\tmp\exe\fonte.exe"
   Copy-Item "$raiz\jogo-original\jogo.exe" $exeFonte -Force
   $b = [IO.File]::ReadAllBytes($exeFonte)
   $ini = 0xD5008
   $esperado = [byte[]](0x82,0x6C,0x82,0x72,0x20,0x83,0x53,0x83,0x56,0x83,0x62,0x83,0x4E,0x00,0x00,0x00)
   if ([Convert]::ToHexString($b[$ini..($ini + 15)]) -ne [Convert]::ToHexString($esperado)) {
     'Os bytes não estão como esperado. Pare aqui.' | Tee-Object "$raiz\evidencias\04-fonte\4E-patch.txt"
   } else {
     $novo = [Text.Encoding]::ASCII.GetBytes('MS Gothic') + [byte[]]::new(7)
     [Array]::Copy($novo, 0, $b, $ini, 16)
     [IO.File]::WriteAllBytes($exeFonte, $b)
     $o = [IO.File]::ReadAllBytes("$raiz\jogo-original\jogo.exe")
     $dif = @(0..($b.Length - 1) | Where-Object { $b[$_] -ne $o[$_] })
     ('bytes alterados: {0}, de 0x{1:X} a 0x{2:X}' -f $dif.Count, $dif[0], $dif[-1]) | Tee-Object "$raiz\evidencias\04-fonte\4E-patch.txt"
   }
   ```

   No jogo de referência: `bytes alterados: 13, de 0xD5008 a 0xD5014`.

2. Ponha a cópia na pasta de trabalho, injete o build da 4D e abra:

   ```powershell
   Copy-Item $exeFonte "$raiz\jogo\jogo.exe" -Force
   Vnf-Ev 04-fonte 4E-inject.txt inject
   if ($LASTEXITCODE -eq 0) { Vnf-Ev 04-fonte 4E-run.txt run }
   ```

3. Vá até as falas e tire `4E-fala1.png` e `4E-fala2.png`. Esperado: a letra de largura fixa da MS Gothic, sem os vãos, e os acentos certos.
4. Feche o jogo, restaure e devolva o exe original:

   ```powershell
   Vnf-Ev 04-fonte 4E-restore.txt restore
   Copy-Item "$raiz\jogo-original\jogo.exe" "$raiz\jogo\jogo.exe" -Force
   Compare-Pasta "$raiz\jogo-original" "$raiz\jogo" | Tee-Object "$raiz\evidencias\04-fonte\diff-restore.txt"
   Traduzir 04-fonte 4E-limpeza @("$f1|", "$f2|")
   ```

5. Escreva a conclusão no `registro.md`, numa linha: "cp1252 com patch da fonte", "cp1252 sem patch" ou "partir para remap". Ela define a estratégia da etapa 5 e o que entra no `vnf patch-exe` (#65).

A lista de fontes da tela CONFIG › MESSAGE fica vazia sem o Locale Emulator, com ou sem o patch. Não serve para trocar a fonte.

## Etapa 5 · Primeira cena em PT-BR · M2 (#67)

As etapas 5 e 6 foram escritas antes da interface v3 e mandam usar a tela Build, que virou Testar no jogo. Para elas, use o [roteiro do teste de um capítulo](2026-10-26-roteiro-capitulo-v3.md), que roda pela interface e depois pela CLI numa cópia limpa do jogo. O texto abaixo fica como registro.

1. Defina a estratégia de acentuação pela conclusão da etapa 4, na aba Ajustes do app ou no `encoding.strategy` do `vnforge.json`. Sem `cp1252` funcionando, use `fold` (acentos trocados pela letra sem acento). Com "cp1252 com patch da fonte", aplique o patch do nome da fonte antes do passo 4 com `Vnf-Ev 05-cena 5-patch-exe.txt patch-exe` (#65; o `vnf restore` devolve o exe), ou, em versão sem o `patch-exe`, copie `$raiz\tmp\exe\fonte.exe` por cima do `jogo.exe` de `$raiz\jogo`: o `vnf inject` não aplica o patch.
2. Escolha uma cena curta que tenha diálogo, nome de personagem e uma escolha. Traduza à mão no Editor do app, com acentos em cada um dos três tipos. Anote os ids no registro.
3. Na Conferência, rode a conferência e resolva o que aparecer. Tire uma captura (`5.3-conferir.png`).
4. Na tela Build do app, gere o build e injete. Tire capturas do resultado do build e da injeção (`5.4-build.png`, `5.4-inject.png`).
5. Jogue a cena, sem o Locale Emulator. Tire uma captura de cada tipo de texto (`5.5-dialogo.png`, `5.5-nome.png`, `5.5-escolha.png`).
6. Esperado: acentos certos (ou trocados pelo `fold`, se essa foi a estratégia), quebras de linha onde a prévia do Editor mostra, escolha clicável e a cena seguindo normalmente.
7. Restaure pela tela Build do app, devolva o `jogo.exe` de `jogo-original` se trocou no passo 1, e confira com `Compare-Pasta` contra `jogo-original` (`diff-restore.txt`).

Com os três tipos certos no jogo e o restore limpo, o M2 está validado.

## Etapa 6 · Capítulo traduzido pela IA · M3 (#85)

Esta etapa também é o teste de uso da V2: paleta de comandos (`Ctrl+K`), Painel, Editor, Conferência e Build com um projeto real. Diferença de layout, texto cortado ou ação errada vira issue com o label "área: app" e captura de tela.

1. Escolha os scripts do primeiro capítulo e anote o padrão (`yst0001*`, por exemplo).
2. Estime o custo antes de chamar a API:

   ```powershell
   Vnf-Ev 06-m3 translate-dry-run.txt translate --files "yst0001*" --dry-run
   ```

   Esperado: tokens e custo estimados, nada gravado. Se o custo passar do que você aceita, use `--model claude-sonnet-5-5` ou `--limit` para uma amostra.

3. Traduza e confira:

   ```powershell
   Vnf-Ev 06-m3 translate.txt translate --files "yst0001*"
   Vnf-Ev 06-m3 check.txt check
   Vnf-Ev 06-m3 status.txt status
   ```

   O `check` sai com 2 se houver erro de QA. Rode `check --fix` para as correções seguras e trate o resto no app.

4. Opcional: peça a revisão da IA, que grava sugestões em `notes` sem trocar traduções:

   ```powershell
   Vnf-Ev 06-m3 review.txt review --files "yst0001*"
   ```

5. Revise no app: Conferência para os problemas de QA, Editor para ler as falas e aprovar. Tire capturas do Painel antes e depois (`6.5-painel-antes.png`, `6.5-painel-depois.png`).
6. Build e injeção pelo app. Jogue o capítulo inteiro e anote no registro cada problema com o id da linha.
7. Esperado: o capítulo roda no jogo sem erro de QA pendente. O custo real fica no Painel (vem de `.cache\ai-usage.json`); anote-o no registro.
8. Restaure ao terminar a sessão de jogo.

## Etapa 7 · Jogo inteiro · M4 (#112, #113, #114, #115 e #12)

As quatro issues correm juntas, ao longo de várias sessões. Use uma subpasta por sessão dentro de `07-m4` (`07-m4\sessao-01`), cada uma com o seu `registro.md`.

| Issue | O que fazer | Pronto quando | Evidência |
|---|---|---|---|
| #112 | `translate --dry-run` e `translate` por capítulo, `review`, revisão no app até aprovar tudo | `vnf status` mostra todas as linhas aprovadas | `status.txt` de cada sessão |
| #113 | Ajustar o `formatting.json` com o que aparecer no jogo e rodar `vnf check` depois de cada ajuste | Nenhuma fala estoura a caixa de texto | `check.txt` e capturas das falas corrigidas |
| #114 | Listar menus, títulos e outras imagens com texto, com o arquivo e o pacote de cada uma (os pacotes de imagem do `unpack` da etapa 1 ajudam) | Lista pronta, como entrada para o E12 (#15) | `imagens-com-texto.md` em `07-m4` |
| #115 | Jogar do início ao fim com a tradução injetada, todas as rotas | Jogo terminado | Uma captura por final e a lista de issues abertas |

O #114 só precisa do jogo desempacotado e pode começar junto com o #112. Com o #115 fechado, o #12 (E11, M4) fecha junto.

## Ordem

```
0 ─► #31 ─► #41 ─► #59 ─► #62 ─► #67 (M2) ─► #85 (M3) ─► #112 ─► #113 ─► #115 ─► #12 (M4)
                                                              └─► #114
```

As etapas 0 a 4 cabem num dia e não chamam a IA. A etapa 6 é a primeira com custo de API.
