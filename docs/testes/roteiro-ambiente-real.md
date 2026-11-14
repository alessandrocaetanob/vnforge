# Roteiro do ambiente real

Teste manual para rodar antes de cada pré-release (tag `vX.Y.Z-rcN`). Cobre o que os testes automáticos não alcançam: o H:, o W: (rclone), pasta com japonês, o jogo real aberto depois da injeção e os fluxos do app que mexem com o jogo e o projeto. Tarefa T6 do [plano de testes](../plans/2026-11-14-plano-de-testes.md); o contexto está em [04-testes.md](../04-testes.md).

Leva mais de uma hora: duas esperas de 10 minutos pelo envio ao B2 e o tempo de ler o jogo inteiro para conferir. Marque cada passo. Se um passo falhar, pare nele, siga a linha "Se falhar" e abra a issue antes de seguir.

## Os dois discos

| | H: | W: |
|---|---|---|
| O que é | Disco local (ReFS, LDM) | Mount rclone (FUSE-rclone/WinFsp, `b2:gamescdn`) |
| Cache | Não tem | `--vfs-cache-mode full`, cache em `G:\B2Cache` |
| Gravação | Direta | `--vfs-write-back 5m`: o arquivo sobe ao B2 uns 5 minutos depois de gravado |
| Serviço | Não tem | `GamesShare` (nssm) |
| Log | Não tem | `H:\RClone\games.log` |

O VNForge reconhece volume que não é local, relê e confere toda gravação e usa o caminho com `.bak`. Foi o que a #697 corrigiu (PR #706): renomear por cima de um arquivo que já subiu ao B2 e foi lido logo antes zerava o destino. É esse defeito que o bloco 2 tenta reproduzir.

## Antes de começar

Abra o PowerShell 7 e rode uma vez:

```powershell
dotnet build H:\Projetos\vnforge\src\VNForge.Cli -c Release
function vnf { dotnet run --project H:\Projetos\vnforge\src\VNForge.Cli -c Release --no-build -- @args }
function Get-Hashes($dir) {
    Get-ChildItem -LiteralPath $dir -Recurse -File | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash)  $($_.Path.Substring($dir.Length))" } | Sort-Object
}
```

`vnf` roda a CLI da árvore atual. Os comandos abaixo rodam dentro da pasta do projeto (`Set-Location`). `Get-Hashes` lista o SHA-256 de cada arquivo da pasta; duas pastas iguais dão o mesmo resultado em `Compare-Object (Get-Hashes A) (Get-Hashes B)`, sem nenhuma linha de saída.

Defina as pastas e tire a foto do jogo original. `$orig` é a pasta do jogo de teste (YU-RIS) como veio, que não se mexe até o bloco 4:

```powershell
$orig = 'D:\Jogos\<jogo>'        # troque pela pasta do jogo
$base = 'H:\VNF-Teste'
$w = 'W:\VNF-Teste'
New-Item -ItemType Directory -Force $base, $w | Out-Null
Get-Hashes $orig | Set-Content "$base-original.txt"
```

Os blocos 1 a 3 usam cópias do jogo, para o original ficar intacto. Se o jogo for grande demais para copiar três vezes, copie só os arquivos do jogo que o projeto registra (os `.ypf` de scripts e o `game.exe`): a conferência do `vnf game move` olha só esses.

