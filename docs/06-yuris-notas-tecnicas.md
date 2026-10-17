# 06 — Notas técnicas: YU-RIS

Resumo do que levantei lendo ferramentas existentes (links no fim). **Nada disso foi testado ainda contra o seu jogo**; o primeiro trabalho na sua máquina é confirmar cada item.

## Primeira coisa a verificar no seu jogo

Abra `ysbin.ypf` num editor hexadecimal (ou rode, no PowerShell, `Format-Hex ysbin.ypf | Select-Object -First 2`):

- Bytes 0–3: `59 50 46 00` (`YPF\0`)
- Bytes 4–7: versão em little-endian. Ex.: `F4 01 00 00` = 500

A versão define hash, tamanho de offset e tabela de nomes. Com ela já dá para saber em qual variante estamos.

## Arquivo YPF

```
u8  magic[4]   = "YPF\0"
u32 version
u32 file_count
u32 header_size     // >= ~300: inclui o próprio cabeçalho (= offset do início dos dados)
u8  padding[16]

Entry[file_count]   // ordenadas pelo hash do nome
    u32 name_hash
    u8  name_size   // codificado: (~b) e depois troca por tabela
    u8  name[]      // cada byte XOR com uma chave (dá para adivinhar: o '.' da extensão)
    u8  file_type   // 0 ybn, 1 bmp, 2 png, 3 jpg, 4 gif, 5 wav, 6 ogg, 7 psd, 8 ycg, 9 psb
    u8  compressed  // 1 = zlib
    u32 size_uncompressed
    u32 size_stored
    u32/u64 offset  // 64 bits a partir da v479
    u32 data_hash   // hash dos bytes armazenados (comprimidos)
```

| Versão | Hash do nome | Hash dos dados | Offset |
|---|---|---|---|
| < 265 | Adler32 | Adler32 | 32 bits |
| 265–478 | CRC32 | Adler32 | 32 bits |
| ≥ 479 | MurmurHash2 (seed 0) | MurmurHash2 | 64 bits |

- Tabela de troca do tamanho do nome: pares `(3,72) (6,53) (9,11) (12,16) (13,19) (17,25) (21,27) (28,30) (32,35) (38,41) (44,47) (46,50)`. A v500 usa uma variante (`3↔10`, `17↔24`, `20↔46` no lugar de `3↔72`, `17↔25`, `46↔50`). Estratégia: tentar as tabelas conhecidas e validar com o hash do nome.
- **Plano de reescrita**: manter versão, chave, tabela e ordem do arquivo original; copiar entradas intactas sem tocar; só as alteradas são recomprimidas e ganham hash novo, com o algoritmo detectado no próprio arquivo. Como os nomes não mudam, os hashes de nome e a ordem continuam válidos.
- Há relatos de executáveis que leem arquivos soltos fora do YPF ("免封包"), mas isso varia; o caminho seguro é reempacotar.

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
- **Códigos de controle dentro do texto**: `EF F0` quebra de linha, `EF F2` nova página, `EF F3` esperar clique, `EF F5` desconhecido. Viram `\n`, `\p`, `\c`, `\u` no nosso JSON.
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
