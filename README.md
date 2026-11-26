# CC Spec Checker
A free, portable Windows hardware spec viewer from Computer Corner. Correct VRAM, real-time temperatures, honest numbers.

Lightweight by design: a quick check, not a deep diagnostic. No per-core temperatures, clock speeds, fan readings, voltage rails, stress testing, or full SMART data.

## CPU temperature and the PawnIO driver

CPU temperature comes from LibreHardwareMonitor, which reads the CPU
sensors through the PawnIO kernel driver (v2.2.0, by namazso,
digitally signed). The driver is bundled with this app — the official
setup, unmodified — and it is installed only if you agree: one prompt
in the app, then one administrator approval. Nothing is installed
silently, and nothing else is installed. The driver is installed once
per machine and can be removed in Settings › Apps › PawnIO. Without
it, CPU temperature stays blank; everything else works.

## License

The app itself is MIT (see LICENSE). The bundled PawnIO driver is
GPL-2.0 — see third-party/pawnio/COPYING for the license text and
third-party/pawnio/README.md for the version, hash and source. The
combined download is GPL-covered for the driver part; the app's own
code stays MIT.

## Screenshot

![CC Spec Checker v2.3](screenshot.png)

See [CHANGELOG.md](CHANGELOG.md) for what changed in each version (build 1 is 0.1, build 2 is 0.2, and so on; build 18 ships as 1.8).

Icons: [Material Design Icons](https://pictogrammers.com/library/mdi/) (Apache License 2.0).
