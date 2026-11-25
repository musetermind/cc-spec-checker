# CC Spec Checker — Speccy research and design

Speccy showed an 8GB RTX 3070 as "4095MB". Another tool showed a 6GB card as "20.0 GB VRAM". Both were wrong in opposite directions, so we researched Speccy and designed a checker that reports honest numbers.

## 1. Speccy: what it is

Piriform (the CCleaner company), first released 2010. Freeware, Windows only, ~8.6 MB, portable build available. Current version v1.34.84 (June 2026) was a maintenance release about EU compliance, not hardware detection — the last real detection update was June 2024. Its own system-requirements page still lists Windows 10/8.1/7/Vista/XP; Windows 11 isn't officially listed.

It is a read-only viewer: it changes nothing, it shows what Windows already knows in one place, with live sensor values beside static inventory. Left category tree, right detail panel. Categories: Summary, Operating System, CPU, RAM, Motherboard, Graphics, Storage, Optical Drives, Audio, Peripherals, Network. It can save a snapshot or export XML/text.

How it gathers data: WMI + registry for most inventory (Win32_Processor, Win32_PhysicalMemory, Win32_VideoController, Win32_DiskDrive, Win32_BaseBoard, Win32_BIOS, Win32_OperatingSystem); DirectX for graphics; a signed kernel-mode helper driver based on CPUID's CPU-Z driver for the low-level reads Windows doesn't expose (PCI config space, RAM SPD data, sensor chips); S.M.A.R.T. via DeviceIoControl / WMI.

Why it's losing ground: the hardware database goes stale, releases are rare, the kernel driver causes freezes mid-scan and trips Windows Memory Integrity, temps are package-level not die-level, VRM temps and PCIe details are often missing, and Windows 11 isn't officially supported. Competitors: HWiNFO (deepest sensors, complex), CPU-Z (CPU/RAM detail, no temps), GPU-Z (GPU detail, correct VRAM), CrystalDiskInfo (SMART), AIDA64 (paid, everything).

## 2. The VRAM bug

We proved it: an 8GB RTX 3070 reads as "4095MB" in Speccy. The mechanism: Speccy reads VRAM from WMI `Win32_VideoController.AdapterRAM`, a 32-bit unsigned integer capped at 4,294,967,295 bytes = 4095MB. Any card over 4GB clamps to 4095MB — 6GB, 8GB, 12GB and 24GB cards all show the same number. The hardware is fine; the reporting path is broken.

It is a decade-old, never-fixed bug: 2016, a GTX 1070 8GB showed 4095MB; 2017, a GTX 1060 6GB; October 2025, an RTX 5060 Ti 16GB; July 2026, an RTX 3090 24GB. Every forum answer is the same: "Speccy's problem, not your hardware — use GPU-Z." Even Windows' own msinfo32 reads the same 32-bit field. Task Manager is the exception: it reads GPU memory through the driver, so its Dedicated GPU memory figure is correct.

The correct ways to read VRAM: DXGI (vendor-neutral, the right default) — `CreateDXGIFactory1` → `EnumAdapters1` → `IDXGIAdapter1::GetDesc1` → `DedicatedVideoMemory`, a 64-bit value, works for NVIDIA, AMD and Intel, discrete and integrated; NVAPI `NvAPI_GPU_GetMemoryInfoEx` for NVIDIA; AMD ADL / Intel equivalents (DXGI makes them unnecessary for the size figure). Not reliable: WMI AdapterRAM, dxdiag, the driver-registry MemorySize value.

## 3. What we tried before, and what it taught us

We tried reading real VRAM from the driver registry — it failed twice, on two different approaches. The registry has no usable device-memory entries on real machines. That path is a dead end.

We tried DXGI — it compiled, but we never proved it on real hardware. So this tool's first job is to prove the DXGI path on real GPUs. It must not ship on an untested claim.

We are reusing the code we already have instead of rewriting it: `DxgiVram.cs` (a manual-vtable DXGI reader, keyed by PCI vendor/device) and `SpecService.cs` (a text report: processor, graphics with VRAM, RAM per slot, motherboard + BIOS, storage, OS, monitors) from the Toolkit's Core library.

## 4. Design: CC Spec Checker v1

A standalone, portable Windows app — one exe, no installer, run from USB like Speccy's portable build. Read-only: it changes nothing on the machine. Made for checking a customer's PC, verifying a build before handover, and confirming the parts in a machine are what they claim to be.

Tech: C# + .NET 8, self-contained single exe (win-x64). UI: Avalonia, two-pane like Speccy, Summary first.

What v1 shows, and how each value is read: Summary (one line per subsystem, like Speccy); Operating System (edition, version/build, arch, install date — WMI + registry); CPU (model, cores/threads, clocks, cache — WMI); RAM (total, type, speed, slots used/free, per-stick brand/part number/capacity/speed — WMI); Motherboard (manufacturer, model, chipset, BIOS version + date — WMI); Graphics (GPU model, VRAM via DXGI 64-bit, driver version, temp, monitor + resolution/refresh); Storage (model, capacity, interface, firmware, SMART health, power-on hours, temp — WMI + SMART); Audio / Network / Peripherals (inventory lists — WMI); Snapshot (save as text + XML, copy to clipboard).

