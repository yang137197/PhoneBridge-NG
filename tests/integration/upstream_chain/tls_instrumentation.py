"""P0-009: isolated Android Keystore and real TLS socket tests, no shared-storage access."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT / '.audit/tools/android-sdk/platform-tools/adb.exe'
BUILD = ROOT / '.audit/p0-009-worktree/android/app/build/outputs/apk'
SERIAL = os.environ['PHONEBRIDGE_TEST_SERIAL']
TARGETS = {'emulator-5558': (26, 'PhoneBridgeP0Api26', 'api26'),
           'emulator-5560': (36, 'PhoneBridgeP0Api36', 'api36'),
           '61c9a964': (36, None, 'phone36')}

def adb(*args, timeout=45):
    p = subprocess.run([str(ADB), '-s', SERIAL, *args], capture_output=True, timeout=timeout)
    if p.returncode:
        raise RuntimeError(f'ADB operation failed: {p.returncode}')
    return p.stdout.decode(errors='replace')

def main():
    api, avd, name = TARGETS[SERIAL]
    assert adb('shell', 'getprop', 'ro.build.version.sdk').strip() == str(api)
    if avd:
        assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == '1'
        assert adb('emu', 'avd', 'name').splitlines()[0] == avd
    else:
        assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() != '1'
        assert adb('shell', 'getprop', 'ro.product.device').strip() == 'alioth'
    run = ROOT / '.audit/runs/P0-009' / name
    run.mkdir(parents=True, exist_ok=True)
    hashes = {}
    for package, path in [('com.phonebridge', BUILD / 'debug/app-debug.apk'),
                          ('com.phonebridge.test', BUILD / 'androidTest/debug/app-debug-androidTest.apk')]:
        hashes[package] = hashlib.sha256(path.read_bytes()).hexdigest()
        assert 'Success' in adb('install', '-r', str(path), timeout=90)
        installed = adb('shell', 'pm', 'path', package).strip().removeprefix('package:')
        assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
        assert adb('shell', 'sha256sum', installed).split()[0] == hashes[package]
    output = adb('shell', 'am', 'instrument', '-w', '-r', '-e', 'class',
                 'com.phonebridge.server.TlsIdentityTest',
                 'com.phonebridge.test/androidx.test.runner.AndroidJUnitRunner', timeout=180)
    (run / 'tls-instrumentation.log').write_text(output, encoding='utf-8')
    rows = []
    test = None
    for line in output.splitlines():
        if line.startswith('INSTRUMENTATION_STATUS: test='):
            test = line.split('=', 1)[1]
        if line.startswith('INSTRUMENTATION_STATUS_CODE: '):
            code = int(line.split(': ', 1)[1])
            if code != 1:
                rows.append({'test': test, 'result': 'PASS' if code == 0 else 'FAIL', 'code': code})
    result = {'api': api, 'target': name, 'apk_hashes': hashes, 'tests': rows,
              'shared_storage_access': False, 'default_identity_modified': False}
    (run / 'tls-instrumentation.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2), flush=True)
    assert len(rows) == 8 and all(r['result'] == 'PASS' for r in rows)
    assert 'FAILURES!!!' not in output and 'INSTRUMENTATION_FAILED' not in output and 'OK (8 tests)' in output

if __name__ == '__main__':
    main()
