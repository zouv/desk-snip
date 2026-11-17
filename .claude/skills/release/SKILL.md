---
name: release
description: 一条命令发布 DeskSnip：校验并更新版本号、归档 CHANGELOG、完整回归与打包、提交并推送 main 和 vX.Y.Z 标签，并通过 GitHub CLI 上传 GitHub Release 资产。仅在用户明确输入 /release X.Y.Z 或明确要求整套发版时使用，不要自动触发。
---

# release · 发布 DeskSnip

> **输入**：`$ARGUMENTS` 必须是版本号 `X.Y.Z`，例如 `/release 0.4.0`。缺少版本号、格式不对或版本不高于当前版本时停止，不要猜。下文 `$VERSION` 均指 `$ARGUMENTS`。
>
> **上传目标**：默认是 `origin` 指向的 GitHub 仓库的 GitHub Releases（当前为 `zouv/desk-snip`），使用已登录的 `gh`。上传其他服务器前必须先修改本节，不能猜地址。
>
> 用户输入本命令即表示授权：修改版本与 CHANGELOG、提交并推送、创建标签、打包并上传。仍禁止强推、改写已推送历史、提交凭据/用户数据/本机路径，或结束用户未确认的 DeskSnip 进程。

## 0. 前置检查（失败即停，不改版本）

1. 必须位于项目根，当前分支为 `main`：
   ```bash
   git rev-parse --show-toplevel
   git branch --show-current
   ```
   不是 `main` 则停止，让用户先合并/切换。
2. 校验 `$ARGUMENTS` 匹配 `^[0-9]+\.[0-9]+\.[0-9]+$`；读取 `Directory.Build.props` 的 `<Version>`，按数值比较确认新版本更高。
3. 校验上传身份与仓库：
   ```bash
   git remote get-url origin
   gh auth status
   gh repo view --json nameWithOwner -q .nameWithOwner
   ```
   origin 与 gh 当前仓库必须一致。
4. 检查工作树：
   ```bash
   git status --porcelain
   ```
   只应包含本次发布预期文件。出现 `.env`、`.runtime/`、`dist/`、`bin/`、`obj/`、数据库、日志、用户数据、本机绝对路径或凭据时停止，不得提交。
5. 检查运行实例：
   ```bash
   sh manager.sh status
   ```
   有实例则停止并提示用户自行关闭后重跑；不要 `Stop-Process`、`taskkill` 或擅自调用 stop。实例必须由用户明确处理，避免打包占用文件或丢失未保存状态。
6. 确认 `CHANGELOG.md` 存在 `## [未发布]` 且正文非空；为空则停止。确认本地不存在 `v$VERSION` 标签，避免覆盖旧发布。
7. 按 AGENTS §3.1 检查 Git 作者身份与待提交范围；不要提交测试截图、配置、历史、日志或用户信息。

## 1. 更新版本与归档 CHANGELOG

1. 用 Edit 把 `Directory.Build.props` 的 `<Version>旧版本</Version>` 改为 `<Version>$VERSION</Version>`。
2. 用 Edit 把 `CHANGELOG.md` 的 `## [未发布]` 改为 `## [$VERSION] - YYYY-MM-DD`，日期用当天本地日期。不要保留空 `[未发布]` 标题；下一次改动再新建。
3. 确认 MSBuild 读到新版本：
   ```bash
   dotnet msbuild src/DeskSnip.App/DeskSnip.App.csproj -getProperty:Version -nologo
   ```
4. 提取 Release Notes 到忽略目录：
   ```bash
   VERSION="$ARGUMENTS"
   mkdir -p .runtime
   sed -n "/^## \[$VERSION\]/,/^## \[/p" CHANGELOG.md | sed '$d' > .runtime/release-notes.md
   ```
   检查文件非空且只包含目标版本小节。
5. 运行 `sh check-docs.sh` 和 `git diff --check`。

## 2. 完整回归与打包

1. 执行项目唯一验证入口并保留日志：
   ```bash
   set -o pipefail
   sh manager.sh verify 2>&1 | tee ".runtime/release-$VERSION-verify.log"
   ```
   任何测试、严格编译或文档检查失败都停止，不提交、不打标签、不上传。不能用远程桌面、既有失败或聚焦测试通过替代完整 verify。
2. 生成 debug/release 包：
   ```bash
   set -o pipefail
   sh manager.sh package 2>&1 | tee ".runtime/release-$VERSION-package.log"
   ```
   失败时保留工作树并报告日志。若因实例占用失败，请用户关闭实例后重跑，不强杀。
3. 校验三项资产存在且版本一致：
   ```bash
   ls -l "dist/dev/DeskSnip_${VERSION}_win-x64-unpack-debug.zip" \
         "dist/release/DeskSnip_${VERSION}_win-x64-unpack.zip" \
         "dist/release/SHA256SUMS.txt"
   ```
   `SHA256SUMS.txt` 必须包含两个 zip 的正确哈希；发布目录不得包含 `.env`、`.runtime`、测试结果、用户配置、历史或日志。

## 3. 提交、标签与推送

1. 再次检查 `git status --porcelain`，逐项确认只包含本次发版代码、技能、文档和版本文件。
2. 暂存并检查：
   ```bash
   git add -A
   git diff --cached --check
   git diff --cached --stat
   ```
   浏览 staged 内容；发现凭据、个人路径、用户数据、日志或二进制产物立即停止。
3. 提交：
   ```bash
   git commit -m "变更：发布 DeskSnip $VERSION" \
              -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
   ```
4. 创建并推送标签：
   ```bash
   git tag -a "v$VERSION" -m "DeskSnip $VERSION"
   git push origin main
   git push origin "v$VERSION"
   ```
   推送失败时停止，不要 `--force`。
5. 校验工作树为空，且 `git rev-parse HEAD` 与 `git rev-list -n1 "v$VERSION"` 相同。

## 4. 上传 GitHub Release

1. 固定上传三项资产：debug zip、release zip、`SHA256SUMS.txt`。
2. 如果 `gh release view "v$VERSION"` 已存在：
   ```bash
   gh release upload "v$VERSION" \
     "dist/dev/DeskSnip_${VERSION}_win-x64-unpack-debug.zip" \
     "dist/release/DeskSnip_${VERSION}_win-x64-unpack.zip" \
     "dist/release/SHA256SUMS.txt" --clobber
   gh release edit "v$VERSION" --title "DeskSnip $VERSION" --notes-file .runtime/release-notes.md
   ```
   否则新建：
   ```bash
   gh release create "v$VERSION" \
     "dist/dev/DeskSnip_${VERSION}_win-x64-unpack-debug.zip" \
     "dist/release/DeskSnip_${VERSION}_win-x64-unpack.zip" \
     "dist/release/SHA256SUMS.txt" \
     --title "DeskSnip $VERSION" --notes-file .runtime/release-notes.md
   ```
3. 验证：
   ```bash
   gh release view "v$VERSION" --json tagName,url,assets
   ```
   确认标题、标签及三个资产的名称和大小。上传失败时保留已推送提交与标签，报告可重试命令，不回滚或强推。

## 5. 收尾

- 按 AGENTS §5 更新 HANDOFF/ROADMAP；已归档的未发布内容不重复保留，未实测项继续列入验收边界。
- 向用户报告版本、提交、标签、Release URL、三个资产、验证与打包结果。
- 真实桌面、混合 DPI、外部设备/服务等未实测项不得写成已验证。
- 任一步失败都停在失败点，明确已生效操作，尤其推送与 Release；不能声称整套发布成功。
