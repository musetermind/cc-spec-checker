# PawnIO driver — bundled with CC Spec Checker

The PawnIO kernel driver lets LibreHardwareMonitor read CPU and
motherboard sensors. It is bundled here so CPU temperature works
without a separate download — and it is installed only when the user
agrees in the app (one prompt, then one administrator approval).

- Version: 2.2.0 (15 March 2026), official signed setup by namazso
- File: PawnIO_setup.exe (3,410,960 bytes, unmodified)
- SHA-256: `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`
- Source: https://github.com/namazso/PawnIO
- Installer releases: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- Website: https://pawnio.eu/

## License

PawnIO is GPL-2.0 — see COPYING (the license text, as published in
the PawnIO repository). The copyright holders add a special
exception, quoted from the PawnIO README:

> In addition, as a special exception, the copyright holders of
> PawnIO give you permission to combine PawnIO program with free
> software programs or libraries that are released under the GNU LGPL
> and with independent modules that communicate with PawnIO solely
> through the device IO control interface.

CC Spec Checker talks to the driver only through that device IO
control interface (via LibreHardwareMonitor), and its own code stays
MIT (see the repo LICENSE). The combined download is GPL-covered for
the driver part.

## Removal

The driver is installed once per machine. Remove it in Settings ›
Apps › PawnIO. Removing it only takes CPU temperature away —
everything else in the app keeps working.
