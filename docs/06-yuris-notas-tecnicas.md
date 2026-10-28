# 06 — Notas técnicas: YU-RIS

Resumo do que levantei lendo ferramentas existentes (links no fim). O que já foi conferido no jogo de referência está em "Resultado no jogo de referência (M1, #88)" e "Testes da V2 no jogo de referência"; o resto continua a confirmar.

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
- **Falante**: no E-ris costuma vir como `名前「fala」` dentro do próprio `WORD`. Na volta o injetor monta `nome + abertura + tradução + fechamento`, e `nome` e as aspas dependem da estratégia de codificação (#380):
  - **Nome**: o Core (`SpeakerText.Resolve`) entrega a tradução de `characters.json`; sem cadastro vale o nome original, com a largura normalizada (NFKC, `？？？` vira `???`) só quando a estratégia não grava o original. Nome japonês sem cadastro na `cp1252` não tem como ser gravado: a Conferência acusa `falante-nao-codificavel` na linha ("O nome do personagem 犬井 não existe na codificação do jogo (cp1252). Cadastre a tradução do nome no Glossário > Personagens."), e o build recusa por ela, não pelo injetor.
  - **Delimitadores**: o jogo separa o nome da fala procurando os marcadores que o `yst00073` cadastra com `GOSUB es.CHAR.NAME.MARK.SET` (`「`, `『` e `（`, uma chamada cada), e a abertura e o fechamento **continuam na caixa de texto**. Em Shift-JIS são `81 75` e `81 76`; com a fonte em `cp1252` o `81` não desenha nada e o segundo byte vira letra: `u` no começo e `v` no fim (teste.13, #434: `u Sim, senhor diretor, bom dia.v`, `u Ugh... Então aqueles dias rigorosos vão começar de novo... Com aquele diretor era tudo tão tranquilo...v`, `u Concluí meu mandato na Academia Himejima e, a partir de hoje, volto a trabalhar nesta escola. [...] colaboração de todos.v`). A solução do #390 (gravar `81 75` e `81 76` em Shift-JIS) tinha esse custo, e a aspa curva continua descartada: `“` na `cp1252` é `0x93`, que engole a letra seguinte (ver "Acentos e fonte"). Por isso, quando o encoder do projeto não grava o delimitador japonês, o injetor grava `«` (`AB`) e `»` (`BB`) no lugar de `「 『` e `」 』`, e troca os dois primeiros `es.CHAR.NAME.MARK.SET` (`「` e `『`) por `«` no `yst00073`; o `（` não tem substituto e fica como está. A escolha mora em `SpeakerDelimiters` (`Cp1252Open`, `Cp1252Close` e `Resolve`), a troca dos marcadores em `SpeakerMarkers`, e a Shift-JIS e a `fold` seguem gravando `「」`. Os caracteres foram escolhidos por serem de um byte fora da faixa de lead bytes e por não aparecerem em tradução nem em narração: se o jogo tomasse um trecho de narração por nome só porque ele tem o marcador no meio, as aspas retas (`"`) dariam esse problema. Para voltar às aspas retas, mude as duas constantes.
  - **Marcadores sem tradução**: o `yst00073` não tem linha para traduzir nos marcadores, então o `ProjectBuilder` pergunta a cada script (`IScriptFormat.RequiresRebuild`) se ele precisa ser reescrito mesmo sem tradução; no YU-RIS a resposta é sim só para o script que cadastra marcador que a codificação não grava. No jogo de referência é o `yst00073` (3405 para 3417 bytes na `cp1252`; os dois recursos novos entram no fim, como na reescrita de qualquer argumento). Um jogo que não chama `es.CHAR.NAME.MARK.SET` não é tocado, mas as falas dele também sairiam com `«`: se aparecer um, o marcador está em outro lugar (exe ou outro script) e esta regra precisa de uma exceção.
  - O falante da linha que não tem tradução nunca é tocado: o trecho volta byte a byte.
  - **Falante escolhido por linha (#437)**: `TextUnit.ChosenSpeaker` vale no lugar do `Speaker` extraído, depois do mesmo `SpeakerText.Resolve` (cadastro do Glossário, largura, recusa na `cp1252`). O Core entrega o nome já resolvido em `ChosenSpeaker`, e o injetor segue estas regras:
    - Nome numa mensagem que já tinha falante: troca só o nome, com os delimitadores do locator (`Misaki「Hello.」`).
    - Nome numa mensagem sem falante (narração): monta `nome「fala」` com os delimitadores de `SpeakerDelimiters` (`nome«fala»` na `cp1252`, #434). Se a tradução já vem inteira entre `「 」` ou `『 』` (a fala sem nome, que a extração guarda com as aspas), só o nome entra na frente.
    - Narração escolhida (nome vazio) numa mensagem com falante: tira o nome e **mantém os delimitadores** (`「Hello.」`, ou `（Hello.）` no formato de parênteses). Tirar também as aspas deixaria texto puro, mas a forma `「fala」` sem nome já existe no jogo (a fala sem nome que a extração lê como `dialogue` com `speaker` nulo); a decisão evita depender de um comportamento que ninguém viu na tela.
    - Nome numa string de expressão (`GOSUB`, `other`): `InvalidDataException` citando a unidade, porque só `WORD` tem falante.
    - A escolha só entra nos bytes quando a linha é reescrita (`NeedsRewrite`). Sem tradução, ou com a tradução igual ao original, o trecho volta byte a byte.
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

### Como a extração trabalha (#37)

`src/VNForge.Engines.YuRis/Text/` (`YstbTextExtractor`, mais `ExpressionStrings` e `SpeakerSplitter`, internas). `YstbTextExtractor.Extract(file, ystb, yscm)` devolve um `ScriptExtraction` com as unidades na ordem do arquivo. A regra segue o `YurisScenarioScript.cs:178-262` do VNTextPatch, com as diferenças abaixo.

- **Mensagem (`WORD`)**: `WORD` em instruções seguidas formam uma unidade, e o `source` é a concatenação dos textos de cada um (cada `WORD` passa por `ControlMarkers.ToMarkers` sozinho). Qualquer outra instrução termina a mensagem, inclusive `RETURNCODE` e `_`. O VNTextPatch junta também um `_` com string literal sem `\p` à mensagem; aqui o `_` não entra, e o `RETURNCODE` que o E-ris põe no fim de cada linha (`yuri.md:349-351`) separa as linhas em mensagens. `WORD` vazio não gera unidade e não quebra a mensagem. `WORD` com número de argumentos diferente de 1 é pulado, quebra a mensagem e gera um aviso.
- **Falante** (regex em `:83`): sobre o texto já com marcadores, `nome「fala」`, `nome『fala』` ou `nome（fala）`: o `nome` vai para `speaker` e o `kind` é `dialogue`. Nos dois primeiros, o `source` é a fala sem as aspas japonesas; em `（ ）` o `source` mantém os parênteses (como o VNTextPatch, que não os tira). O nome não pode ter `。`, `、`, colchete, `\` nem `{`, e só pode ter `！` ou `？` se for feito só de sinais (`？？？`): o VNTextPatch aceita qualquer nome, o que faria de `彼は言った。「やあ」` e de `彼は叫んだ！「逃げろ」` um falante, e aqui isso fica como narração. A abertura e o fechamento não precisam ser do mesmo par (`「` com `』`, como no VNTextPatch); o locator guarda os dois como estavam. Uma fala inteira entre `「 」` ou `『 』`, sem nome, é `dialogue` com `speaker` nulo. Todo o resto é `narration`.
- **`GOSUB`**: se o primeiro argumento é a string `ES.SEL.SET` (`choice`) ou `ES.CHAR.NAME` (`name`), sem diferenciar maiúsculas (`YurisScenarioScript.cs:236-242`), cada argumento seguinte que for string literal não vazia vira uma unidade, até o primeiro que não for (`:248-249`). Outras rotinas não geram nada.
- **Outras strings (`other`)**: qualquer argumento que seja só uma string literal com hiragana, katakana, ideograma CJK ou katakana de meia largura, em qualquer comando menos `WORD`, `_`, `RETURNCODE`, `CG`, `GOSUB`, `IF`, `ELSE`, `LOOP` e `FONT` (`:253-262`). `GOSUB`, `IF`, `ELSE` e `LOOP` ficam de fora também porque o `size` e o `offset` de alguns de seus argumentos não apontam para recursos. O argumento do `FONT` é o nome de uma fonte (`ＭＳ ゴシック` no jogo de referência); traduzido, o comando pediria uma fonte que não existe, então a extração não o vê (#288). O `STR` do mesmo script (`yst00011:059393`) continua saindo como `other` até se saber onde o jogo usa a string.
- **String literal**: a expressão inteira é `4D <u16 tamanho> <aspas> texto <aspas>`, com `"`, `'` ou crase iguais nas duas pontas. Os escapes `\\`, `\n` e `\t` são desfeitos (`\n` vira o código `EF F0`, que o `ControlMarkers` mostra como `\n`; a barra de um caractere de dois bytes, como o `5C` de `ソ`, não é escape). Outro escape, ou uma barra no fim, faz a string não ser extraída e gera um aviso. Concatenações, variáveis e as expressões `$_CHR[0xEF] + ...` do VNTextPatch (`:333-374`) não são extraídas.
- **Id**: `ScriptFile.UnitId(índice da instrução * 256 + índice do argumento)`, da primeira instrução com texto da unidade. Para as mensagens, o argumento é sempre 0; nas escolhas e nomes do mesmo `GOSUB`, o argumento desempata sem mexer nos ids das outras unidades. Depende só do conteúdo do script, então duas extrações dão os mesmos ids.
- **Locator** (versão 1): `{ "version": 1, "command": "WORD", "parts": [ { "instruction": 12, "argument": 0, "size": 14, "offset": 120 } ], "speaker": { "name": "美咲", "open": "「", "close": "」" } }`. `parts` lista, na ordem do arquivo, os argumentos que formam o `source` (um por `WORD` não vazio), com o `size` e o `offset` de antes da reescrita. `speaker` só existe com falante, e o texto original é `name + open + source + close`. Em `GOSUB` há também `routine`. A E2.7 usa isso para conferir que o trecho ainda decodifica para o `source` e para reescrever só os argumentos da unidade.

### `ES.CHAR.NAME` é a lista de personagens do jogo (#426)

No jogo de referência o script `yst00073` chama `ES.CHAR.NAME` uma vez por personagem (`真弓`, `犬井`, `猿山`, `亀島`, `学生`, `学生Ａ`, `学生Ｂ`, `学生Ｃ`, `父兄`, `男`, `息子`, `教員`, `女`, `山之辺`, `？？？`). A extração já gera essas chamadas como unidades `name`, e `GameCharacters.Names` (`src/VNForge.Core/Glossary/GameCharacters.cs`) as lê como a lista de personagens:

- **Proposta**: ao abrir Glossário > Personagens, cada nome de unidade `name` que ainda não é personagem entra na tabela com o original exato (sem espaços nas pontas, uma linha por nome, na ordem do jogo). O personagem sem tradução recebe a que a unidade `name` já tem. A proposta não suja a página: as linhas vão para o `characters.json` na primeira gravação. A comparação com os personagens existentes passa por `CharacterBook.Find`.
- **Uma tradução por nome**: o `characters.json` e as unidades `name` com o mesmo original guardam o mesmo texto. Salvar o personagem grava a tradução nas unidades `name` do mesmo original, como edição manual (`CharacterNameSync.ApplyToUnits`); linhas "não traduzir" e personagens sem tradução ficam de fora. No sentido contrário, traduzir uma unidade `name` pelo Editor, `vnf lines` ou a Conferência (`LineEditor.SetTranslation`) atualiza o personagem do mesmo original, se o `characters.json` existe, e as outras unidades `name` desse original. Apagar a tradução da unidade não apaga a do personagem. Um `characters.json` inválido não impede gravar a linha.
- **O que ficou de fora**: a tradução feita em lote pela IA (`vnf translate`) não passa pelo `LineEditor`. Se ela traduz uma unidade `name`, o personagem recebe o texto na próxima vez que a página abrir, mas só enquanto o campo dele está vazio; se os dois já têm tradução e divergem, vale a do personagem na próxima gravação.
- **Sem a rotina**: se o jogo não chama `ES.CHAR.NAME` (a extração não gera nenhuma unidade `name`), a proposta é vazia e a página continua como antes: a tabela vem do `characters.json` e das sugestões de voz da IA, que partem dos falantes. Cadastre os nomes à mão com o original exato do `speaker`, ou com `vnf chars set`. Outro jeito de o jogo listar personagens (outra rotina, uma tabela em `ybn`) pede uma extração própria; registre a rotina nesta seção.

### O que confirmar no jogo (#37)

A extração foi escrita a partir das fontes acima e de scripts sintéticos (`tests/VNForge.Engines.YuRis.Tests/Text/YstbTextExtractorTests.cs`); nenhum `yst*.ybn` real foi extraído.

1. **Mensagens**: se o `RETURNCODE` entre linhas separa as mensagens como o esperado, ou se o jogo usa `_` com string literal no meio da fala (o VNTextPatch junta esse caso).
2. **Falantes**: quantas mensagens saem como `dialogue` com `speaker`, e se aparece falso positivo (nome com pontuação que o filtro não pega) ou falante perdido (nome com `。` ou colchete). Num projeto real, falas com nome saíram como narração; de onde o nome vem nesse jogo está em #364.

Falantes feitos só de sinais (`？？？`) saem da extração como `speaker`, mas não viram personagem: `SpeakerNames.IsSignsOnly` os tira da sugestão de vozes do glossário e do bloco "Personagens" do prompt de tradução, e a sugestão só considera falantes com pelo menos 3 falas (#355).
3. **`ES.SEL.SET` e `ES.CHAR.NAME`**: se os argumentos de texto vêm antes dos outros, já que a extração para no primeiro que não é string literal.
4. **`other`**: a lista de comandos que geram strings com japonês e se alguma é código ou nome de arquivo que não se traduz.
5. **Strings fora do padrão**: quantos avisos de escape desconhecido e quantas expressões com `$_CHR` ou concatenação ficam sem unidade.

### Como a reconstrução trabalha (#38)

Duas camadas. `YstbWriter` (`src/VNForge.Engines.YuRis/Ystb/`) grava o arquivo a partir do `YstbFile` e de bytes novos por argumento; `YstbTextInjector` (`src/VNForge.Engines.YuRis/Text/`) converte as traduções das unidades nesses bytes.

- **`YstbWriter.Write(file, replacements)`**: `replacements` é um dicionário do índice do argumento no arquivo (`FirstArgumentIndex` da instrução mais a posição) para os bytes novos do recurso. Cabeçalho (versão, padding), instruções (`opcode`, número de argumentos, `extra`), números de linha e a seção de argumentos saem do arquivo lido; só o `size` e o `offset` dos argumentos trocados mudam, e `id`, `type` e `assign_op` são copiados. Os bytes novos vão para o fim dos recursos, na ordem do índice do argumento (não do dicionário), e os trechos antigos ficam onde estavam, sem uso. Bytes vazios dão `size` 0 e `offset` no fim dos recursos. Os argumentos de `RETURNCODE`, `IF`, `ELSE` e `LOOP` nunca são tocados, a menos que o chamador peça o índice deles. As quatro seções são cifradas de novo com `YstbFile.Key`. Sem nenhuma troca o resultado é idêntico ao arquivo lido (teste de ida e volta com as chaves `0xD36FAC96`, `0x07B4024A`, uma derivada, chave 0 e padding diferente de 0).
- **Primeiro argumento com chave derivada**: se a chave não é uma das conhecidas, o leitor a deriva do `offset` do primeiro argumento, que precisa ser 0. Trocar esse argumento moveria o offset e o leitor não abriria mais o arquivo gravado; a gravação recusa essa troca com `VNForgeException`. O jogo em si não é afetado, só a nossa leitura (e a re-extração).
- **Limite**: o arquivo resultante não passa de 32 MiB (`YbnReader.MaxFileBytes`), o mesmo limite do leitor; acima disso, `VNForgeException`.
- **`YstbTextInjector.Inject(file, script, units, encoder)`**: segue o contrato do `IScriptFormat.Rebuild`. `file.ValidateUnits` confere os ids; só as unidades com `NeedsRewrite` são reescritas. Cada uma é casada com o script pelo locator, e o `id` só aparece nas mensagens. Antes de trocar, o injetor confere que a instrução ainda é do comando do locator, que `size` e `offset` do argumento são os da extração e que o trecho ainda decodifica para o `source` (com o falante: `name + open + source + close`); se não, `InvalidDataException` citando o id. Duas unidades no mesmo argumento também são recusadas. A lista pode ser um subconjunto, em qualquer ordem: o resultado é o mesmo.
- **Texto**: a tradução (com `speaker.name + open` na frente e `close` atrás quando a unidade tinha falante; vale o `Speaker` da unidade se estiver preenchido, senão o nome do locator) passa por `ControlMarkers.ToBytes` com o `ITextEncoder`, que devolve os marcadores a códigos de controle e codifica só os trechos de texto. Caractere que o codificador não representa vira `EncoderFallbackException` citando o id, e marcador inválido vira `FormatException`; nada é gravado e nenhum caractere é trocado em silêncio. Se os bytes saem iguais aos do original (um `fold`, por exemplo), o argumento fica como estava.
- **Mensagem de vários `WORD`**: os bytes da tradução inteira vão para o primeiro `WORD` da unidade (`parts[0]`) e os outros ficam com `size` 0. A extração seguinte dá a mesma unidade, com o mesmo id e uma parte só.
- **Strings de expressão** (`GOSUB`, `other`): os bytes voltam a `4D <u16 tamanho> aspas texto aspas`, com a barra refeita como `\\`, o `EF F0` como `\n` e o byte `09` como `\t`; a varredura respeita os limites de caractere do CP932, então um `5C` que é segundo byte (`ソ` = `83 5C`) não é escapado. O delimitador é o do original; se o texto novo tem esse caractere, vale o primeiro de `"`, crase e `'` que o texto não tem, o que dá crase para um texto com aspas duplas num original com aspas duplas. Texto com os três, ou que passa de 65535 bytes na expressão, é recusado com `ArgumentException` citando o id.
- **No `IScriptFormat` e no YPF (E8.1)**: `YstbScriptFormat.Rebuild` lê o script com o `YscmFile` do `YuRisContext` e chama o `Inject`, convertendo `VNForgeException` em `InvalidDataException`. A gravação do script dentro do YPF é do `YuRisRepacker`: abre o `.ypf` original do jogo, confere que a entrada ainda tem o conteúdo que o `vnf extract` descompactou e grava com `YpfArchive.WriteTo(saída, trocas)` direto em `build/`, o mesmo caminho da reescrita mínima do `vnf ypf pack`, sem precisar de pasta com os arquivos. Pacote sem script alterado é copiado idêntico, sem passar pelo escritor.

### O que confirmar no jogo (#38)

A reconstrução foi escrita a partir das fontes acima e de scripts sintéticos (`tests/VNForge.Engines.YuRis.Tests/Ystb/YstbWriterTests.cs`, `Text/YstbTextInjectorTests.cs`); nenhum `yst*.ybn` real foi regravado e nenhum jogo rodou com o resultado.

1. **O jogo aceita recursos fora da ordem dos argumentos**: os textos novos ficam depois dos antigos e o tamanho da seção cresce. Se o runtime esperar a seção na ordem ou com tamanho fixo, a regra de reescrita muda no mesmo PR.
2. **`WORD` com `size` 0**: o jogo precisa tolerar a instrução vazia (o `offset` aponta para o fim dos recursos). Se travar ou mostrar uma linha em branco indesejada, a alternativa é reaproveitar o `offset` original com `size` 0.
3. **Delimitador de string**: se o runtime lê a string só pelo `u16` de tamanho (como parece), aspas dentro do texto são inofensivas e a recusa do texto com `"`, crase e `'` ao mesmo tempo é conservadora demais.
4. **`\n` e `\t` em string de expressão**: o extrator e o injetor tratam `\n` como o código `EF F0`, como o VNTextPatch; confirmar que o jogo mostra a quebra de linha do mesmo jeito que num `WORD`.
5. **Nome do falante** (e #437): o injetor grava o `Speaker` que o Core resolveu (tradução do personagem, ou o nome do locator quando a unidade não tem falante). Resolvido nos prints da teste.13 (#434): com os delimitadores `「」` em Shift-JIS e a fonte em `cp1252`, a caixa de texto desenhava `u` e `v` (`81 75` e `81 76`); a correção é `«»` mais a troca dos marcadores no `yst00073`. Para o falante escolhido (#437), falta confirmar também que `nome«fala»` montado numa mensagem que era narração separa o nome como nas falas originais, e que `«fala»` sem nome (narração escolhida) aparece sem placa de nome. Falta confirmar no jogo, na teste seguinte: que o nome continua na caixa de nome, que a fala aparece como `«fala»` sem `u` nem `v`, e se o jogo usa o nome da mensagem para algo além de exibir (histórico, voz, teste de nome). A fala de pensamento (`nome（fala）`) usa o marcador `（`, que a `cp1252` não grava; o corpo da tradução fica com os parênteses ASCII, e é a mesma pergunta da busca pelo marcador, fora desta correção.
6. **Primeiro argumento com chave fora da lista**: confirmar se algum script real tem uma instrução de texto como a primeira; a recusa só vale para chave derivada.

## Configuração do jogo e título da janela (`yscfg.ybn`, #427)

O título da barra da janela do jogo não está nos scripts: vem de `ysbin/yscfg.ybn`, dentro do `ysbin.ypf`. Sem tradução, o jogo abre com o título em japonês, que num Windows sem a localidade japonesa aparece quebrado.

Layout, conferido em um único arquivo real (jogo de referência da teste.12, versão 485 = `0x1E5`, 106 bytes). Nenhuma fonte aberta que eu tenha achado documenta o formato (o RxYuris cita um leitor de YSCFG, mas o código não foi lido), então o que segue vale para esse arquivo e para o que o leitor aceita:

| Posição | Tamanho | Campo |
|---|---|---|
| 0 | 4 | Assinatura `YSCF` |
| 4 | 4 | Versão, inteiro de 32 bits little-endian (a mesma faixa 200 a 599 dos outros `.ybn`) |
| 8 | 68 no arquivo de referência | Configuração do jogo (resolução, opções de janela e afins); o VNForge não a interpreta |
| 76 | 2 | Tamanho do título em bytes, `uint16` little-endian (`0x1C`) |
| 78 | tamanho acima | Título em Shift-JIS, sem terminador, até o fim do arquivo |

Nenhum campo diz onde o título começa, e o tamanho da configuração pode mudar entre versões. O leitor (`YscfgReader`) procura a posição `p` a partir do byte 8 em que o `uint16` lido bate exatamente com o que sobra até o fim do arquivo e os bytes seguintes são Shift-JIS válido, sem byte abaixo de `0x20`. Mais de uma posição possível recusa o arquivo em vez de adivinhar; nenhuma também recusa, com a mensagem citando o `yscfg.ybn`.

Um título que o build gravou com a estratégia `cp1252` (`í` é `0xED`, que o Shift-JIS não aceita nessa posição) não passa na leitura em Shift-JIS; sem candidato, o leitor tenta Windows-1252 e marca `YscfgFile.TitleIsShiftJis` como falso. Serve para reler um jogo já injetado; o `vnf extract` deve rodar no jogo original, como para os scripts.

**Reescrita mínima.** O `YscfgWriter` copia os bytes de antes do campo de tamanho como foram lidos e regrava só o tamanho e o título. Com o título original, o arquivo sai idêntico (teste de ida e volta). Dentro do YPF, o `YuRisRepacker` troca só a entrada `ysbin/yscfg.ybn` (`YpfArchive.WriteTo`) e copia as outras byte a byte. O título cabe em 65.535 bytes.

**Codificação.** O build codifica o título com o `ITextEncoder` da estratégia do projeto (`GameTitleText.Resolve`): com `cp1252` o `í` sai como `0xED`, com `fold` as letras perdem o acento, com `sjis` o `í` é recusado. A Conferência acusa o título pela regra `titulo-nao-codificavel` (erro, categoria Acentos), que usa a mesma função do build. Largura total (`ＡＢＣ`) vira largura normal (NFKC) só quando a estratégia não grava a original, como no nome de falante.

### O que confirmar no jogo (#427)

1. **Título em `cp1252` na barra da janela.** Falta ver, no jogo de referência, se a barra mostra `Título` quando o `yscfg.ybn` leva o `í` em `0xED`: a janela é criada pela API ANSI, então o resultado depende da página de código do Windows. Com `fold` não há risco.
2. **Outras versões do YU-RIS.** Só a versão 485 foi vista. Se uma versão grava campos depois do título, o leitor recusa o arquivo (nenhuma posição fecha com o fim do arquivo) em vez de gravar errado.

## Detecção da pasta do jogo (E2.8)

`YuRisEngine.Detect` (`src/VNForge.Engines.YuRis/YuRisDetector.cs`) só lê. Procura `*.ypf` na pasta do jogo e na subpasta `pac` (sem diferenciar maiúsculas) e reconhece o jogo quando algum arquivo abre com a assinatura `YPF\0`. No pacote de scripts (`ysbin.ypf` primeiro; vale o primeiro que tenha algum `yst<dígitos>.ybn`, o `ysc.ybn` sozinho não basta) lê o `ysc.ybn` e, entre os 8 primeiros `yst<dígitos>.ybn`, o primeiro que o `YstbReader` decifra: dele saem a versão do YSTB e a chave XOR. O que não der para ler (índice corrompido, `ysc.ybn` ausente, YSTB abaixo da 300, entrada de mais de 32 MiB) não derruba a detecção: vira o detalhe `warnings`, e a versão do YSTB sai do cabeçalho do primeiro script que tenha a assinatura. Pasta inexistente, vazia ou sem YPF devolve `EngineDetection.NotDetected`; se houve falha ao listar a pasta ou ao abrir um `.ypf` (por exemplo, bloqueado por outro programa), o resultado não reconhecido traz só o detalhe `warnings`. Passando de 64 arquivos `.ypf`, só os 64 primeiros são examinados e um aviso diz isso.

Detalhes devolvidos (chaves em inglês e camelCase, parte do contrato do `--json`): `ypf:<caminho>` (versão e contagem de cada pacote), `scriptsArchive`, `ypfVersion`, `scripts`, `ystbVersion`, `scriptKey`, `scriptKeyOrigin` e `warnings`. A codificação do texto ainda não é detectada. `vnf detect --game <dir>` mostra esse diagnóstico com rótulos em português (ou JSON com `--json`, que traz as chaves como estão).

## Resultado no jogo de referência (M1, #88)

Jogo YU-RIS testado em 2026-10-23 com `init`, `extract`, `build`, `inject` e `restore`:

- **YPF** (#23): `ysbin.ypf` e os outros cinco pacotes na v485. Hash do nome e dos dados MurmurHash2, offset de 64 bits, tabela de troca `Standard`, chave do nome 0x00. `vnf ypf unpack` + `pack` do `ysbin.ypf` sem mudanças deu arquivo idêntico.
- **Scripts**: 131 `yst*.ybn`, YSTB v485, chave 0xD36FAC96 (a padrão). A extração deu 6.855 unidades: 3.679 narração, 3.089 diálogos, 53 `other`, 30 nomes e 4 escolhas.
- **Ida e volta**: build sem tradução idêntico ao original, jogo abrindo igual e `restore` devolvendo os hashes originais.
- **Reescrita**: três narrações trocadas por texto ASCII aparecem no jogo, sem travar. O jogo aceita o texto novo no fim dos recursos.
- **Acentos**: `ã` e `é` param o build com o id da linha, como previsto para a Shift-JIS pura do M1.
- **Formatação**: cada letra latina ocupa uma célula fixa de meia largura e a fonte desenha o glifo estreito nela, então as letras ficam espaçadas (ver #62 e #105). O jogo quebra a linha pela largura, no meio da palavra (ver #56 e #59). A causa do espaçamento e a medida da caixa estão na seção seguinte.

Ainda não conferido: `WORD` com `size` 0 (nenhuma fala de várias partes foi trocada), escolhas, nomes e strings de expressão traduzidos.

## Testes da V2 no jogo de referência

Roteiro em `docs/plans/2026-10-24-plano-de-testes-no-jogo.md`, rodado com `vnf 0.0.0-teste.4` (commit `02ce4da`) em 2026-10-24 e 2026-10-25. As evidências (saídas, hashes e capturas de tela) ficam na pasta de testes do usuário, fora do repositório, porque mostram o jogo. Da etapa 3 em diante o jogo rodou sem o Locale Emulator, que é o cenário de quem joga a tradução.

### Pacotes e scripts (#31, #41)

- **Variante do YPF**: os seis pacotes (`ysbin`, `bgm`, `cg`, `cgsys`, `se`, `voice`) estão na v485, com os parâmetros da seção anterior.
- **Ida e volta dos YPF**: `vnf ypf unpack` seguido de `vnf ypf pack`, sem mudanças, deu os seis pacotes idênticos ao original por SHA-256.
- **Scripts**: YSTB v485, chave 0xD36FAC96, 131 scripts e 6.855 unidades: 3.679 narração, 3.089 diálogo, 53 `other`, 30 nomes e 4 escolhas.
- **Build sem tradução**: o `ysbin.ypf` gerado é idêntico ao original. Nesse caso o build copia o pacote sem passar pelo `YstbWriter`.
- **Uma linha trocada**: só o `yst00114.ybn` muda dentro do pacote, e os outros 130 scripts saem iguais byte a byte.

### Caixa de texto (#59)

- **Largura**: o jogo quebra a fala sozinho a cada 73 colunas de meia largura, no meio da palavra. A medida é a mesma com a fonte `System` e com a MS Gothic, porque o motor conta células, não a largura do glifo.
- **Linhas**: cabem 3. A 4ª linha é desenhada por baixo da barra de botões (SAVE, LOAD e os outros) e a 5ª fica fora da tela. O jogo não abre página nova sozinho, então uma fala com mais de 3 linhas fica cortada.
- **Como medir**: uma régua de 520 caracteres sem espaço, com uma letra a cada 10 colunas (`A234567890B234567890…`), mostra a coluna de cada quebra. Uma fala com 6 linhas curtas separadas por `\n` mostra quantas linhas cabem. O `\n` (`EF F0`) dentro de um `WORD` vira quebra de linha no jogo.
- **Validação**: com `wrap.maxWidth` 73 e `wrap.maxLines` 3 no `formatting.json`, uma fala de três linhas, duas delas com exatamente 73 colunas, quebrou no jogo onde o `vnf preview` mostrou. Uma fala de 5 linhas gerou o aviso `caixa-acima-do-limite` no preview e no build. Esses valores estão em `samples/formatting.json`.

### Acentos e fonte (#62)

- **Acentos em `cp1252` com o exe original**: a frase `Ação, coração, você é ótimo! Àquela ponte, pôr o pé.` aparece com todos os acentos certos. Não foi preciso mexer na tabela de lead bytes nem no charset da fonte.
- **Acento no fim da fala**: `Até já, café` aparece inteira. O motor não engole o byte seguinte ao `é`, embora `0xE9` esteja na faixa de lead bytes. Essa nota vale para o byte seguinte comum e para a fonte `System` da época; a teste.13 e a teste.14 mostraram o que ela não cobria (item "Letra acentuada junta com o byte seguinte", logo abaixo).
- **Letra acentuada junta com o byte seguinte (teste.13 e teste.14, #435)**: a tabela de lead bytes do exe (0xC20A0) segue marcando `0xE0` a `0xFC`, então o motor lê uma letra acentuada da `cp1252` e o byte seguinte como um caractere só. O que se viu no jogo, com a `cp1252` e o patch da fonte MS Gothic:
  - a quebra de linha `\n` (`EF F0`) depois de `á`, `é` e `ê` some e o `F0` aparece como `ð`: `do que já` + `\n` + `era...?` saiu `do que jáïðera...?`, `que até` + `\n` + `há pouco` saiu `que atéïðhá pouco`, `éïðsemp re entediante` e `seráïðprática`. A quebra depois de vírgula (`antes agitada,` + `\n`) funcionou, porque `,` não é lead byte;
  - o espaço logo depois da letra acentuada some: `só na teoria` saiu `sóna teoria` (`F3 20`).

  **Regra do build**: com a `cp1252`, o `ControlMarkers.ToBytes` grava um `0x20` a mais depois de uma letra de `0xE0` a `0xFC` quando o byte seguinte é um espaço ou o `EF` de um controle (`\n` `EF F0`, `\p` `EF F2`, `\c` `EF F3`, `\u` `EF F5` e `{EF:xx}`). O par `letra + 0x20` fica com o motor e o byte seguinte volta a ser lido como era: `até\nhá` sai `E9 20 EF F0` e `só na` sai `F3 20 20`. A regra está num ponto só (`BytesBuilder`, usado por falas, nomes de falante e delimitadores), segue a varredura do motor da esquerda para a direita (dois acentos seguidos formam um par, e o espaço depois deles fica como está), não toca nos bytes do `{RAW:...}` e não vale para `sjis` nem `fold`. O contador de largura da caixa (`caixa-acima-do-limite`) trabalha sobre o texto com marcadores, antes da codificação, então não conta o espaço extra. A ida e volta sem alteração segue idêntica, porque uma unidade sem tradução copia os bytes do original.

  **Hipótese e o que falta ver no jogo.** O motor parece tratar o par `lead + byte` como um caractere de duas células e perder o segundo byte só quando ele é espaço ou `EF`; uma letra comum depois do acento aparece inteira (`Ação, coração, você é ótimo! Àquela ponte`, #62), e por isso a regra não cobre todo byte seguinte. Pontos abertos, todos para conferir na próxima teste:
  1. `Até já, café` do #62 tem `é` seguido de espaço e aparecia inteiro com a fonte `System`. A teste.14 usa a MS Gothic (células fixas). Falta ver se o espaço engolido só se nota com a fonte de largura fixa ou se depende da posição na linha.
  2. O espaço extra pode aparecer como espaço duplo se o motor, nessa posição, já não engolisse o `20`. Conferir `só na teoria`, `até` + `\n` + `há` e `seráïð` (agora `será` + `\n` + `prática`) e olhar se há vão a mais.
  3. Nome de falante com acento seguido de espaço (`José Maria`) e strings de expressão (escolhas, sistema) passam pela mesma regra e levam o espaço extra; só a fala (`WORD`) foi vista no jogo. Falta ver se a caixa do nome e o casamento do marcador de falante aceitam.
  4. Delimitador de falante: na `cp1252` os delimitadores são `«` (`AB`) e `»` (`BB`), que não são espaço nem `EF`, então um acento antes deles não leva guarda e o par `letra + AB` deve aparecer inteiro; falta ver `José«` no jogo. Um delimitador que a `cp1252` não grava e cai no Shift-JIS (como `（`, `81 69`) depois de uma letra acentuada se juntaria com o lead byte `81`, e a regra não cobre esse caso, que não apareceu no jogo de referência.
- **Caractere gravado entre `0x81` e `0x9F` engole a letra seguinte** (reteste da teste.10, #390): `“Falando` em `cp1252` é `93 46 61 6C...`, e o jogo mostrou `alando`. O motor lê o `0x93` e o `F` como um caractere duplo só. O `”` no fim da fala não fez efeito porque nada vem depois dele. O mesmo vale para todo caractere que a `cp1252` grava nessa faixa: `‚ ƒ „ … † ‡ ˆ ‰ Š ‹ Œ Ž ‘ ’ “ ” •`, meia-risca, travessão, `˜ ™ š › œ ž Ÿ`. O teste de acentos (#62) não mostrou o problema porque `é ç ã` ficam em `0xE0` a `0xFC`, que o jogo não trata assim (a tabela marca a faixa, mas o `é` no fim de `Até já, café` não engoliu nada). Por isso o `Cp1252TextEncoder` recusa a faixa `0x81` a `0x9F` (a Conferência acusa `caractere-nao-codificavel` com o motivo e a troca: aspas retas, `...`, hífen) e os acentos de `0xE0` a `0xFC` continuam liberados. As opções de formatação de aspas retas e de reticências em três pontos evitam o erro; o `€` (`0x80`) fica fora da faixa. O delimitador do falante também não passa por isso: na `cp1252` são `«` e `»`, bytes `AB` e `BB` (ver "Falante" e #434).
- **O `~` alto (teste.13, #434)**: em `É~, como todos sabem` o til aparece no topo da célula, como `˜`. A tradução do projeto tem o `~` ASCII (U+007E, byte `0x7E`; o `˜` da `cp1252` é `0x98`, que o encoder recusa), então o byte gravado é o `0x7E` e o que muda é o desenho dele na fonte do jogo (a MS Gothic desenha o til do Shift-JIS no alto da célula). Não é outro byte e não pede correção; a conferência é visual, no jogo.
- **Corretor de caractere não gravável (#417)**: `vnf check --fix` e o "Corrigir com um clique" trocam cada caractere que a estratégia não grava por um substituto só ASCII (`UnwritableCharacters`, no Core), e só quando o substituto é gravável. Marcadores de controle nunca são tocados, e caractere fora da tabela segue como erro para a pessoa decidir. Os substitutos só ASCII são os que já se viram no jogo de referência; os símbolos `· « » ° ¡ ¿` da cp1252 passam no encoder mas nunca foram vistos na tela, então não entram. Gravar `♪` ou `★` em Shift-JIS no meio do texto cp1252, como os delimitadores do falante, fica fora: não foi testado e depende da fonte. A tabela:

  | Caractere | Substituto |
  |---|---|
  | aspas curvas duplas (U+201C, U+201D, U+201E) | `"` |
  | aspas curvas simples (U+2018, U+2019, U+201A) e `‹ ›` | `'` |
  | reticências (U+2026) e `‥` | `...` e `..` |
  | meia-risca, travessão, barra horizontal e traço de caixa (U+2013, U+2014, U+2015, U+2500) | `-` |
  | marcador (U+2022), `★ ☆ ※` | `*` |
  | `™` | `TM` |
  | `Š š Ž ž Œ œ Ÿ ƒ ˆ ˜` | `S s Z z OE oe Y f ^ ~` |
  | `ï` | `i` |
  | `♪ ♫ ♥ ♡` | `~` |
  | `～ 〜` | `~` |
  | `・` (e `･`) | espaço; `・・・` vira `...` |
  | `「 」 『 』` | `"` |
  | `→ ←` | `->` e `<-` |
  | espaço japonês (U+3000) | espaço comum |
  | pontuação e letras de largura cheia (`（ ） ！ ？ Ａ １`) | a versão ASCII |
  | `。 、 【 】 〔 〕` e o resto da tabela de `JapanesePunctuation` | o da tabela dela (`. , [ ] ( )`) |
  | letra com acento que a cp1252 não tem (`ő ā ě`) | a letra sem o acento (decomposta, sem as marcas U+0300 a U+036F) |

  `† ‡ ‰ €` ficam fora: são raros e não têm troca óbvia. Na `sjis` a mesma tabela vale para o que o Shift-JIS não grava, mas a troca de letra acentuada só vale quando a estratégia grava os acentos comuns (`é`): tirar o acento de `ação` numa estratégia `sjis` seria a `fold` sem a pessoa escolher. O `CharacterFolder` da `fold` não muda, e a opção "Trocar pontuação japonesa pela do teclado" também não.
- **Ajuste que a estratégia não grava (#417)**: um ajuste de formatação pode criar o caractere que a estratégia recusa. O caso do teste.11 foi Reticências = "Caractere …" com a `cp1252`: a tradução gravada tem `...`, o ajuste a troca por `…` no build, e a `cp1252` recusa o `…` (byte `0x85`). A regra `ajuste-nao-codificavel` (erro) acusa o ajuste, uma ocorrência por linha afetada, no lugar da `caractere-nao-codificavel`, que se cala no caractere que o ajuste cria. Os ajustes conferidos são Reticências, Aspas (`「 」`), Travessão, Travessão de fala e Largura cheia; as substituições do usuário ficam de fora. O valor seguro é Três pontos, Aspas retas, travessão desligado, hífen (`-`) no travessão de fala e largura desligada. `vnf check --fix` (`QaFixer.Apply`) troca o ajuste antes das correções de texto e grava o `formatting.json` só quando algo mudou.
- **Tabela de lead bytes**: fica em 0xC20A0 no exe, com `01` nas posições `0x81` a `0x9F` e `0xE0` a `0xFC`. Com a tabela inteira zerada o jogo não abre e mostra `ルビ設定開始文字が不正です。` (caractere inicial de ruby inválido): o motor usa a tabela nos próprios códigos de controle, então ela fica como está. Zerar só `0xE0` a `0xFC` não foi testado, porque deixou de ser necessário.
- **Fonte das falas**: `CreateFontIndirectA` recebe a face `System`, charset 1 (`DEFAULT_CHARSET`), peso 700 e altura 92 (84 na segunda chamada). O exe guarda os nomes de fonte em Shift-JIS a partir de 0xD5008 (`ＭＳ ゴシック`, `標準ゴシック`, `ＭＳ Ｐゴシック`, `ＭＳ 明朝` e `標準明朝`, cada um terminado em zero). Num Windows sem o japonês como idioma de programas não Unicode, nenhuma fonte responde por esses nomes, e o motor pede a `System`, que é proporcional. Como cada caractere ocupa uma célula fixa, as letras estreitas (`i`, `l`, `t`, `r`, `f`, `j`) ficam com um vão depois delas, com ou sem acento.
- **Correção da fonte**: trocar os 13 bytes de `ＭＳ ゴシック` em 0xD5008 por `MS Gothic` em ASCII, completando com zeros até 0xD5017. Com o exe assim, as falas saem em MS Gothic, sem os vãos e com os acentos certos. É o patch do `vnf patch-exe` (#65), que acha o nome no exe em vez de usar o endereço 0xD5008 e recusa o exe em que o nome não aparece uma só vez em Shift-JIS, no começo de uma string (byte anterior zero) e seguido de zero (o `ＭＳ ゴシック` em UTF-16 de 0x11EF02 é outra sequência de bytes e não conta). Troca os 13 bytes do nome por `MS Gothic` e zeros, sem tocar no resto. Testado só com exes sintéticos; no jogo de referência o resultado esperado é o da busca 4E: 13 bytes alterados, de 0xD5008 a 0xD5014.
- **O que não troca a fonte**: o comando `FONT` em `yst00011:044288` e a string em `yst00011:059393` também têm `ＭＳ ゴシック`. Trocar os dois por `MS Gothic` no build não muda a fonte das falas. A extração não gera mais unidade para o `FONT` (#288); a do `STR` continua saindo como `other` até se descobrir onde o jogo usa a string.
- **Tela de configuração**: a lista de fontes de CONFIG › MESSAGE fica vazia sem o Locale Emulator, mesmo com o exe corrigido, e a caixa da fonte mostra o nome em Shift-JIS lido como cp1252 (`‚l‚r ƒSƒVƒbƒN`). Não afeta as falas.
- **Outro nome no exe**: o `ＭＳ ゴシック` em UTF-16 em 0x11EF02 fica junto de `コメント編集` e é a fonte de uma caixa de diálogo do Windows, não das falas.

Conclusão da #62: seguir com `cp1252`, sem Locale Emulator, mais o patch do nome da fonte. O `remap` (#66) não é necessário no jogo de referência.

## Acentuação: o problema central

Shift-JIS não tem `á à â ã é ê í ó ô õ ú ç` (nem maiúsculas). Também faltam `« » ª º`. Já existem em Shift-JIS: `“ ” ‘ ’ … ― °` e todo o ASCII.

Opções, da mais simples para a melhor:

1. **`fold`**: tirar acentos. Funciona em qualquer jogo, fica feio.
2. **`remap`**: as katakanas de meia largura (`0xA1`–`0xDF`) ocupam **1 byte** em Shift-JIS e quase nunca aparecem em VN. Mapeamos `á→ｱ`, `ç→ｲ` etc. e trocamos os glifos numa fonte embutida/instalada. Truque clássico de fan-translation.
3. **`cp1252`**: o texto é gravado em Windows-1252. O executável do YU-RIS tem uma tabela de 256 bytes que marca quais bytes iniciam caractere duplo (lead bytes do Shift-JIS: `0x81–0x9F`, `0xE0–0xFC`); ferramentas chinesas trocam essa tabela para GBK e usam um DLL que muda o `lfCharSet` em `CreateFontIndirectA`. No jogo de referência nada disso foi preciso: com o exe original e sem Locale Emulator os acentos aparecem certos, e o único patch é o nome da fonte (#62, seção "Testes da V2 no jogo de referência"). Pontos de atenção:
   - o byte `0xEF` (ï) colide com o prefixo de código de controle; o QA proíbe `ï`
   - os bytes `0x81` a `0x9F` engolem a letra seguinte (aspas curvas, reticências, meia-risca, travessão e afins); o QA proíbe esses caracteres na `cp1252` (#390)
   - os bytes `0xE0` a `0xFC` (letras acentuadas) se juntam ao byte seguinte: um espaço ou um código de controle `EF xx` logo depois deles se perde; o build grava um `0x20` a mais nessa posição (#435, ver "Acentos e fonte")
   - zerar a tabela inteira impede o jogo de abrir (erro de ruby), então ela fica como está
   - nomes de arquivo e strings internas continuam Shift-JIS; só o texto exibido muda

Fonte do jogo: ele pede a fonte pelo nome ＭＳ ゴシック (MS Gothic, `msgothic.ttc`, largura fixa). Sem o japonês como idioma de programas não Unicode, o Windows não acha a fonte por esse nome e o jogo usa a `System`, até o patch do nome (#65). O app não a redistribui: a prévia usa a instalada no sistema e, na falta dela, a BIZ UDGothic (SIL OFL 1.1), que é também a candidata para a fonte modificada do `remap` (#66). A decisão está em `docs/04-app-windows.md` (D6, #105).

Plano: `fold` validou a ida e volta (M1). A tradução segue com `cp1252` mais o patch do nome da fonte (#62, #65). O `remap` fica como alternativa para um jogo em que o `cp1252` não funcione.

## Referências

- [GARbro — ArcYPF.cs](https://github.com/morkt/GARbro/blob/master/ArcFormats/YuRis/ArcYPF.cs): leitura/escrita de YPF, decifragem de YSTB
- [Xorboth/YPF-Manager](https://github.com/Xorboth/YPF-Manager): reempacotador YPF em C#, tabelas por versão, MurmurHash2
- [arcusmaximus/VNTranslationTools](https://github.com/arcusmaximus/VNTranslationTools) (MIT): `VNTextPatch.Shared/Scripts/Yuris`, documentação dos formatos (Notes.txt), extração e reinjeção de texto, códigos de controle
- [shimamura-sakura/yuri](https://github.com/shimamura-sakura/yuri): decompilador/compilador e o documento `yuri.md` com a análise mais completa da engine
- [jyxjyx1234/YURIS_TOOLS](https://github.com/jyxjyx1234/YURIS_TOOLS): fluxo de tradução chinês, patch de lead bytes (GBK.py) e DLL de troca de fonte
- [fengberd/YuRISTools](https://github.com/fengberd/YuRISTools), [ZQF-ReVN/RxYuris](https://github.com/ZQF-ReVN/RxYuris): mais implementações de referência

Antes de copiar qualquer trecho de código, conferir a licença de cada repositório. Usar como referência de formato não tem problema; código copiado precisa ser compatível com a licença que escolhermos.
