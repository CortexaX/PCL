# Terracotta integration notes

This directory vendors the Terracotta v0.4.2 source used as the source reference
for PCL2's Taowa/Terracotta link mode.

## Provenance

- Upstream repository: https://github.com/burningtnt/Terracotta
- Upstream tag: `v0.4.2`
- Upstream commit: `70d542156377b316659b4ec2ac62341a43ddc4b2`
- License: GNU Affero General Public License v3.0, see `LICENSE`

The transitional binaries currently stored in
`Plain Craft Launcher 2/Resources/Taowa/` match the upstream v0.4.2 Windows
x86_64 release package:

| File | SHA256 |
| --- | --- |
| `terracotta.exe` | `74c10568a7fea9c1d38cf8d2d4ca90baf1517f8e5a26c63d3349db70bc449796` |
| `VCRUNTIME140.DLL` | `475ab98b7722e965bd38c8fa6ed23502309582ccf294ff1061cb290c7988f0d1` |

The same version also identifies itself in the PE metadata as Terracotta 0.4.2
and embeds the string `Easytier: v2.5.0-terracotta.2`.

## Migration target

The current PCL2 implementation uses these binaries as a temporary backend from
`Modules/ModTaowa.vb`. The long-term target is to port the behavior into PCL2
itself and remove the standalone Terracotta process.

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
  framing, and Minecraft LAN scanner from the Rust source.
- `Plain Craft Launcher 2/Modules/ModTaowa.vb` still uses the transitional
  `terracotta.exe --hmcl` backend until the EasyTier control and scaffolding
  session loops have also been ported.
