"""P1-006 native tests. Only dedicated API 26/36 emulators and the isolated test APK."""
import argparse, hashlib, json, os, re, subprocess, time, uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT / '.audit/tools/android-sdk/platform-tools/adb.exe'
APK = ROOT / 'android/credentials-store/build/outputs/apk/androidTest/debug/phonebridge-credentials-store-debug-androidTest.apk'
PACKAGE = 'org.phonebridge.credentials.test'
RUNNER = PACKAGE + '/org.phonebridge.credentials.StorageTestRunner'
TARGETS = {26: ('emulator-5558', 'PhoneBridgeP0Api26'), 36: ('emulator-5560', 'PhoneBridgeP0Api36')}
CLEANUP_SERIAL = None

def main():
    global CLEANUP_SERIAL
    parser = argparse.ArgumentParser(); parser.add_argument('--api', type=int, choices=TARGETS, required=True)
    parser.add_argument('--label', default='first'); args = parser.parse_args()
    assert re.fullmatch('[a-z0-9-]{1,32}', args.label)
    serial, avd = TARGETS[args.api]
    run = ROOT / '.audit/p1-006' / ('api' + str(args.api) + '-' + args.label); run.mkdir(parents=True, exist_ok=False)
    def adb(*items, timeout=45, allowed=(0,)):
        result = subprocess.run([str(ADB), '-s', serial, *items], capture_output=True, timeout=timeout)
        if result.returncode not in allowed: raise RuntimeError('ADB failed with exit ' + str(result.returncode))
        return result.stdout.decode('utf-8', errors='replace')
    assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == '1'
    assert adb('emu', 'avd', 'name').splitlines()[0] == avd
    assert adb('shell', 'getprop', 'ro.build.version.sdk').strip() == str(args.api)
    assert adb('shell', 'getprop', 'sys.boot_completed').strip() == '1'
    CLEANUP_SERIAL = serial
    apk_hash = hashlib.sha256(APK.read_bytes()).hexdigest()
    assert 'Success' in adb('install', '-r', str(APK), timeout=90)
    installed = adb('shell', 'pm', 'path', PACKAGE).strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
    assert adb('shell', 'sha256sum', installed).split()[0] == apk_hash
    output = adb('shell', 'am', 'instrument', '-w', '-r', '-e', 'class', 'org.phonebridge.credentials.StoreTests', RUNNER, timeout=300)
    (run / 'instrumentation.log').write_text(output, encoding='utf-8')
    rows = []; current = None
    for line in output.splitlines():
        if line.startswith('INSTRUMENTATION_STATUS: test='): current = line.split('=', 1)[1]
        if line.startswith('INSTRUMENTATION_STATUS_CODE: '):
            code = int(line.split(': ', 1)[1])
            if code not in (1, 2): rows.append(dict(test=current, passed=code == 0, code=code))
    result = dict(api=args.api, emulator=avd, apk_sha256=apk_hash, tests=rows, process_cases=[], real_phone_access=False)
    coverage = [line.split('=', 1)[1] for line in output.splitlines() if line.startswith('INSTRUMENTATION_STATUS: hardlink_coverage=')]
    result['hardlink_coverage'] = coverage
    def save(): (run / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    save(); print(json.dumps(dict(api=args.api, unit_passed=sum(r['passed'] for r in rows), unit_total=len(rows))), flush=True)
    expected = len(re.findall(r'@Test fun ', (ROOT / 'android/credentials-store/src/androidTest/java/org/phonebridge/credentials/StoreTests.kt').read_text(encoding='utf-8')))
    assert len(rows) == expected and all(r['passed'] for r in rows) and f'OK ({expected} tests)' in output
    def fixture(action, name, label, stage=None):
        adb('shell', 'am', 'force-stop', PACKAGE)
        extra = ['-e', 'fixture', action, '-e', 'store', name]
        if stage: extra += ['-e', 'stage', stage]
        text = adb('shell', 'am', 'instrument', '-w', '-r', *extra, RUNNER, timeout=90)
        (run / (label + '.log')).write_text(text, encoding='utf-8')
        return text
    def field(text, name):
        match = re.search(r'^INSTRUMENTATION_RESULT: ' + name + r'=(.*)$', text, re.M)
        assert match, 'missing fixed fixture field'; return match.group(1).strip()
    for scenario, stage in [('normal-restart', None), ('before-rename', 'FLUSHED'), ('after-rename', 'RENAMED')]:
        name = 'restart-' + uuid.uuid4().hex
        first = fixture('prepare', name, scenario + '-prepare'); assert field(first, 'fixture_state') == 'ACTIVE'
        first_pid = int(field(first, 'fixture_pid'))
        if stage:
            died = fixture('crash', name, scenario + '-crash', stage)
            assert 'INSTRUMENTATION_STATUS: checkpoint=' + stage in died
            assert 'fixture_error' not in died
            # Kill was inside the checkpoint; the complete record is checked in a distinct new process below.
            remaining = adb('shell', 'pidof', PACKAGE, allowed=(0, 1)).strip(); assert not remaining
        else:
            changed = fixture('revoke', name, scenario + '-revoke'); assert field(changed, 'fixture_state') == 'REVOKED'
        restored = fixture('read', name, scenario + '-read')
        expected_state = 'ACTIVE' if stage == 'FLUSHED' else 'REVOKED'
        assert field(restored, 'fixture_state') == expected_state
        restored_pid = int(field(restored, 'fixture_pid')); assert first_pid != restored_pid
        result['process_cases'].append(dict(scenario=scenario, passed=True, state=expected_state, distinct_processes=True,
            security_level=field(restored, 'security_level'), inside_secure_hardware=field(restored, 'inside_secure_hardware')))
        save(); print(json.dumps(dict(api=args.api, scenario=scenario, passed=True)), flush=True)
    adb('shell', 'am', 'force-stop', PACKAGE)
    assert not adb('shell', 'pidof', PACKAGE, allowed=(0, 1)).strip()
    result['test_app_stopped'] = True; save()
    print(json.dumps(dict(api=args.api, tests=len(rows), process_cases=len(result['process_cases']), passed=True)), flush=True)

if __name__ == '__main__':
    try: main()
    finally:
        if CLEANUP_SERIAL:
            cleanup = subprocess.run([str(ADB), '-s', CLEANUP_SERIAL, 'shell', 'am', 'force-stop', PACKAGE], capture_output=True, timeout=45)
            assert cleanup.returncode == 0, 'test app cleanup failed'

