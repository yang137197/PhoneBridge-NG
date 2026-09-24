"""P1-001: explicitly authorized spare-phone discovery, no file transfer or pairing.

Requires PHONEBRIDGE_TEST_SERIAL / LOCAL_IP / PHONE_IP as in the P0 harness.
Uses the existing fixed P0-009 APK and test host; it never installs an APK.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tests/integration/upstream_chain'))
import real_device_chain as chain
import tls_runtime as tls


def main():
    run = ROOT / '.audit/runs/P1-001' / ('real-' + uuid.uuid4().hex[:12])
    run.mkdir(parents=True)
    chain.RUN = run
    chain.RUN_ID = run.name
    chain.ROWS = []
    tls.RUN = run / 'host'
    tls.RUN.mkdir()
    tls.ROWS = []
    assert tls.SERIAL == '61c9a964'
    assert tls.adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
    assert tls.adb('shell', 'getprop', 'ro.build.version.sdk').strip() == b'36'
    assert f'inet {chain.PHONE_IP}/24' in tls.adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
    with socket.socket() as check:
        check.bind((chain.LOCAL_IP, 0))
    assert not tls.adb('forward', '--list').strip()
    assert 'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
    for package, expected in [('com.phonebridge', chain.APK_SHA), ('com.phonebridge.test',
            'e6c62d4fd05367feecb61f8640cf5263bc14c8c4d3bef648b1a3c34178587a5d')]:
        path = tls.adb('shell', 'pm', 'path', package).decode().strip().removeprefix('package:')
        assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', path)
        assert tls.adb('shell', 'sha256sum', path).decode().split()[0] == expected
    fingerprint = hashlib.sha256(tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', tls.CERT)).hexdigest()
    assert fingerprint == '65b52a178912e36cc73dc820f4029e85efa75a3427c2a341902326a8d2480903'
    files_before = tls.adb('shell', 'find', '/sdcard/Music', '-type', 'f')
    metadata_before = chain.metadata_snapshot()
    dll = ROOT / 'windows/src/PhoneBridge.Discovery.Diagnostics/bin/Release/net10.0-windows10.0.19041.0/PhoneBridge.Discovery.Diagnostics.dll'
    dotnet = Path(os.environ.get('PHONEBRIDGE_DOTNET', ROOT / '.audit/tools/dotnet-10.0.401/dotnet.exe'))
    events, checks = [], []
    lock = threading.Lock()

    def record(name, passed, **details):
        checks.append({'test': name, 'passed': passed, **details})
        (run / 'result.json').write_text(json.dumps({'run': run.name, 'checks': checks}, indent=2), encoding='utf-8')
        print(json.dumps(checks[-1]), flush=True)

    with (run / 'stderr.log').open('wb') as err, (run / 'events.jsonl').open('w', encoding='utf-8') as log:
        process = subprocess.Popen([str(dotnet), str(dll), '--json', '--seconds', '95'],
            stdout=subprocess.PIPE, stderr=err, encoding='utf-8')

        def read():
            for line in process.stdout:
                log.write(line)
                log.flush()
                with lock:
                    events.append(json.loads(line))

        reader = threading.Thread(target=read)
        reader.start()

        def until(predicate, seconds):
            deadline = time.monotonic() + seconds
            while time.monotonic() < deadline:
                with lock:
                    if predicate(events):
                        return True
                if process.poll() is not None:
                    return False
                time.sleep(.1)
            return False

        def phone_added(event):
            change = event['change']
            return change['Kind'] == 'Added' and any(e['Address'] == chain.PHONE_IP
                for e in (change.get('Candidate') or {}).get('Endpoints', []))

        try:
            assert until(lambda es: any(e['change']['Reason'] == 'watching' for e in es), 10), 'watcher-start-failed'
            started = time.monotonic()
            with chain.phone_service_host('windows-discovery'):
                found = until(lambda es: any(phone_added(e) for e in es), 30)
                record('real-phone-appearance', found, seconds_from_host_launch=round(time.monotonic() - started, 3))
                assert found, 'phone-not-discovered'
                with lock:
                    candidate_id = next(e['change']['Id'] for e in events if phone_added(e))
                time.sleep(32)  # three 12-second scan windows with the phone still sharing
                with lock:
                    stable = sum(phone_added(e) for e in events) == 1 and not any(
                        e['change']['Id'] == candidate_id and e['change']['Kind'] == 'Removed' for e in events)
                record('live-candidate-no-flicker', stable, observed_seconds=32)
            stopped = time.monotonic()
            removed = until(lambda es: any(e['change']['Id'] == candidate_id and e['change']['Kind'] == 'Removed'
                and e['change']['Reason'] in ('service-removed', 'not-rediscovered') for e in es), 40)
            record('real-phone-removal', removed, seconds_after_host_stop=round(time.monotonic() - stopped, 3))
        finally:
            # The diagnostic owns a bounded lifetime and awaits native watcher shutdown.
            process.wait(timeout=100)
            reader.join(timeout=5)
            record('diagnostic-exit', process.returncode == 0, exit_code=process.returncode)
            record('phone-preserved', fingerprint == hashlib.sha256(tls.adb('exec-out', 'run-as',
                'com.phonebridge', 'cat', tls.CERT)).hexdigest() and
                files_before == tls.adb('shell', 'find', '/sdcard/Music', '-type', 'f') and
                metadata_before == chain.metadata_snapshot())
            record('service-stopped', 'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity',
                'services', 'com.phonebridge').decode() and not tls.adb('forward', '--list').strip())
    assert all(c['passed'] for c in checks)
    print(json.dumps({'run': str(run), 'passed': True}), flush=True)


if __name__ == '__main__':
    main()
