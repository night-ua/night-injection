"""Static analysis helper for OpenSteamTool.dll (evidence gathering).

Reads PE headers, exports and ASCII/UTF-16 strings. READ-ONLY.

Usage:
    python tools/analyze_opensteamtool.py "C:\\Program Files (x86)\\Steam\\OpenSteamTool.dll"
"""
from __future__ import annotations

import re
import struct
import sys
from pathlib import Path


def read_pe(path: Path) -> dict:
    data = path.read_bytes()
    info: dict = {"size": len(data)}
    if data[:2] != b"MZ":
        info["error"] = "not MZ"
        return info
    pe_off = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_off:pe_off + 4] != b"PE\x00\x00":
        info["error"] = "not PE"
        return info
    machine, num_sections, _, _, _ = struct.unpack_from("<HHIII", data, pe_off + 4)
    opt_off = pe_off + 24
    magic = struct.unpack_from("<H", data, opt_off)[0]
    info["machine"] = hex(machine)
    info["sections"] = num_sections
    info["pe64"] = magic == 0x20B
    # data directories: exports at index 0
    dd_off = opt_off + (112 if magic == 0x20B else 96)
    exp_rva, exp_size = struct.unpack_from("<II", data, dd_off)
    # section table for RVA->file offset
    sec_off = opt_off + struct.unpack_from("<H", data, pe_off + 20)[0]
    sections = []
    for i in range(num_sections):
        s = sec_off + i * 40
        name = data[s:s + 8].rstrip(b"\x00").decode("ascii", "replace")
        vsize, va, rsize, ro = struct.unpack_from("<IIII", data, s + 8)
        sections.append((name, va, vsize, ro, rsize))
    info["sections_table"] = sections

    def rva_to_off(rva: int) -> int | None:
        for name, va, vsize, ro, rsize in sections:
            if va <= rva < va + max(vsize, rsize):
                return ro + (rva - va)
        return None

    if exp_rva:
        eoff = rva_to_off(exp_rva)
        if eoff is not None:
            _, _, _, nfuncs, nnames, _, name_rva, _, ord_base, _, nnames2 = struct.unpack_from(
                "<IIHHIIIIIIII", data, eoff)[:12]
            dll_name_off = rva_to_off(name_rva)
            info["export_dll_name"] = data[dll_name_off:data.index(b"\x00", dll_name_off)].decode(
                "ascii", "replace") if dll_name_off else None
            names_rva = struct.unpack_from("<I", data, eoff + 32)[0]
            noff = rva_to_off(names_rva)
            exports = []
            if noff is not None:
                for i in range(min(nnames, 200)):
                    nrva = struct.unpack_from("<I", data, noff + i * 4)[0]
                    fo = rva_to_off(nrva)
                    if fo is not None:
                        end = data.index(b"\x00", fo)
                        exports.append(data[fo:end].decode("ascii", "replace"))
            info["exports"] = exports
    return info


ASCII_RUN = re.compile(rb"[\x20-\x7e]{5,}")
UTF16_RUN = re.compile(rb"(?:[\x20-\x7e]\x00){5,}")


def interesting_strings(path: Path, limit: int = 400) -> dict:
    data = path.read_bytes()
    out: dict[str, list[str]] = {"ascii": [], "utf16": []}
    seen = set()
    for m in ASCII_RUN.finditer(data):
        s = m.group().decode("ascii")
        low = s.lower()
        if any(k in low for k in (
                "lua", "stplug", "steam", "depot", "manifest", "appid", "config",
                "pipe", "inject", "http", "watch", "library", "appcache", "refresh",
                "unlock", "plugin", "hook", "dxgi", "dwmapi", "xinput")):
            if s not in seen:
                seen.add(s)
                out["ascii"].append(s)
                if len(out["ascii"]) >= limit:
                    break
    seen = set()
    for m in UTF16_RUN.finditer(data):
        s = m.group().decode("utf-16-le", "replace")
        low = s.lower()
        if any(k in low for k in ("lua", "stplug", "steam", "depot", "manifest", "appid",
                                  "config", "pipe", "library", "appcache", "plugin")):
            if s not in seen:
                seen.add(s)
                out["utf16"].append(s)
                if len(out["utf16"]) >= limit:
                    break
    return out


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        return
    path = Path(sys.argv[1])
    if not path.exists():
        print(f"file not found: {path}")
        return
    info = read_pe(path)
    print("=== PE INFO ===")
    for k, v in info.items():
        if k != "sections_table":
            print(f"{k}: {v}")
    print("=== INTERESTING STRINGS ===")
    strs = interesting_strings(path)
    print("-- ascii --")
    for s in strs["ascii"]:
        print(" ", s)
    print("-- utf16 --")
    for s in strs["utf16"]:
        print(" ", s)


if __name__ == "__main__":
    main()
