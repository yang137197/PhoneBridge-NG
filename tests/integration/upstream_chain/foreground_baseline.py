"""P0-007: reproduce lifecycle defects only in the dedicated API 36 AVD/P0-006 APK."""
import http.client
import json
import re
import ssl
import time
import xml.etree.ElementTree as ET
from start_android import adb, assert_installed_apk, RUN, TASK_ID, TEST_API, VARIANT, AVD_NAME, FORWARD_PORT

assert (TASK_ID, TEST_API, VARIANT) == ('P0-007', 36, 'baseline')
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
apk_hash = assert_installed_apk()
rows = []


def available():
    conn = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=ssl._create_unverified_context(), timeout=2)
    try:
        conn.request('OPTIONS', '/')
        response = conn.getresponse()
        response.read()
        return response.status == 401
    except (OSError, http.client.HTTPException):
        return False
    finally:
        conn.close()


def app_pid():
    # ps succeeds even if the target app is absent; do not swallow adb failures.
    lines = adb('shell', 'ps', '-A', '-o', 'PID,NAME').decode().splitlines()
    pids = [line.split()[0] for line in lines if line.split()[-1] == 'com.phonebridge']
    assert len(pids) <= 1
    return pids[0] if pids else None


def start_ui():
    adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity')
    time.sleep(2)
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    tree = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    button = next(n for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle')
    bounds = list(map(int, re.findall(r'\d+', button.attrib['bounds'])))
    adb('shell', 'input', 'tap', str((bounds[0]+bounds[2])//2), str((bounds[1]+bounds[3])//2))
    deadline = time.monotonic() + 15
    while not available() and time.monotonic() < deadline:
        time.sleep(0.25)
    assert available()


def save(name, **data):
    rows.append({'name': name, **data})
    (RUN / 'foreground-baseline.json').write_text(json.dumps({'apk_sha256': apk_hash, 'checks': rows}, indent=2), encoding='utf-8')
    print(json.dumps(rows[-1]), flush=True)


assert available()
adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
old_pid = app_pid()
assert old_pid and old_pid.isdecimal()
adb('shell', 'run-as', 'com.phonebridge', 'kill', '-9', old_pid)
deadline = time.monotonic() + 90
new_pid = None
while time.monotonic() < deadline:
    new_pid = app_pid()
    if new_pid and new_pid != old_pid:
        break
    time.sleep(1)
assert new_pid and new_pid != old_pid, 'No actual process restart observed; do not infer null-intent recovery'
time.sleep(8)
restored = available()
assert not restored, 'Baseline unexpectedly recovered; investigate before changing code'
save('sticky-process-restart-loses-sharing', expected_defect_reproduced=True, process_restarted=True, https_restored=restored)
(RUN / 'sticky-service.log').write_text(adb('logcat', '-d', '-s', 'PhoneBridgeService:I', 'AndroidRuntime:E').decode(errors='replace'), encoding='utf-8')

original = adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').decode().strip()
assert original == 'null' or original.isdecimal()
try:
    adb('shell', 'am', 'compat', 'enable', 'FGS_INTRODUCE_TIME_LIMITS', 'com.phonebridge')
    adb('shell', 'device_config', 'put', 'activity_manager', 'data_sync_fgs_timeout_duration', '5000')
    assert adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').strip() == b'5000'
    start_ui()
    adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
    deadline = time.monotonic() + 45
    crash = ''
    while time.monotonic() < deadline:
        crash = adb('logcat', '-d', '-s', 'AndroidRuntime:E').decode(errors='replace')
        if 'did not stop within its timeout' in crash:
            break
        time.sleep(1)
    (RUN / 'timeout-crash.log').write_text(crash, encoding='utf-8')
    assert 'did not stop within its timeout' in crash and 'dataSync' in crash
    save('data-sync-timeout-crashes', expected_defect_reproduced=True, official_compat_enabled=True, shortened_limit_ms=5000)
finally:
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    adb('shell', 'am', 'compat', 'reset', 'FGS_INTRODUCE_TIME_LIMITS', 'com.phonebridge')
    if original == 'null':
        adb('shell', 'device_config', 'delete', 'activity_manager', 'data_sync_fgs_timeout_duration')
    else:
        adb('shell', 'device_config', 'put', 'activity_manager', 'data_sync_fgs_timeout_duration', original)
    assert adb('shell', 'device_config', 'get', 'activity_manager', 'data_sync_fgs_timeout_duration').decode().strip() == original
    (RUN / 'baseline-settings-restored.json').write_text(json.dumps({'timeout_original': original, 'timeout_restored': True, 'compat_reset': True, 'app_force_stopped': True}, indent=2), encoding='utf-8')
