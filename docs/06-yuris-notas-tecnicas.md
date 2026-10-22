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
- **Tabela de troca ambígua**: se os nomes do arquivo não têm tamanho que separe duas tabelas, vale a primeira da ordem de tentativa. O `name_size` gravado é guardado cru em cada entrada, então a gravação não depende dessa escolha.
- `YpfArchive.Parameters` guarda o que foi detectado (versão, algoritmos, largura do offset, tabela, XOR do nome, semântica do `header_size`) para a gravação (#29) usar os mesmos parâmetros; cada `YpfEntry` guarda os campos crus do índice.

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

## Scripts YSTB (`yst*.ybn`), versão ≥ 300

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

- Cada uma das 4 seções é XOR com uma chave de 4 bytes, recomeçando no início de cada seção.
- **Descobrir a chave**: o primeiro argumento quase sempre tem `offset = 0`, então os bytes 8–11 da seção de argumentos, ainda cifrados, *são* a chave. Chaves conhecidas para validar: `0xD36FAC96` (≥ 290), `0x07B4024A` (versões 2xx). Jogos comerciais podem ter outra.
- **Opcodes não são fixos**: vêm de `ysc.ybn` (YSCM), que lista os comandos na ordem do id. Procuramos `WORD` (texto), `_` (expressão), `RETURNCODE`, `GOSUB`, `CG`.
- **Texto (`WORD`)**: 1 argumento, bytes crus em Shift-JIS, sem aspas. Linhas seguidas de `WORD` formam uma mensagem; dá para pôr a tradução inteira no primeiro e esvaziar os demais.
- **Códigos de controle dentro do texto**: `EF F0` quebra de linha, `EF F2` nova página, `EF F3` esperar clique, `EF F5` desconhecido. Viram `\n`, `\p`, `\c`, `\u` no nosso JSON. Outro `EF xx` vira `{EF:xx}`. A varredura respeita os limites de caractere do CP932: um `EF` que é segundo byte de um caractere de dois bytes (ex.: `88 EF`) não é código de controle, assim como um segundo byte `5C` ou `7B` (ex.: `ソ` = `83 5C`) não vira barra ou chave. Byte que o Shift-JIS não decodifica, ou que decodifica para um par diferente na volta (o CP932 tem duplicatas, como `87 90` e `81 E0`), vira `{RAW:hex}`, para a ida e volta gerar os mesmos bytes. A implementação é `ControlMarkers`, em `VNForge.Engines.YuRis.Text`.
- **Falante**: no E-ris costuma vir como `名前「fala」` dentro do próprio `WORD`.
- **Escolhas e nomes**: chamadas `GOSUB` para `ES.SEL.SET` e `ES.CHAR.NAME`, com strings em expressão.
- **Strings em expressão**: `4D <u16 len> "texto"`. O YU-RIS não tem escape de aspas, mas só confere se o primeiro e o último caractere são iguais, então dá para delimitar com crase: `` `texto com "aspas" e 'apóstrofo'` ``.
- **Plano de reescrita**: anexar os textos novos no fim da seção de recursos e atualizar `size`/`offset` só dos argumentos alterados. Os outros argumentos (inclusive os de `RETURNCODE`, cujo offset nem sempre aponta para recursos) não são tocados.
- `yst_list.ybn` (YSTL) dá o caminho original de cada script (ex.: `userscript\01_prologo.yst`), útil para nomes legíveis.
- Versões < 300 têm outro layout (instruções de tamanho variável). Só vamos tratar se o seu jogo precisar.

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
