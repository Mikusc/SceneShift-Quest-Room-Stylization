# 13 MCP Working Configuration

## Purpose

This file records the known-working Codex <-> Unity MCP setup before trying another Unity AI Assistant upgrade.

Do not commit `/Users/mikusc/.codex/config.toml` into this repository. That file is machine-local Codex configuration and may contain unrelated user settings. Record only the relevant non-secret state here.

## Snapshot

- Recorded: `2026-05-24`
- Unity Editor: `6000.4.3f1`
- Unity AI Assistant package: `2.0.0-pre.1`
- Unity MCP relay binary: `/Users/mikusc/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64`
- Unity project: `/Users/mikusc/Documents/UnityProjects/SceneShift Discussion Room Latest`

## Project Package State

`Packages/manifest.json` no longer lists `com.unity.ai.assistant` as a direct dependency.

`Packages/packages-lock.json` currently resolves:

- `com.unity.ai.assistant`: `2.0.0-pre.1`
- `com.meta.xr.unity-mcp.extension`: Git dependency from `https://github.com/meta-quest/Unity-MCP-Extensions.git`
- `com.unity.cloud.gltfast`: `6.14.1`

This state is the current working baseline for Unity MCP access.

## Codex MCP State

Relevant state in `/Users/mikusc/.codex/config.toml`:

```toml
[mcp_servers.unity-mcp]
args = ["--mcp"]
command = "/Users/mikusc/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64"
enabled = false

[mcp_servers.unity_mcp]
command = "/Users/mikusc/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64"
args = ["--mcp"]
enabled = true
```

The important fix is that only one Unity MCP server entry is enabled. Having both `unity-mcp` and `unity_mcp` enabled can start duplicate direct MCP clients and make Unity show Codex under `Other Connections` with capacity/approval confusion.

## Verification

Verified on `2026-05-24`:

- `Unity_GetConsoleLogs` executed successfully through Codex MCP.
- Unity Console result reported `errorCount: 0`.
- Unity Console result reported `warningCount: 1`.
- The remaining warning was `Account API did not become accessible within 30 seconds`, which is accepted Editor noise for now.
- `ps aux | rg -i 'relay_mac_arm64 --mcp'` showed one Unity MCP relay process after duplicate entries were disabled and old relay processes were stopped.

## If Upgrading Assistant Again

Before upgrading:

1. Confirm MCP still works with `Unity_GetConsoleLogs`.
2. Confirm only one `relay_mac_arm64 --mcp` process is running.
3. Keep this package state committed so the project can be rolled back.

After upgrading to `2.9.x`:

1. Open `Project Settings > AI > Unity MCP Server`.
2. Confirm `Connected Clients` no longer says `Up to 0 direct connections allowed at a time`.
3. Confirm `codex-mcp-client` appears under `Connected Clients`, not only under `Other Connections`.
4. Run `Unity_GetConsoleLogs`.
5. If `codex-mcp-client` shows `Capacity limit`, roll back to the `2.0.0-pre.1` package state recorded here.
