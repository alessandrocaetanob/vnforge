#!/usr/bin/env bash
# Roda as suítes de tests/*.Tests em paralelo, cada uma num processo, e grava um JUnit por processo
# em test-results/ (o CircleCI lê essa pasta no store_test_results). A suíte do App, que sozinha
# levava 18 dos 23 minutos do job, é dividida em VNFORGE_APP_SHARDS processos (padrão 3), com as classes
# repartidas pelo tempo de .circleci/tempos-app.txt (ver docs/02-arquitetura.md, "Testes no CI (#578)").
#
# Chama `dotnet <dll>`, nunca o apphost: assim o mesmo script roda no Linux do CI e no Git Bash do Windows.
# Variáveis:
#   VNFORGE_TEST_CONFIGURATION  padrão Release
#   VNFORGE_APP_SHARDS          fatias do App, padrão 3
#   VNFORGE_SMALL_PARALLEL      quantos projetos pequenos rodam ao mesmo tempo, ao lado das fatias do App; padrão 2
#   VNFORGE_TIMINGS_FILE        tempos por classe do App, padrão .circleci/tempos-app.txt
#   VNFORGE_RENDER_DIR          base dos PNGs de render: com ela definida, cada processo grava em <dir>/<processo>/ e
#                               a pasta fica; sem ela, uma pasta temporária que o script apaga ao sair
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

configuration="${VNFORGE_TEST_CONFIGURATION:-Release}"
shards="${VNFORGE_APP_SHARDS:-3}"
small_parallel="${VNFORGE_SMALL_PARALLEL:-2}"
results=test-results
logs="$results/logs"
app=VNForge.App.Tests
timings_file="${VNFORGE_TIMINGS_FILE:-.circleci/tempos-app.txt}"

# O runner é reaproveitado entre jobs: um XML de outra rodada contaria testes que esta não rodou.
rm -rf "$results"
mkdir -p "$logs"

# Sem testes rodados, o coverage.xml sai vazio e o Sonar end não pode publicá-lo por cima da análise anterior.
abort() {
  echo "ERRO: $*" >&2
  rm -f .sonar-begun
  exit 1
}

[[ "$shards" =~ ^[1-9][0-9]*$ ]] || abort "VNFORGE_APP_SHARDS precisa ser um inteiro positivo (veio '$shards')."
[[ "$small_parallel" =~ ^[1-9][0-9]*$ ]] || abort "VNFORGE_SMALL_PARALLEL precisa ser um inteiro positivo (veio '$small_parallel')."

