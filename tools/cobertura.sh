#!/usr/bin/env bash
# Mede a cobertura de linhas e de ramos de um projeto de testes sobre o código de produção que ele testa, do mesmo
# jeito em toda tarefa da suíte nova (docs/04-testes.md, "Cobertura").
#
# Uso: tools/cobertura.sh <projeto> [-- argumentos do runner do xUnit]
#   <projeto>  Core, VNForge.Core ou VNForge.Core.Tests (idem para App, Cli, AI, Vndb, Installer, Platform.Windows,
#              Patching e Engines.YuRis). O código de produção medido é o do projeto src/VNForge.<projeto>.
#
# Variáveis:
#   VNFORGE_TEST_CONFIGURATION  padrão Release; o dll de tests/VNForge.<projeto>.Tests/bin/<configuração> já tem que existir
#   VNFORGE_FATIAS              fatias do projeto, rodadas uma de cada vez; padrão 1. Serve para o App, cujo processo
#                               inteiro com cobertura gasta muita memória e tempo. Cada fatia leva um -class por classe,
#                               repartidas em rodízio, a maior primeiro (sem tempos, todas pesam 1). O merge do
#                               dotnet-coverage descarta os ramos, então as fatias são unidas aqui: a linha vale se alguma
#                               fatia a cobriu (exato). Os ramos de uma linha só se sabe a quantidade coberta por fatia, não
#                               quais, então a saída traz um mínimo (o maior número coberto por uma só fatia, que é o número
#                               principal) e um máximo (a soma entre fatias, até o total da linha). O valor exato só sai de
#                               um processo único (VNFORGE_FATIAS=1).
#   VNFORGE_FATIA               índice (de 0) de uma fatia só, para dividir a medição em chamadas curtas: roda essa fatia e
#                               soma as que as chamadas anteriores deixaram em VNFORGE_COBERTURA_DIR, avisando se faltam.
#                               Com "juntar" não roda nada e só soma as fatias já medidas, sem recalcular a divisão; aborta
#                               se faltar fatia ou se as medições forem de outra partição. A partição (número de fatias e
#                               hash das listas de classes) fica em <projeto>.particao: uma fatia medida com partição
#                               diferente apaga as medições velhas. Sem a variável, todas as fatias rodam e as medições
#                               antigas são apagadas.
#   VNFORGE_SO_RESUMIR          caminho de um Cobertura já gravado (de um processo único): não roda teste, só calcula os
#                               números dele com as mesmas exclusões. O projeto ainda é o argumento.
#   VNFORGE_COBERTURA_DIR     onde ficam os arquivos de cobertura; padrão .tmp/cobertura
#
# Como mede: dotnet-coverage (18.11.2, a versão do CI) roda o projeto de testes e grava um Cobertura por fatia. Só o dll
# de produção do projeto é instrumentado, e as linhas e ramos contados são os dos arquivos de src/VNForge.<projeto>,
# menos o sonar.coverage.exclusions de .circleci/config.yml, lido de lá a cada execução para as duas listas nunca
# divergirem. Uma linha de código que aparece em mais de um método (lambda, máquina de estado) conta uma vez; com mais de
# uma cobertura de ramos para a mesma linha, vale a maior.
#
# Saída, na última linha: "VNForge.Core  linhas 87.30% (1234/1414)  ramos 75.10% (400/532)", com ponto decimal.
# Sai com 1 se o dotnet-coverage ou os testes falharem; os números só valem com saída 0.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

abort() {
  echo "ERRO: $*" >&2
  exit 1
}

