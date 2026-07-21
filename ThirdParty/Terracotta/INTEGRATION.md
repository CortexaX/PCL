# Terracotta integration notes

This directory vendors the Terracotta v0.4.2 source used as the source reference
for PCL2's Taowa/Terracotta link mode.

## Provenance

- Upstream repository: https://github.com/burningtnt/Terracotta
- Upstream tag: `v0.4.2`
- Upstream commit: `70d542156377b316659b4ec2ac62341a43ddc4b2`
- License: GNU Affero General Public License v3.0, see `LICENSE`

The previously used standalone process backend was taken from the upstream
v0.4.2 Windows x86_64 release package:

| File | SHA256 |
| --- | --- |
| `terracotta.exe` | `74c10568a7fea9c1d38cf8d2d4ca90baf1517f8e5a26c63d3349db70bc449796` |
| `VCRUNTIME140.DLL` | `475ab98b7722e965bd38c8fa6ed23502309582ccf294ff1061cb290c7988f0d1` |

Those files are no longer stored under `Plain Craft Launcher 2/Resources/Taowa/`
and are no longer copied to PCL2 build outputs. Their hashes are kept here only
as provenance for the reverse-engineering and porting reference.

The internal .NET path uses EasyTier as a separately shipped transition asset
under `Plain Craft Launcher 2/Resources/Taowa/EasyTier/`:

| File | SHA256 |
| --- | --- |
| `easytier-core.exe` | `5371afc6432141813664bd8c50c682010a26c6c409d643960ed1a9fdda115bc7` |
| `easytier-cli.exe` | `d7ff40af0e5c62f51ce9ac2b7682502e6cd0f3e7575972b72783325526283b7d` |
| `Packet.dll` | `c7c03a87eac7243ccbe331554624b18803010b740e311fc8cfddb573096eacac` |

## Migration status

PCL2 now calls the VB/.NET Taowa implementation from `Modules/ModTaowa.vb`
directly. The standalone `terracotta.exe --hmcl` process backend and
`VCRUNTIME140.DLL` runtime dependency have been removed from the project.

The Terracotta source splits the behavior into these main areas:

- `src/controller/states.rs`: state machine and `/state` API output shape
- `src/controller/api.rs`: `/state`, `/state/ide`, `/state/scanning`, and `/state/guesting`
- `src/mc/scanning.rs`: Minecraft LAN server detection
- `src/scaffolding/`: room/profile exchange protocol
- `src/easytier/`: EasyTier argument construction, process/linkage backend, and public node handling
- `web/`: embedded local status UI

When the .NET port is complete, PCL2 should call the internal implementation
directly instead of starting `terracotta.exe --hmcl`.

## .NET port progress

- `Plain Craft Launcher 2/Modules/ModTaowaCore.vb` ports the room code
  generation/parsing, profile model, app-state JSON shape, scaffolding packet
  framing, TCP client/server session layer, default `c:*` protocol handlers,
  Minecraft LAN scanner, fake LAN server broadcaster, and Minecraft connection
  probing from the Rust source.
- `Plain Craft Launcher 2/Modules/ModTaowaEasyTier.vb` ports EasyTier argument
  construction, NAT/difficulty mapping, local port allocation, process startup,
  CLI peer parsing, and port-forward control scaffolding from the Rust source.
- `Plain Craft Launcher 2/Modules/ModTaowaInternal.vb` ports the in-process
  host/guest orchestration loops that connect the state machine, LAN scanner,
  EasyTier, scaffolding session, Minecraft port forwarding, and player profile
  synchronization.
- `Plain Craft Launcher 2/Modules/ModTaowa.vb` uses the internal .NET backend
  by default. There is no `PCL_TAOWA_INTERNAL` opt-in switch and no fallback
  launch path for `terracotta.exe --hmcl`.
