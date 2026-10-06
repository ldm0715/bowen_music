"""Exercise the native mpv API with generated music fixtures before installing a DLL.

All fixtures and reports stay under artifacts. --reference uses the full DLL
only to encode synthetic WAV audio into other formats (no private media needed).
"""
from __future__ import annotations

import argparse
import ctypes as C
from functools import partial
import hashlib
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import math
from pathlib import Path
import struct
import threading
import time
from urllib.parse import quote
import wave

REPO = Path(__file__).resolve().parents[2]


class Event(C.Structure):
    _fields_ = [("event_id", C.c_int), ("error", C.c_int), ("reply_userdata", C.c_ulonglong), ("data", C.c_void_p)]


class EndFile(C.Structure):
    _fields_ = [("reason", C.c_int), ("error", C.c_int), ("playlist_entry_id", C.c_longlong), ("playlist_insert_id", C.c_longlong), ("playlist_insert_num_entries", C.c_int)]


class Mpv:
    def __init__(self, dll: Path, *, encode: tuple[Path, str, str] | None = None, ao: str = "null"):
        self.lib = C.CDLL(str(dll.resolve()))
        signatures = {
            "mpv_create": (C.c_void_p, []),
            "mpv_initialize": (C.c_int, [C.c_void_p]),
            "mpv_set_option_string": (C.c_int, [C.c_void_p, C.c_char_p, C.c_char_p]),
            "mpv_set_property_string": (C.c_int, [C.c_void_p, C.c_char_p, C.c_char_p]),
            "mpv_get_property": (C.c_int, [C.c_void_p, C.c_char_p, C.c_int, C.c_void_p]),
            "mpv_get_property_string": (C.c_void_p, [C.c_void_p, C.c_char_p]),
            "mpv_observe_property": (C.c_int, [C.c_void_p, C.c_ulonglong, C.c_char_p, C.c_int]),
            "mpv_command": (C.c_int, [C.c_void_p, C.POINTER(C.c_char_p)]),
            "mpv_wait_event": (C.POINTER(Event), [C.c_void_p, C.c_double]),
            "mpv_request_log_messages": (C.c_int, [C.c_void_p, C.c_char_p]),
            "mpv_client_api_version": (C.c_ulong, []),
            "mpv_error_string": (C.c_char_p, [C.c_int]),
            "mpv_free": (None, [C.c_void_p]),
            "mpv_terminate_destroy": (None, [C.c_void_p]),
        }
        for name, (restype, argtypes) in signatures.items():
            f = getattr(self.lib, name)
            f.restype, f.argtypes = restype, argtypes
        self.handle = self.lib.mpv_create()
        if not self.handle:
            raise RuntimeError("mpv_create failed")
        self.logs: list[str] = []
        self.options = {"config": "no", "terminal": "no", "vo": "null", "vid": "no", "audio-display": "no", "idle": "yes", "keep-open": "no", "pause": "yes", "ao": ao, "volume": "0"}
        if encode:
            target, codec, container = encode
            self.options.update(o=str(target.resolve()), oac=codec, of=container, pause="no", volume="100")
        for name, value in self.options.items():
            self.check(self.lib.mpv_set_option_string(self.handle, name.encode(), value.encode("utf-8")), "option " + name)
        self.check(self.lib.mpv_initialize(self.handle), "initialize")
        self.lib.mpv_request_log_messages(self.handle, b"warn")
        # Match LibMpvPlaybackService's post-initialize property sequence.
        for name in ["vo", "vid", "audio-display", "keep-open", "idle"]:
            self.set(name, self.options[name])
        if self.string("options/ytdl") is not None:
            self.set("ytdl", "no")
        self.set("gapless-audio", "yes")

    def check(self, result: int, action: str) -> None:
        if result < 0:
            raise RuntimeError(f"{action}: {self.lib.mpv_error_string(result).decode()}")

    def set(self, name: str, value: str) -> None:
        self.check(self.lib.mpv_set_property_string(self.handle, name.encode(), value.encode("utf-8")), name)

    def number(self, name: str) -> float | None:
        result = C.c_double()
        code = self.lib.mpv_get_property(self.handle, name.encode(), 5, C.byref(result))
        return result.value if code >= 0 else None

    def flag(self, name: str) -> bool | None:
        result = C.c_int()
        code = self.lib.mpv_get_property(self.handle, name.encode(), 3, C.byref(result))
        return bool(result.value) if code >= 0 else None

    def string(self, name: str) -> str | None:
        pointer = self.lib.mpv_get_property_string(self.handle, name.encode())
        if not pointer:
            return None
        try:
            return C.string_at(pointer).decode("utf-8", errors="replace")
        finally:
            self.lib.mpv_free(pointer)

    def command(self, *args: str) -> None:
        array = (C.c_char_p * (len(args) + 1))(*[a.encode("utf-8") for a in args], None)
        self.check(self.lib.mpv_command(self.handle, array), "command " + args[0])

    def wait(self, expected: int, timeout: float = 20) -> Event:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            event = self.lib.mpv_wait_event(self.handle, 0.1).contents
            if event.event_id == 7:
                end = C.cast(event.data, C.POINTER(EndFile)).contents
                if end.error < 0 or (expected != 7 and end.reason == 4):
                    raise RuntimeError(f"Playback ended with reason={end.reason}, error={end.error}")
            if event.event_id == expected:
                return event
        raise TimeoutError(f"mpv event {expected} not received")

    def close(self) -> None:
        if self.handle:
            self.lib.mpv_terminate_destroy(self.handle)
            self.handle = None