# Cada pasta tests/*.Tests tem que ter o dll: um projeto que não compilou não pode sumir da rodada em silêncio.
# O ConformanceKit é biblioteca, não casa com *.Tests e não entra.
declare -A dlls=()
for dir in tests/*.Tests; do
  name="$(basename "$dir")"
  dll="$dir/bin/$configuration/net10.0/$name.dll"
  [ -f "$dll" ] || abort "$dll não existe: compile a solução em $configuration antes."
  dlls[$name]="$dll"
done
[ -n "${dlls[$app]:-}" ] || abort "tests/$app não foi encontrado."

# Divide as classes do App. O -list classes imprime um banner e uma linha em branco antes das classes; só isso é
# descartado. Qualquer outra linha tem que parecer um nome de classe, senão a conferência de completude abaixo
# compararia as fatias com uma lista que já perdeu classes.
classes="$logs/$app.classes"
dotnet "${dlls[$app]}" -list classes > "$logs/$app.list" || abort "o -list classes do $app falhou."
tr -d '\r' < "$logs/$app.list" | sed -e '/^xUnit\.net /d' -e '/^[[:space:]]*$/d' | LC_ALL=C sort > "$classes"
[ -s "$classes" ] || abort "o -list classes não devolveu nenhuma classe do $app."
unexpected="$(grep -Ev '^[^[:space:]]+$' "$classes" || true)"
[ -z "$unexpected" ] || abort $'linha do -list classes que não é nome de classe:\n'"$unexpected"
class_total="$(wc -l < "$classes")"

# Soma, por classe, o atributo time dos <testcase> de um ou mais JUnit do xUnit v3. Saída: "classe segundos".
junit_class_times() {
  { grep -ho '<testcase [^>]*>' "$@" || true; } | LC_ALL=C awk '
    match($0, / classname="[^"]*"/) {
      cls = substr($0, RSTART + 12, RLENGTH - 13)
      if (match($0, / time="[^"]*"/)) t[cls] += substr($0, RSTART + 7, RLENGTH - 8)
      else if (!(cls in t)) t[cls] = 0
    }
    END { for (c in t) printf "%s %.1f\n", c, t[c] }' | LC_ALL=C sort
}

# Reparte as classes do App entre as fatias pelo tempo medido (maior classe primeiro, sempre na fatia mais leve).
# O tempo de cada classe vem de $timings_file, a soma dos tempos dos testes da classe numa rodada anterior. Essa soma
# passa de 3 vezes o tempo de parede porque os testes que desenham na janela esperam uns pelos outros na sessão headless
# do processo, mas a razão parede/soma ficou entre 0,26 e 0,28 nas três fatias do job 1717; a soma serve de peso.
# Classe sem tempo (nova, ou arquivo ausente) recebe a mediana das classes do mesmo grupo (Rendering, ou as demais) que
# têm tempo; sem nenhuma, 40 para Rendering e 4 para as demais. O grupo importa porque Rendering abre a janela
# principal a cada caso e leva 81% do tempo: um piso igual para todas deixaria uma fatia só com elas.
plan_split() {
  local known="$timings_file" weights="$logs/$app.weights"
  [ -f "$known" ] || known=/dev/null
  LC_ALL=C awk -v rdef=40 -v odef=4 '
    function median(v, n,   i, j, x) {
      for (i = 2; i <= n; i++) { x = v[i]; for (j = i - 1; j >= 1 && v[j] > x; j--) v[j + 1] = v[j]; v[j + 1] = x }
      return n % 2 ? v[(n + 1) / 2] : (v[n / 2] + v[n / 2 + 1]) / 2
    }
    FILENAME == ARGV[1] { if ($1 !~ /^#/ && NF >= 2 && $2 ~ /^[0-9.]+$/) known[$1] = $2; next }
    { names[++total] = $1 }
    END {
      for (i = 1; i <= total; i++) {
        c = names[i]; r = (c ~ /^VNForge\.App\.Tests\.Rendering\./)
        if (c in known) { if (r) rv[++rn] = known[c]; else ov[++on] = known[c] }
      }
      rmed = rn ? median(rv, rn) : rdef; omed = on ? median(ov, on) : odef
      for (i = 1; i <= total; i++) {
        c = names[i]; r = (c ~ /^VNForge\.App\.Tests\.Rendering\./)
        if (c in known) printf "%s %s medido\n", known[c], c
        else printf "%s %s estimado\n", (r ? rmed : omed), c
      }
    }' "$known" "$classes" | LC_ALL=C sort -k1,1gr -k2,2 > "$weights"
  for ((i = 0; i < shards; i++)); do : > "$logs/$app-$i.classes"; done
  LC_ALL=C awk -v n="$shards" -v dir="$logs" -v app="$app" '
    BEGIN { for (i = 0; i < n; i++) load[i] = 0 }
    { w = $1; c = $2; src = $3
      m = 0; for (i = 1; i < n; i++) if (load[i] < load[m]) m = i
      load[m] += w; cnt[m]++; if (src == "estimado") est[m]++
      print c > (dir "/" app "-" m ".classes") }
    END { for (i = 0; i < n; i++) printf "%d %d %.1f %d\n", i, cnt[i], load[i], est[i] }' "$weights" > "$logs/$app.plan"
  estimated="$(grep -c ' estimado$' "$weights" || true)"
}

# Remove CR e linhas vazias das fatias e monta a lista atribuída (ordenada, com repetições).
assign_shards() {
  local f
  shard_files=()
  for ((i = 0; i < shards; i++)); do
    f="$logs/$app-$i.classes"
    tr -d '\r' < "$f" | sed '/^[[:space:]]*$/d' | LC_ALL=C sort > "$f.tmp"
    mv "$f.tmp" "$f"
    shard_files+=("$f")
  done
  LC_ALL=C sort "${shard_files[@]}" > "$logs/$app.assigned"
}

refresh_hint="Para atualizar $timings_file, baixe o logs/tempos-app.txt dos artefatos do job linux da última rodada verde da main (job=<número>; curl -fsS https://circleci.com/api/v2/project/gh/alessandrocaetanob/vnforge/\$job/artifacts lista as URLs) e copie sobre ele."

estimated=0
plan_split
assign_shards

# Todas as classes, cada uma em exatamente uma fatia. Uma rodada parcial calada é o pior resultado possível aqui.
# Daqui em diante uma divergência é bug do script.
duplicated="$(uniq -d "$logs/$app.assigned")"
[ -z "$duplicated" ] || abort $'classe em mais de uma fatia:\n'"$duplicated"
missing="$(LC_ALL=C comm -23 "$classes" <(uniq "$logs/$app.assigned"))"
if [ -n "$missing" ]; then
  abort $'a lista de classes do App ficou incompleta ('"$(wc -l <<< "$missing")"$' de '"$class_total"$' classes sem fatia):\n'"$missing"
fi
extra="$(LC_ALL=C comm -13 "$classes" <(uniq "$logs/$app.assigned"))"
[ -z "$extra" ] || abort $'a divisão devolveu classe que o -list classes não tem:\n'"$extra"

if [ -f "$timings_file" ]; then
  echo "Divisão do App por tempo: $((class_total - estimated)) de $class_total classes com tempo em $timings_file, $estimated estimadas."
else
  echo "AVISO: $timings_file não existe; as $class_total classes do App foram estimadas, com Rendering espalhada pelas fatias." >&2
fi
if ((estimated * 10 > class_total)); then
  echo "AVISO: mais de 10% das classes do App estão sem tempo. $refresh_hint" >&2
fi
while read -r i count planned est; do
  printf '  fatia %s: %s classes, %s s planejados (soma dos tempos dos testes), %s estimadas\n' "$i" "$count" "$planned" "$est"
done < "$logs/$app.plan"

names=()
pids=()

# Roda um processo e grava "<código de saída> <segundos>" ao terminar; o arquivo é o que o laço de espera confere.
run_process() {
  local name="$1" start=$SECONDS rc=0 child
  shift
  "$@" > "$logs/$name.log" 2>&1 &
  child=$!
  # O subshell recebe o TERM do cancelamento e repassa ao dotnet, que senão sobrevive no runner reaproveitado.
  trap 'kill "$child" 2> /dev/null || true' INT TERM
  wait "$child" || rc=$?
  echo "$rc $((SECONDS - start))" > "$logs/$name.status"
}

# Cada processo desenha os PNGs numa pasta própria: duas fatias na mesma pasta se sobrescreveriam se uma classe
# repetisse um nome de arquivo, e um processo lendo o PNG de outro daria um resultado que depende da ordem.
# A pasta temporária é do script e some ao sair (o runner é reaproveitado e os PNGs enchiam o disco); a que o
# chamador define em VNFORGE_RENDER_DIR fica, é onde ele vai buscar os PNGs.
render_base="${VNFORGE_RENDER_DIR:-}"
if [ -z "$render_base" ]; then
  render_base="$(mktemp -d)"
  trap 'rm -rf "$render_base"' EXIT
fi

launch() {
  local name="$1"
  shift
  mkdir -p "$render_base/$name"
  names+=("$name")
  run_process "$name" env VNFORGE_RENDER_DIR="$render_base/$name" "$@" &
  pids+=("$!")
}

# Se o CircleCI cancela o job, os processos de teste não podem ficar vivos no runner: cada subshell mata o seu
# dotnet, e o script espera por eles antes de sair com erro.
cancel() {
  trap - INT TERM
  echo "Cancelado: encerrando os processos de teste." >&2
  kill "${pids[@]}" 2> /dev/null || true
  wait || true
  exit 130
}
trap cancel INT TERM

# As fatias do App saem primeiro: são o caminho crítico do passo.
for ((i = 0; i < shards; i++)); do
  # Fatia vazia não roda: sem -class o xUnit executaria a suíte inteira.
  [ -s "$logs/$app-$i.classes" ] || continue
  class_args=()
  while IFS= read -r class; do
    [ -n "$class" ] && class_args+=(-class "$class")
  done < "$logs/$app-$i.classes"
  launch "$app-$i" dotnet "${dlls[$app]}" -noLogo "${class_args[@]}" -result-junit "$results/$app-$i.xml"
done

# Os demais projetos entram numa fila e rodam no máximo VNFORGE_SMALL_PARALLEL de cada vez. O container do job tem
# 4 CPUs e 6 GB e um processo do App com a suíte inteira chega a 3,7 GB; com as 3 fatias, 8 projetos em paralelo
# eram 11 processos disputando 4 CPUs. Os 8 somam cerca de 290 s de parede, então com 2 por vez a fila esvazia em
# 150 a 200 s, bem antes das fatias do App. A fila sai do mais lento para o mais rápido, para a cauda ficar curta; a
# ordem é a do job 1717 (Cli 94 s, Core 57, Installer 40, Patching 31, AI 30, YuRis 27, Vndb 7, Platform.Windows 3).
# Projeto que não está nesta lista (novo) entra antes dos conhecidos, por nome, porque não se sabe quanto leva.
known_order=(Cli Core Installer Patching AI Engines.YuRis Vndb Platform.Windows)
small=()
declare -A is_small=()
for name in "${!dlls[@]}"; do
  [ "$name" = "$app" ] && continue
  is_small[$name]=1
done
while IFS= read -r name; do small+=("$name"); done < <(
  printf '%s\n' "${!is_small[@]}" | LC_ALL=C sort | while IFS= read -r name; do
    [ -n "$name" ] || continue
    found=0
    for k in "${known_order[@]}"; do
      if [ "$name" = "VNForge.$k.Tests" ]; then found=1; fi
    done
    if [ "$found" -eq 0 ]; then echo "$name"; fi
  done
  for k in "${known_order[@]}"; do
    if [ -n "${is_small[VNForge.$k.Tests]:-}" ]; then echo "VNForge.$k.Tests"; fi
  done
)
next_small=0

echo "Rodando o App em $shards fatias ($class_total classes) e ${#small[@]} projetos pequenos, $small_parallel por vez."
# O passo tem no_output_timeout de 30m: sem a linha de progresso, uma fatia lenta calaria o job.
tick=$SECONDS
while :; do
  running=()
  running_small=0
  for name in "${names[@]}"; do
    if [ ! -f "$logs/$name.status" ]; then
      running+=("$name")
      if [ -n "${is_small[$name]:-}" ]; then running_small=$((running_small + 1)); fi
    fi
  done
  while ((next_small < ${#small[@]} && running_small < small_parallel)); do
    name="${small[$next_small]}"
    launch "$name" dotnet "${dlls[$name]}" -noLogo -result-junit "$results/$name.xml"
    running+=("$name")
    running_small=$((running_small + 1))
    next_small=$((next_small + 1))
  done
  [ "${#running[@]}" -gt 0 ] || break
  if ((SECONDS - tick >= 120)); then
    echo "[$((SECONDS / 60)) min] rodando: ${running[*]}"
    tick=$SECONDS
  fi
  sleep 1
done
wait

count() { { grep -o "$1" "$2" || true; } | wc -l | tr -d ' '; }

failed=0
total_tests=0
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  xml="$results/$name.xml"
  tests=0 failures=0 skipped=0
  if [ -f "$xml" ]; then
    tests="$(count '<testcase ' "$xml")"
    failures=$(($(count '<failure[ >]' "$xml") + $(count '<error[ >]' "$xml")))
    skipped="$(count '<skipped' "$xml")"
  fi
  total_tests=$((total_tests + tests))
  echo
  echo "===== $name: saída $rc, ${seconds}s, $tests testes, $failures falhas, $skipped ignorados ====="
  cat "$logs/$name.log"
  # Código 0 sem JUnit é processo que não rodou teste nenhum (ou saiu antes de gravar): também reprova.
  if [ "$rc" -ne 0 ] || [ ! -f "$xml" ]; then
    failed=1
    echo ">>> $name reprovou (saída $rc, JUnit $([ -f "$xml" ] && echo gravado || echo ausente))."
  fi
done

# Tempos medidos nesta rodada, no formato de $timings_file: é o arquivo que se copia dos artefatos de uma rodada da
# main para .circleci/tempos-app.txt. Só vale de rodada verde, com todas as classes do App rodadas.
app_xmls=()
for ((i = 0; i < shards; i++)); do
  if [ -f "$results/$app-$i.xml" ]; then app_xmls+=("$results/$app-$i.xml"); fi
done
if [ "${#app_xmls[@]}" -gt 0 ]; then
  {
    echo "# Tempo por classe do $app: soma do time de cada teste do JUnit, em segundos (classe segundos)."
    echo "# Gerado por .circleci/run-tests.sh; para atualizar, copie o tempos-app.txt dos artefatos de uma rodada verde da main."
    junit_class_times "${app_xmls[@]}"
  } > "$logs/tempos-app.txt"
fi

echo
echo "Resumo: $total_tests testes em ${#names[@]} processos, $SECONDS s no total."
for name in "${names[@]}"; do
  read -r rc seconds < "$logs/$name.status"
  printf '  %-36s saída %s, %ss\n' "$name" "$rc" "$seconds"
done

# Planejado contra medido, por fatia, a partir do tempos-app.txt que acabou de ser gerado. A razão parede/soma tem que
# ficar parecida entre as fatias: se uma se afasta das outras (por exemplo, fatia só de Rendering contra fatia sem
# Rendering), a soma deixou de servir de peso e o peso passa a ser o tempo de parede por classe. O aviso sai quando a
# soma medida de uma fatia difere da planejada em mais de 25%, sinal de que o arquivo de tempos envelheceu.
if [ "${#app_xmls[@]}" -gt 0 ]; then
  walls=""
  slice_files=()
  for ((i = 0; i < shards; i++)); do
    slice_files+=("$logs/$app-$i.classes")
    if [ -f "$results/$app-$i.xml" ]; then
      read -r _ seconds < "$logs/$app-$i.status"
      walls+=" $i:$seconds"
    fi
  done
  echo "Fatias do App, planejado contra medido (soma dos tempos dos testes, em s):"
  LC_ALL=C awk -v walls="$walls" -v n="$shards" '
    BEGIN { split(walls, w, " "); for (k in w) { split(w[k], p, ":"); wall[p[1]] = p[2] } }
    FILENAME == ARGV[1] { planned[$1] = $3; next }
    FILENAME == ARGV[2] { if ($1 !~ /^#/ && NF >= 2) t[$1] = $2; next }
    { sum[FILENAME] += t[$1] }
    END {
      for (i = 0; i < n; i++) {
        if (!(i in wall)) continue
        m = sum[ARGV[i + 3]]; pl = planned[i]
        printf "  fatia %d: planejado %.0f, medido %.0f, parede %ss, parede/soma %s\n", i, pl, m, wall[i], (m > 0 ? sprintf("%.2f", wall[i] / m) : "-");
        if (pl > 0 && (m - pl > pl / 4 || pl - m > pl / 4)) stale = 1
      }
      if (stale) print "STALE"
    }' "$logs/$app.plan" "$logs/tempos-app.txt" "${slice_files[@]}" > "$logs/resumo-fatias.txt"
  grep -vx STALE "$logs/resumo-fatias.txt" || true
  if grep -qx STALE "$logs/resumo-fatias.txt"; then
    echo "AVISO: a soma medida de uma fatia difere da planejada em mais de 25%. $refresh_hint" >&2
  fi
fi
exit "$failed"
