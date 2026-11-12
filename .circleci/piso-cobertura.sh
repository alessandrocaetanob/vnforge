#!/usr/bin/env bash
# Piso de cobertura de linhas por projeto de produção: lê o coverage.xml do dotnet-coverage (formato xml, o que o Sonar
# lê), calcula a cobertura de cada src/VNForge.* e sai com 1 quando um projeto fica abaixo do piso de
# .circleci/piso-cobertura.txt, imprimindo projeto, valor medido e piso.
#
# Uso: bash .circleci/piso-cobertura.sh [coverage.xml] [piso-cobertura.txt]
#      bash .circleci/piso-cobertura.sh --gravar [coverage.xml]   imprime o conteúdo de um piso novo, a partir da medição
#
# O coverage.xml junta todos os processos de teste, então a cobertura de um projeto inclui o que os testes dos outros
# projetos exercitam nele (o Core é coberto também pelos testes do App e da Cli). Por isso o número difere da base
# de docs/testes/base-2026-11-11.md, que conta só os testes do próprio projeto.
#
# Conta as linhas dos arquivos de src/VNForge.<projeto>/, menos o sonar.coverage.exclusions de .circleci/config.yml, lido
# de lá a cada execução para as duas listas nunca divergirem. Uma linha vale como coberta se algum intervalo dela foi
# coberto, inteiro ou em parte; uma linha que aparece em mais de um método (lambda, máquina de estado) conta uma vez.
# O piso é em centésimos de ponto: o valor medido, arredondado para baixo.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

abort() {
  echo "ERRO: $*" >&2
  exit 1
}

record=0
if [[ "${1:-}" = --gravar ]]; then
  record=1
  shift
fi
xml="${1:-coverage.xml}"
floor_file="${2:-.circleci/piso-cobertura.txt}"
[[ -s "$xml" ]] || abort "$xml não existe ou está vazio."

# Só entendo "pasta/**" (tudo sob a pasta) e o caminho exato de um arquivo; qualquer outro padrão aborta em vez de
# medir de outro jeito que o Sonar.
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

# Projetos de produção: toda pasta de src/ com um .csproj próprio.
projects=""
for csproj in src/*/*.csproj; do
  projects+="$(basename "$(dirname "$csproj")") "
done
[[ -n "$projects" ]] || abort "nenhum projeto em src/."

measured="$(LC_ALL=C awk -v excl="$exclusions" '
  BEGIN { n = split(excl, e, "|") }
  function excluded(rel,   i) {
    if (rel ~ /\/(obj|bin)\//) return 1
    for (i = 1; i <= n; i++) {
      if (e[i] == "") continue
      if (substr(e[i], length(e[i])) == "/" ? index(rel, e[i]) == 1 : rel == e[i]) return 1
    }
    return 0
  }
  function flush(   i, p, key, f) {
    for (i = 1; i <= nr; i++) {
      split(rng[i], p, " ")
      f = file[p[1]]
      if (f == "") continue
      for (l = p[2]; l <= p[3]; l++) {
        key = f SUBSEP l
        seen[key] = 1
        if (p[4] != "no") hit[key] = 1
      }
    }
    delete rng; delete file; nr = 0
  }
  # Os intervalos vêm antes dos source_files dentro do módulo, e o id do arquivo vale só no módulo: guarda os
  # intervalos e resolve o caminho ao fechar o módulo.
  /<range / {
    match($0, /source_id="[0-9]+"/); sid = substr($0, RSTART + 11, RLENGTH - 12)
    match($0, /start_line="[0-9]+"/); s = substr($0, RSTART + 12, RLENGTH - 13)
    match($0, /end_line="[0-9]+"/); en = substr($0, RSTART + 10, RLENGTH - 11)
    match($0, /covered="[a-z]+"/); c = substr($0, RSTART + 9, RLENGTH - 10)
    rng[++nr] = sid " " s " " en " " c
    next
  }
  /<source_file / {
    match($0, /id="[0-9]+"/); sid = substr($0, RSTART + 4, RLENGTH - 5)
    match($0, / path="[^"]*"/); p = substr($0, RSTART + 7, RLENGTH - 8)
    gsub(/\\/, "/", p)
    if (match(p, /\/src\/VNForge\.[^\/]+\//)) {
      rel = substr(p, RSTART + 1)
      if (!excluded(rel)) file[sid] = rel
    }
    next
  }
  /<\/module>/ { flush(); next }
  END {
    for (k in seen) {
      split(k, kp, SUBSEP)
      match(kp[1], /^src\/[^\/]+/); proj = substr(kp[1], RSTART + 4, RLENGTH - 4)
      total[proj]++
      if (k in hit) covered[proj]++
    }
    for (proj in total) printf "%s %d %d\n", proj, covered[proj] + 0, total[proj]
  }' "$xml" | LC_ALL=C sort)"
[[ -n "$measured" ]] || abort "nenhuma linha de src/VNForge.* em $xml: o coverage.xml não é de uma rodada com testes?"

# Centésimos de ponto, para baixo, sem ponto flutuante na comparação.
hundredths() { awk -v c="$1" -v t="$2" 'BEGIN { printf "%d", int(c * 10000 / t) }'; }
format() { awk -v h="$1" 'BEGIN { printf "%d.%02d", int(h / 100), h % 100 }'; }

if [[ "$record" -eq 1 ]]; then
  echo "# Piso de cobertura de linhas por projeto, medido no job linux (coverage.xml da rodada inteira)."
  echo "# Gerado por: bash .circleci/piso-cobertura.sh --gravar coverage.xml. Uma linha por projeto de src/: nome e piso em %."
  for project in $projects; do
    line="$(awk -v p="$project" '$1 == p' <<< "$measured")"
    [[ -n "$line" ]] || abort "$project não aparece no $xml."
    read -r _ covered total <<< "$line"
    echo "$project $(format "$(hundredths "$covered" "$total")")"
  done
  exit 0
fi

[[ -f "$floor_file" ]] || abort "$floor_file não existe."
failed=0
printf '%-28s %10s %18s %10s\n' Projeto Medido Linhas Piso
for project in $projects; do
  line="$(awk -v p="$project" '$1 == p' <<< "$measured")"
  floor="$(awk -v p="$project" '$1 == p { print $2 }' "$floor_file" | tr -d '\r')"
  if [[ -z "$floor" ]]; then
    echo "ERRO: $project não tem piso em $floor_file (acrescente a linha '$project <piso>')." >&2
    failed=1
    continue
  fi
  if [[ -z "$line" ]]; then
    echo "ERRO: $project não aparece no $xml: nenhum teste exercitou o projeto." >&2
    failed=1
    continue
  fi
  read -r _ covered total <<< "$line"
  value="$(hundredths "$covered" "$total")"
  floor_h="$(awk -v f="$floor" 'BEGIN { printf "%d", int(f * 100 + 0.5) }')"
  printf '%-28s %9s%% %18s %9s%%\n' "$project" "$(format "$value")" "($covered de $total)" "$floor"
  if ((value < floor_h)); then
    echo "ERRO: $project: cobertura de linhas $(format "$value")% ($covered de $total), abaixo do piso de $floor%." >&2
    failed=1
  fi
done
if [[ "$failed" -ne 0 ]]; then
  echo "Piso de cobertura reprovado. Para subir o piso depois de um ganho, rode: bash .circleci/piso-cobertura.sh --gravar coverage.xml > .circleci/piso-cobertura.txt" >&2
fi
exit "$failed"
