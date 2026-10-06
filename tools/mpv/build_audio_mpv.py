"""Build a static Windows x64 music profile of libmpv; keep outputs in artifacts.

Requirements: Windows x64, Python 3.12+, Git for Windows. The script downloads
an isolated LLVM toolchain and Python build tools, never edits system PATH,
and never replaces the application's DLL. See docs/libmpv-audio-build.md for build, validation and rollback instructions.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

REPO = Path(__file__).resolve().parents[2]
LOCK_PATH = Path(__file__).with_name("sources.lock.json")
SOURCES = {
    "vulkan-headers": ("KhronosGroup/Vulkan-Headers", "v1.4.304"),
    "pkgconf": ("pkgconf/pkgconf", "pkgconf-2.4.3"),
    "freetype": ("freetype/freetype", "VER-2-13-3"),
    "fribidi": ("fribidi/fribidi", "v1.0.16"),
    "harfbuzz": ("harfbuzz/harfbuzz", "10.4.0"),
    "libass": ("libass/libass", "0.17.4"),
    "libplacebo": ("haasn/libplacebo", "v7.360.1"),
    "ffmpeg": ("FFmpeg/FFmpeg", "939c2c733"),
    "mpv": ("mpv-player/mpv", "e470f8986e"),
}
TOOL_URLS = {
    "llvm": "https://github.com/mstorsjo/llvm-mingw/releases/download/20260922/llvm-mingw-20260922-ucrt-x86_64.zip",
    "nasm": "https://www.nasm.us/pub/nasm/releasebuilds/2.16.03/win64/nasm-2.16.03-win64.zip",
}
TOOL_HASHES = {
    "llvm": "e3ad77d117a4bea19a7a3b333341824d79a5a371004a10e25b8504e7b3047666",
    "nasm": "3ee4782247bcb874378d02f7eab4e294a84d3d15f3f6ee2de2f47a46aa7226e6",
}
PYTHON_PACKAGES = ["meson==1.8.3", "ninja==1.11.1.4", "jinja2==3.1.6", "MarkupSafe==3.0.3"]
DEMUXERS = "aac,ac3,aiff,ape,asf,au,caf,concat,dsf,dsdiff,dts,eac3,flac,hls,matroska,mov,mp3,mpegts,ogg,pcm_alaw,pcm_mulaw,pcm_f32le,pcm_f64le,pcm_s16le,pcm_s24le,pcm_s32le,pcm_u8,spdif,tta,wav,wv"
PARSERS = "aac,aac_latm,ac3,adts_header,dca,flac,mpegaudio,opus,vorbis"
FILTERS = "abuffer,abuffersink,anull,aformat,aresample,asetpts,atempo,atrim,volume,pan,equalizer,alimiter,loudnorm,replaygain,buffer,buffersink,null,format,scale"
PROTOCOLS = "file,pipe,fd,http,https,httpproxy,tcp,tls,crypto,data,concat,subfile"


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def download(url: str, path: Path, expected: str | None = None) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        print(f"Downloading {path.name}", flush=True)
        request = urllib.request.Request(url, headers={"User-Agent": "Bodian-mpv-audio-build"})
        with urllib.request.urlopen(request, timeout=120) as response, path.open("wb") as output:
            shutil.copyfileobj(response, output, length=1024 * 1024)
    digest = sha256(path)
    if expected and digest != expected:
        raise RuntimeError(f"SHA256 mismatch: {path}. Keep the file for inspection and use a fresh --work-dir.")
    return digest


def extract_source(archive: Path, target: Path) -> None:
    if (target / ".bodian-extracted").exists():
        return
    target.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as source:
        members = []
        for member in source.getmembers():
            parts = Path(member.name).parts
            if len(parts) < 2:
                continue
            member.name = str(Path(*parts[1:]))
            members.append(member)
        source.extractall(target, members=members, filter="data")
    (target / ".bodian-extracted").write_text(sha256(archive), encoding="utf-8")


class Builder:
    def __init__(self, work: Path, bash: Path, jobs: int, bootstrap_lock: bool, resume_from: str = "pkgconf"):
        self.work, self.bash, self.jobs = work, bash, jobs
        self.bootstrap_lock = bootstrap_lock
        self.resume_from = resume_from
        self.prefix = work / "install"
        self.env = os.environ.copy()
        self.lock = json.loads(LOCK_PATH.read_text(encoding="utf-8")) if LOCK_PATH.exists() else {}
        for directory in ["bootstrap", "downloads", "src", "build", "logs", "output", "install", "tmp"]:
            (work / directory).mkdir(parents=True, exist_ok=True)

    def run(self, name: str, command: list[str], cwd: Path | None = None) -> None:
        print(f"[{name}]", flush=True)
        log = self.work / "logs" / f"{name}.log"
        with log.open("w", encoding="utf-8") as stream:
            result = subprocess.run(command, cwd=cwd or self.work, env=self.env, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode:
            print(log.read_text(encoding="utf-8", errors="replace")[-9000:], flush=True)
            raise RuntimeError(f"{name} failed ({result.returncode}). Log: {log}")

    def bootstrap(self) -> None:
        for name, url in TOOL_URLS.items():
            path = self.work / "bootstrap" / f"{name}.zip"
            download(url, path, TOOL_HASHES[name])
            directory = "llvm-mingw-20260922-ucrt-x86_64" if name == "llvm" else "nasm-2.16.03"
            if not (path.parent / directory).exists():
                with zipfile.ZipFile(path) as source:
                    source.extractall(path.parent)
        self.toolchain = self.work / "bootstrap/llvm-mingw-20260922-ucrt-x86_64/bin"
        self.nasm = self.work / "bootstrap/nasm-2.16.03"
        python = self.work / "venv/Scripts/python.exe"
        if not python.exists():
            self.run("venv", [sys.executable, "-m", "venv", str(self.work / "venv")])
        self.python = python
        if not (self.work / "venv/.bodian-packages").exists():
            self.run("pip", [str(python), "-m", "pip", "install", "--disable-pip-version-check", *PYTHON_PACKAGES])
            (self.work / "venv/.bodian-packages").write_text("\n".join(PYTHON_PACKAGES), encoding="utf-8")
        self.meson = self.work / "venv/Scripts/meson.exe"
        self.ninja = self.work / "venv/Scripts/ninja.exe"
        self.env["PATH"] = ";".join(map(str, [self.toolchain, self.nasm, python.parent, self.prefix / "bin", self.bash.parent])) + ";" + self.env["PATH"]
        self.env.update(CC="clang", CXX="clang++", AR="llvm-ar", NM="llvm-nm", RANLIB="llvm-ranlib", STRIP="llvm-strip")
        self.env["PKG_CONFIG_LIBDIR"] = str(self.prefix / "lib/pkgconfig")
        self.env["PKG_CONFIG_PATH"] = str(self.prefix / "lib/pkgconfig")
        self.env["SOURCE_DATE_EPOCH"] = "1790553600"
        self.env["PYTHONUTF8"] = "1"
        # NASM 2.x cannot open non-ASCII Windows TEMP paths. Keep scratch local.
        self.env["TEMP"] = str(self.work / "tmp")
        self.env["TMP"] = str(self.work / "tmp")
        self.env["TMPDIR"] = (self.work / "tmp").as_posix()
        self.env["PYTHON"] = str(python)

    def sources(self) -> None:
        def fetch(item):
            name, (repository, ref) = item
            entry = self.lock.get(name)
            if not entry and not self.bootstrap_lock:
                raise RuntimeError("Missing sources.lock.json; source hashes must be reviewed before building.")
            url = entry["url"] if entry else f"https://codeload.github.com/{repository}/tar.gz/{ref}"
            path = self.work / "downloads" / f"{name}-{ref}.tar.gz"
            digest = download(url, path, entry["sha256"] if entry else None)
            extract_source(path, self.work / "src" / name)
            return name, {"repository": repository, "ref": ref, "url": url, "sha256": digest}
        with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
            for name, entry in pool.map(fetch, SOURCES.items()):
                self.lock[name] = entry
        if self.bootstrap_lock:
            LOCK_PATH.write_text(json.dumps(self.lock, indent=2) + "\n", encoding="utf-8")

    def meson_build(self, name: str, options: list[str], *, shared: bool = False) -> None:
        source, build = self.work / "src" / name, self.work / "build" / name
        base = [str(self.meson), "setup", str(build), str(source), "--prefix=" + str(self.prefix), "--libdir=lib", "--buildtype=minsize", "--wrap-mode=nofallback", "-Dauto_features=disabled", "-Dprefer_static=true", "-Ddefault_library=" + ("shared" if shared else "static"), "-Db_lto=true", "-Db_ndebug=true", "-Dc_args=-march=x86-64 -mtune=generic -ffunction-sections -fdata-sections -I" + (self.prefix / "include").as_posix(), "-Dcpp_args=-march=x86-64 -mtune=generic -ffunction-sections -fdata-sections", "-Dc_link_args=" + ("--driver-mode=g++ -static-libstdc++ " if name == "mpv" else "") + "-static-libgcc -Wl,--gc-sections", "-Dcpp_link_args=-static-libgcc -static-libstdc++ -Wl,--gc-sections", *options]
        if (build / "build.ninja").exists():
            base.insert(2, "--reconfigure")
        self.run(name + "-configure", base)
        self.run(name + "-compile", [str(self.ninja), "-C", str(build), "-j", str(self.jobs)])
        self.run(name + "-install", [str(self.meson), "install", "-C", str(build), "--no-rebuild"])

    def ffmpeg(self) -> None:
        source = self.work / "src/ffmpeg"
        text = (source / "libavcodec/allcodecs.c").read_text(encoding="utf-8")
        audio = text.split("/* audio codecs */", 1)[1].split("/* subtitles */", 1)[0]
        decoders = sorted(set(re.findall(r"extern const FFCodec ff_(\w+)_decoder;", audio)))
        decoders = [name for name in decoders if not name.startswith("lib")]
        self.decoders = decoders
        # Configure in a separate directory so downloaded sources remain inspectable.
        build = self.work / "build/ffmpeg"
        build.mkdir(exist_ok=True)
        flags = [(source / "configure").as_posix(), "--prefix=" + self.prefix.as_posix(), "--target-os=mingw32", "--arch=x86_64", "--cc=clang", "--cxx=clang++", "--ar=llvm-ar", "--nm=llvm-nm", "--ranlib=llvm-ranlib", "--strip=llvm-strip", "--pkg-config=" + (self.prefix / "bin/pkgconf.exe").as_posix(), "--disable-autodetect", "--disable-everything", "--disable-programs", "--disable-doc", "--disable-debug", "--disable-shared", "--enable-static", "--enable-small", "--enable-network", "--enable-w32threads", "--enable-schannel", "--enable-avcodec", "--enable-avformat", "--enable-avfilter", "--enable-swresample", "--enable-swscale", "--disable-avdevice", "--disable-encoders", "--disable-muxers", "--disable-hwaccels", "--enable-decoder=" + ",".join(decoders), "--enable-demuxer=" + DEMUXERS, "--enable-parser=" + PARSERS, "--enable-filter=" + FILTERS, "--enable-protocol=" + PROTOCOLS, "--extra-cflags=-march=x86-64 -mtune=generic -ffunction-sections -fdata-sections", "--extra-ldflags=-static-libgcc -Wl,--gc-sections"]
        self.run("ffmpeg-configure", [str(self.bash), *flags], build)
        # FFmpeg requires GNU make. LLVM MinGW ships a native mingw32-make.
        make = self.toolchain / "mingw32-make.exe"
        if not make.exists():
            make = Path(shutil.which("mingw32-make") or shutil.which("make") or "")
        if not make.is_file():
            raise RuntimeError("GNU make is required; put mingw32-make.exe on PATH.")
        self.run("ffmpeg-compile", [str(make), "-j", str(self.jobs), "SHELL=" + self.bash.as_posix()], build)
        self.run("ffmpeg-install", [str(make), "install", "V=1", "SHELL=" + self.bash.as_posix()], build)
        self.record_ffmpeg_profile()

    def record_ffmpeg_profile(self) -> None:
        build = self.work / "build/ffmpeg"
        text = (self.work / "src/ffmpeg/libavcodec/allcodecs.c").read_text(encoding="utf-8")
        config = (build / "config_components.h").read_text(encoding="utf-8")
        enabled = re.findall(r"#define CONFIG_(\w+)_DECODER 1", config)
        video = text.split("/* audio codecs */", 1)[0]
        video_names = {n.upper() for n in re.findall(r"extern const FFCodec ff_(\w+)_decoder;", video)}
        if set(enabled) & video_names:
            raise RuntimeError("Unexpected video decoder enabled: " + str(set(enabled) & video_names))
        required = {"AAC", "ALAC", "APE", "FLAC", "MP3", "OPUS", "VORBIS", "PCM_S16LE", "PCM_S24LE", "WAVPACK", "WMAV2"}
        if missing := required - set(enabled):
            raise RuntimeError("Required music decoders are missing: " + str(missing))
        profile = {"decoders": sorted(enabled), "video_decoders": [], "demuxers": re.findall(r"#define CONFIG_(\w+)_DEMUXER 1", config), "filters": re.findall(r"#define CONFIG_(\w+)_FILTER 1", config), "protocols": re.findall(r"#define CONFIG_(\w+)_PROTOCOL 1", config), "sources": self.lock}
        (self.work / "output/audio-profile.json").write_text(json.dumps(profile, indent=2) + "\n", encoding="utf-8")

    def build(self) -> None:
        self.bootstrap()
        self.sources()
        # Headers only: libplacebo requires Vulkan types even with its backend disabled.
        shutil.copytree(self.work / "src/vulkan-headers/include", self.prefix / "include", dirs_exist_ok=True)
        stages = [
            ("pkgconf", []),
            ("freetype", ["-Dzlib=disabled", "-Dbzip2=disabled", "-Dpng=disabled", "-Dharfbuzz=disabled", "-Dbrotli=disabled"]),
            ("fribidi", ["-Ddocs=false", "-Dbin=false", "-Dtests=false"]),
            ("harfbuzz", ["-Dtests=disabled", "-Dutilities=disabled", "-Ddocs=disabled", "-Dfreetype=disabled"]),
            ("libass", ["-Drequire-system-font-provider=false", "-Ddirectwrite=disabled", "-Dasm=enabled"]),
            ("libplacebo", ["-Ddemos=false", "-Dtests=false", "-Ddovi=disabled"]),
            ("ffmpeg", []),
            ("mpv", ["-Dcplayer=false", "-Dlibmpv=true", "-Dbuild-date=false", "-Dlua=disabled", "-Dgl=disabled", "-Dwasapi=enabled", "-Dwin32-threads=enabled", "-Dvector=enabled"]),
        ]
        self.env["PKG_CONFIG"] = str(self.prefix / "bin/pkgconf.exe")
        ready = False
        for name, options in stages:
            ready = ready or name == self.resume_from
            if not ready:
                continue
            if name == "ffmpeg":
                self.ffmpeg()
            else:
                if name == "mpv":
                    self.record_ffmpeg_profile()
                self.meson_build(name, options, shared=name == "mpv")
        dll = self.work / "build/mpv/libmpv-2.dll"
        if not dll.exists():
            raise RuntimeError("Expected libmpv-2.dll was not generated")
        output = self.work / "output/libmpv-2.dll"
        shutil.copy2(dll, output)
        self.run("strip", [str(self.toolchain / "llvm-strip.exe"), "--strip-all", str(output)])
        manifest = {"dll": output.name, "size_bytes": output.stat().st_size, "sha256": sha256(output), "architecture": "x86_64 (SSE2 baseline)", "toolchain": TOOL_URLS, "toolchain_sha256": TOOL_HASHES, "sources": self.lock}
        (output.parent / "build-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        self.run("imports", [str(self.toolchain / "llvm-readobj.exe"), "--coff-imports", str(output)])
        imports = re.findall(r"^  Name: (.+)$", (self.work / "logs/imports.log").read_text(encoding="utf-8"), re.MULTILINE)
        system_libraries = {"ole32.dll", "user32.dll", "avrt.dll", "ntdll.dll", "kernel32.dll", "shell32.dll", "advapi32.dll", "bcrypt.dll", "shlwapi.dll", "ws2_32.dll", "ncrypt.dll", "crypt32.dll", "secur32.dll"}
        if external := [name for name in imports if name.lower() not in system_libraries and not name.lower().startswith("api-ms-win-")]:
            raise RuntimeError("Unexpected external DLL dependencies: " + str(external))
        manifest["system_imports"] = imports
        (output.parent / "build-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(manifest, indent=2), flush=True)
        print("Candidate built. Run verify_audio_mpv.py before installing it.", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-dir", type=Path, default=REPO / "artifacts/native-mpv")
    parser.add_argument("--bash", type=Path, default=Path(shutil.which("git") or "E:/Git/cmd/git.exe").parents[1] / "usr/bin/bash.exe")
    parser.add_argument("--jobs", type=int, default=min(os.cpu_count() or 2, 8))
    parser.add_argument("--resume-from", choices=["pkgconf", "freetype", "fribidi", "harfbuzz", "libass", "libplacebo", "ffmpeg", "mpv"], default="pkgconf", help="Resume an existing work directory after a failed stage")
    parser.add_argument("--bootstrap-lock", action="store_true", help="Maintainer only: record hashes for review on the first build")
    args = parser.parse_args()
    if os.name != "nt" or sys.maxsize < 2**32:
        parser.error("Windows and a 64-bit Python are required")
    if not args.bash.is_file():
        parser.error("Git Bash not found; pass --bash <path-to-bash.exe>")
    Builder(args.work_dir.resolve(), args.bash.resolve(), args.jobs, args.bootstrap_lock, args.resume_from).build()


if __name__ == "__main__":
    main()
