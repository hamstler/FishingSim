"""Packs the local Unity builds for a GitHub release (each file under GitHub's 2 GB limit).

    build/venv/Scripts/python.exe -I tools/release/package_unity.py windows|linux [--version 0.12.3]

windows: build/local/Windows -> build/release/<ver>/FishingSim-<ver>-Windows-czesc1.zip (the player and everything but
         the building and fish models) and -czesc2.zip (those two folders); both hold the folder FishingSim-<ver>/, which
         tools/release/installer/Setup.cs strips while unpacking. Then build_installer.ps1 makes the setup.exe.
linux:   build/local/Linux -> one tar.gz of the folder FishingSim-<ver>-Linux/ (+ icon.png) split into
         FishingSim-<ver>-Linux.tar.gz.part-0, -1 ... (1900 MB each), SHA256SUMS and install.sh
         (tools/release/linux/install.sh joins, checks and unpacks them into ~/.local/share/fishingsim).
"""
import hashlib
import os
import shutil
import sys
import tarfile
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
argv = sys.argv[1:]
platform = argv[0] if argv else "windows"
version = argv[argv.index("--version") + 1] if "--version" in argv else open(os.path.join(ROOT, "tools", "release", "VERSION")).read().strip()
OUT = os.path.join(ROOT, "build", "release", version)
os.makedirs(OUT, exist_ok=True)
SKIP = ("BackUpThisFolder", "DoNotShip")
PART = 1900 * 1024 * 1024


def files(src):
    for d, dirs, fs in os.walk(src):
        dirs[:] = [x for x in dirs if not any(k in x for k in SKIP)]
        for f in fs:
            p = os.path.join(d, f)
            yield p, os.path.relpath(p, src).replace("\\", "/")


def windows():
    src = os.path.join(ROOT, "build", "local", "Windows")
    assert os.path.exists(os.path.join(src, "FishingSim.exe")), "no Windows build (tools/unity/build_local.ps1)"
    big = ("FishingSim_Data/StreamingAssets/Models/buildings/", "FishingSim_Data/StreamingAssets/Models/fish/")
    top = f"FishingSim-{version}"
    names = [os.path.join(OUT, f"FishingSim-{version}-Windows-czesc{i}.zip") for i in (1, 2)]
    zs = [zipfile.ZipFile(n, "w", zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) for n in names]
    for p, rel in files(src):
        zs[1 if rel.startswith(big) else 0].write(p, f"{top}/{rel}")
    for z, n in zip(zs, names):
        z.close()
        mb = os.path.getsize(n) / 1048576
        print(f"PACK {os.path.basename(n)}: {mb:.0f} MB")
        assert mb < 2000, "a part is over GitHub's 2 GB"


class Splitter:
    """A file-like sink cutting the stream into numbered parts."""

    def __init__(self, base):
        self.base, self.n, self.size, self.f, self.names = base, 0, 0, None, []
        self._next()

    def _next(self):
        if self.f:
            self.f.close()
        name = f"{self.base}.part-{self.n}"
        self.names.append(name)
        self.f = open(name, "wb")
        self.n += 1
        self.size = 0

    def write(self, data):
        while data:
            room = PART - self.size
            if room <= 0:
                self._next()
                continue
            chunk = data[:room]
            self.f.write(chunk)
            self.size += len(chunk)
            data = data[room:]

    def flush(self):
        self.f.flush()

    def close(self):
        self.f.close()


def linux():
    src = os.path.join(ROOT, "build", "local", "Linux")
    assert os.path.exists(os.path.join(src, "FishingSim.x86_64")), "no Linux build"
    top = f"FishingSim-{version}-Linux"
    base = os.path.join(OUT, top + ".tar.gz")
    for old in os.listdir(OUT):
        if old.startswith(top + ".tar.gz.part-"):
            os.remove(os.path.join(OUT, old))
    sink = Splitter(base)
    with tarfile.open(fileobj=sink, mode="w|gz", compresslevel=6) as tar:
        def add(path, arc, exe=False):
            info = tar.gettarinfo(path, arc)
            info.mode = 0o755 if exe else 0o644      # Windows has no unix modes: set them
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            with open(path, "rb") as fh:
                tar.addfile(info, fh)
        for p, rel in files(src):
            add(p, f"{top}/{rel}", exe=rel.endswith((".x86_64", ".so")) or rel == "UnityCrashHandler64")
        add(os.path.join(ROOT, "tools", "release", "icon.png"), f"{top}/icon.png")
    sink.close()
    sums = []
    for n in sink.names:
        h = hashlib.sha256()
        with open(n, "rb") as fh:
            for block in iter(lambda: fh.read(1 << 22), b""):
                h.update(block)
        sums.append(f"{h.hexdigest()}  {os.path.basename(n)}")
        print(f"PACK {os.path.basename(n)}: {os.path.getsize(n) / 1048576:.0f} MB")
    with open(os.path.join(OUT, "SHA256SUMS"), "w", newline="\n") as fh:
        fh.write("\n".join(sums) + "\n")
    # Unix line endings whatever the checkout did (bash stops at the first carriage return)
    script = open(os.path.join(ROOT, "tools", "release", "linux", "install.sh"), "rb").read().replace(b"\r\n", b"\n")
    with open(os.path.join(OUT, "install.sh"), "wb") as fh:
        fh.write(script)
    print("PACK SHA256SUMS, install.sh")


{"windows": windows, "linux": linux}[platform]()
