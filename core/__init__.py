"""night-injection — a clean-room Python port of the Lightning Tools feature of
Project Lightning v5.0.8 (Electron, ByDraXx), reconstructed strictly from
evidence extracted from the original application (resources/app.asar) and
runtime artifacts on this machine.

Evidence sources:
  * src/main/lightningtools-ipc.js        (original embedded JS, read verbatim)
  * src/renderer/pages/lightningtools/lightningtools.js
  * src/main/main.js                      (lightningtools:downloadAndInstall / verifySteamPath / clearPlugins)
  * src/renderer/pages/ajustes/ajustes.js (steam path settings)
  * src/main/biblioteca-ipc.js            (library DB + covers)
  * OpenSteamTool.dll (installed in Steam) — strings/PE analysis in tools/

All functions document their original counterpart and evidence level.
"""

__version__ = "1.0.0"
