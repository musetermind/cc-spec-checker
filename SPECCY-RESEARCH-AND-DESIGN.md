# CC Spec Checker — Speccy research + design proposal

Date: 2026-11-25. Status: RESEARCH + DESIGN ONLY. No code until Sithu says "Go".
Trigger: Speccy v1.34.084 showed Sithu's 8GB Zotac RTX 3070 as "4095MB" on the bench
(2026-11-25 02:15). Same class of bug as the ApkStart checkup's "20.0 GB VRAM" for a 6GB
GTX 1660 Super (2026-11-24).

---

## Part 1 — Speccy: what it is

- **Maker:** Piriform Software (the CCleaner/Recuva company; Piriform was bought by Avast
  in 2017, now Gen Digital). First released **23 April 2010**. Freeware, Windows only,
  ~8.6 MB, also available as a portable build (run from USB, no install).
- **Current version: v1.34.84 (build 1.34.0.84), released 22 June 2026** (Wikipedia +
  tutorialpedia agree). Previous: v1.33.75/1.33.079, June 2024 (new hardware-detection
  library). The June 2026 update was a maintenance release about EU regulatory
  compliance, NOT new hardware detection. Releases now come roughly every 2 years.
- **Editions:** Free (full inventory + temps), Professional (adds auto-updates + priority
  support), CCleaner Professional Plus bundle (adds Driver Updater, CCleaner Pro,
  Recuva Pro). The scan engine and data are identical in Free and Pro.
- **Official system requirements still list Windows 10, 8.1, 7, Vista and XP** — Windows 11
  is not officially listed, though Speccy runs on it.
- **What it is:** a READ-ONLY viewer. It changes nothing, cleans nothing, tunes nothing.
  It collects what Windows already knows and shows it in one place, with live sensor
  values beside static inventory data.
- **Interface:** left category tree + right detail panel. Categories: Summary, Operating
  System, CPU, RAM, Motherboard, Graphics, Storage, Optical Drives, Audio, Peripherals,
  Network. Summary shows headline values + a temperature per major component on one screen.
  Can save a snapshot (Speccy snapshot file), or export XML / text for sharing with
  tech support or for before/after upgrade comparisons.

### What each panel shows
- **Summary:** OS, CPU + temp, RAM, motherboard + temp, graphics + temp, storage + temp.
- **Operating System:** edition, version/build, 64-bit, install date, updates, computer name.
- **CPU:** model, code name, family, cores/threads, clock speeds, cache sizes, instruction
  sets, per-core temperatures (package temp — NOT exact die temp).
- **RAM:** total capacity, type, rated speed, slots used/free, per-stick brand, part
  number, frequency, timings (e.g. 16-20-20-38).
- **Motherboard:** manufacturer, model, chipset, BIOS/UEFI version and date, sensor temps.
- **Graphics:** GPU model, VRAM, driver version, GPU temperature, monitor model,
  resolution, refresh rate.
- **Storage:** model, capacity, interface, firmware, S.M.A.R.T. health attributes,
  power-on hours, temperature.
- **Optical / Audio / Peripherals / Network:** the remaining inventory.

### How Speccy gathers data (the important part)
- **WMI + registry** for most inventory: Win32_Processor, Win32_PhysicalMemory,
  Win32_VideoController (incl. AdapterRAM), Win32_DiskDrive, Win32_BaseBoard,
  Win32_BIOS, Win32_OperatingSystem, plus registry keys under
  HKLM\HARDWARE\DESCRIPTION and HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion.
- **DirectX** for graphics information.
- **A signed kernel-mode helper driver based on CPUID's CPU-Z driver**
  (e.g. `cpuz149_x64.sys`, dropped into a temp folder while it runs) for the
  low-level reads Windows does not expose to normal apps: PCI configuration space,
  RAM SPD data (part numbers, timings), and sensor chips. This is how it gets
  per-core temps, RAM timings and motherboard sensors.
- **S.M.A.R.T.** via DeviceIoControl / WMI (`MSStorageDriver_FailurePredictStatus`).

### Why Speccy is still used — and why it is losing ground
- Used because: simple, free, portable, one screen, temps + snapshot export.
- Losing ground because: hardware database goes stale (new CPUs/GPUs misidentified),
  releases are rare, the kernel driver causes freezes/crashes mid-scan and trips
  Windows Memory Integrity (HVCI) blocks, temps are package-level not die-level,
  VRM temps and PCIe details are often missing, RAM timings are sometimes wrong,
  and Windows 11 is not officially supported.
