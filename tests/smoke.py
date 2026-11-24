"""Windows offline check of the import ZIP with the installed game's embedded Mono."""
import argparse
import ctypes
import json
import os
from pathlib import Path
import subprocess
import sys
from tempfile import TemporaryDirectory
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]


def run_mono(game, core, exe, dll, version, extra):
    managed = game / "Super Fantasy Kingdom_Data" / "Managed"
    runtime = game / "MonoBleedingEdge"
    paths = os.pathsep.join(map(str, [managed, core, exe.parent]))
    os.environ["MONO_PATH"] = paths
    ctypes.windll.kernel32.SetErrorMode(3)
    with os.add_dll_directory(str(runtime / "EmbedRuntime")):
        mono = ctypes.CDLL(str(runtime / "EmbedRuntime/mono-2.0-bdwgc.dll"))
    ptr, string = ctypes.c_void_p, ctypes.c_char_p

    def api(name, result, *types):
        func = getattr(mono, name)
        func.argtypes, func.restype = types, result
        return func

    api("mono_set_dirs", None, string, string)(str(managed).encode(), str(runtime / "etc").encode())
    api("mono_set_assemblies_path", None, string)(paths.encode())
    domain = api("mono_jit_init_version", ptr, string, string)(b"CharterSmoke", b"v4.0.30319")
    # The commission callback ends on GameCamera, whose static initializer requests
    # a shader property ID. No renderer is present in this process.
    shader_id = ctypes.CFUNCTYPE(ctypes.c_int, ptr)(lambda name: 1)
    api("mono_add_internal_call", None, string, ptr)(b"UnityEngine.Shader::PropertyToID", shader_id)
    args = [str(exe).encode(), str(dll).encode(), version.encode(), extra.encode()]
    assembly = api("mono_domain_assembly_open", ptr, ptr, string)(domain, args[0])
    if not assembly:
        raise SystemExit("Mono could not open the smoke-test assembly.")
    return api("mono_jit_exec", ctypes.c_int, ptr, ptr, ctypes.c_int, ctypes.POINTER(string))(
        domain, assembly, len(args), (string * len(args))(*args))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, default=Path(r"D:\SteamLibrary\steamapps\common\Super Fantasy Kingdom"))
    parser.add_argument("--profile", type=Path, default=Path(os.environ["APPDATA"]) /
                        "Thunderstore Mod Manager/DataFolder/SuperFantasyKingdom/profiles/Test - Mods")
    parser.add_argument("--compat-dlls", type=Path, help="Optional directory containing ExtraStartingCarrier, CheatMenu, DailyCaravan and CaravanCommissions DLLs")
    args = parser.parse_args()
    version = json.loads((ROOT / "thunderstore/manifest.json").read_text())["version_number"]
    core = args.profile / "BepInEx/core"
    archive = ROOT / "dist" / f"KingdomCharters-{version}.zip"
    compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    with TemporaryDirectory(dir=ROOT / "dist") as work:
        exe = Path(work) / "Smoke.exe"
        dll = exe.with_name("KingdomCharters.dll")
        with ZipFile(archive) as package:
            dll.write_bytes(package.read("plugins/KingdomCharters.dll"))
        managed = args.game / "Super Fantasy Kingdom_Data/Managed"
        references = [managed / (name + ".dll") for name in
                      ["Assembly-CSharp", "UnityEngine.CoreModule", "UnityEngine", "UnityEngine.UI",
                       "UnityEngine.UIModule", "UnityEngine.LocalizationModule", "Unity.Localization", "netstandard", "Newtonsoft.Json"]]
        references.append(core / "BepInEx.dll")
        subprocess.run([str(compiler), "/nologo", f"/out:{exe}", f"/reference:{core / '0Harmony.dll'}",
                        *[f"/reference:{ref}" for ref in references], str(ROOT / "tests/Smoke.cs"),
                        str(ROOT / "tests/Interactions.cs")], check=True)
        # Mono maps its assemblies until process exit. Let the child exit before cleanup.
        return subprocess.run([sys.executable, __file__, "--worker", str(args.game.resolve()),
                               str(core.resolve()), str(exe), str(dll), version, str(args.compat_dlls.resolve()) if args.compat_dlls else ""]).returncode


if __name__ == "__main__":
    if sys.argv[1:2] == ["--worker"]:
        sys.exit(run_mono(*map(Path, sys.argv[2:6]), sys.argv[6], sys.argv[7]))
    sys.exit(main())
