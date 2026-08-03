#!/bin/bash
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

if ! dotnet --list-sdks 2>/dev/null | grep -q "^8\."; then
  if ! { apt-get update -qq && apt-get install -y dotnet-sdk-8.0; }; then
    curl -fsSL https://dot.net/v1/dotnet-install.sh \
      | bash -s -- --channel 8.0 --install-dir /usr/share/dotnet
    ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
  fi
fi

{
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "$CLAUDE_ENV_FILE"

dotnet restore "$CLAUDE_PROJECT_DIR/GuildSimulator.Tests/GuildSimulator.Tests.csproj"