def fixtures(reference: Path, directory: Path) -> list[Path]:
    directory.mkdir(parents=True, exist_ok=True)
    source = directory / "闊充箰-pcm16.wav"
    if not source.exists():
        with wave.open(str(source), "wb") as output:
            output.setnchannels(2)
            output.setsampwidth(2)
            output.setframerate(48000)
            output.writeframes(b"".join(struct.pack("<hh", int(5000 * math.sin(2 * math.pi * 440 * i / 48000)), int(5000 * math.sin(2 * math.pi * 660 * i / 48000))) for i in range(48000 * 4)))
    paths = [source]
    profiles = [
        ("pcm24.wav", "pcm_s24le", "wav"),
        ("flac.flac", "flac", "flac"),
        ("mp3.mp3", "libmp3lame", "mp3"),
        ("aac.m4a", "aac", "ipod"),
        ("alac.m4a", "alac", "ipod"),
        ("vorbis.ogg", "libvorbis", "ogg"),
        ("opus.opus", "libopus", "opus"),
        ("wavpack.wv", "wavpack", "wv"),
        ("wma.wma", "wmav2", "asf"),
    ]
    for filename, codec, container in profiles:
        target = directory / ("闊充箰-" + filename)
        if not target.exists():
            print("Encoding " + filename, flush=True)
            player = Mpv(reference, encode=(target, codec, container))
            try:
                player.command("loadfile", str(source.resolve()), "replace")
                player.wait(7)
            finally:
                player.close()
        if not target.exists() or target.stat().st_size < 100:
            raise RuntimeError("Fixture encoding failed: " + str(target))
        paths.append(target)
    for rate in [96000, 192000]:
        high = directory / f"闊充箰-pcm24-{rate}.wav"
        if not high.exists():
            with wave.open(str(high), "wb") as output:
                output.setnchannels(2)
                output.setsampwidth(3)
                output.setframerate(rate)
                output.writeframes(b"".join(int(2000000 * math.sin(2 * math.pi * frequency * i / rate)).to_bytes(3, "little", signed=True) for i in range(rate * 4) for frequency in [440, 660]))
        target = directory / f"闊充箰-flac24-{rate}.flac"
        if not target.exists():
            player = Mpv(reference, encode=(target, "flac", "flac"))
            try:
                player.command("loadfile", str(high.resolve()), "replace")
                player.wait(7)
            finally:
                player.close()
        paths.extend([high, target])
    return paths


def playback(dll: Path, source: str, *, ao: str = "null", known_duration: bool = True) -> dict:
    player = Mpv(dll, ao=ao)
    try:
        player.check(player.lib.mpv_observe_property(player.handle, 42, b"time-pos", 5), "observe time-pos")
        player.check(player.lib.mpv_observe_property(player.handle, 43, b"pause", 3), "observe pause")
        player.command("loadfile", source, "replace", "0", "pause=yes")
        player.wait(8)
        duration = player.number("duration")
        codec = player.string("audio-codec-name") or player.string("audio-codec")
        if known_duration and (duration is None or not 3.5 <= duration <= 5.5):
            raise RuntimeError(f"Invalid fixture duration: {duration}")
        player.set("volume", "37")
        assert abs((player.number("volume") or 0) - 37) < .01
        player.set("volume", "0")  # Never produce audible output during validation.
        assert player.flag("pause") is True
        player.set("time-pos", "1.5")
        player.set("pause", "no")
        deadline = time.monotonic() + 8
        changes = set()
        progressing = False
        while time.monotonic() < deadline:
            event = player.lib.mpv_wait_event(player.handle, .05).contents
            if event.event_id == 22:
                changes.add(event.reply_userdata)
            if event.event_id == 7:
                end = C.cast(event.data, C.POINTER(EndFile)).contents
                raise RuntimeError(f"Unexpected end while testing progress: reason={end.reason}, error={end.error}")
            position = player.number("time-pos")
            if position is not None and position > 1.65 and 42 in changes:
                progressing = True
                break
        if not progressing:
            raise RuntimeError("No observed position progress after resume and seek")
        player.set("pause", "yes")
        assert player.flag("pause") is True
        player.set("pause", "no")
        device = player.string("current-ao")
        event = player.wait(7, timeout=12)
        end = C.cast(event.data, C.POINTER(EndFile)).contents
        assert end.reason == 0 and end.error == 0, (end.reason, end.error)
        player.command("stop")
        return {"source": source, "codec": codec, "duration": duration, "audio_output": device, "seek_resume_pause_eof": True, "position_event": 42 in changes}
    finally:
        player.close()


