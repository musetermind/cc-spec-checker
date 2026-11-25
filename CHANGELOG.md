# Changelog

All notable changes to CC Spec Checker. Versions follow the build order:
build 1 is 0.1, build 2 is 0.2, and so on. The version number is shown in
the window title and the header.

## 0.17 — 2026-11-25

- Fixed: RAM detection was broken in 0.16 — the MEMORY card showed "0 GB /
  No RAM sticks found" on a machine with 64 GB installed. The 0.16 query
  asked WMI for CASLatency, which is not a Win32_PhysicalMemory property
  (Microsoft's class definition lists 36 properties; CASLatency is not
  one), so the whole query failed and the app saw zero sticks. The query
  no longer asks for it.
- The CL is gone from the MEMORY line: there is no WMI field for the
  running CAS latency — it lives in the SPD chip on the stick and is
  readable only via the SMBus with a driver. No CL is shown rather than
  a guessed one. The channel mode stays ("Dual Channel").

## 0.16 — 2026-11-25

- MEMORY card: the summary line now shows the negotiated CAS latency and
  the channel mode — "2 sticks · DDR4 @ 3200MHz · CL16 · Dual Channel".
  The CL is the running value WMI reports per stick, not the kit's rated
  CL; when the sticks disagree, the loosest one is shown, because the
  memory controller runs them all at that timing. The channel comes from
  WMI's interleave depth when the BIOS reports it, otherwise from the
  stick count.
- GRAPHICS card: the board-partner brand next to the model — "NVIDIA
  GeForce RTX 3070 · Zotac". Read from the PCI subsystem vendor ID in
  the device's PNPDeviceID; unknown vendors show no brand rather than a
  guess.
- Fixed: the drive-letter lines (0.15) never appeared — the volume
  mapping joined on a DeviceID that WMI object paths escape with doubled
  backslashes, so no drive ever matched its volumes. Unescaped now.

## 0.15 — 2026-11-25

- Dark / Light / System theme toggle (button under the shop credit, cycles
  Dark → Light → System; the choice is saved, System follows Windows live).
  The light theme keeps the green identity on white.
- Storage card shows drive letters with used and free space, e.g.
  "C: 123 GB used · 342 GB free".
- Quick-launch tool strip under the header, two rows of six: Refresh, Copy,
  Save snapshot, Device Manager, Disk Management, Task Manager, Settings,
  File Explorer, Recycle Bin, Disk Cleanup, Resource Monitor, Reliability
  Monitor.
- The shop name in the header credit is a button to the CC Facebook page.
- CPU temperature message reworded: "Restart as Admin to check CPU
  temperature".
- Window 1000×600 → 1000×720 so all six cards stay visible with the strip.

## 0.14 — 2026-11-25

- Header shows the version ("CC SPEC CHECKER v0.14") and the shop credit:
  "Presented to you by CC Computer Corner (Yangon, Myanmar)" with both
  phone numbers.
- Window 1280×800 → 1000×600, compact.

## 0.13 — 2026-11-25

- Cards only: the detail sections under the cards are gone. Each card
  carries all its info, one glance is enough.
- Drive types really fixed: "SSD (NVMe)", "HDD (SATA)", "Removable (USB)"
  — the same labels Task Manager shows. (The previous build read the
  Storage class values with the wrong integer width, so every drive fell
  back to "Fixed hard disk media".)

## 0.12 — 2026-11-25

- CPU temperature is back on the Processor card, on its own line under the
  specs.
- "(via DXGI)" removed from the VRAM line — the provenance belongs in the
  log.
- Real drive type from MSFT_PhysicalDisk (first attempt; see 0.13 for the
  fix).
- Version numbering adopted from here on: build 12 is 0.12. The 0.12 exe
  itself was never released — superseded by 0.13 the same day.

## 0.11 — 2026-11-25

- One scrolling page: cards on top, all sections below, no separate tabs.
- CPU card shows cores / threads / cache only. Max clock removed — WMI's
  MaxClockSpeed is the base clock, not the boost.
- Drive Health removed (the WMI SMART class is not exposed by these
  drives).
- Drive temperature removed — the sensor read 94°C for a drive that reads
  39°C in HD Sentinel.
- DDR type fixed: the bench BIOS reports code 26 for DDR4 DIMMs; the
  mapping now uses the correct WMI codes (25 = DDR4, 33 = DDR5) with a
  speed cross-check.

## 0.10 — 2026-11-25

- CPU card shows cores, threads, cache; temperature colour bands (≤70°C
  green, ≤80°C yellow, ≤97°C orange, above red) on CPU, GPU, board and
  RAM temperatures.
- GPU hotspot temperature when a third sensor exists.
- RAM card shows DDR4/DDR5 and per-stick vendor; motherboard and SSD
  temperatures when available.
- OS card shows Windows activation.

## 0.9 — 2026-11-25

- Full modern redesign: six-card dashboard (Processor, Graphics, Memory,
  Motherboard, Storage, Operating System), VRAM as the Graphics headline.
- Engine rebuilt to a structured model; the plain-text report is rendered
  from the same model, so snapshots and clipboard are unchanged.

## 0.8 — 2026-11-25

- "Restart as administrator" button in the header (visible only when
  unelevated). Cancelling the UAC prompt keeps the app running unelevated.
- SMART read now reports the honest reason when it cannot read (not
  exposed / needs administrator rights / other).

## 0.7 — 2026-11-25

- AMD GPU temperature (ADL) removed entirely — the ADL builds crashed on
  real AMD hardware at launch (0xc0000005), twice, and the heap fix did
  not resolve it. AMD cards show no temperature rather than crashing.
- Proven clean on the bench: VRAM "8 GB", GPU 40°C, CPU 38°C when
  elevated.

## 0.6 — 2026-11-25

- ADL memory allocation fixed to use the CRT malloc/free (the first crash
  was suspected to be a heap mismatch). Still crashed the same way — the
  diagnosis was wrong; see 0.7.

## 0.5 — 2026-11-25

- AMD GPU temperature via ADL (first attempt). Crashed at launch on real
  AMD hardware (0xc0000005).

## 0.4 — 2026-11-25

- VRAM shown as whole gigabytes ("8 GB", "12 GB") instead of raw
  megabytes. DXGI reports available dedicated memory, slightly under the
  rated size, so the display rounds to the nearest whole GB.

## 0.3 — 2026-11-25

- SMART drive health reading added (WMI MSStorageDriver_FailurePredictStatus).

## 0.2 — 2026-11-25

- GPU temperature via NVAPI (NVIDIA driver, no kernel driver needed) —
  proven on the bench at 41°C.
- CPU and board temperatures via LibreHardwareMonitor (PawnIO driver,
  opt-in).
- CPU temperature honestly reports "not available (PawnIO driver not
  installed)" when the driver is absent — never a fake number.

## 0.1 — 2026-11-25

- First build: portable single exe, read-only, unelevated by default.
- VRAM via DXGI — proven on the bench: an 8 GB RTX 3070 reads "8192MB",
  where Speccy showed "4095MB" (WMI's 32-bit AdapterRAM cap).
- Dashboard cards for processor, graphics, memory, motherboard, storage
  and operating system; snapshot (save the report as text, copy to
  clipboard).
