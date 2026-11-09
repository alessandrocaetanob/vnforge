# Avisos de terceiros

O VNForge usa os componentes abaixo além dos pacotes NuGet de uso geral listados em `Directory.Packages.props`. Este arquivo acompanha o App e a CLI (pasta `licenses/` ao lado do executável, em cada pacote de release: `vnforge-app-<versão>-win-x64.zip`, `vnf-<versão>-win-x64.zip` e `vnf-<versão>-linux-x64.tar.gz`) e registra o que cada licença exige de quem distribui o VNForge.

### O que vai na pasta `licenses/` de cada pacote

| Arquivo | O que é | Origem |
|---|---|---|
| `THIRD-PARTY-NOTICES.md` | Este arquivo | Raiz do repositório |
| `VERO-README_pt_BR.txt` | Créditos e aviso de licença do dicionário pt-BR | `README_pt_BR.txt` do VERO, sem alteração |
| `WeCantSpell.Hunspell-license.txt` | `license.txt` do pacote WeCantSpell.Hunspell 7.0.1 (herda a licença do Hunspell) | Pacote NuGet, sem alteração |
| `LGPL-3.0.txt` e `GPL-3.0.txt` | Texto da LGPL versão 3 (que remete à GPL versão 3) citada pelo README do VERO | [gnu.org/licenses](https://www.gnu.org/licenses/lgpl-3.0.txt) |
| `MPL-2.0.txt` e `MPL-1.1.html` | Texto da Mozilla Public License 2.0 e da 1.1 | [mozilla.org/MPL](https://www.mozilla.org/en-US/MPL/) |
| `INSTALADOR-LICENCAS.txt` | Apache-2.0 do VCDiff e licenças MIT das dependências, o texto que o instalador do jogador leva embutido | Repositórios e pacotes NuGet de cada biblioteca, sem alteração |
| `VCDiff-Apache-2.0.txt` | Licença Apache 2.0 do VCDiff, a biblioteca de delta do `vnf release` | `LICENSE` do repositório SnowflakePowered/vcdiff, sem alteração |

O README do VERO cita a "Mozilla Public License" sem versão; por isso os dois textos vão no pacote. Os arquivos ficam em `licenses/` na raiz do repositório e entram em `VNForge.Core.csproj` como conteúdo copiado para a saída; a etapa `publicar` do CircleCI copia a pasta para cada pacote e confere que ela chegou.

## Corretor ortográfico (#482)

| Componente | Versão | Licença | Onde fica |
|---|---|---|---|
| [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell) | 7.0.1 (30/11/2025) | MPL 1.1, GPL 2.0 ou LGPL 2.1, à escolha de quem usa | Pacote NuGet de `VNForge.Core`, linkado como DLL |
| Dicionário pt-BR do projeto [VERO](https://github.com/LibreOffice/dictionaries/tree/master/pt_BR) (Raimundo Santos Moura e colaboradores), o do LibreOffice | 3.2 (arquivos da `master` do LibreOffice/dictionaries) | LGPLv3 e MPL, conforme o `README_pt_BR.txt` | `src/VNForge.Core/Spelling/Dictionaries/`, embutido no `VNForge.Core.dll` sem alteração |

### O que cada licença exige aqui

- **WeCantSpell.Hunspell.** O `license.txt` do pacote repete a licença do Hunspell original (MPL 1.1 / GPL 2.0 / LGPL 2.1, à escolha). O VNForge usa a biblioteca sob a **MPL 1.1**: o aviso fica neste arquivo, o código da biblioteca não foi alterado (o código-fonte está no repositório linkado) e o VNForge continua com a licença que o repositório declarar, porque a MPL tem copyleft por arquivo e só alcança os arquivos da própria biblioteca.
- **Dicionário VERO.** O `README_pt_BR.txt` diz que o dicionário é licenciado "sob os termos da Licença Pública Geral Menor versão 3 (LGPLv3) [...] e pela Mozilla Public License" e não informa a versão da MPL. Os arquivos `pt_BR.dic` e `pt_BR.aff` são copiados sem alteração; o texto do aviso e os créditos (`README_pt_BR.txt`) vão junto com o app e dentro da DLL (recurso `VNForge.Core.Spelling.README_pt_BR.txt`). O dicionário é dado, não código, e o VNForge não o modifica: as palavras do projeto ficam em memória (`ProjectDictionary`), nunca nos arquivos do dicionário. Quem quiser trocar o dicionário recompila o `VNForge.Core` com outro par `.dic` e `.aff` em `Spelling/Dictionaries/`.
- Nenhuma das duas licenças exige abrir o código do restante do VNForge.

### Como a escolha foi feita

Requisitos: roda em Windows, Linux e macOS sem binário nativo, tem alvo `net10.0` ou compatível, é mantido, aceita dicionário Hunspell e tem licença que permite distribuir o app (código aberto ou fechado).

| Alternativa | Licença | Por que não |
|---|---|---|
| [NHunspell](https://www.nuget.org/packages/NHunspell/) | GPL, LGPL ou MPL | Última versão de 2015, só .NET Framework, embrulha DLLs nativas por sistema |
| Wrappers P/Invoke do Hunspell em C++ | MPL, GPL ou LGPL | Exigem um binário nativo por sistema e arquitetura; complicam o pacote do app |
| [SymSpell](https://github.com/wolfgarbe/SymSpell) | MIT | Não lê dicionário Hunspell (`.dic` e `.aff`): trabalha com lista de frequência, sem afixos, e o português tem muita flexão |

Fontes conferidas: [WeCantSpell.Hunspell no NuGet](https://www.nuget.org/packages/WeCantSpell.Hunspell/) (nuspec: `license.txt`, alvos `net8.0`, `net10.0` e `netstandard2.0`), [license.txt do repositório](https://github.com/aarondandy/WeCantSpell.Hunspell/blob/main/license.txt), [README_pt_BR.txt do dicionário](https://github.com/LibreOffice/dictionaries/blob/master/pt_BR/README_pt_BR.txt), [MPL 1.1](https://www.mozilla.org/en-US/MPL/1.1/) e [LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html).

## Delta binário do `vnf release` (#540)

| Componente | Versão | Licença | Onde fica |
|---|---|---|---|
| [VCDiff](https://github.com/SnowflakePowered/vcdiff) (fork do [VCDiff de Metric](https://github.com/Metric/VCDiff), derivado de [open-vcdiff](https://github.com/google/open-vcdiff) e do [xdelta3](https://github.com/jmacd/xdelta)) | 5.0.0 (03/03/2026) | Apache-2.0 | Pacote NuGet de `VNForge.Patching`, linkado como DLL; vai no `vnf` (via `VNForge.Core`) e dentro do `vnf-instalador` |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | 0.50.7 | MIT | Dependência do VCDiff. Fixada acima da 0.46.3 que o VCDiff traz, que tem o aviso GHSA-6c8g-7p36-r338 (zip slip em `WriteToDirectory`, corrigido na 0.48.0). O patch não usa essa API |
| Newtonsoft.Json, Microsoft.IO.RecyclableMemoryStream | 13.0.4, 2.2.0 | MIT | Dependências do VCDiff |

### O que a licença exige aqui

- **VCDiff (Apache-2.0).** Permite uso comercial e distribuição, inclusive dentro de um instalador fechado, desde que a distribuição leve uma cópia da licença e os avisos de autoria. O texto está em `licenses/VCDiff-Apache-2.0.txt` (copiado do `LICENSE` do repositório, sem alteração) e vai na pasta `licenses/` do `vnf`. O instalador que o jogador recebe é outro pacote, então leva os textos dentro: `licenses/INSTALADOR-LICENCAS.txt` (Apache-2.0 completo do VCDiff, e as licenças MIT do SharpCompress, do Newtonsoft.Json e do Microsoft.IO.RecyclableMemoryStream; o VCDiff não tem arquivo NOTICE) fica embutido no `VNForge.Patching`, aparece em `vnf-instalador --licencas` e o `vnf release` o grava como `LICENCAS.txt` na pasta do release; o `LEIA-ME.txt` aponta para ele. O VNForge não altera o código da biblioteca.
- **Formato.** O delta é o VCDIFF da [RFC 3284](https://www.rfc-editor.org/rfc/rfc3284), com Adler-32 por janela (`ChecksumFormat.SDCH`), e depois comprimido em Brotli (da biblioteca padrão do .NET). Trocar de biblioteca muda só `DeltaCodec.cs` e a versão do formato em `PatchPackage`.

### Como a escolha foi feita

Requisitos: roda sem binário nativo, tem alvo `net10.0`, é mantida, licença permissiva compatível com distribuir um instalador, aceita arquivos de centenas de MB e produz o mesmo patch para as mesmas entradas.

| Alternativa | Licença | Por que não |
|---|---|---|
| [BsDiff](https://www.nuget.org/packages/BsDiff) (LogosBible/bsdiff.net) | MIT | Última versão em 07/2024, alvo `net8.0`. O bsdiff ordena sufixos do arquivo inteiro e usa bem mais memória que o arquivo (pacotes `.ypf` passam de 200 MB). Depende do SharpZipLib para o bzip2 |
| [Octodiff](https://www.nuget.org/packages/Octodiff) | Apache-2.0 (a API do GitHub devolve `NOASSERTION`) | Pacote marcado como obsoleto no NuGet ("no longer maintained"). É rsync: o delta depende do tamanho do bloco e fica maior que um VCDIFF em binários que deslocam bytes |
| xdelta3 por P/Invoke | Apache-2.0 | Exige um binário nativo por sistema e arquitetura; complica o instalador de um arquivo |
| MSDelta / PatchAPI do Windows | API do Windows | Só Windows, e o formato não é aberto |

Fontes conferidas: [VCDiff no NuGet](https://www.nuget.org/packages/VCDiff) (5.0.0, alvos `net6.0`, `net10.0`, `netstandard2.0/2.1`, README com a licença), [LICENSE do repositório](https://github.com/SnowflakePowered/vcdiff/blob/master/LICENSE) (a API do GitHub devolve `Apache-2.0`, último push em 03/03/2026), [BsDiff no NuGet](https://www.nuget.org/packages/BsDiff), [Octodiff no NuGet](https://www.nuget.org/packages/Octodiff), [aviso GHSA-6c8g-7p36-r338](https://github.com/advisories/GHSA-6c8g-7p36-r338). O determinismo foi conferido por teste (`Gerar_o_patch_duas_vezes_da_os_mesmos_bytes`), não pela documentação da biblioteca.