Em qualquer falha, anexe à issue: o comando que rodou, a saída inteira, o log do comando (`<projeto>\.cache\logs\`, ou `%LOCALAPPDATA%\VNForge\logs\` quando a pasta não é um projeto) e, nos blocos 2 e 3, o trecho do `H:\RClone\games.log` da hora do teste. Os rótulos: `bug` mais a área (`área: cli`, `área: core`, `área: app` ou `área: yuris`); `infra` quando o defeito é do ambiente (rclone, disco, instalador); `no jogo` quando só aparece com o jogo real.

## 1. Projeto e jogo no H:

1.1. Copie o jogo e crie o projeto:

```powershell
Copy-Item -LiteralPath $orig -Destination "$base\jogo" -Recurse
vnf init "$base\projeto" --game "$base\jogo"
Set-Location "$base\projeto"
vnf extract
vnf status
```

Esperado: `Engine detectada: YU-RIS`, o arquivo de scripts e a chave XOR, depois a contagem de scripts e de linhas extraídas. O `status` mostra todas as linhas em "sem tradução". Nenhum aviso de "disco que não é local".

Se falhar: issue `bug` + `área: cli` (ou `área: yuris` se a detecção ou a extração é que falhou).

1.2. Reconstrua, injete e restaure sem tradução nenhuma:

```powershell
vnf build
vnf inject --yes
vnf restore --yes
Compare-Object (Get-Hashes "$base\jogo") (Get-Content "$base-original.txt")
```

Esperado: o `inject` diz o backup criado em `backup\` e o que foi injetado; o `restore` termina com código 0; o `Compare-Object` não imprime nada, ou seja, o jogo voltou byte a byte ao original.

Se falhar: issue `bug` + `área: core`, com o `Compare-Object` anexado (mostra quais arquivos diferem).

1.3. Repita a extração para ver que ela é estável:

```powershell
vnf extract
vnf status
```

Esperado: os mesmos números do passo 1.1, sem erro.

## 2. Projeto e jogo no W:

O cenário da #697. Sempre use uma pasta nova no W: e deixe o `vnf` rodar de dentro dela.

2.1. Copie o projeto e o jogo para o W: e anote a hora:

```powershell
Copy-Item -LiteralPath "$base\projeto" -Destination "$w\projeto" -Recurse
Copy-Item -LiteralPath "$base\jogo" -Destination "$w\jogo" -Recurse
Get-Date
```

Esperado: a cópia termina sem erro. O W: pode ficar lento enquanto grava.

2.2. Espere o envio ao B2. O `--vfs-write-back 5m` segura cada arquivo por 5 minutos antes de subir, então espere pelo menos 10 minutos depois do fim da cópia. Enquanto espera, olhe o log:

```powershell
Select-String -Path H:\RClone\games.log -Pattern 'ERROR' | Select-Object -Last 20
```

Esperado: nenhuma linha `ERROR` com arquivo de `W:\VNF-Teste`. O texto exato do log depende do nível configurado; na dúvida, a espera de 10 minutos é o que vale.

Se falhar: issue `infra` com o trecho do `games.log`. O defeito é do rclone ou do B2, não do VNForge, e o roteiro não segue com envio quebrado.

2.3. Leia todos os arquivos, como o app e a CLI fazem ao abrir o projeto, e compare com o H:

```powershell
Compare-Object (Get-Hashes "$base\projeto") (Get-Hashes "$w\projeto")
Compare-Object (Get-Hashes "$base\jogo") (Get-Hashes "$w\jogo")
```

Esperado: nenhuma saída nas duas. Dá para ver também por tamanho:

```powershell
Get-ChildItem -LiteralPath $w -Recurse -File | Where-Object Length -eq 0
```

Esperado: nenhuma saída, a menos que o H: também tenha arquivo vazio com o mesmo nome (compare com a mesma linha rodada em `$base`).

Se falhar: o arquivo já veio diferente do H: antes de o VNForge tocar nele. Issue `infra` com o nome dos arquivos e o `games.log`.

2.4. Mude a pasta do jogo do projeto para a cópia do W:, logo depois de ter lido tudo (é a sequência que zerava os arquivos):

```powershell
Set-Location "$w\projeto"
vnf game move --game "$w\jogo"
```

Esperado: código 0 e a mensagem de que a pasta do jogo foi trocada, com a contagem de arquivos conferidos. Pode aparecer o aviso "está num disco que não é local ... confere cada gravação relendo o arquivo": é o esperado no W:. No H: ele não aparece.

Se falhar: issue `bug` + `área: core`, com a saída completa e o `games.log`. Erro de "arquivo diferente" ou "ausente" é conferência do jogo e vale a mesma issue se o jogo do W: bate com o H: no passo 2.3.

2.5. Confira que nada ficou vazio nem pela metade:

```powershell
Get-ChildItem -LiteralPath "$w\projeto" -Recurse -File | Where-Object { $_.Length -eq 0 -or $_.Name -match '\.(bak|tmp)$' }
Get-Content "$w\projeto\vnforge.json" -Raw | ConvertFrom-Json | Out-Null
Get-Content "$w\projeto\.cache\state.json" -Raw | ConvertFrom-Json | Out-Null
vnf status
```

Esperado: a primeira linha não imprime nada (sem arquivo vazio, sem `.bak` ou `.tmp` sobrando); os dois JSON abrem sem erro; o `status` mostra os mesmos números do passo 1.3. Confira também que o `game.path` do `vnforge.json` e o `gameDirectory` do `state.json` apontam para `W:/VNF-Teste/jogo`.

Se falhar: issue `bug` + `área: core`. Anexe o nome dos arquivos vazios, o `games.log` e a hora do passo 2.4. Se o arquivo vazio é um do projeto (`vnforge.json`, `state.json`, `text\*.json`), é a #697 de volta: avise o Alessandro antes de seguir com o pré-release.

2.6. Espere de novo 10 minutos (o `game move` gravou arquivos, que sobem outra vez), leia tudo de novo e repita a conferência:

```powershell
Get-Hashes "$w\projeto" | Out-Null
Get-ChildItem -LiteralPath "$w\projeto" -Recurse -File | Where-Object Length -eq 0
Select-String -Path H:\RClone\games.log -Pattern 'ERROR' | Select-Object -Last 20
```

Esperado: nenhuma saída. O arquivo só zerava depois do envio, então é aqui que ele apareceria.

2.7. Injete e restaure no W:, com o projeto e o jogo dele:

```powershell
vnf build
vnf inject --yes
vnf restore --yes
Compare-Object (Get-Hashes "$w\jogo") (Get-Content "$base-original.txt")
```

Esperado: igual ao passo 1.2: nenhuma saída do `Compare-Object`, `restore` com código 0. Na primeira vez no W:, o `Get-Hashes` lê o jogo todo: pode levar vários minutos.

Se falhar: issue `bug` + `área: core`, com a saída e o `games.log`.

## 3. Pasta com japonês

3.1. Repita o ciclo numa pasta com japonês no nome, nos dois discos. O nome de teste é `ゲーム テスト` (com espaço, para pegar as aspas também):

```powershell
foreach ($raiz in 'H:\VNF-Teste-日本語', 'W:\VNF-Teste-日本語') {
    New-Item -ItemType Directory -Force $raiz | Out-Null
    $jogo = Join-Path $raiz 'ゲーム テスト'
    Copy-Item -LiteralPath $orig -Destination $jogo -Recurse
    vnf init (Join-Path $raiz 'プロジェクト') --game $jogo
    Set-Location (Join-Path $raiz 'プロジェクト')
    vnf extract
    vnf build
    vnf inject --yes
    vnf restore --yes
    Compare-Object (Get-Hashes $jogo) (Get-Content "$base-original.txt")
}
```

Esperado: nos dois discos, `init`, `extract`, `build`, `inject` e `restore` terminam com código 0 e o `Compare-Object` não imprime nada. Se o terminal mostrar `?` no lugar do japonês, é a fonte do terminal: o que vale é a pasta existir e o comando terminar certo. No `vnforge.json`, `game.path` guarda o caminho com os mesmos caracteres.

Se falhar: issue `bug` + `área: core` (ou `área: cli`), dizendo em qual disco e em que comando; anexe o log e o caminho exato.

3.2. Mova o jogo do projeto de uma pasta com japonês para outra:

```powershell
$raiz = 'H:\VNF-Teste-日本語'   # depois repita com W:\VNF-Teste-日本語
Copy-Item -LiteralPath (Join-Path $raiz 'ゲーム テスト') -Destination (Join-Path $raiz 'ゲーム 移動') -Recurse
Set-Location (Join-Path $raiz 'プロジェクト')
vnf game move --game (Join-Path $raiz 'ゲーム 移動')
vnf status
```

Esperado: código 0, `status` igual ao de antes.

Se falhar: mesma issue do 3.1.

## 4. O jogo real

Use o jogo de verdade (a VN que será traduzida), no lugar onde ela vai morar, ou seja, no W:. Antes, tire a foto dele:

```powershell
$real = 'W:\<pasta do jogo real>'
Get-Hashes $real | Set-Content "$base-real.txt"
```

4.1. Crie o projeto, extraia e conte as linhas:

```powershell
New-Item -ItemType Directory -Force W:\Projetos | Out-Null
vnf init W:\Projetos\<nome> --game $real
Set-Location W:\Projetos\<nome>
vnf extract
vnf status
```

Esperado: a engine YU-RIS, o número de scripts e de linhas, sem erro. Com `--game` num volume que não é local, o aviso de disco aparece e a operação segue.

4.2. Traduza uma cena. A tradução precisa da chave do Claude configurada (`vnf config ai`):

```powershell
vnf translate --limit 30
vnf status
```

Esperado: as 30 linhas passam a "máquina", com o custo no fim. Anote a chave da cena traduzida (o prefixo do id da linha, como `yst00012`, o mesmo nome do arquivo em `text\`).

Se falhar: issue `bug` + `área: ia`, com o log do comando.

4.3. Injete e abra o jogo só com essa cena, como o app faz em "Testar no jogo":

```powershell
vnf test --only-script yst00012 --allow-warnings
```

Troque `yst00012` pela chave do passo 4.2. O comando confere a tradução, constrói, guarda o backup, injeta e abre o jogo. Responda `s` à confirmação.

Esperado: o jogo abre; ao chegar na cena traduzida, o texto aparece em português, com acento certo e sem caractere quebrado, e a caixa de texto não corta nenhuma linha. As outras cenas ficam em japonês.

Se o jogo não abre, ou abre com tela preta ou com o texto errado: feche o jogo e, antes de qualquer outra coisa, rode o passo 4.4. Depois abra issue `bug` + `no jogo` + `área: yuris`, com o log, a saída de `vnf status` e uma foto da tela.

4.4. Teste de acentos (opcional, mas rápido: ele isola a codificação):

```powershell
vnf test --accent-test
```

Esperado: a primeira fala do primeiro script vira `Teste de acentos: ação, coração, é, avô, não.`, sem caractere trocado.

4.5. Restaure o original e confira:

```powershell
vnf restore --yes
Compare-Object (Get-Hashes $real) (Get-Content "$base-real.txt")
```

Esperado: `restore` com código 0 e nenhuma saída do `Compare-Object`: todo arquivo do jogo, incluindo o `game.exe`, voltou ao hash da foto. Abra o jogo uma vez, sem VNForge, e confira que roda em japonês como antes.

Se falhar: issue `bug` + `área: core` + `no jogo`. É o defeito mais grave do roteiro, porque deixa o jogo do Alessandro alterado. Anexe a lista do `Compare-Object` e a pasta `backup\` do projeto, que guarda os originais.

## 5. No app

Abra `VNForge.App` (a pasta de saída do build ou o zip da pré-release). Os passos usam o projeto e o jogo do bloco 1 e o jogo real do bloco 4.

5.1. Biblioteca. Entre em "Biblioteca", clique em "Adicionar pasta" e escolha a pasta que contém `$base\jogo` (o app só lê a pasta).

Esperado: o jogo aparece como cartão, com a engine YU-RIS reconhecida. Rode também com a pasta com japonês (`H:\VNF-Teste-日本語`) e confira que o nome do jogo e o da pasta saem sem caractere quebrado. Abrir a Biblioteca não pode mudar arquivo nenhum do jogo.

Se falhar: issue `bug` + `área: app`, com captura da tela e o log de `%LOCALAPPDATA%\VNForge\logs\`.

5.2. Ligar a um projeto que já existe (#688). Clique no cartão do jogo sem projeto. Na gaveta, "Ligar a um projeto que já existe" e escolha `$base\projeto`.

Esperado: aparece o aviso "<jogo> ligado ao projeto de tradução." e o cartão passa a mostrar o projeto. Repita com um projeto cujo `game.path` aponta para outra pasta (por exemplo, o de `W:\VNF-Teste` antes do passo 2.4): o app troca a pasta do jogo do projeto para a da Biblioteca, com a mesma conferência do `vnf game move`. Quando a pasta do jogo do projeto é outra, o app pergunta ('Mudar a pasta e ligar'). Escolha o projeto de um jogo diferente: o app recusa com o motivo, sem gravar nada na Biblioteca nem no projeto.

Se falhar: issue `bug` + `área: app` (e `área: core` se o `vnf library link` pela CLI se comporta igual). Anexe o log e o `vnforge.json` do projeto.

5.3. Ficha sem projeto (#690 e #691). Escolha na Biblioteca um jogo sem projeto ligado e casado com o VNDB (tire a ligação com `vnf library link <jogo> --remove`, se preciso) e clique em "Abrir jogo".

Esperado: abre a Ficha do jogo, com a pílula neutra "Sem projeto" no cabeçalho, sinopse, gêneros e elenco com a descrição. Os retratos dos personagens aparecem nítidos, no tamanho da imagem de origem, sem ampliar e sem borrar. O cabeçalho oferece "Ligar a um projeto que já existe" e, para engine que o VNForge traduz, "Traduzir este jogo". Ligar o projeto por ali abre o projeto ligado, com a Ficha e as abas dele.

Se falhar: issue `bug` + `área: app`, com captura da tela. Se o visual está diferente do quadro aprovado, diga isso na issue.

5.4. Feche o app e rode `vnf library state` no terminal.

Esperado: os jogos aparecem com o estado certo (`original` para o que ficou restaurado, `traduzido` para o que não).

## Depois do roteiro

Registre o resultado na issue da pré-release: a data, o commit (`git rev-parse --short HEAD`), e uma linha por bloco com "passou" ou o número da issue aberta. Uma pré-release só segue com os blocos 2 e 4 passando.

Limpe as pastas de teste quando terminar (`$base`, `W:\VNF-Teste`, as pastas com japonês e o projeto do jogo real). Não apague a pasta do jogo real.
