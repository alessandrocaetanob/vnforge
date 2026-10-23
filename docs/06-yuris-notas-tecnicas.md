# 06 — Notas técnicas: YU-RIS

Resumo do que levantei lendo ferramentas existentes (links no fim). **Nada disso foi testado ainda contra o seu jogo**; o primeiro trabalho na sua máquina é confirmar cada item.

## Primeira coisa a verificar no seu jogo

Abra `ysbin.ypf` num editor hexadecimal (ou rode, no PowerShell, `Format-Hex ysbin.ypf | Select-Object -First 2`):

- Bytes 0–3: `59 50 46 00` (`YPF\0`)
- Bytes 4–7: versão em little-endian. Ex.: `F4 01 00 00` = 500

A versão define hash, tamanho de offset e tabela de nomes. Com ela já dá para saber em qual variante estamos.

## Arquivo YPF

Fontes citadas abaixo como `arquivo:linha`, sobre as cópias lidas em 2026-10-22 (as linhas podem se mover nos repositórios):

- `yuri.md` e `ypf.py`: [shimamura-sakura/yuri](https://github.com/shimamura-sakura/yuri), `yuri.md` e `yuri/fileformat/ypf.py`
- `GARbro`: [morkt/GARbro](https://github.com/morkt/GARbro), `ArcFormats/YuRis/ArcYPF.cs`
- `Xorboth`: [Xorboth/YPF-Manager](https://github.com/Xorboth/YPF-Manager), `YPF/YPFHeader.cs` e `YPF/YPFArchive.cs`
- `GARbro#452`: [issue 452 do GARbro](https://github.com/morkt/GARbro/issues/452), com versões de jogos reais

```
u8  magic[4]   = "YPF\0"
u32 version
u32 file_count
u32 header_size     // v300 ou mais: inclui o cabeçalho de 32 bytes; abaixo disso, só o índice
u8  padding[16]

Entry[file_count]   // ordenadas pelo hash do nome
    u32 name_hash
    u8  name_size   // gravado como ~(troca(tamanho)); na leitura, ~b e depois a troca
    u8  name[]      // cada byte gravado como ~(byte ^ K); K = 0 na maioria das versões
    u8  file_type   // 0 ybn, 1 bmp, 2 png, 3 jpg, 4 gif, 5 wav, 6 ogg, 7 psd, 8 ycg, 9 psb
    u8  compressed  // 1 = zlib
    u32 size_uncompressed
    u32 size_stored
    u32/u64 offset  // 64 bits nas versões com MurmurHash2 (limiar em discussão, abaixo)
    u32 data_hash   // hash dos bytes armazenados (comprimidos)
```

Layout geral: `yuri.md:440-457`, `ypf.py:31-37` e `:115-145`.

| Versão | Hash do nome | Hash dos dados | Offset |
|---|---|---|---|
| < 265 | Adler32 | Adler32 | 32 bits |
| 265 a 476 | CRC32 | Adler32 | 32 bits |
| 477 ou 479 em diante | MurmurHash2 (seed 0) | MurmurHash2 | 64 bits |

Fontes da tabela: `yuri.md:500-513`, `ypf.py:54-62` e `:65-80`, `Xorboth YPFHeader.cs:128-140` e `YPFArchive.cs:78,228-235`.

- **Ambiguidade do limiar do MurmurHash2 e do offset de 64 bits (a confirmar na #23)**: `Xorboth YPFHeader.cs:130` troca o algoritmo na versão 479 (`< 479` é CRC32 e Adler32); `yuri.md:511` e `ypf.py:55,69` trocam na 477, com o comentário `# real number ?` (o número real é desconhecido). `GARbro#452` mediu apenas: v474 com CRC32, Adler32 e offset de 32 bits; v479 e v490 com MurmurHash2 e offset de 64 bits. As versões 475 a 478 nunca foram medidas, e hash e offset podem mudar em versões diferentes. O gerador sintético trata hash do nome, hash dos dados e largura do offset como três opções independentes (por arquivo YPF), com padrão 479, para os testes do leitor cobrirem as duas leituras. Se o `ysbin.ypf` do jogo for 475 a 478, o próprio arquivo resolve a dúvida.
- **Tabela de troca do tamanho do nome**: pares `(3,72) (6,53) (9,11) (12,16) (13,19) (17,25) (21,27) (28,30) (32,35) (38,41) (44,47) (46,50)` (`yuri.md:468-478`, `ypf.py:39-41`, `GARbro ArcYPF.cs:530-534`). A v500 usa uma variante (`3↔10`, `17↔24`, `20↔46` no lugar de `3↔72`, `17↔25`, `46↔50`; `ypf.py:41`, `Xorboth YPFHeader.cs:116-118`). Quando cada tabela vale diverge: `ypf.py:78` usa a variante só na versão 500 exata; `Xorboth YPFHeader.cs:116` usa em 500 ou mais; `GARbro ArcYPF.cs:361-375` usa uma tabela reduzida abaixo da v256 (`SwapTable04`, sem `3↔72` e `6↔53`, linhas 535-538), outra de v300 a v405 (`SwapTable10`, só 7 pares, linhas 539-541) e a padrão no resto. O gerador oferece as quatro (`Standard`, `V500`, `Garbro04`, `Garbro10`), com o padrão do `ypf.py` (`V500` só na versão 500 exata). Estratégia de leitura: tentar as tabelas conhecidas e validar com o hash do nome.
- **Bytes do nome**: o gravado é `~(byte ^ K)`, com K = 0x00 por padrão, 0x40 na v290 e 0x36 na v500 (`ypf.py:42-44,79,141`; `Xorboth YPFArchive.cs:219`, `YPFHeader.cs:143-158`). O XOR efetivo de um byte gravado é `K ^ 0xFF`: 0xFF, 0xBF na v290 e 0xC9 na v500. `yuri.md:480-487` descreve só o XOR efetivo (padrão 0xFF); o GARbro grava XOR único com uma chave configurável (`ArcYPF.cs:254-255`) e a recupera lendo o byte na posição `len-4` contra `'.'` (`ArcYPF.cs:473`, supõe extensão de 3 letras). A API do gerador usa a leitura do YPF-Manager: `WithNameKey(K)` define o K, com o padrão por versão acima (v290 e v500 exatas, como em `ypf.py:79`; o YPF-Manager usa 500 ou mais, `YPFHeader.cs:150`).
- **`header_size`**: abaixo da v300 o campo não inclui os 32 bytes do cabeçalho; a partir da v300 inclui (`yuri.md:459-462`, `ypf.py:91,137`). GARbro e YPF-Manager gravam sempre 0x20 mais o índice e só usam o campo como limite na leitura (`GARbro ArcYPF.cs:133-136,264,320`, `Xorboth YPFArchive.cs:78,198`). O gerador segue o `ypf.py` por padrão e aceita forçar a outra semântica (`WithHeaderSizeIncludesHeader`). O início dos dados é sempre 0x20 mais o índice.
- **Hash do nome**: sobre os bytes do nome em claro, em Shift-JIS, sem converter caixa nem separador (`yuri.md:490`, `ypf.py:99`, `GARbro ArcYPF.cs:251`, `Xorboth YPFArchive.cs:71-74`). **Hash dos dados**: sobre os bytes armazenados, sem descomprimir (`yuri.md:491`, `ypf.py:105,134`, `Xorboth YPFArchive.cs:134,157`).
- **Ordem do índice**: pelo hash do nome crescente (`GARbro ArcYPF.cs:271`, `Xorboth YPFArchive.cs:83-84`; o `ypf.py` não reordena e respeita a ordem da lista). **Ordem dos dados**: na ordem do índice (`GARbro ArcYPF.cs:271-285`, `Xorboth YPFArchive.cs:84,90,124`, `ypf.py:142-145`). O gerador usa a ordem do índice por padrão e aceita a ordem de inserção (`WithDataOrder`).
- **Compressão**: só grava zlib quando o resultado é menor que o original (`ypf.py:131`, `Xorboth YPFArchive.cs:130`); o GARbro comprime todo arquivo `ybn` (`ArcYPF.cs:262,289`). O YPF-Manager não aceita arquivo de 0 byte (`YPFArchive.cs:113-115`). O nível é 9 em `ypf.py:19` e no GARbro (`ArcYPF.cs:293`). O gerador usa o zlib do .NET (nível Optimal), cujos bytes não servem de vetor de ouro.
- **Dados duplicados**: o YPF-Manager aponta entradas com o mesmo hash e tamanho para um só offset (`YPFArchive.cs:136-141,159-163`). O gerador faz isso com `WithSharedDuplicateData`.
- **Separador de pasta**: os nomes reais usam barra invertida (`yuri.md:616`, `split('\\')`). Os testes usam `\` e mantêm um caso com `/`.
- **Tipos de arquivo**: o valor de `file_type` varia por versão (`GARbro ArcYPF.cs:340-342,481-483`: na v247 o `avi` é 5; `ycg` e `psb` aparecem na tabela da v500).
- **Plano de reescrita**: manter versão, chave, tabela e ordem do arquivo original; copiar entradas intactas sem tocar; só as alteradas são recomprimidas e ganham hash novo, com o algoritmo detectado no próprio arquivo. Como os nomes não mudam, os hashes de nome e a ordem continuam válidos.
- Há relatos de executáveis que leem arquivos soltos fora do YPF ("免封包"), mas isso varia; o caminho seguro é reempacotar.

### Como o leitor detecta os parâmetros (#27)

`src/VNForge.Engines.YuRis/Ypf/` (`YpfArchiveFormat`, `YpfIndexReader`). Nada depende só da versão: a versão dá a primeira tentativa, e o que vale é o que confere com o índice.

- **Versão**: o leitor tenta ler qualquer versão, porque a detecção é pelo conteúdo. `UnknownVersionException` só sai quando nenhuma combinação leu o índice e a versão está fora de 200 a 599 (`YpfVersionDefaults.MinKnownVersion` e `MaxKnownVersion`: o yuri.md trata as 2xx, o GARbro cita a 247 e as medidas chegam a 500; o resto é margem). Dentro dessa faixa o erro é `InvalidFileException`.
- **Índice**: o leitor tenta as combinações de largura do offset (32 e 64 bits), tabela de troca (as quatro) e algoritmo do hash do nome (os três), a da versão primeiro, e aceita a que lê todas as entradas com o hash do nome conferindo, o `header_size` consistente e os dados dentro do arquivo. Se nenhuma serve, o erro descreve a tentativa que foi mais longe (entrada, posição, o que esperava e o que achou).
- **Leitura do índice**: o `header_size` dá o tamanho a ler nas duas semânticas (só o índice, ou o índice mais 32). Se ele estiver errado, o índice é lido de novo com o teto pela contagem de arquivos (282 bytes por entrada). O índice vai inteiro para a memória, até 128 MiB; acima disso o leitor recusa o arquivo e a mensagem cita o limite.
- **Chave do nome**, por entrada: a dedução do GARbro (byte `len-4` contra `.`), o XOR da entrada anterior, os XOR conhecidos (0xFF, 0xBF, 0xC9) e, por último, os 256 valores, sempre validando pelo hash do nome. Cada `YpfEntry` guarda o XOR que usou.
- **Nomes repetidos**: detectados pelos bytes do nome com `\` e `/` equivalentes (o byte 0x5C como segundo byte de um caractere Shift-JIS não conta) e, depois, pelo caminho decodificado.
- **`header_size`**: vale se for o tamanho do índice ou o tamanho do índice mais 32; a semântica fica em `YpfParameters.HeaderSizeIncludesHeader`. Qualquer outro valor é erro.
- **Hash dos dados**: comparado com os três algoritmos sobre os bytes armazenados das menores entradas (até 16 entradas de até 4 MiB, 8 MiB no total). Vence o que confere em mais entradas. Se nenhuma confere, ou a amostra empata (por exemplo, só entradas vazias, cujo hash é 0 em CRC32 e MurmurHash2), vale o padrão da versão e `YpfParameters.DataHashVerified` fica falso; o leitor não rejeita o arquivo por isso. `OpenEntry` confere cada `data_hash` só quando `DataHashVerified` é verdadeiro (sem algoritmo confirmado, comparar com o padrão da versão recusaria dado íntegro) e lança `InvalidDataException`, com o valor gravado e o calculado, se divergir. Confere também o tamanho descomprimido contra `size_uncompressed`, nem a mais nem a menos. Bytes de preenchimento depois do fluxo zlib completo dentro de `size_stored` são tolerados, como no GARbro (`ArcYPF.cs:155-157`), no `ypf.py` (`zlib.decompress`) e no YPF-Manager (`Util.cs:42-59`); fluxo truncado e Adler-32 errado são erro.
- **Offset de entrada vazia**: o limite inferior (os dados começam depois do índice) não vale para entrada sem bytes armazenados.
- **O que a gravação (#29) copia do original**: `YpfHeader.Padding` (os 16 bytes de 0x10 a 0x1F, normalmente zeros), `YpfParameters.Gaps` (trechos da área de dados que nenhuma entrada referencia, antes do fim do último dado) e `YpfParameters.Trailer` (bytes depois do último dado). Cada trecho vem com offset e tamanho no arquivo de origem; a #29 os copia do fluxo de origem, nas mesmas posições relativas, para a ida e volta ficar idêntica num YPF real que não seja o zerado dos escritores conhecidos.
- **Escritor (`YpfArchive.WriteTo`, #29)**: o cabeçalho (com o padding), o índice cru, as lacunas e o trailer saem do original; só `size_uncompressed`, `size_stored`, `data_hash` (nas entradas trocadas) e `offset` (nas que mudaram de lugar) são regravados. Os bytes armazenados de entrada não trocada são copiados crus do fluxo de origem, na mesma ordem. O bloco de uma entrada trocada ocupa o lugar do antigo; se outra entrada ainda usa o bloco antigo (dados compartilhados), o bloco antigo fica e o novo vem logo depois. Entrada vazia que ganha conteúdo recebe o bloco no fim dos dados, antes do trailer. Offset de entrada vazia sem troca acompanha o segmento onde cai. A entrada trocada mantém o flag `compressed` do original e é comprimida com `ZLibStream` em nível `Optimal`; conteúdo vazio comprimido grava o zlib fixo `78 9C 03 00 00 00 00 01`, porque o `ZLibStream` não escreve nada para zero bytes (medido). O hash novo usa `YpfParameters.DataHash`.
- **Escritor sem hash confirmado**: com `DataHashVerified` falso o algoritmo do hash dos dados é desconhecido, e o escritor recusa qualquer troca (`NotSupportedException`, mensagem em pt-BR citando a entrada) em vez de gravar o hash padrão da versão. O pacote sem trocas continua sendo copiado. Se aparecer um jogo real nesse caso, a saída é confirmar o algoritmo no jogo (#23) e acrescentar a escolha explícita do algoritmo; gravar um palpite em silêncio fica de fora.
- **Escritor com blocos sobrepostos**: se dois blocos de dados se sobrepõem sem ter o mesmo offset e tamanho, a área de dados não se divide em segmentos. Sem troca, o arquivo é copiado inteiro; com troca, o escritor recusa (`NotSupportedException`).
- **Tabela de troca ambígua**: se os nomes do arquivo não têm tamanho que separe duas tabelas, vale a primeira da ordem de tentativa. O `name_size` gravado é guardado cru em cada entrada, então a gravação não depende dessa escolha.
- `YpfArchive.Parameters` guarda o que foi detectado (versão, algoritmos, largura do offset, tabela, XOR do nome, semântica do `header_size`) para a gravação (#29, `YpfWriter`) usar os mesmos parâmetros; cada `YpfEntry` guarda os campos crus do índice.

### O que confirmar no jogo (#23)

O gerador de testes (`tests/VNForge.Engines.YuRis.Tests/Ypf/Synthetic/SyntheticYpfBuilder.cs`, #26) segue as leituras acima; nenhuma foi conferida contra o `ysbin.ypf` do jogo. Se o jogo divergir, corrija o gerador e este trecho no mesmo PR.

1. **Versão e limiares**: a versão do `ysbin.ypf` e, se ela cair entre 475 e 478, qual hash e qual largura de offset o arquivo usa (a leitura de 477 contra a de 479).
2. **Chave efetiva do nome**: o XOR efetivo de `ysbin.ypf` (0xFF, 0xBF ou 0xC9 nas fontes) e, daí, o K (0x00, 0x40 ou 0x36); conferir lendo o byte `len-4` contra `'.'`.
3. **Tabela de troca**: qual das quatro tabelas valida o hash do nome do jogo, e se a variante da v500 vale na 500 exata ou de 500 em diante.
4. **`header_size`**: o valor gravado no arquivo do jogo (com ou sem os 32 bytes) e se o executável aceita a outra semântica.
5. **Ordem dos dados**: se os dados seguem a ordem do índice ou outra, e se há padding ou lacunas entre entradas.
6. **Dados duplicados**: se o arquivo original compartilha offsets entre entradas idênticas, e se o executável aceita os dois jeitos.
7. **Separador e caixa**: barra invertida contra normal, maiúsculas e minúsculas nos nomes, e se o hash do nome depende da caixa.
8. **Compressão e entradas de 0 byte**: quais tipos o jogo comprime, se existem entradas de 0 byte e se o zlib do original cabe no nosso nível.
9. **`file_type` 8 e 9** (`ycg` e `psb`) e o resto da tabela de tipos: os valores usados na versão do jogo.
10. **Tolerância do executável**: se o jogo aceita `header_size` ou hash inconsistentes (as mutações de `SyntheticYpfMutations` servem de teste).

## Lista de comandos (YSCM) e lista de scripts (YSTL)

Fontes citadas como `arquivo:linha`, sobre as cópias lidas em 2026-10-22 (as linhas podem se mover nos repositórios):

- `yuri.md`, `yscm.py`, `ystl.py` e `common.py`: [shimamura-sakura/yuri](https://github.com/shimamura-sakura/yuri), `yuri.md` e `yuri/fileformat/`
- `VNTextPatch Notes.txt` e `YurisCommandList.cs`: [arcusmaximus/VNTranslationTools](https://github.com/arcusmaximus/VNTranslationTools), `VNTextPatch.Shared/Scripts/Yuris/`
- `RxYuris`: [ZQF-ReVN/RxYuris](https://github.com/ZQF-ReVN/RxYuris), só confirma que existem leitores de YSCM e YSTL nas versões 2.xx a 5.xx; o código não foi lido

### `ysc.ybn` (YSCM)

```
u8  magic[4]   = "YSCM"
u32 version
u32 cmd_count
u32 padding         // 0
Command[cmd_count]  // o id do comando é a posição na lista, 0 em diante
    sz  name        // Shift-JIS, terminado em 0
    u8  param_count
    Parameter[param_count]
        sz name
        u8 type     // yscm.py lê como Typ; o yuri.md chama de desconhecido
        u8 flags    // desconhecido nas duas fontes
sz  error_msg[37]   // ou 0x90, ver abaixo
u8  table[256]
```

Layout: `yuri.md:624-638`, `yscm.py:4,14-18,26-30,45-58`, `VNTextPatch Notes.txt:2-17`, `YurisCommandList.cs:19-30,48-63`. O opcode que o YSTB grava em cada instrução é esse id (`yuri.md:849`, `YurisScenarioScript.cs:529-533` busca `WORD`, `_`, `RETURNCODE`, `GOSUB` e `CG` por nome).

Onde as fontes divergem, e o que o leitor (`src/VNForge.Engines.YuRis/Yscm/`) faz:

- **Mensagens de erro**: 37 no yuri (`NErrStr`, `common.py:9`, `yuri.md:636`) e `0x90` (144) no `Notes.txt:16`. O `YurisCommandList.cs` para de ler depois dos comandos (`:26-29`) e não resolve a dúvida; o `yscm.py:52-54` lê as 37 strings, a tabela de 256 bytes e exige o fim do arquivo. O leitor valida a cauda: depois do último comando lê strings terminadas em 0 até restarem exatamente 256 bytes, exige que o arquivo termine ali e que a quantidade de strings seja 37 ou `0x90` (`YscmFile.ErrorMessageCount`). Qualquer outra contagem é erro, com o valor encontrado na mensagem, porque a causa provável é uma contagem de comandos ou de parâmetros errada, que faria o leitor tomar as mensagens por parâmetros. Se o jogo tiver outra quantidade, este trecho e o leitor mudam no mesmo PR.
- **Padding**: o `yscm.py:50` exige 0; o VNTextPatch o ignora (`YurisCommandList.cs:25`). O leitor ignora.
- **Quantidade de comandos**: o VNTextPatch converte para `byte` com `checked` (`YurisCommandList.cs:24`), então 256 comandos quebram; o id é `u8` (`yuri.md:849`), então 256 cabem. O leitor aceita até 256 e recusa acima disso, com a contagem na mensagem.
- **Nome repetido**: o VNTextPatch falha na leitura (`Dictionary.Add`, `YurisCommandList.cs:51`) para comando repetido e guarda o primeiro parâmetro repetido (`TryAdd`, `:58`); o yuri guarda o último (`setattr`, `yscm.py:56-57`). O leitor guarda tudo e só falha ao procurar o opcode de um nome repetido (`YscmFile.TryGetOpcode`), com os ids na mensagem.
- **Versão**: o yuri aceita 200 a 599 (`common.py:8`). O leitor usa a mesma faixa (`UnknownVersionException` fora dela); o VNTextPatch lê a versão e não a usa (`YurisCommandList.cs:23`).

`YscmFile.GetOpcodes()` devolve `WORD`, `_`, `RETURNCODE`, `GOSUB` e `CG`; se faltar algum, a `InvalidFileException` nomeia todos os que faltam. A comparação do nome é exata, com caixa.

### `yst_list.ybn` (YSTL)

```
u8  magic[4]   = "YSTL"
u32 version
u32 scr_count
Script[scr_count]
    u32 index       // 0, 1, 2... sequencial
    u32 path_size
    u8  path[path_size]  // caminho original do .yst, Shift-JIS (ex.: userscript\01_prologo.yst)
    u64 mod_time    // FILETIME do Windows
    i32 var_count   // negativo = não há YSTB para este script
    u32 lbl_count
    u32 txt_count   // só a partir de certa versão, ver abaixo
```

Layout: `yuri.md:773-792`, `ystl.py:3-9,21-25,43-51`, `VNTextPatch Notes.txt:79-91`. O YSTB do script `index` é `ystNNNNN.ybn` (`yuri.md:423`); o leitor assume cinco dígitos (`yst00012.ybn`), o que o `yuri.md` não confirma além do `XXXXX`. `var_count` negativo marca o `global.yst` e scripts vazios, que o compilador pula (`yuri.md:789-792`).

Onde as fontes divergem, e o que o leitor (`src/VNForge.Engines.YuRis/Ystl/`) faz:

- **Limiar do `txt_count`**: o `ystl.py:8` troca na 474 (`VScrNew`); o `yuri.md:783` na 477; o `Notes.txt:91` não menciona versão e grava o campo sempre. Nenhuma das três mediu um jogo na faixa 474 a 476. O leitor tenta primeiro o layout da versão (com o campo a partir da 474, sem ele abaixo) e, se esse não consome o arquivo inteiro com índices sequenciais, tenta o outro. `YstlFile.HasTextCount` diz qual valeu. Se os dois falham, a mensagem é a da tentativa que foi mais longe.
- **Sinal dos contadores**: o `ystl.py:6-7` lê `var_count`, `lbl_count` e `txt_count` como inteiros com sinal; o `yuri.md:781-784` só dá sinal a `var_count`. O leitor segue o `yuri.md`.
- **Índice**: o `ystl.py:24` exige `index == posição`. O leitor também exige, e a mensagem dá a posição e o índice encontrado.
- **Fim do arquivo**: o `ystl.py:50` exige que o arquivo acabe no último script. O leitor recusa bytes sobrando.
- **Caminho**: o `ystl.py:25` decodifica em CP932, como o leitor. Os nomes reais usam barra invertida; `YstlScript.ReadableName` aceita `\` e `/` e tira a pasta e a extensão.

### O que confirmar no jogo (#35)

Os leitores foram escritos só a partir das fontes acima e de arquivos sintéticos (`tests/VNForge.Engines.YuRis.Tests/Yscm/Synthetic/` e `Ystl/Synthetic/`); nenhum foi conferido contra o `ysc.ybn` ou o `yst_list.ybn` do jogo.

1. **Versão** gravada em `ysc.ybn` e em `yst_list.ybn` (iguais à do `ysbin.ypf`?), e, se cair em 474 a 476, se os registros do YSTL têm o `txt_count`.
2. **Cauda do YSCM**: o leitor exige 37 ou 0x90 mensagens de erro (`YscmFile.ErrorMessageCount`) seguidas da tabela de 256 bytes até o fim do arquivo. Conferir qual quantidade o `ysc.ybn` do jogo tem; outra quantidade, ou uma tabela de outro tamanho, é erro do leitor a corrigir.
3. **Os cinco comandos**: se `WORD`, `_`, `RETURNCODE`, `GOSUB` e `CG` existem com esses nomes e uma vez só, e quais ids têm. O `GetOpcodes()` falha com o nome do que faltar.
4. **Padding do YSCM** e o byte `type`/`flags` dos parâmetros: valores reais, para trocar os nomes `TypeCode` e `Flags` por algo com sentido.
5. **YSTL**: se `index` é sempre sequencial, se o nome do YSTB tem cinco dígitos e quais scripts têm `var_count` negativo.

## Scripts YSTB (`yst*.ybn`), versão ≥ 300

Fontes citadas como `arquivo:linha`, sobre as cópias lidas em 2026-10-22 (as linhas podem se mover nos repositórios):

- `yuri.md` e `ystb.py`: [shimamura-sakura/yuri](https://github.com/shimamura-sakura/yuri), `yuri.md` e `yuri/fileformat/ystb.py`
- `VNTextPatch Notes.txt` e `YurisScenarioScript.cs`: [arcusmaximus/VNTranslationTools](https://github.com/arcusmaximus/VNTranslationTools), `VNTextPatch.Shared/Scripts/Yuris/`
- `GARbro`: [morkt/GARbro](https://github.com/morkt/GARbro), `ArcFormats/YuRis/ArcYPF.cs` (`DecryptYstb`)

```
u8  magic[4] = "YSTB"
u32 version
u32 num_instructions
u32 instructions_size   (= num_instructions * 4)
u32 args_size           (múltiplo de 12)
u32 resources_size
u32 line_numbers_size   (= num_instructions * 4)
u32 padding

Instruction[n]:  u8 opcode, u8 num_args, u16 extra
Arg[m]:          u16 id, u8 type, u8 assign_op, u32 size, u32 offset   // offset dentro de resources
u8   resources[]
u32  line_numbers[n]
```

Layout: `yuri.md:862-886`, `ystb.py:5,12,129-136`, `VNTextPatch Notes.txt:96-119`, `YurisScenarioScript.cs:51-70`. O `extra` é o `gosub_npar` do yuri (`yuri.md:876`); o `Notes.txt:108-109` o descreve como um byte desconhecido e um byte de padding.

- Cada uma das 4 seções é XOR com uma chave de 4 bytes, recomeçando no início de cada seção (`yuri.md:803-832`, `ystb.py:141-144`, `YurisScenarioScript.cs:537-549`, `GARbro ArcYPF.cs:408-424`).
- **Descobrir a chave**: o primeiro argumento quase sempre tem `offset = 0`, então os bytes 8–11 da seção de argumentos, ainda cifrados, *são* a chave (`YurisScenarioScript.cs:72-73` lê exatamente esses bytes). Chaves conhecidas para validar: `0xD36FAC96` (≥ 290), `0x07B4024A` (versões 2xx) (`ystb.py:9-10,113`). Jogos comerciais podem ter outra.
- **Notação da chave**: o yuri escreve a chave em big-endian (`0xD36FAC96` cifra com os bytes `D3 6F AC 96` nessa ordem, `yuri.md:809-813`, `ystb.py:115`). O VNTextPatch e o GARbro leem os mesmos 4 bytes como `uint` little-endian (`0x96AC6FD3`, `YurisScenarioScript.cs:73`). Este projeto usa a notação do yuri.
- **Opcodes não são fixos**: vêm de `ysc.ybn` (YSCM), que lista os comandos na ordem do id (leitor: seção acima). Procuramos `WORD` (texto), `_` (expressão), `RETURNCODE`, `GOSUB`, `CG`.
- **Texto (`WORD`)**: 1 argumento, bytes crus em Shift-JIS, sem aspas. Linhas seguidas de `WORD` formam uma mensagem; dá para pôr a tradução inteira no primeiro e esvaziar os demais.
- **Códigos de controle dentro do texto**: `EF F0` quebra de linha, `EF F2` nova página, `EF F3` esperar clique, `EF F5` desconhecido. Viram `\n`, `\p`, `\c`, `\u` no nosso JSON. Outro `EF xx` vira `{EF:xx}`. A varredura respeita os limites de caractere do CP932: um `EF` que é segundo byte de um caractere de dois bytes (ex.: `88 EF`) não é código de controle, assim como um segundo byte `5C` ou `7B` (ex.: `ソ` = `83 5C`) não vira barra ou chave. Byte que o Shift-JIS não decodifica, ou que decodifica para um par diferente na volta (o CP932 tem duplicatas, como `87 90` e `81 E0`), vira `{RAW:hex}`, para a ida e volta gerar os mesmos bytes. A implementação é `ControlMarkers`, em `VNForge.Engines.YuRis.Text`.
- **Falante**: no E-ris costuma vir como `名前「fala」` dentro do próprio `WORD`.
- **Escolhas e nomes**: chamadas `GOSUB` para `ES.SEL.SET` e `ES.CHAR.NAME`, com strings em expressão.
- **Strings em expressão**: `4D <u16 len> "texto"`. O YU-RIS não tem escape de aspas, mas só confere se o primeiro e o último caractere são iguais, então dá para delimitar com crase: `` `texto com "aspas" e 'apóstrofo'` ``.
- **Plano de reescrita**: anexar os textos novos no fim da seção de recursos e atualizar `size`/`offset` só dos argumentos alterados. Os outros argumentos (inclusive os de `RETURNCODE`, cujo offset nem sempre aponta para recursos) não são tocados.
- `yst_list.ybn` (YSTL) dá o caminho original de cada script (ex.: `userscript\01_prologo.yst`), útil para nomes legíveis (leitor: seção acima).
- Versões < 300 têm outro layout (instruções de tamanho variável). Só vamos tratar se o seu jogo precisar.

### Como o leitor trabalha (#36)

`src/VNForge.Engines.YuRis/Ystb/` (`YstbReader`, `YstbFile`). Recebe o `YscmFile` do mesmo jogo, porque o opcode só tem nome pelo YSCM.

- **Versão**: abaixo de 300, `InvalidFileException` dizendo que o layout da versão não é suportado (`ystb.py:116` trata o layout antigo em outro ramo, que não implementamos). Fora de 200 a 599, `UnknownVersionException` (`ystb.py:112`, `common.py:8`).
- **Cabeçalho**: exige `instructions_size == n * 4`, `line_numbers_size == n * 4`, `args_size % 12 == 0` (`ystb.py:130-131`) e que a soma `32 + as quatro seções` seja exatamente o tamanho do arquivo, como o VNTextPatch (`YurisScenarioScript.cs:57-58,69-70`) e o GARbro (`ArcYPF.cs:397-407`, que não decifra se não fecha). As contas são feitas em 64 bits e conferidas contra o tamanho do arquivo antes de qualquer alocação, então um cabeçalho corrompido não pede memória. O `ystb.py` não confere o fim do arquivo; o leitor confere. O arquivo inteiro vai para a memória, até 32 MiB (`YbnReader.MaxFileBytes`); as instruções criadas são proporcionais ao tamanho real do arquivo, nunca ao que o cabeçalho declara.
- **Padding** (bytes 28 a 31): o `ystb.py:132` exige 0 e o VNTextPatch o ignora (`:63`). O leitor aceita qualquer valor e o guarda (`YstbFile.Padding`) para a gravação (E2.7) copiar.
- **Chave**: tenta, nesta ordem, as conhecidas `0xD36FAC96` e `0x07B4024A` (`YstbKeyOrigin.Known`, ou `Derived` se coincidem com a derivada) e depois a derivada dos bytes 8 a 11 da seção de argumentos (`Derived`). As conhecidas vêm antes porque uma derivada errada, quando o primeiro `offset` muda só alguns bytes da chave (por exemplo só os bytes 2 e 3), decifra quase tudo e passa nas conferências se o script não tem argumento conferido que pegue o desvio, enquanto uma conhecida errada decifra lixo e não passa. Limite: se o jogo usa uma chave fora da lista e o primeiro argumento tem `offset` assim, a derivada vale mesmo errada quando passa nas conferências, e os campos que elas não conferem (`extra`, `type`, e o `assign_op` dentro de 0 a 8) podem sair trocados sem erro. Sem argumentos (`args_size == 0`) não há de onde derivar: tenta primeiro tudo em claro (chave 0, `None`, que é o que o VNTextPatch faz com `key == 0`, `:72-75,539-540`) e depois as conhecidas. Uma chave só vale se o script inteiro decifra de forma consistente. Se nenhuma serve, o erro lista as chaves tentadas e dá o problema da tentativa que foi mais longe. Descobrir uma chave nova por tentativa (os números de linha, `yuri.md:931-980`) fica fora do leitor.
- **Consistência por chave**: o opcode de toda instrução existe no YSCM; a soma de `num_args` das instruções é exatamente o número de argumentos da seção; `assign_op` vai de 0 a 8 (o `AOp` do `ystb.py:13-26,46` recusa outro valor); e todo argumento confere `offset + size <= tamanho dos recursos`, como o `assert len(dat[off:off+siz]) == siz` do `ystb.py:43`, com as exceções abaixo.
- **Offset e tamanho dos argumentos**: o `ystb.py` só lê o trecho dos recursos (`RArg.read` com `dat`, `ystb.py:39-46`) nos argumentos que passam `dat`; o leitor confere o mesmo conjunto (`YstbReader.RangeRules`). As exceções, onde `size` e `offset` não apontam para recursos: o único argumento do `RETURNCODE` (`ystb.py:78-80`; o `size` é o valor devolvido, `yuri.md:999`), o 2º e o 3º argumento de `IF` e `ELSE` com 3 argumentos (`ystb.py:86-87`; `size` é a posição de código do `ELSE` ou `LOOPEND` e `offset` o offset da expressão, `yuri.md:991-996`) e o 2º argumento de `LOOP` (`ystb.py:93`). Todo o resto cai no ramo padrão (`ystb.py:97`) e é conferido, `WORD` (`ystb.py:94-96`), `G_xxx` e `LET` inclusive: o `yuri.md:997` lista esses dois comandos, mas o item está vazio na fonte e não dá base a uma regra própria. `IF`, `ELSE` e `LOOP` são achados pelo nome no YSCM (`TryGetOpcode`). Tamanho 0 vale com qualquer `offset`, porque a fatia vazia do yuri tem tamanho 0. As conferências do `ELSE` sem 3 argumentos (`na == 0`, `ystb.py:89`), do `LOOP` (`na == 2`, `:92`) e do `WORD` (`na == 1`, `:95`) o leitor não repete. `YstbFile.GetResource(argumento)` aplica o mesmo critério e falha, com os limites na mensagem, se o trecho sai dos recursos. `type` é guardado cru (o yuri usa só os dois bits baixos, `ystb.py:46`).
- **`extra`**: guardado sem interpretar, porque alguns jogos calculam o `gosub_npar` de outro jeito (`yuri.md:888-928`).
- **O que a E2.7 recebe**: `YstbFile.Key` (a efetiva), `Padding`, `ArgumentBytes` e `Resources` (as seções em claro), `Instructions` com `FirstArgumentIndex` e os campos crus de cada argumento. A gravação recifra cada seção com a mesma chave e copia o que não mudou. Os números de linha vêm em cada instrução.
- **Onde as fontes divergem**: (1) o limiar do layout novo: o yuri usa 300 (`ystb.py:116`, `yuri.md:796-799`); o GARbro só trata como layout novo as versões 300, 408, 410, 412, 451 e de 459 em diante (`ArcYPF.cs:386`: `0x12C`, `0x198`, `0x19A`, `0x19C`, `0x1C3` e `>= 0x1CB`), e o resto como o antigo; o leitor segue o yuri e, se um jogo de 301 a 458 não fechar o cabeçalho, o erro diz isso. (2) o padding: o `ystb.py` exige 0, o VNTextPatch ignora. (3) a chave: o yuri aceita a derivada ou uma informada (`ystb.py:113`), o VNTextPatch usa só a derivada (`:72-73`) e o GARbro usa uma chave por jogo guardada no esquema (`ArcYPF.cs:145,159`).

### O que confirmar no jogo (#36)

O leitor foi escrito a partir das fontes acima e de arquivos sintéticos (`tests/VNForge.Engines.YuRis.Tests/Ystb/Synthetic/`, com um vetor de ouro montado à mão em `YstbReaderTests`); nenhum `yst*.ybn` real foi lido.

1. **Versão** dos `yst*.ybn` (igual à do `ysbin.ypf`?) e se ela é 300 ou mais; abaixo disso o leitor recusa.
2. **Chave**: a efetiva do jogo e se ela coincide com a derivada dos bytes 8 a 11 (primeiro argumento com `offset = 0`).
3. **Padding** do cabeçalho e **`extra`** das instruções `GOSUB` e `RETURN`: valores reais, e se o jogo usa o cálculo alternativo do `gosub_npar`.
4. **`assign_op` de 0 a 8** em todos os argumentos, e **argumentos conferidos sempre dentro dos recursos**: se algum script real quebrar uma dessas duas conferências, a regra de consistência do leitor muda no mesmo PR.
5. **Scripts sem argumentos** (por exemplo, só um `RETURN`): se estão cifrados e com qual chave.

## Detecção da pasta do jogo (E2.8)

`YuRisEngine.Detect` (`src/VNForge.Engines.YuRis/YuRisDetector.cs`) só lê. Procura `*.ypf` na pasta do jogo e na subpasta `pac` (sem diferenciar maiúsculas) e reconhece o jogo quando algum arquivo abre com a assinatura `YPF\0`. No pacote de scripts (`ysbin.ypf` primeiro; vale o primeiro que tenha algum `yst<dígitos>.ybn`, o `ysc.ybn` sozinho não basta) lê o `ysc.ybn` e, entre os 8 primeiros `yst<dígitos>.ybn`, o primeiro que o `YstbReader` decifra: dele saem a versão do YSTB e a chave XOR. O que não der para ler (índice corrompido, `ysc.ybn` ausente, YSTB abaixo da 300, entrada de mais de 32 MiB) não derruba a detecção: vira o detalhe `warnings`, e a versão do YSTB sai do cabeçalho do primeiro script que tenha a assinatura. Pasta inexistente, vazia ou sem YPF devolve `EngineDetection.NotDetected`; se houve falha ao listar a pasta ou ao abrir um `.ypf` (por exemplo, bloqueado por outro programa), o resultado não reconhecido traz só o detalhe `warnings`. Passando de 64 arquivos `.ypf`, só os 64 primeiros são examinados e um aviso diz isso.

Detalhes devolvidos (chaves em inglês e camelCase, parte do contrato do `--json`): `ypf:<caminho>` (versão e contagem de cada pacote), `scriptsArchive`, `ypfVersion`, `scripts`, `ystbVersion`, `scriptKey`, `scriptKeyOrigin` e `warnings`. A codificação do texto ainda não é detectada. `vnf detect --game <dir>` mostra esse diagnóstico com rótulos em português (ou JSON com `--json`, que traz as chaves como estão).

## Acentuação: o problema central

Shift-JIS não tem `á à â ã é ê í ó ô õ ú ç` (nem maiúsculas). Também faltam `« » ª º`. Já existem em Shift-JIS: `“ ” ‘ ’ … ― °` e todo o ASCII.

Opções, da mais simples para a melhor:

1. **`fold`**: tirar acentos. Funciona em qualquer jogo, fica feio.
2. **`remap`**: as katakanas de meia largura (`0xA1`–`0xDF`) ocupam **1 byte** em Shift-JIS e quase nunca aparecem em VN. Mapeamos `á→ｱ`, `ç→ｲ` etc. e trocamos os glifos numa fonte embutida/instalada. Truque clássico de fan-translation.
3. **`cp1252`**: o executável do YU-RIS tem uma tabela de 256 bytes que marca quais bytes iniciam caractere duplo (lead bytes do Shift-JIS: `0x81–0x9F`, `0xE0–0xFC`). Ferramentas chinesas trocam essa tabela para GBK e usam um DLL que muda o `lfCharSet` em `CreateFontIndirectA`. Para português: zerar a tabela (todo byte vira caractere simples) e forçar `ANSI_CHARSET` na fonte. Resultado: acentos nativos com qualquer fonte do Windows. Pontos de atenção:
   - o byte `0xEF` (ï) colide com o prefixo de código de controle; o QA proíbe `ï`
   - a troca de charset pode exigir um DLL pequeno de hook (C++/Detours) ou patch direto nas chamadas de fonte; precisa ser investigado no seu executável
   - nomes de arquivo e strings internas continuam Shift-JIS; só o texto exibido muda

Plano: começar com `fold` para validar a ida e volta (M1), investigar `cp1252` no seu jogo (E5) e manter `remap` como alternativa.

## Referências

- [GARbro — ArcYPF.cs](https://github.com/morkt/GARbro/blob/master/ArcFormats/YuRis/ArcYPF.cs): leitura/escrita de YPF, decifragem de YSTB
- [Xorboth/YPF-Manager](https://github.com/Xorboth/YPF-Manager): reempacotador YPF em C#, tabelas por versão, MurmurHash2
- [arcusmaximus/VNTranslationTools](https://github.com/arcusmaximus/VNTranslationTools) (MIT): `VNTextPatch.Shared/Scripts/Yuris`, documentação dos formatos (Notes.txt), extração e reinjeção de texto, códigos de controle
- [shimamura-sakura/yuri](https://github.com/shimamura-sakura/yuri): decompilador/compilador e o documento `yuri.md` com a análise mais completa da engine
- [jyxjyx1234/YURIS_TOOLS](https://github.com/jyxjyx1234/YURIS_TOOLS): fluxo de tradução chinês, patch de lead bytes (GBK.py) e DLL de troca de fonte
- [fengberd/YuRISTools](https://github.com/fengberd/YuRISTools), [ZQF-ReVN/RxYuris](https://github.com/ZQF-ReVN/RxYuris): mais implementações de referência

Antes de copiar qualquer trecho de código, conferir a licença de cada repositório. Usar como referência de formato não tem problema; código copiado precisa ser compatível com a licença que escolhermos.
