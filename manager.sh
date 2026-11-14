# =============================================================================
# manager.sh — desk-snip 项目统一管理脚本（本项目所有命令的唯一来源）
# 用法：./manager.sh <command> [args]    (Windows: sh manager.sh <command>，在 Git Bash 里)
#
# 本脚本只做分发：真正的逻辑在 scripts/*.ps1（Windows PowerShell 5.1 / PowerShell 7 都能跑）。
# 构建 / 运行只支持 Windows（WPF / WinForms）；其它系统只有 check-docs 和 help 可用。
# 新增或修改命令时，同步 README 的命令表与 AGENTS.md §4（sh check-docs.sh 会核对 help 与 README 是否一致）。
# =============================================================================
# 被 `sh manager.sh` 调起且 sh 不是 bash（如 Debian 的 dash）时，自动改用 bash 重跑。
if [ -z "${BASH_VERSION:-}" ]; then exec bash "$0" "$@"; fi
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${PROJECT_ROOT}"   # 从任何目录调用都在项目根执行

log()  { printf "\033[1;34m[manager]\033[0m %s\n" "$*"; }
ok()   { printf "\033[1;32m[ ok  ]\033[0m %s\n" "$*"; }
warn() { printf "\033[1;33m[warn ]\033[0m %s\n" "$*"; }
err()  { printf "\033[1;31m[err  ]\033[0m %s\n" "$*" >&2; }

require_windows() {
  case "$(uname -s 2>/dev/null)" in
    MINGW*|MSYS*|CYGWIN*) ;;
    *) err "此命令仅支持 Windows（WPF / WinForms）；当前系统可用：check-docs、help"; exit 2 ;;
  esac
}

find_ps() {
  if command -v pwsh >/dev/null 2>&1; then echo pwsh
  elif command -v powershell.exe >/dev/null 2>&1; then echo powershell.exe
  else return 1; fi
}

PS_ARGS=()
to_ps_args() {
  PS_ARGS=()
  local arg out part
  local -a parts
  for arg in "$@"; do
    if [[ "${arg}" == --?* ]]; then
      out=""
      IFS='-' read -ra parts <<< "${arg#--}"
      for part in "${parts[@]}"; do out+="${part^}"; done
      PS_ARGS+=("-${out}")
    else
      PS_ARGS+=("${arg}")
    fi
  done
}

run_ps() {
  local script="$1"; shift
  require_windows
  local ps
  ps="$(find_ps)" || { err "找不到 pwsh / powershell.exe"; exit 1; }
  to_ps_args "$@"
  "${ps}" -NoProfile -ExecutionPolicy Bypass -File "scripts/${script}.ps1" ${PS_ARGS[@]+"${PS_ARGS[@]}"}
}

cmd_check_docs() { sh "${PROJECT_ROOT}/check-docs.sh" "$@"; }

cmd_help() {
  cat <<EOF
desk-snip — manager.sh（本项目所有命令的唯一来源）

用法：
  ./manager.sh <command> [args]    (Windows: sh manager.sh <command>)

命令：
  build        构建解决方案（Debug；--configuration Release 出 Release）
  assets       从矢量源生成蓝色应用图标与多尺寸 ICO
  dev          构建 Debug 并启动窗口
  run          启动已构建的应用（没有构建产物时先构建）
  start        同 run（后台启动并记录 .runtime/app.json）
  stop         请求应用正常退出并等待最多 30 秒（绝不强杀）
  restart      stop 后再 start
  status       查看是否在运行（pid、exe、启动时间）
  test         dotnet test (optional: --filter test-expression)
  verify       交付前总检：test + 严格编译（warnaserror）+ check-docs
  package      出包到 dist/dev（Debug 调试包）与 dist/release（unpack/ + 免安装 zip + SHA256SUMS）
  clean        清理 bin/obj、dist/、.runtime/staging
  doctor       检查 .NET SDK / PowerShell / 目标框架
  check-docs   文档体系机械体检（同 sh check-docs.sh）
  help         显示本帮助

双击脚本（Windows 资源管理器里）：dev.bat run.bat stop.bat build.bat test.bat package.bat clean.bat，详见 README。
EOF
}

main() {
  local cmd="${1:-help}"
  shift || true
  case "${cmd}" in
    build)      run_ps build "$@" ;;
    assets)     run_ps assets "$@" ;;
    dev)        run_ps dev "$@" ;;
    run|start)  run_ps desktop start "$@" ;;
    stop)       run_ps desktop stop "$@" ;;
    restart)    run_ps desktop restart "$@" ;;
    status)     run_ps desktop status "$@" ;;
    test)       run_ps test "$@" ;;
    verify)     run_ps verify "$@" ;;
    package|pack) run_ps package "$@" ;;
    clean)      run_ps clean "$@" ;;
    doctor)     run_ps doctor "$@" ;;
    check-docs) cmd_check_docs "$@" ;;
    help|-h|--help) cmd_help ;;
    *) err "未知命令：${cmd}"; cmd_help; exit 2 ;;
  esac
}

main "$@"
