"""P0-007 real service/OS lifecycle checks; only the dedicated Android 16 AVD."""
import hashlib
import http.client
import json
import os
import re
import ssl
import subprocess
import time
import xml.etree.ElementTree as ET
from start_android import adb, assert_installed_apk, ADB, SERIAL, RUN, TASK_ID, TEST_API, VARIANT, AVD_NAME, FORWARD_PORT

assert (TASK_ID, TEST_API, VARIANT) == ('P0-007', 36, 'repair')
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
apk_hash = assert_installed_apk()
rows = []


def probe():
    conn = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=ssl._create_unverified_context(), timeout=2)
    try:
        conn.connect()
        fingerprint = hashlib.sha256(conn.sock.getpeercert(binary_form=True)).hexdigest()
        conn.request('OPTIONS', '/')
        response = conn.getresponse()
        response.read()
        return fingerprint if response.status == 401 else None
    except (OSError, http.client.HTTPException):
        return None
    finally:
        conn.close()


def wait_for(condition, seconds=30):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        if condition(): return
        time.sleep(0.5)
    raise AssertionError('Runtime condition timed out; inspect the existing device, do not restart it')


def record(name, **data):
    rows.append({'name': name, 'result': 'PASS', **data})
    (RUN / 'foreground-runtime.json').write_text(json.dumps({'apk_sha256': apk_hash, 'api': TEST_API, 'checks': rows}, indent=2), encoding='utf-8')
    print(json.dumps(rows[-1]), flush=True)


def services():
    return adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()


def foreground_type():
    dump = services()
    return bool(re.search(r'isForeground=true.*types=0x0*10\b', dump))


def app_pid():
    lines = adb('shell', 'ps', '-A', '-o', 'PID,NAME').decode().splitlines()
    found = [line.split()[0] for line in lines if line.split()[-1] == 'com.phonebridge']
    assert len(found) <= 1
    return found[0] if found else None


def ui():
    launch = adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity').decode(errors='replace')
    assert 'Error' not in launch
    time.sleep(1)
    # A dump command can report failure without a nonzero shell exit status.
    # A unique file prevents reusing an earlier Activity's stale coordinates.
    path = '/sdcard/p0-007-window-' + str(time.time_ns()) + '.xml'
    command = subprocess.run([str(ADB), '-s', SERIAL, 'shell', 'uiautomator', 'dump', path], capture_output=True, timeout=45)
    result = command.stdout.decode(errors='replace')
    (RUN / 'last-ui-command.json').write_text(json.dumps({'launch': launch, 'dump': result, 'dump_stderr': command.stderr.decode(errors='replace'), 'exit_code': command.returncode, 'path': path}, indent=2), encoding='utf-8')
    assert command.returncode == 0 and 'dumped to:' in result, 'Fresh UI dump failed; do not tap stale coordinates'
    return ET.fromstring(adb('exec-out', 'cat', path))


def click(node):
    x1,y1,x2,y2 = map(int, re.findall(r'\d+', node.attrib['bounds']))
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))