- Competitors: HWiNFO (deepest sensors, complex), CPU-Z (CPU/RAM detail, no temps),
  GPU-Z (GPU detail, correct VRAM), CrystalDiskInfo (SMART), AIDA64 (paid, everything).
  CC Spec Checker sits in Speccy's slot: simple, one screen, correct numbers.

---

## Part 2 — The VRAM bug (proven, with history)

### What we proved on the bench
- 2026-11-25: Speccy v1.34.084 shows Sithu's **8GB Zotac RTX 3070 as "4095MB"**.
- 2026-11-24: ApkStart's checkup showed the **6GB GTX 1660 Super as "20.0 GB VRAM"**
  (dedicated 6GB + ~14GB shared system memory lumped together).

### The mechanism (Speccy)
Speccy reads VRAM from WMI `Win32_VideoController.AdapterRAM`. That field is a
**32-bit unsigned integer: max 4,294,967,295 bytes = 4095MB**. Any card over 4GB gets
clamped to 4095MB. So a 6GB, 8GB, 12GB or 24GB card all show the same "4095MB".
The hardware is fine; the reporting path is broken.

### It is a decade-old, never-fixed bug
- **2016** (Tom's Hardware): MSI GTX 1070 8GB shows "4095MB" in Speccy.
- **2017** (CCleaner forum): GTX 1060 6GB — Speccy showed "Physical Memory 2047 MB /
  Virtual Memory 2048 MB" while WMI AdapterRAM was 4,293,918,720 bytes. Piriform
  support asked for a wmic dump; the thread notes "Speccy releases are few and far
  between, so also don't expect any resolution any time soon."
- **Oct 2025** (Tom's Hardware): Gigabyte RTX 5060 Ti 16GB — Speccy "Physical Memory
  4022 MB / Virtual Memory 4023 MB". Windows System Information was ALSO wrong
  (Adapter RAM 1,048,576 bytes = 1MB). GPU-Z reported the correct 16385 MB.
- **July 2026** (BleepingComputer): RTX 3090 24GB shows "4095MB" in Speccy.
- Piriform has never fixed it in 10 years. Every forum answer is the same:
  "Speccy's problem, not your hardware — use GPU-Z."

### Why even Windows' own tools get it wrong
`msinfo32` reads the same 32-bit WMI field. On the 5060 Ti it showed 1MB. Task
Manager is the exception: it reads GPU memory through the driver/WDDM, not WMI,
so its Dedicated GPU memory figure is correct.

### The correct ways to read VRAM (what GPU-Z and Task Manager use)
1. **DXGI (vendor-neutral, the right default):** `CreateDXGIFactory1` →
   `EnumAdapters1` → `IDXGIAdapter1::GetDesc1` → `DedicatedVideoMemory`, a 64-bit
   value. Works for NVIDIA, AMD and Intel, discrete and integrated, multi-GPU.
   This is the same API Task Manager uses. No vendor SDK needed; dxgi.dll ships
   with Windows.
2. **NVAPI (NVIDIA only):** `NvAPI_GPU_GetMemoryInfoEx` →
   `NV_GPU_MEMORY_INFO_EX.dedicatedVideoMemory` (KB, physical framebuffer).
   The older `NvAPI_GPU_GetMemoryInfo` was deprecated in driver release 520.
   Needs nvapi64.dll, which ships with the NVIDIA driver. Dynamic loading via
   `NvAPI_QueryInterface` magic offsets (e.g. 0x774AA982 for GetMemoryInfo).
3. **AMD ADL / Intel equivalents:** vendor SDKs; DXGI makes them unnecessary for
   the size figure.
4. **NOT reliable:** WMI AdapterRAM (32-bit clamp), dxdiag `szDisplayMemoryEnglish`
   (old, can be wrong), driver-registry `MemorySize` (see the Toolkit history below).

---

## Part 3 — The CC Toolkit already fought this bug (Oct 2026)

This is not a new problem for Computer Corner. The Toolkit's "View Detail Specs"
hit the exact same 4095MB wall, and the fix history matters for the new tool:

- **Oct 5:** Sithu reported View Detail Specs shows 4096MB for every GPU. Fix staged:
  read real VRAM from the driver registry (64-bit `MemorySize`, matched by GPU name).
- **Oct 5–6, v30.5:** built with the registry fix. Sithu tested on his 6GB RTX 2060 —
  **still 4095MB**. v30.6 (same code) — still 4095MB.
- **v30.7:** second-generation lookup — match WMI `PNPDeviceID` to registry
  `MatchingDeviceId` by PCI VEN/DEV first, name matching only as fallback, check
  `MemorySize` both on the adapter key and under `HardwareInformation`, log a
  diagnostic when no match. Sithu tested — **still wrong**. The diagnostic showed
  no usable registry device-memory entries at all. Conclusion: the driver-registry
  approach is not general enough. Sithu clarified the fix must support ALL GPUs,
  not just his 2060, and asked for research into VRAM methods.
- **v30.8:** DXGI implementation (`DxgiVram.cs`, .NET ComImport interfaces) —
  threw `InvalidCastException` ("Specified cast is not valid") before enumerating
  any adapter.
- **v30.9:** `DxgiVram.cs` rewritten to call the DXGI COM vtables directly via
  delegates (no RCW, no cast): `IDXGIFactory1::EnumAdapters1` = vtable slot 12,
  `IDXGIAdapter1::GetDesc1` = slot 10, skip software adapters, key by PCI VEN/DEV.
  Compiled, released, MediaFire-verified — **but NEVER verified on real Windows
  hardware**. Sithu has not yet run it and confirmed a correct reading.
- **v30.11 (current, Nov 23):** the DXGI code is in the shipped build, still
  unverified on a real PC.

**Lesson for CC Spec Checker:** the registry path is a dead end (proven twice on
Sithu's own PC). DXGI is the right method, but the existing code has never been
proven on hardware. The new tool's first job is to PROVE the DXGI path on real
GPUs — it must not ship on an untested claim.

Existing code to reuse (do not rewrite from scratch):
- `~/workspace/cc-toolkit-rebuild/src/CcToolkit.Core/Services/DxgiVram.cs` —
  manual-vtable DXGI reader, keyed by PCI VEN/DEV, with per-step diagnostics.
- `~/workspace/cc-toolkit-rebuild/src/CcToolkit.Core/Services/SpecService.cs` —
  `GetReport()` already produces a text report: Processor (name), Graphics (name +
  VRAM via DXGI → registry → WMI fallback), RAM (per-slot capacity/speed + total),
  Motherboard (manufacturer/product + BIOS version), Storage (model/size/media),
  OS (caption/arch/build), Monitors (name). The new tool's engine starts here.

---

## Part 4 — Design proposal: CC Spec Checker v1

### What it is
A standalone, portable Windows app — one exe, no installer, run from USB like
Speccy's portable build. Read-only: it changes nothing on the machine. Made for
the shop: checking a customer's PC, verifying a build before handover, and
confirming the parts in a machine are what they claim to be.

### Tech (proposed)
- **C# + .NET 8, self-contained single exe (win-x64)** — the same stack as the CC
  Toolkit, which already compiles on this Linux VM (`CcToolkit.Core` net8.0-windows
  classlib compiles here; the UI needs Windows to run, same as the Toolkit).
- **UI: Avalonia** — same as the Toolkit, so the shop's tools share one look and one
  skill set. Two-pane layout like Speccy: category list on the left, details on the
  right, Summary first.
- **Engine: reuse `CcToolkit.Core`'s `DxgiVram.cs` + `SpecService.cs`** as the
  starting point — do not rewrite the VRAM reader. The new work is the app around
  it: the panels Speccy has that the Toolkit's text report lacks (temps, SMART,
  timings, snapshot save, a real GUI).

### What v1 shows (per panel) and how each value is read
| Panel | Shows | How it is read |
|---|---|---|
| Summary | OS, CPU + temp, RAM total, board + temp, GPU + VRAM + temp, drives + temp | one line per subsystem, like Speccy |
| Operating System | edition, version/build, arch, install date | WMI Win32_OperatingSystem + registry install date |
| CPU | model, cores/threads, base/max clock, cache, code name | WMI Win32_Processor (+ registry CentralProcessor) |
| RAM | total, type, speed, slots used/free, per-stick brand/part no./capacity/speed | WMI Win32_PhysicalMemory |
| Motherboard | manufacturer, model, chipset, BIOS version + date | WMI Win32_BaseBoard + Win32_BIOS |
| Graphics | GPU model, **VRAM (DXGI, 64-bit)**, driver version, temp, monitor + resolution/refresh | DXGI GetDesc1 for VRAM; WMI for name/driver; monitor via display config |
| Storage | model, capacity, interface, firmware, SMART health, power-on hours, temp | WMI Win32_DiskDrive + SMART via MSStorageDriver / DeviceIoControl |
| Audio / Network / Peripherals | inventory list | WMI Win32_SoundDevice / Win32_NetworkAdapter / Win32_PnPEntity |
| Snapshot | save report as text + XML, copy to clipboard | for listings, customer handovers, tech-support sharing |

### The honest limits (v1 does NOT pretend)
- **No kernel driver in v1.** Speccy's temps and RAM timings come from its CPU-Z
  driver, and that driver is also why Speccy freezes mid-scan and trips Windows
  Memory Integrity. CC Spec Checker v1 reads only what Windows and the GPU driver
  expose without a driver. Consequences, stated openly:
  - **CPU temperature: stretch goal, not v1.** WMI does not expose CPU temp.
    Options for later: LibreHardwareMonitor's library (open source, but it brings
    its own driver — same HVCI trade-off as Speccy), or the Windows thermal zone
    (unreliable, often absent). If v1 cannot read it honestly, it shows
    "not available" — never a fake number.
  - **RAM timings (16-20-20-38): v2, not v1.** Timings live in the SPD EEPROM on
    each stick, read over SMBus — needs the driver. v1 shows capacity, speed,
    type, slots and part numbers (all from WMI).
  - **GPU temperature: v1, via the vendor driver** — NVAPI
    (`NvAPI_GPU_GetThermalSettings`) for NVIDIA, ADL for AMD. No kernel driver
    needed; the GPU driver already exposes it.
  - **Drive temperature + SMART: v1** — SMART comes through DeviceIoControl, no
    driver needed.
- **If a value cannot be read, show "not available".** Never a wrong number. That
  is the whole point of this tool, and the rule Speccy broke.

### Acceptance test (must pass before any release)
The DXGI VRAM path must be proven on real hardware, on Sithu's bench:
- NVIDIA RTX 2060 6GB (his test card) — must read **6GB**, not 4095MB.
- NVIDIA Zotac RTX 3070 8GB (his personal card) — must read **8GB**.
- AMD RX 7900 XT 20GB (flagship) — must read **20GB**.
- Intel UHD integrated graphics (14900K iGPU) — must read its shared figure
  honestly, not a clamped or invented number.
- Multi-GPU machine (discrete + iGPU) — both listed, each with its own figure.
No release until all of these pass on his PC. The Toolkit's v30.9 DXGI code is
still unverified — this tool does not inherit that claim, it re-proves it.

### What v1 is NOT
- Not a tweaker, cleaner or overclocking tool (read-only, like Speccy).
- Not a benchmark (no FurMark/OCCT-style load testing — the shop already has
  those tools).
- Not a replacement for HWiNFO (deep sensors) or GPU-Z (deep GPU detail). It is
  the quick, honest one-screen answer: "what is in this PC, and is it healthy?"

### Open decisions for Sithu
1. **Standalone portable app** (one exe, USB, no install) — as proposed, or fold it
   into the Toolkit as a bigger View Detail Specs?
2. **v1 scope as above** (full static inventory + GPU/drive temps, CPU temp and RAM
   timings deferred) — or push CPU temp into v1 via LibreHardwareMonitor?
3. **Name/branding:** "CC Spec Checker" with the Computer Corner name on it, same
   style as the Spec Generator and Toolkit?
4. **Snapshot format:** text + XML like Speccy, or also a one-page shareable image
   for customers?

### Next step (after Sithu approves the design)
1. Prove the DXGI VRAM path on the bench (his 2060 + 3070 + 7900 XT + iGPU).
2. Build v1 (engine from CcToolkit.Core, new Avalonia two-pane UI).
3. He tests it on a real customer machine; only then a release + MediaFire link.

---

## Part 5 — Real-time temperature: proposal (added 2026-11-25, Sithu asked)

### The problem, restated
WMI does not expose CPU temperature. Speccy gets its temps from a kernel driver
(CPU-Z based). The question was whether CC Spec Checker should do the same — and
whether it can do it without Speccy's problems (freezes, Memory Integrity blocks).

### What changed since the first design draft
The first draft assumed the only sensor library (LibreHardwareMonitor) used the
old WinRing0 driver, which Microsoft blocks. That is now OUT OF DATE:
- **LibreHardwareMonitor 0.9.6 (stable, 14 Feb 2026) switched from WinRing0 to
  PawnIO.** PawnIO (https://pawnio.eu) is a free, open-source, digitally-signed
  kernel driver that IS compatible with Memory Integrity (HVCI), VBS and the
  Microsoft vulnerable-driver blocklist. WinRing0 is on that blocklist
  (CVE-2020-14979) and is blocked whenever Memory Integrity, Smart App Control or
  S mode is active (blocklist on by default since Win11 22H2); Defender also flags
  it as HackTool:Win32/Winring0.
- **LibreHardwareMonitorLib is actively maintained:** stable 0.9.6 (Feb 2026,
  ~230k downloads) plus 0.9.7-pre builds almost daily (latest pre785, 11 Nov 2026).
  Use the pinned stable 0.9.6 for a shop tool; move to a newer pre build only if
  a new CPU/GPU is not detected.
- Multiple tools (Vexis, LoadView, owlette) have already made the same switch in
  2026, and report the same result: CPU temps work with Memory Integrity ON, and
  machines without the PawnIO driver keep working with CPU temp blank while GPU
  temps still arrive through vendor APIs.

### Proposal: LibreHardwareMonitorLib 0.9.6 + opt-in PawnIO
**APPROVED by Sithu 2026-11-25 02:34 ("Yes, LHM + opt-in PawnIO in v1").**
- **Sensor engine: LibreHardwareMonitorLib 0.9.6 (NuGet, MPL-2.0).** One library
  that reads CPU temps (per-core DTS + package), motherboard temps, fan speeds,
  voltages and clocks — Intel MSR, AMD SMU, Super I/O chips (ITE/Nuvoton/Fintek),
  SMBus — through PawnIO. It is the maintained successor to OpenHardwareMonitor
  (abandoned 2020); do not write a custom MSR reader.
- **PawnIO is a one-time, OPT-IN install — never silent, never bundled.**
  The default download stays a portable, no-install, no-admin single exe. On
  first run the tool offers "Enable accurate CPU temperature": one UAC prompt,
  PawnIO installs once, then no more prompts and temps work from then on —
  including on customer machines with Memory Integrity on. If the user declines,
  or the install fails, or the machine is offline, CPU/board temps show
  "not available" and everything else still works. Nothing breaks.
- **GPU temperature never needs the driver.** NVAPI (`NvAPI_GPU_GetThermalSettings`,
  NVIDIA) and ADL (AMD) go through the GPU driver itself. GPU temp — plus
  hotspot and VRAM temp where the driver exposes them — works even without
  PawnIO, on every machine.
- **Drive temperature + SMART need no driver either** (DeviceIoControl). Full
  SMART attributes need admin; the tool already elevates at launch like the
  Toolkit does.
- **Polling:** refresh every 1–2 seconds, like Speccy. Show CPU package temp +
  hottest core (under load the max is the number that matters — same as the
  shop's benchmark rule: judge at the saturated plateau, not minute 1).
- **Standing rule:** never run two sensor tools at once. HWiNFO + Spec Checker
  together fight over the same SMBus/sensor chips and can freeze the machine —
  the same lesson as Speccy's mid-scan freezes.

### What v1 shows for temps (revised)
| Sensor | Source | Needs PawnIO? |
|---|---|---|
| CPU package + hottest core | LHM via PawnIO (Intel DTS / AMD SMU) | Yes (opt-in) |
| GPU temp, hotspot, VRAM temp | NVAPI (NVIDIA) / ADL (AMD) | No |
| Motherboard / chipset temp | LHM via PawnIO (Super I/O) | Yes (opt-in) |
| Fan speeds (CPU/case/GPU) | LHM via PawnIO / NVAPI | Yes for board fans; No for GPU fan |
| Drive temp + SMART health | DeviceIoControl SMART | No (admin only) |

### Fallback chain (so no number is ever faked)
LHM/PawnIO → vendor APIs (NVAPI/ADL) → SMART/IOCTL → "not available" + the
reason (e.g. "sensor driver not installed" or "Memory Integrity blocked it" —
never a bare N/A with no explanation).

---

## Part 6 — GitHub check before building (added 2026-11-25, Sithu asked)

Sithu: "Should we check GitHub before building blindly?" Yes — done. Findings:

### LibreHardwareMonitor — verified on GitHub
- Repo: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- **v0.9.6 (14 Feb 2026) is the latest stable.** Release notes: "Update PawnIO
  modules to 2.2" + "Fix for new PawnIO release + new installer". PR #1857
  ("Swap WinRing0 to PawnIO", merged 16 Sep 2025) shipped in v0.9.5 (7 Jan 2026).
- **License: MPL-2.0.** Using the NuGet package as a library is fine; distribute
  the license notice with the DLLs. File-level copyleft — our app does not need
  to be open-sourced.
- NuGet: `LibreHardwareMonitorLib` 0.9.6 (~230k downloads) + 0.9.7-pre daily
  builds (latest pre785, 11 Nov 2026). Pin the stable 0.9.6.
- **How other apps ship it:** copy 4 files from the official 0.9.6 release,
  SHA-256 verified — `LibreHardwareMonitorLib.dll`, `System.Memory.dll`,
  `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`.
  Do NOT bundle PawnIO.
- **Caution:** v0.9.6 changed sensor names/ordering vs 0.9.4 (FanCtrl issue #96).
  Irrelevant for a new app, but pin 0.9.6 and test against it.
- AnxOS README confirms our fallback design: "Without a compatible signed PawnIO
  installation, AnxOS reports the temperature as unavailable without treating it
  as an application failure."

### PawnIO — verified on GitHub
- Author namazso. Repos: `namazso/PawnIO` (driver, **GPL-2.0 with IOCTL
  exception**), `namazso/PawnIO.Modules` (modules, LGPL-2.1-or-later),
  `namazso/PawnIO.Setup` (installer). Site: https://pawnio.eu
- **Latest installer: 2.2.0 (15 Mar 2026).** Modules 0.2.11 (30 Aug 2026).
  LHM 0.9.6 bundles PawnIO modules 2.2.
- **Official edition is digitally signed** (signer: Microsoft Windows Hardware
  Compatibility Publisher — verified by a FanControl user via pnputil).
  Unrestricted edition is unsigned — use Official only.
- **Do NOT bundle the PawnIO installer.** Nova-Tweaks' license review found no
  authoritative license for redistributing the installer wrapper, so they
  removed it from their app entirely. The safe pattern (used by capture-engine,
  LoadView, FanControl): when CPU sensors are requested and PawnIO is missing,
  ask once, then open/download the OFFICIAL setup (pawnio.eu or
  `winget install namazso.PawnIO`) and verify its signature before running it.
- How it works: a "scriptable universal kernel driver" — it runs small signed
  hardware modules (IntelMSR, RyzenSMU, AMDFamily17, LpcIO) instead of exposing
  raw memory/port access to any program. That is why it is HVCI-compatible
  while WinRing0 (raw access, CVE-2020-14979) is blocked.
- Uninstall: `sc.exe delete PawnIO` + `pnputil /delete-driver oemNN.inf /uninstall`.

### Glow — the open-source Speccy alternative (reference, NOT a fork)
- Türkaysoft Glow, https://github.com/turkaysoft/glow, **MIT license**, C# on
  .NET Framework 4.8.1, portable ZIP, 500+ features, export as text/HTML/Markdown,
  16 languages. Min Win10 22H2 x64, recommended Win11 25H2. Latest 25.16 (Dec 2025).
- Covers hardware, drivers, services, applications, batteries, storage, network,
  displays, plus diagnostic tools (TPM audit, file cleanup). Broader than Speccy.
- **Why not fork it:** it is a 500-feature general tool on the old .NET Framework
  stack; our tool is a focused shop tool on .NET 8 (same as the Toolkit). Forking
  would be more work than building our own. Its VRAM method was NOT verified —
  do not assume it reads VRAM correctly. Use it as a reference for how to
  structure inventory and exports, not as a base.
- Other alternatives seen: CPU-Z, HWiNFO, AIDA64, SANDRA, PC WIZARD, Gallium
  (GPL-3.0, new). None is a C# Speccy clone we should fork.

### Licensing summary for our build
- LHM (MPL-2.0): use the NuGet package, include the license notice with the DLLs.
- PawnIO (GPL-2.0 + exception): never bundle the installer; point at the official
  setup / winget and verify the signature.
- Glow (MIT): may borrow ideas/code with attribution; not forking.
- Our own code: Sithu's call whether CC Spec Checker stays a private shop tool
  (like the Toolkit) or goes public (like ApkStart).

### Pinned versions for v1
- LibreHardwareMonitorLib **0.9.6** (NuGet, stable).
- PawnIO **2.2.0** (official signed installer, via pawnio.eu / winget namazso.PawnIO).
- DXGI VRAM reader: our own `DxgiVram.cs` from CcToolkit.Core (already written).

---

## Part 7 — Is a proper spec checker a gap in the industry? (added 2026-11-25, Sithu asked)

**Short answer: yes — the niche Speccy owned is effectively abandoned, and nobody
has fully reclaimed it. But it is a niche gap, not a mass-market one.**

### Evidence
- **Speccy is in maintenance mode.** CCleaner community (Nov 2026): "There have
  been no *functional* updates to either programme for years. There was a
  *non-functional* update to each so that they complied with new Piriform licence,
  and complied with new legislation in Europe." Last real hardware-detection
  update: v1.33.75 (Jun 2024). The Jun 2026 v1.34.84 was EU-compliance, not
  detection. VRAM bug (4095MB) unfixed for 10 years; no official Win11 support.
- **The 2026 guides recommend three tools to replace one Speccy.** trendingreview
  (2026): "For most home users, two free system information tools cover almost
  everything you need: CPU-Z to read your exact hardware specs, and HWiNFO to
  watch temperatures and voltages while the PC runs. Add CrystalDiskInfo... and
  you are effectively done." And: "a lot of the popular 'best of' lists pad their
  counts with duplicate tools and software that has been abandoned for years."
- **Speccy is still named because people still want what it was.** AI-mention
  counts across guides: HWiNFO 74, Speccy 52, CPU-Z 52, GPU-Z 38, AIDA64 29.
  Speccy is described as "easiest full overview" / "simple readable snapshot"
  while HWiNFO is "most detailed / sensors" — the simple-overview slot is still
  Speccy's in people's minds, even though the tool itself is stale.
- **"speccy download" is ~1,900 searches/month** (Google Keyword Planner, via
  essentras) — small but real, steady demand for exactly this tool.

### Why the gap exists
Doing it RIGHT is hard, and the three hard parts each broke at a different time:
1. Correct VRAM — needs DXGI, not WMI. The WMI 4095MB bug is 10 years old and
   most tools still use WMI.
2. Temps on modern Win11 — the old WinRing0 driver was blocklisted (CVE-2020-14979,
   HVCI). Only in 2025–2026 did the PawnIO replacement mature (LHM 0.9.5/0.9.6).
3. Simplicity + accuracy together — HWiNFO has the accuracy but not the simple
   customer-facing UI; AIDA64 has both but is paid.

The economics: Speccy was free from a company (Piriform → Avast → Gen Digital)
whose money is in CCleaner — no business model in a free spec viewer, so it
rotted. Open source filled the sensor side (LibreHardwareMonitor) but nobody
assembled the whole thing into a simple spec checker — until small projects like
Glow started trying.

### Who the gap matters to
Not most home users — Task Manager + msinfo32 cover their questions (the guides
say so explicitly). It matters to technicians, PC shops and enthusiasts who need
a trustworthy, complete, shareable spec readout in one window. That is exactly
Sithu's world: customer-PC checks, build verification, handover reports.
- Glow (Türkaysoft, MIT) is the only open-source all-in-one trying — small
  one-developer project, .NET Framework 4.8.1, VRAM method unverified. The gap
  is not totally empty, but there is no *maintained, verified-accurate* simple
  spec checker. Our differentiator is verification: DXGI proven on real GPUs,
  temps proven on real hardware, honest "not available" instead of fake numbers.

---

## Part 8 — Community project: the plan (added 2026-11-25, Sithu decided)

**Sithu: "We will help the community then."** CC Spec Checker is a public
community project, same spirit as ApkStart ("we are doing this for everyone").
This part is the plan. The build "Go" is still pending — the plan is the proposal.

### Repo and license (proposed)
- **Repo:** `github.com/musetermind/cc-spec-checker` (musetermind account —
  Bro manages it, same as ApkStart).
- **License (proposed): MIT.** Permissive, simplest, matches Glow (the reference
  project), compatible with LHM (MPL-2.0) used as a library. GPL is not forced
  on us — PawnIO is a separate driver component, not linked into our code.
  Sithu's call; MIT is the recommendation.
- **Credits in the README:** LibreHardwareMonitor (MPL-2.0), PawnIO (GPL-2.0 +
  exception), and a note that the engine builds on the CC Toolkit's
  `DxgiVram.cs` / `SpecService.cs`. The README also states the "why": the Speccy
  gap, the 10-year VRAM bug, and the honest-numbers rule (never a fake number).

### Project structure
- Workspace: `~/workspace/cc-spec-checker/` (report lives here already).
- New solution under `src/`: a Core library (the engine) + an Avalonia app.
  Reuse `DxgiVram.cs` + `SpecService.cs` from
  `~/workspace/cc-toolkit-rebuild/src/CcToolkit.Core/Services/` — do not rewrite
  the VRAM reader. .NET 8, self-contained win-x64, single-file portable exe.
- **Pinned:** LibreHardwareMonitorLib 0.9.6 (NuGet), PawnIO 2.2.0 (official
  signed installer, never bundled — fetched from pawnio.eu / winget with
  signature verification).

### Phases
1. **Setup:** create the repo + MIT license + README; project skeleton; add the
   LHM 0.9.6 NuGet reference; **prove the Avalonia build compiles on this Linux
   VM early** (the Toolkit's WPF app cannot compile here — Avalonia should, but
   verify before committing to it).
2. **Engine:** port DxgiVram.cs + SpecService.cs into the new Core; add the new
   pieces — NVAPI GPU temperature (NVIDIA), ADL GPU temperature (AMD), SMART
   reader (drives), LHM integration with the opt-in PawnIO flow. Engine logic is
   unit-tested where possible; DXGI/NVAPI/ADL can only be proven on Windows.
3. **UI:** Avalonia two-pane layout, Summary first; live temp polling (1–2s);
   snapshot export (text + XML, proposed for v1 — the one-page image is a v2
   idea); copy to clipboard.
4. **Bench proof (the gate):** Sithu runs the build on the bench. Acceptance
   test — VRAM reads correctly on RTX 2060 6GB, personal Zotac RTX 3070 8GB,
   RX 7900 XT 20GB, Intel UHD iGPU, and a multi-GPU machine; temps read
   correctly with PawnIO installed. **No release until this passes.** The tool
   must not inherit the Toolkit's unverified DXGI claim — it re-proves it.
5. **Release:** GitHub **pre-release** first (same discipline as ApkStart);
   flip to full only after the bench test passes. Portable zip + MediaFire
   mirror (his delivery rule). README with the why, the credits, and the
   honest-numbers rule.
6. **Community:** announce where Sithu chooses (CC Facebook Page, Reddit,
   GitHub — his call). Then maintain: new-hardware support follows LHM releases;
   bugs reported by users go through the same bench-proof rule.

### Sithu's role
- The "Go" to start building (this plan is the proposal).
- Bench testing (Phase 4) — he runs the exe, reports VRAM + temps.
- PawnIO install on the bench for the temp test (one UAC prompt).
- Decisions: license (MIT proposed), snapshot formats (text+XML for v1 proposed),
  where to announce.

### Honest limits / risks
- Avalonia-on-Linux build is unproven — Phase 1 proves it first; if it fails,
  the fallback is building the UI on the bench (like the ApkStart Setup).
- NVAPI/ADL temperature code is new — needs research and bench proof.
- The self-contained single-file exe will be ~100–150MB (runtime included) —
  portable but big; normal for self-contained .NET.
- No dates promised: the build is a few focused sessions, but the bench test is
  the long pole and runs on his schedule.

### Name — DECIDED 2026-11-25 02:47: **CC Spec Checker**
- Sithu proposed "Ez Spec" (fits the Ez Computer brand) but chose **CC Spec
  Checker** instead after the trademark check: "EzSpec" is a trademark of
  HORIBA Instruments (spectroscopy software) — different field, low risk, but
  Sithu chose zero risk. Repo name: `cc-spec-checker`. Exe: `CCSpecChecker.exe`.
  The Ez Spec idea is parked, not forgotten — it stays a possible future name
  only if the trademark picture ever changes.
