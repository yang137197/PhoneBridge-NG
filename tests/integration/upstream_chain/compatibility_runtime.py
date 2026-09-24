"""P0-006 activity/service runtime checks, bound to the exact dedicated AVD and built APK."""
import base64
import hashlib
import http.client
import json
import re
import ssl
import time
import xml.etree.ElementTree as ET
from start_android import adb, assert_installed_apk, auth_password, RUN, TASK_ID, TEST_API, AVD_NAME, FORWARD_PORT

assert TASK_ID in {'P0-006', 'P0-007'}
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
apk_hash = assert_installed_apk()
context = ssl._create_unverified_context()  # Dedicated ADB loopback only.
results = []

def ui():
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    tree = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    status = next(n.attrib.get('text') for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/tvStatus')
    button = next(n for n in tree.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle')
    return status, list(map(int, re.findall(r'\d+', button.attrib['bounds'])))

def request(method, path='/', data=None, authenticated=False):
    conn = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=context, timeout=3)
    headers = {}
    if authenticated:
        headers['Authorization'] = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
    try:
        conn.request(method, path, data, headers)
        response = conn.getresponse()
        return response.status, response.read()
    finally:
        conn.close()

def record(name, **values):
    row = {'name': name, 'result': 'PASS', **values}
    results.append(row)
    (RUN / 'compatibility-runtime.json').write_text(json.dumps({'api': TEST_API, 'apk_sha256': apk_hash, 'checks': results}, indent=2), encoding='utf-8')
    print(json.dumps(row), flush=True)

for cycle in range(3):
    state, (x1, y1, x2, y2) = ui()
    assert state == 'Running'
    assert request('OPTIONS')[0] == 401
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(0.5)
    state, bounds = ui()
    assert state == 'Tap to start'
    try:
        request('OPTIONS')
    except (OSError, http.client.HTTPException):
        pass
    else:
        raise AssertionError('Server still listening after stop')
    record('stop-' + str(cycle + 1), ui_status=state, server_unavailable=True)
    x1, y1, x2, y2 = bounds
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(0.5)
    assert ui()[0] == 'Running'
    assert request('OPTIONS')[0] == 401
    record('start-' + str(cycle + 1), https=True, unauthenticated_status=401)
    adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
    time.sleep(0.25)
    assert request('OPTIONS')[0] == 401
    adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity')
    time.sleep(0.25)
    assert ui()[0] == 'Running'
    record('home-resume-' + str(cycle + 1), sharing_continues=True)

data = hashlib.shake_256(b'P0-006 compatibility smoke').digest(1_000_000)
name = TASK_ID.lower() + '-api' + str(TEST_API) + '-' + str(time.time_ns()) + '.bin'
assert request('PUT', '/' + name, data, True)[0] == 201
status, actual = request('GET', '/' + name, authenticated=True)
assert status == 200 and actual == data
phone_hash = adb('shell', 'sha256sum', '/sdcard/Download/' + name).decode().split()[0]
assert phone_hash == hashlib.sha256(data).hexdigest()
record('small-transfer-smoke', bytes=len(data), sha256=phone_hash, note='Not a large-file acceptance test')
log = adb('logcat', '-d', '-s', 'AndroidRuntime:E').decode(errors='replace')
(RUN / 'crash-log.txt').write_text(log, encoding='utf-8')
assert 'FATAL EXCEPTION' not in log
record('no-android-runtime-crash')
