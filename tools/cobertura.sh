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
#                               repartidas pelo tempo de .circleci/tempos-app.txt (sem o arquivo, em rodízio). O merge do
#                               dotnet-coverage descarta os ramos, então as fatias são unidas aqui: a linha vale se alguma
#                               fatia a cobriu, e os ramos de uma linha valem o maior número coberto por uma só fatia. Com
#                               mais de uma fatia os ramos são, por isso, um mínimo; compare sempre com o mesmo número de
#                               fatias (e a mesma divisão) da base.
#   VNFORGE_COBERTURA_DIR       onde ficam os arquivos de cobertura; padrão .tmp/cobertura
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
dll_name="$(grep -o '<AssemblyName>[^<]*' "src/$assembly/$assembly.csproj" | head -n1 | cut -d'>' -f2 | tr -d '\r')"
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
  case "$pattern" in
    */\*\*) exclusions+="${pattern%\*\*}"$'\n' ;;
    *\**) abort "padrão de exclusão que não sei aplicar: $pattern" ;;
    *) exclusions+="$pattern"$'\n' ;;
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
  local timings=/dev/null
  [[ -f .circleci/tempos-app.txt ]] && timings=.circleci/tempos-app.txt
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
    }' "$timings" "$list"
}

if [[ "$slices" -gt 1 ]]; then slice_classes; fi

failed=0
coverage_files=()
for ((i = 0; i < slices; i++)); do
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
  coverage_files+=("$file")
done


LC_ALL=C awk -v assembly="$assembly" -v excl="$exclusions" '
  BEGIN {
    n = split(excl, e, "\n")
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
    }
  }
  END {
    for (k in seen) {
      if (k in covered) lc++
      if (k in total) { bt += total[k]; bc += hit[k] }
    }
    if (lines == 0) { print "ERRO: nenhuma linha de " assembly " nos relatórios; o dll instrumentado não é o do projeto?" > "/dev/stderr"; exit 2 }
    printf "%s  linhas %.2f%% (%d/%d)  ramos %.2f%% (%d/%d)\n", assembly,
      (lines > 0 ? 100 * lc / lines : 0), lc, lines, (bt > 0 ? 100 * bc / bt : 0), bc, bt
  }' "${coverage_files[@]}"

if [[ "$failed" -ne 0 ]]; then
  abort "os testes de $tests_name reprovaram; os números acima não valem."
fi
