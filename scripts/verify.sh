#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# 一键验证脚本
#
#   * 任意平台：构建核心层并运行单元测试
#   * Windows ：完整构建 WinUI 应用（含 XAML 编译，即 x:Bind / 资源键校验）
#   * Linux/macOS：XamlCompiler 不可用，改为对应用层做 C# 类型检查（tools/typecheck）
#
# 用法：  ./scripts/verify.sh
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

cd "$(dirname "$0")/.."

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# 若 SDK 装在用户目录且未加入 PATH，则自动补上
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$DOTNET_ROOT:$PATH"
  else
    echo "未找到 dotnet，请先安装 .NET 8 SDK：https://dot.net" >&2
    exit 1
  fi
fi

echo "==> dotnet $(dotnet --version)"
echo
echo "==> [1/3] 核心层单元测试"
dotnet test tests/ADOFAI.ModManager.Core.Tests/ADOFAI.ModManager.Core.Tests.csproj -c Release

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    echo
    echo "==> [2/3] WinUI 应用完整构建（x64，含 XAML 编译）"
    dotnet build src/ADOFAI.ModManager/ADOFAI.ModManager.csproj -c Release -p:Platform=x64
    ;;
  *)
    echo
    echo "==> [2/3] 应用层类型检查（本平台无法运行 XAML 编译器，详见 tools/typecheck/README.md）"
    dotnet build tools/typecheck/Typecheck.csproj -c Release
    ;;
esac

echo
echo "==> [3/3] 完成"