[[ $# -ge 1 ]] || abort "uso: tools/cobertura.sh <projeto> [-- argumentos do runner]"
project="$1"
shift
if [[ $# -gt 0 ]]; then
  [[ "$1" = "--" ]] || abort "depois do projeto só vale '--' seguido dos argumentos do runner (veio '$1')."
  shift
fi
runner_args=("$@")

project="${project#VNForge.}"
project="${project%.Tests}"
assembly="VNForge.$project"
tests_name="$assembly.Tests"
[[ -d "src/$assembly" ]] || abort "src/$assembly não existe."
# O dll nem sempre leva o nome da pasta: o Cli é vnf.dll e o Installer é vnf-instalador.dll.
dll_name="$(grep -o '<AssemblyName>[^<]*' "src/$assembly/$assembly.csproj" | head -n1 | cut -d'>' -f2 | tr -d '\r' || true)"
dll_name="${dll_name:-$assembly}"
[[ -d "tests/$tests_name" ]] || abort "tests/$tests_name não existe."

configuration="${VNFORGE_TEST_CONFIGURATION:-Release}"
slices="${VNFORGE_FATIAS:-1}"
out="${VNFORGE_COBERTURA_DIR:-.tmp/cobertura}"
dll="tests/$tests_name/bin/$configuration/net10.0/$tests_name.dll"
[[ "$slices" =~ ^[1-9][0-9]*$ ]] || abort "VNFORGE_FATIAS precisa ser um inteiro positivo (veio '$slices')."
[[ -f "$dll" ]] || abort "$dll não existe: compile a solução em $configuration antes."

expected_version=18.11.2
if command -v dotnet-coverage > /dev/null 2>&1; then
  coverage=dotnet-coverage
elif [[ -x ".tmp/tools/dotnet-coverage" || -x ".tmp/tools/dotnet-coverage.exe" ]]; then
  coverage=".tmp/tools/dotnet-coverage"
else
  abort "dotnet-coverage não encontrado. Instale: dotnet tool install --tool-path .tmp/tools dotnet-coverage --version $expected_version"
fi
version="$("$coverage" --version | tr -d '\r' | head -n1)"
[[ "$version" == "$expected_version"* ]] || echo "AVISO: dotnet-coverage $version; o CI usa $expected_version." >&2

# As exclusões vêm do config.yml. Só entendo dois formatos, e qualquer outro aborta em vez de medir de outro jeito:
# "pasta/**" (tudo sob a pasta) e o caminho exato de um arquivo.
raw="$(grep -o "sonar\.coverage\.exclusions=[^']*" .circleci/config.yml | head -n1 | cut -d= -f2-)"
[[ -n "$raw" ]] || abort "sonar.coverage.exclusions não encontrado em .circleci/config.yml."
exclusions=""
IFS=',' read -ra patterns <<< "$raw"
for pattern in "${patterns[@]}"; do
  [[ -n "$pattern" ]] || continue
  case "$pattern" in
    */\*\*)
      base="${pattern%\*\*}"
      [[ "$base" != *[\*\?]* ]] || abort "padrão de exclusão que não sei aplicar (só 'pasta/**' sem curinga antes): $pattern"
      exclusions+="$base|"
      ;;
    *[\*\?]*) abort "padrão de exclusão que não sei aplicar (só 'pasta/**' ou o caminho exato): $pattern" ;;
    *) exclusions+="$pattern|" ;;
  esac
done

mkdir -p "$out"
settings="$out/$tests_name.settings.xml"
bs='\'
module_regex=".*${dll_name//./${bs}.}${bs}.dll\$"
cat > "$settings" << EOF
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <CodeCoverage>
    <ModulePaths>
      <Include><ModulePath>$module_regex</ModulePath></Include>
    </ModulePaths>
  </CodeCoverage>
</Configuration>
EOF

# Reparte as classes entre as fatias, a maior primeiro, sempre na fatia mais leve.
slice_classes() {
  local list="$out/$tests_name.classes"
  dotnet "$dll" -list classes | tr -d '\r' | sed -e '/^xUnit\.net /d' -e '/^[[:space:]]*$/d' | LC_ALL=C sort > "$list"
  [[ -s "$list" ]] || abort "o -list classes não devolveu nenhuma classe de $tests_name."
  rm -f "$out/$tests_name".slice-*.classes
  LC_ALL=C awk -v n="$slices" -v prefix="$out/$tests_name.slice-" '
    FILENAME == ARGV[1] { if ($1 !~ /^#/ && NF >= 2) t[$1] = $2; next }
    { names[++count] = $1 }
    END {
      for (i = 1; i <= count; i++) { w[i] = (names[i] in t) ? t[names[i]] : 1 }
      for (i = 1; i <= count; i++) {
        best = 0
        for (j = 1; j <= count; j++) if (!used[j] && (best == 0 || w[j] > w[best])) best = j
        used[best] = 1
        s = 0
        for (k = 1; k < n; k++) if (load[k] < load[s]) s = k
        load[s] += w[best]
        print names[best] > (prefix s ".classes")
      }
    }' /dev/null "$list"
}

# A partição é o número de fatias mais um hash das listas de classes de cada fatia. Fica gravada ao lado das medições:
# fatias de partições diferentes não se somam (cobririam classes repartidas de outro jeito).
partition_id() {
  if [[ "$slices" -eq 1 ]]; then
    echo "1 -"
    return
  fi
  local i
  for ((i = 0; i < slices; i++)); do
    cat "$out/$tests_name.slice-$i.classes" 2> /dev/null || true
    echo "--"
  done | cksum | cut -d' ' -f1 | sed "s/^/$slices /"
}

failed=0
only="${VNFORGE_FATIA:-}"
partition_file="$out/$tests_name.particao"
summarize_only="${VNFORGE_SO_RESUMIR:-}"
if [[ -n "$summarize_only" ]]; then
  # Não roda teste: só resume um Cobertura que um processo único já gravou, com as mesmas exclusões.
  [[ -f "$summarize_only" ]] || abort "VNFORGE_SO_RESUMIR aponta para um arquivo que não existe: $summarize_only"
  coverage_files=("$summarize_only")
  only=-1
elif [[ "$only" = juntar ]]; then
  # Só soma: não recalcula as fatias, usa as listas e as medições que já estão em disco.
  only=-1
  [[ -f "$partition_file" ]] || abort "sem medições anteriores em $out: rode as fatias antes de 'juntar'."
  [[ "$(cat "$partition_file")" == "$(partition_id)" ]] || abort "as medições em $out são de outra partição (VNFORGE_FATIAS ou lista de classes mudou); rode as fatias de novo."
  for ((i = 0; i < slices; i++)); do
    [[ -f "$out/$tests_name.$i.cobertura.xml" ]] || abort "falta a fatia $i para 'juntar'."
  done
else
  if [[ "$slices" -gt 1 ]]; then slice_classes; fi
  if [[ -n "$only" ]]; then
    [[ "$only" =~ ^[0-9]+$ && "$only" -lt "$slices" ]] || abort "VNFORGE_FATIA precisa ser 'juntar' ou um índice de 0 a $((slices - 1)) (veio '$only')."
  fi
  # Partição nova (ou rodada inteira): as medições velhas saem, para nunca se somarem às novas.
  if [[ -z "$only" || ! -f "$partition_file" || "$(cat "$partition_file")" != "$(partition_id)" ]]; then
    rm -f "$out/$tests_name".*.cobertura.xml
    partition_id > "$partition_file"
  fi
fi
for ((i = 0; i < slices; i++)); do
  [[ -z "$only" || "$i" -eq "$only" ]] || continue
  command_line="dotnet $dll -noLogo"
  if [[ "$slices" -gt 1 ]]; then
    slice_list="$out/$tests_name.slice-$i.classes"
    [[ -s "$slice_list" ]] || continue
    while IFS= read -r class; do
      [[ -n "$class" ]] && command_line+=" -class $class"
    done < "$slice_list"
    echo "Fatia $((i + 1)) de $slices: $(wc -l < "$slice_list" | tr -d ' ') classes."
  fi
  if [[ ${#runner_args[@]} -gt 0 ]]; then command_line+=" ${runner_args[*]}"; fi
  file="$out/$tests_name.$i.cobertura.xml"
  rm -f "$file"
  "$coverage" collect --settings "$settings" -f cobertura -o "$file" "$command_line" || failed=1
  [[ -f "$file" ]] || abort "o dotnet-coverage não gravou $file."
done

# Soma o que já foi medido, inclusive de chamadas anteriores com VNFORGE_FATIA.
if [[ -z "$summarize_only" ]]; then
  coverage_files=()
  measured=""
  for ((i = 0; i < slices; i++)); do
    file="$out/$tests_name.$i.cobertura.xml"
    if [[ -f "$file" ]]; then
      coverage_files+=("$file")
      measured+="$i "
    fi
  done
  [[ ${#coverage_files[@]} -gt 0 ]] || abort "nenhuma fatia medida."
  if [[ ${#coverage_files[@]} -lt "$slices" ]]; then
    echo "AVISO: só as fatias ${measured% } de $slices foram medidas; o número abaixo é parcial." >&2
  fi
fi

LC_ALL=C awk -v assembly="$assembly" -v excl="$exclusions" -v nfiles="${#coverage_files[@]}" '
  BEGIN {
    n = split(excl, e, "|")
    scope = "src/" assembly "/"
  }
  function excluded(rel,   i) {
    if (index(rel, scope) != 1) return 1
    for (i = 1; i <= n; i++) {
      if (e[i] == "") continue
      if (substr(e[i], length(e[i])) == "/" ? index(rel, e[i]) == 1 : rel == e[i]) return 1
    }
    return 0
  }
  /<class / {
    file = ""
    if (match($0, /filename="[^"]*"/)) {
      f = substr($0, RSTART + 10, RLENGTH - 11)
      gsub(/\\/, "/", f)
      if (match(f, /src\/VNForge\.[^\/]+\//)) {
        rel = substr(f, RSTART)
        if (!excluded(rel)) file = rel
      }
    }
    next
  }
  /<line / && file != "" {
    match($0, /number="[0-9]+"/); num = substr($0, RSTART + 8, RLENGTH - 9)
    match($0, /hits="[0-9]+"/); hits = substr($0, RSTART + 6, RLENGTH - 7) + 0
    key = file SUBSEP num
    if (!(key in seen)) { seen[key] = 1; lines++ }
    if (hits > 0) covered[key] = 1
    if (match($0, /condition-coverage="[^"]*"/)) {
      cc = substr($0, RSTART, RLENGTH)
      match(cc, /\([0-9]+\/[0-9]+\)/)
      split(substr(cc, RSTART + 1, RLENGTH - 2), p, "/")
      if (!(key in total) || p[2] > total[key]) total[key] = p[2]
      if (p[1] > hit[key]) hit[key] = p[1]
      pf = key SUBSEP FILENAME
      if (p[1] > perfile[pf]) { sumhit[key] += p[1] - perfile[pf]; perfile[pf] = p[1] }
    }
  }
  END {
    for (k in seen) {
      if (k in covered) lc++
      if (k in total) { bt += total[k]; bc += hit[k]; bu += (sumhit[k] < total[k] ? sumhit[k] : total[k]) }
    }
    if (lines == 0) { print "ERRO: nenhuma linha de " assembly " nos relatórios; o dll instrumentado não é o do projeto?" > "/dev/stderr"; exit 2 }
    printf "%s  linhas %.2f%% (%d/%d)  ramos %.2f%% (%d/%d)", assembly,
      100 * lc / lines, lc, lines, (bt > 0 ? 100 * bc / bt : 0), bc, bt
    if (nfiles > 1) printf "  (fatias: ramos no mínimo %.2f%%, no máximo %.2f%%)", (bt > 0 ? 100 * bc / bt : 0), (bt > 0 ? 100 * bu / bt : 0)
    printf "\n"
  }' "${coverage_files[@]}"

if [[ "$failed" -ne 0 ]]; then
  abort "os testes de $tests_name reprovaram; os números acima não valem."
fi