def lossless_pcm(dll: Path, source: Path, reference: Path, directory: Path) -> dict:
    target = directory / (source.name + ".decoded.wav")
    with wave.open(str(reference), "rb") as original:
        sample_width = original.getsampwidth()
    player = Mpv(dll, ao="pcm")
    try:
        player.set("ao-pcm-file", str(target.resolve()))
        player.set("audio-format", "s32" if sample_width == 3 else "s16")
        player.set("volume", "100")
        player.set("pause", "no")
        player.command("loadfile", str(source.resolve()), "replace")
        player.wait(7, timeout=15)
    finally:
        player.close()
    with wave.open(str(reference), "rb") as original, wave.open(str(target), "rb") as decoded:
        assert decoded.getframerate() == original.getframerate()
        assert decoded.getnchannels() == original.getnchannels()
        expected = original.readframes(original.getnframes())
        if sample_width == 3:
            expected = b"".join(b"\x00" + expected[i:i+3] for i in range(0, len(expected), 3))
        actual = decoded.readframes(original.getnframes())
        rate, channels = original.getframerate(), original.getnchannels()
        assert actual == expected, "Lossless PCM differs: " + source.name
    return {"source": source.name, "pcm_equal": True, "sample_rate": rate, "channels": channels, "source_bits": sample_width * 8}


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_args):
        pass


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", type=Path, default=REPO / "artifacts/native-mpv/output/libmpv-2.dll")
    reference = REPO / "artifacts/native-mpv/reference/libmpv-2.dll"
    parser.add_argument("--reference", type=Path, default=reference if reference.exists() else REPO / "libmpv/libmpv-2.dll")
    parser.add_argument("--work-dir", type=Path, default=REPO / "artifacts/native-mpv/verification-signals")
    parser.add_argument("--https-url", default="https://samplelib.com/lib/preview/mp3/sample-3s.mp3")
    args = parser.parse_args()
    files = fixtures(args.reference, args.work_dir / "fixtures")
    results = []
    for path in files:
        print("Testing " + path.name, flush=True)
        results.append(playback(args.dll, str(path.resolve())))
    server = ThreadingHTTPServer(("127.0.0.1", 0), partial(QuietHandler, directory=str(files[0].parent.resolve())))
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        url = f"http://127.0.0.1:{server.server_port}/" + quote(files[2].name)
        print("Testing HTTP FLAC", flush=True)
        results.append(playback(args.dll, url))
    finally:
        server.shutdown()
        server.server_close()
    print("Testing HTTPS MP3", flush=True)
    results.append(playback(args.dll, args.https_url, known_duration=False))
    print("Testing WASAPI (muted)", flush=True)
    results.append(playback(args.dll, str(files[0].resolve()), ao="wasapi"))
    pcm_checks = [lossless_pcm(args.dll, files[i], files[0], args.work_dir) for i in [0, 1, 2, 5, 8]]
    pcm_checks.extend(lossless_pcm(args.dll, files[i + 1], files[i], args.work_dir) for i in [10, 12])
    library = Mpv(args.dll)
    try:
        version = library.lib.mpv_client_api_version()
        assert version >= 0x20005
        configuration = library.string("mpv-configuration")
        # Regression: disabling Lua removes ytdl; app initialization must tolerate it.
        assert library.string("options/ytdl") is None
    finally:
        library.close()
    with args.dll.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    report = {"dll": str(args.dll.resolve()), "size_bytes": args.dll.stat().st_size, "sha256": digest, "client_api_version": hex(version), "configuration": configuration, "checks": results, "lossless_pcm_checks": pcm_checks}
    target = args.work_dir / "verification-report.json"
    target.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"PASS: {len(results)} playback cases. Report: {target}", flush=True)


if __name__ == "__main__":
    main()