def set_auto_start(enabled):
    tree = ui()
    toggle = next(n for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/switchAutoStart')
    if (toggle.attrib['checked'] == 'true') != enabled:
        click(toggle)
        time.sleep(0.5)
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    stored = next((n.attrib.get('value') for n in prefs if n.attrib.get('name') == 'auto_start_on_boot'), 'false')
    assert (stored == 'true') == enabled


def reboot(expected_stopped):
    # PackageManager persists stopped state asynchronously. Verify the state
    # before reboot and let its Android 16 ten-second write delay settle.
    time.sleep(15)
    state = adb('shell', 'dumpsys', 'package', 'com.phonebridge').decode()
    line = next(line for line in state.splitlines() if 'User 0:' in line and 'stopped=' in line)
    assert ('stopped=true' in line) == expected_stopped
    (RUN / ('package-before-reboot-' + str(len(rows)) + '.json')).write_text(json.dumps({'state': line.strip(), 'settle_seconds': 15}), encoding='utf-8')
    crash = adb('logcat', '-d', '-s', 'AndroidRuntime:E').decode(errors='replace')
    (RUN / ('crash-before-reboot-' + str(len(rows)) + '.txt')).write_text(crash, encoding='utf-8')
    assert 'FATAL EXCEPTION' not in crash
    before = adb('shell', 'cat', '/proc/sys/kernel/random/boot_id').strip()
    adb('reboot')
    def booted():
        try:
            return adb('shell', 'getprop', 'sys.boot_completed').strip() == b'1' and adb('shell', 'cat', '/proc/sys/kernel/random/boot_id').strip() != before
        except RuntimeError:
            return False  # Re-observe this reboot; never launch another emulator.
    wait_for(booted, 180)
    assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
    assert_installed_apk()
    adb('forward', f'tcp:{FORWARD_PORT}', 'tcp:8273')


resume_boot = os.environ.get("PHONEBRIDGE_FOREGROUND_RESUME_BOOT") == "1"
initial_cert = probe()
assert resume_boot or (initial_cert and foreground_type()), 'Expected an actual connectedDevice foreground service'
initial_prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
assert next((n.attrib.get('value') for n in initial_prefs if n.attrib.get('name') == 'auto_start_on_boot'), 'false') == 'false'
if resume_boot:
    previous = json.loads((RUN / 'foreground-runtime.json').read_text())
    assert previous['apk_sha256'] == apk_hash and previous['api'] == TEST_API
    expected = ['connected-device-service', 'correct-type-survives-data-sync-test-limit', 'system-process-recreation-restores-sharing', 'explicit-stop-remains-stopped']
    assert [r['name'] for r in previous['checks']][:4] == expected
    assert len(previous['checks']) in (4,5)
    assert all(r['result'] == 'PASS' for r in previous['checks'])
    rows = previous['checks'][:4]
    # The failed simulated broadcast was followed by finally's force-stop.
    assert probe() is None and 'ServiceRecord{' not in services()
else:
    record('connected-device-service', type_hex='0x10', https=True)
original_timeout = adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').decode().strip()
assert original_timeout == 'null' or original_timeout.isdecimal()
try:
    if not resume_boot:
        adb('shell', 'device_config', 'put', 'activity_manager', 'data_sync_fgs_timeout_duration', '5000')
        assert adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').strip() == b'5000'
        adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
        time.sleep(20)
        assert probe() == initial_cert and foreground_type()
        record('correct-type-survives-data-sync-test-limit', shortened_data_sync_limit_ms=5000, observed_background_seconds=20)

        old_pid = app_pid()
        assert old_pid and old_pid.isdecimal()
        start = time.monotonic()
        adb('shell', 'run-as', 'com.phonebridge', 'kill', '-9', old_pid)
        wait_for(lambda: app_pid() not in (None, old_pid) and probe() == initial_cert, 90)
        assert foreground_type()
        record('system-process-recreation-restores-sharing', new_process=True, same_certificate=True, observed_seconds=round(time.monotonic()-start,2))

        tree = ui()
        assert next(n.attrib.get('text') for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/tvStatus') == 'Running'
        click(next(n for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle'))
        wait_for(lambda: probe() is None and 'ServiceRecord{' not in services())
        adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
        time.sleep(10)
        assert probe() is None and 'ServiceRecord{' not in services()
        record('explicit-stop-remains-stopped', observed_seconds=10)

    set_auto_start(False)
    adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
    reboot(expected_stopped=False)
    time.sleep(10)
    assert probe() is None and 'ServiceRecord{' not in services()
    record('auto-start-disabled', method='Actual reboot with new kernel boot ID')

    set_auto_start(True)
    adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
    reboot(expected_stopped=False)
    if resume_boot:
        wait_for(lambda: probe() is not None, 60)
        initial_cert = probe()
    else:
        wait_for(lambda: probe() == initial_cert, 60)
    assert foreground_type()
    record('actual-reboot-with-auto-start-enabled', new_kernel_boot_id=True, https=True, same_certificate=None if resume_boot else True)

    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    assert probe() is None
    reboot(expected_stopped=True)
    time.sleep(10)
    assert probe() is None and 'ServiceRecord{' not in services()
    package = adb('shell', 'dumpsys', 'package', 'com.phonebridge').decode()
    assert re.search(r'User 0:.*stopped=true', package)
    record('user-force-stop-respected-across-reboot', auto_start_preference_remained_enabled=True)
    assert probe() is None
    crash = adb('logcat', '-d', '-s', 'AndroidRuntime:E').decode(errors='replace')
    (RUN / 'foreground-crash-log.txt').write_text(crash, encoding='utf-8')
    assert 'FATAL EXCEPTION' not in crash
finally:
    diagnostic_errors = []
    for label, arguments in {
        'boot-service-log.txt': ('logcat', '-d', '-s', 'BootReceiver:I', 'PhoneBridgeService:I', 'AndroidRuntime:E'),
        'boot-package.txt': ('shell', 'dumpsys', 'package', 'com.phonebridge'),
        'boot-broadcasts.txt': ('shell', 'dumpsys', 'activity', 'broadcasts'),
    }.items():
        try:
            (RUN / label).write_bytes(adb(*arguments))
        except (RuntimeError, OSError, subprocess.TimeoutExpired) as error:
            # Preserve restoration even if a supplementary diagnostic fails.
            diagnostic_errors.append({'file': label, 'error_type': type(error).__name__})
    # Restore the one official test setting; no production protection is changed.
    if original_timeout == 'null':
        adb('shell', 'device_config', 'delete', 'activity_manager', 'data_sync_fgs_timeout_duration')
    else:
        adb('shell', 'device_config', 'put', 'activity_manager', 'data_sync_fgs_timeout_duration', original_timeout)
    assert adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').decode().strip() == original_timeout
    # start_android creates this test preference disabled; restore it even if a
    # later assertion fails. Preserve all other preferences and their secrets.
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    auto = next((n for n in prefs if n.attrib.get('name') == 'auto_start_on_boot'), None)
    if auto is not None:
        auto.attrib['value'] = 'false'
        adb('shell', 'run-as', 'com.phonebridge', 'sh', '-c', "'cat > shared_prefs/phonebridge_prefs.xml'", data=ET.tostring(prefs))
    restored = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    assert next((n.attrib.get('value') for n in restored if n.attrib.get('name') == 'auto_start_on_boot'), 'false') == 'false'
    (RUN / 'foreground-settings-restored.json').write_text(json.dumps({'original_data_sync_timeout': original_timeout, 'timeout_restored': True, 'auto_start_disabled': True, 'app_force_stopped': True, 'diagnostic_errors': diagnostic_errors}, indent=2), encoding='utf-8')
