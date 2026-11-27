# Tested hardware

What CC Spec Checker has actually run on, and what it has not run on yet.
A blank temperature on AMD graphics is expected, not a bug — the AMD
temperature reader was removed, so the app shows nothing rather than guessing.

## Proven

| Machine | CPU | GPU | RAM | Motherboard | Version | Result |
| --- | --- | --- | --- | --- | --- | --- |
| Bench (Intel + NVIDIA) | i9-14900K | Zotac RTX 3070 8 GB | 64 GB DDR4 @ 3200 MHz, dual channel (Corsair + Kingston) | ASUS PRIME B660M-K D4, BIOS 4004 | 2.1 / 2.2 | All cards correct; CPU temp 39 °C elevated; DDR4 re-test passed 2026-11-26 |
| Bench (Intel + AMD) | i9-14900K | Radeon RX 5500 XT 8 GB | Same 64 GB DDR4 | Same PRIME B660M-K D4 | 2.1 | No crash; VRAM reads a true 8 GB; GPU temp blank as designed |
| Monster / flagship | i9-13900KF | Sapphire Pure RX 7900 XT 20 GB | 64 GB JUHOR DDR5 @ 6000 MHz | MSI PRO Z790-P WiFi | 2.1 / 2.3 | All cards correct; CPU temp 42 °C package, 44 °C hottest core on 2.3; this run exposed the DDR5 label bug (fixed in 2.2) |
| Bench (Intel + NVIDIA + iGPU) | i9-14900K | Zotac RTX 3070 8 GB + Intel UHD Graphics 770 | Same 64 GB DDR4 | Same PRIME B660M-K D4 | 2.4 | Both GPUs listed, discrete first; UHD reads "128 MB Dedicated + 47.7 GB Shared"; thin divider between the cards |
| Bench (Intel + NVIDIA + iGPU) | i9-14900K | ASUS RTX 3050 6 GB + Intel UHD Graphics 770 | Same 64 GB DDR4 | Same PRIME B660M-K D4 | 2.4 | GPU temperature matched by PCI key: the 3050 reads 37 °C on the right card; UHD line shows no board-partner brand |
| Bench (Intel iGPU only) | i9-14900K | Intel UHD Graphics 770 only (discrete card removed) | Same 64 GB DDR4 | Same PRIME B660M-K D4 | 2.4 | Single GPU, honest shared-memory label, no divider, no crash |

All runs on Windows 11 Pro, Build 26300.

## Not yet tested

- AMD CPUs
- Laptops
- AMD integrated graphics
- Gigabyte motherboards
- Any other GPU, RAM or board combination

If you run it on one of those, a bug report with your specs and a
screenshot is the test — that is how the table grows.