The honest limits, stated openly: no kernel driver in v1, so CPU temperature is a stretch goal (WMI doesn't expose it — see section 5), RAM timings are v2 (they live in the SPD EEPROM, read over SMBus — needs the driver), GPU temperature is v1 via the vendor driver (NVAPI for NVIDIA, ADL for AMD — no kernel driver needed), drive temperature + SMART is v1 (DeviceIoControl, no driver needed). If a value cannot be read, it shows "not available" with the reason. Never a wrong number — that is the whole point of this tool.

Acceptance test, must pass before any release: the DXGI VRAM path proven on real hardware — RTX 2060 6GB, RTX 3070 8GB, RX 7900 XT 20GB, Intel UHD integrated graphics, and a multi-GPU machine with both listed. No release until all of these pass.

What v1 is not: not a tweaker, cleaner or overclocking tool; not a benchmark; not a replacement for HWiNFO or GPU-Z. It is the quick, honest one-screen answer: what is in this PC, and is it healthy?

## 5. Real-time temperature

WMI doesn't expose CPU temperature. Speccy gets its temps from a kernel driver. The old WinRing0 driver is blocklisted (CVE-2020-14979, blocked when Memory Integrity is on). LibreHardwareMonitor 0.9.6 (stable, February 2026) switched to PawnIO — a free, open-source, digitally-signed kernel driver that works with Memory Integrity on.

So: sensor engine is LibreHardwareMonitorLib 0.9.6 (NuGet, MPL-2.0), which reads CPU temps (per-core + package), motherboard temps, fan speeds, voltages and clocks through PawnIO. PawnIO is a one-time, opt-in install — never silent, never bundled. The default download stays a portable, no-install, no-admin single exe. On first run the tool offers "Enable accurate CPU temperature": one UAC prompt, PawnIO installs once, then temps work from then on, including on machines with Memory Integrity on. If the user declines, or the install fails, or the machine is offline, CPU/board temps show "not available" and everything else still works.

GPU temperature never needs the driver — NVAPI (NVIDIA) and ADL (AMD) go through the GPU driver itself, including hotspot and VRAM temp where the driver exposes them. Drive temperature + SMART need no driver either. Polling every 1–2 seconds, like Speccy. CPU package temp + hottest core (under load the max is the number that matters). Standing rule: never run two sensor tools at once — they fight over the same sensor chips and can freeze the machine.

Fallback chain, so no number is ever faked: LHM/PawnIO → vendor APIs (NVAPI/ADL) → SMART/IOCTL → "not available" + the reason.

## 6. GitHub check (versions and licenses)

LibreHardwareMonitor 0.9.6 is the latest stable (14 February 2026), MPL-2.0, on NuGet. We ship four files from the official release, SHA-256 verified: `LibreHardwareMonitorLib.dll`, `System.Memory.dll`, `System.Numerics.Vectors.dll`, `System.Runtime.CompilerServices.Unsafe.dll`. We do not bundle PawnIO.

PawnIO 2.2.0, official signed installer (GPL-2.0 with an IOCTL exception). We never bundle the installer — when CPU sensors are requested and PawnIO is missing, we ask once, then fetch the official setup (pawnio.eu or `winget install namazso.PawnIO`) and verify its signature before running it. Uninstall is `sc.exe delete PawnIO` + `pnputil /delete-driver`.

Glow (Türkaysoft, MIT, C# on .NET Framework 4.8.1) is a reference, not a fork — a 500-feature general tool, and its VRAM method was not verified. Our own code is MIT (proposed). Pinned for v1: LibreHardwareMonitorLib 0.9.6, PawnIO 2.2.0, our own `DxgiVram.cs`.

## 7. Is there a gap?

Yes — a niche one. Speccy is in maintenance mode: no functional updates for years, last real detection update June 2024, the VRAM bug unfixed for 10 years, no official Windows 11 support. The 2026 guides tell people to use three tools (CPU-Z + HWiNFO + CrystalDiskInfo) to replace one Speccy. Speccy is still named because people still want what it was — the simple one-screen overview. "Speccy download" is ~1,900 searches a month.

Why the gap exists: doing it right is hard, and the three hard parts each broke at a different time — correct VRAM needs DXGI, not WMI; temps on modern Windows 11 need PawnIO, which only matured in 2025–2026; and simplicity + accuracy together is rare. Speccy's owner makes its money from CCleaner, not a free viewer, so it rotted. Open source filled the sensor side, but nobody assembled the whole thing into a simple spec checker.

Who it matters to: not most home users — Task Manager covers their questions. It matters to technicians, PC shops and enthusiasts who need a trustworthy, complete, shareable spec readout in one window. Our differentiator is verification: DXGI proven on real GPUs, temps proven on real hardware, honest "not available" instead of fake numbers.

## 8. The plan

A public community project: "we are doing this for everyone." Repo `github.com/musetermind/cc-spec-checker`, MIT license (proposed), credits to LibreHardwareMonitor and PawnIO in the README.

Phases: 1. Setup — repo + skeleton + LHM NuGet reference; prove the Avalonia build compiles on Linux early (if it fails, build the UI on the bench). 2. Engine — port `DxgiVram.cs` + `SpecService.cs` into the new Core; add NVAPI/ADL GPU temperature, SMART reader, LHM integration with the opt-in PawnIO flow. 3. UI — Avalonia two-pane, Summary first, live temp polling, snapshot export (text + XML for v1). 4. Bench proof, the gate — the acceptance test in section 4; no release until it passes. 5. Release — pre-release first, flip to full only after the bench test passes; portable zip + MediaFire mirror. 6. Community — announce, maintain; new-hardware support follows LHM releases.

Risks: the Avalonia-on-Linux build is unproven (proved in phase 1); NVAPI/ADL temperature code is new; the self-contained single-file exe will be ~100–150MB; no dates promised — the bench test is the long pole.
