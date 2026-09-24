"""Authorized P1-002 C# lifecycle test; retain the existing P0-010 fixtures.

Secrets cross only stdin. This is experimental USB bootstrap, not pairing.
"""
import base64
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import socket
import ssl
import subprocess
import sys
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tests/integration/upstream_chain'))
import real_device_chain as chain
import tls_runtime as tls

CA_SHA = '65b52a178912e36cc73dc820f4029e85efa75a3427c2a341902326a8d2480903'
DIRECTORY = 'P0-010-84926bfb5cd4'
TEXT_SHA = '7374185329b3a98d96e953d8ed38bfe1c528ca5dd6b8298bdead468f65c5251e'
NAMES = ['pc-upload-100MB.bin', 'phone-100MB.bin', 'phone-origin.txt', 'renamed-folder']
kernel = ctypes.WinDLL('kernel32', use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
kernel.CloseHandle.argtypes = [wintypes.HANDLE]


def drive_present():
    return bool(kernel.GetLogicalDrives() & (1 << 15))


@contextmanager
def mount_phone_host():
    original = tls.write_private

    def signal(path, content):
        if path == 'cache/p0-009-stop':
            assert content == b'stop'
            # TlsServiceHost checks existence then deletes this marker on exit.
            # Its final OK is the acknowledgment; reading the consumed marker races teardown.
            tls.adb('shell', 'run-as', 'com.phonebridge', 'touch', path)
        else:
            original(path, content)

    tls.write_private = signal
    try:
        with chain.phone_service_host('csharp-mount'):
            yield
    finally:
        tls.write_private = original


def main():
    run = ROOT / '.audit/runs/P1-002' / ('real-' + uuid.uuid4().hex[:12])
    run.mkdir(parents=True)
    chain.RUN = run; chain.RUN_ID = run.name; chain.ROWS = []
    tls.RUN = run / 'host'; tls.RUN.mkdir(); tls.ROWS = []
    chain.REMOTE = '/sdcard/Music/' + DIRECTORY
    checks = []

    def record(test, passed, **data):
        checks.append(dict(test=test, passed=passed, **data))
        (run / 'results.json').write_text(json.dumps(dict(run=run.name, checks=checks), indent=2), encoding='utf-8')
        print(json.dumps(checks[-1]), flush=True)
        assert passed, test

    assert tls.SERIAL == '61c9a964'
    assert tls.adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
    assert tls.adb('shell', 'getprop', 'ro.build.version.sdk').strip() == b'36'
    assert f'inet {chain.PHONE_IP}/24' in tls.adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
    with socket.socket() as check:
        check.bind((chain.LOCAL_IP, 0))
    assert not drive_present() and not tls.adb('forward', '--list').strip()
    for package, expected in [('com.phonebridge', chain.APK_SHA), ('com.phonebridge.test',
            'e6c62d4fd05367feecb61f8640cf5263bc14c8c4d3bef648b1a3c34178587a5d')]:
        path = tls.adb('shell', 'pm', 'path', package).decode().strip().removeprefix('package:')
        assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', path)
        assert tls.adb('shell', 'sha256sum', path).decode().split()[0] == expected
    der = tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', tls.CERT)
    assert hashlib.sha256(der).hexdigest() == CA_SHA
    before_files = tls.adb('shell', 'find', '/sdcard/Music', '-type', 'f')
    before_meta = chain.metadata_snapshot()
    before_data = {name: chain.phone_info(name) for name in NAMES if name.endswith(('.bin', '.txt'))}
    secret = tls.password()
    config = dict(Certificate=base64.b64encode(der).decode(), Fingerprint=CA_SHA, User='phonebridge', Password=secret,
                  Address=chain.PHONE_IP, Port=8273, Directory=DIRECTORY, Drive='P', Rclone=str(tls.RCLONE),
                  SessionRoot=str(run / 'sessions with spaces'))
    dotnet = ROOT / '.audit/tools/dotnet-10.0.401/dotnet.exe'
    dll = ROOT / 'windows/src/PhoneBridge.Mounting.Diagnostics/bin/Release/net10.0-windows10.0.19041.0/PhoneBridge.Mounting.Diagnostics.dll'

    def launch(label, override=None):
        stream = (run / f'{label}.jsonl').open('w', encoding='utf-8')
        errors = (run / f'{label}.stderr').open('wb')
        child = subprocess.Popen([str(dotnet), str(dll)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=errors, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
        events = []; updates = queue.Queue()

        def read():
            for line in child.stdout:
                assert secret not in line, 'secret-in-diagnostics'
                event = json.loads(line)
                events.append(event); updates.put(event)
                stream.write(line); stream.flush()

        reader = threading.Thread(target=read); reader.start()
        child.stdin.write(json.dumps(config | (override or {})) + '\n'); child.stdin.flush()
        return child, events, updates, reader, stream, errors

    def finish(instance, stop=False, expected=0, drive_expected=False):
        child, events, updates, reader, stream, errors = instance
        try:
            if stop and child.poll() is None:
                child.stdin.write('stop\n'); child.stdin.flush()
            child.wait(timeout=50)
            reader.join(timeout=5)
            assert not reader.is_alive()
        finally:
            if child.poll() is None: child.kill(); child.wait(timeout=5)
            stream.close(); errors.close(); child.stdin.close(); child.stdout.close()
        assert child.returncode == expected, 'diagnostic-exit-' + str(child.returncode)
        assert drive_present() == drive_expected, 'drive-state-mismatch'
        return events

    def rejected(label, changes, code=None):
        events = finish(launch(label, changes), expected=2)
        record(label, not any(e['kind'] == 'mounted' for e in events) and
               any(e['kind'] == 'error' and (code is None or e['result']['code'] == code) for e in events)
               and events[-1]['result']['State'] == ('Idle' if code == 'identity-mismatch' else 'Failed'), events=events)

    try:
        rejected('fingerprint-mismatch', {'Fingerprint': '0' * 64}, 'identity-mismatch')
        rejected('occupied-drive', {'Drive': 'C'}, 'drive-occupied')
        bad_exe = run / 'untrusted rclone.exe'; bad_exe.write_bytes(b'P1-002 synthetic invalid executable')
        rejected('executable-hash-mismatch', {'Rclone': str(bad_exe)}, 'rclone-hash-mismatch')
        with mount_phone_host():
            normal = finish(launch('normal', {'Probe': True}))
            probe = next(e['result'] for e in normal if e['kind'] == 'probe')
            record('real-readonly-read-and-write-rejection', probe == dict(names=NAMES, bytes=49, sha256=TEXT_SHA,
                   writeBlocked=True, writeError=probe.get('writeError')) and probe['writeError'] in (5, 19), probe=probe)
            end = normal[-1]['result']
            record('normal-unmount', end['State'] == 'Stopped' and end['ExitCode'] == 0 and not end['Forced'], snapshot=end)
            rejected('wrong-password', {'Password': 'P1-002-wrong-' + uuid.uuid4().hex})
            # A valid synthetic CA with its own matching fingerprint must still be rejected by real TLS.
            wrong = ssl.PEM_cert_to_DER_cert(Path(__file__).with_name('wrong-ca.pem').read_text(encoding='utf-8'))
            rejected('wrong-ca', {'Certificate': base64.b64encode(wrong).decode(), 'Fingerprint': hashlib.sha256(wrong).hexdigest()})

            # Hold a real owned handle before killing the diagnostic host; do not look up and kill a later PID.
            held = launch('host-crash')
            child, events, updates, reader, stream, errors = held
            handle = None
            try:
                event = updates.get(timeout=40)
                assert event['kind'] == 'mounted', event
                pid = event['result']['ProcessId']
                handle = kernel.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE only
                assert handle and kernel.WaitForSingleObject(handle, 0) == 258
                ca_paths = list((run / 'sessions with spaces').glob('*/device-ca.pem'))
                assert len(ca_paths) == 1
                locked = False
                try:
                    with ca_paths[0].open('r+b'): pass  # open only, never mutate
                except PermissionError: locked = True
                record('active-ca-write-lock', locked)
                duplicate = finish(launch('duplicate-process'), expected=2, drive_expected=True)
                record('cross-process-drive-lease', any(e['kind'] == 'error' and e['result']['code'] == 'drive-reserved' for e in duplicate)
                       and kernel.WaitForSingleObject(handle, 0) == 258)
            finally:
                if child.poll() is None: child.kill()
                child.wait(timeout=5); reader.join(timeout=5)
                stream.close(); errors.close(); child.stdin.close(); child.stdout.close()
                if handle:
                    exited = kernel.WaitForSingleObject(handle, 8000) == 0
                    kernel.CloseHandle(handle)
                    deadline = time.monotonic() + 8
                    while drive_present() and time.monotonic() < deadline: time.sleep(.1)
                    record('host-crash-removes-child-and-drive', exited and not drive_present(), rclone_pid=pid)
    finally:
        record('phone-data-and-identity-preserved', before_files == tls.adb('shell', 'find', '/sdcard/Music', '-type', 'f')
               and before_meta == chain.metadata_snapshot() and before_data == {name: chain.phone_info(name) for name in before_data}
               and hashlib.sha256(tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', tls.CERT)).hexdigest() == CA_SHA)
        record('cleanup', not drive_present() and not tls.adb('forward', '--list').strip() and
               'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode())
    print(json.dumps(dict(run=str(run), passed=True)), flush=True)


if __name__ == '__main__':
    main()
