"""Unavailable/unknown folder selection must stop sharing, not expose external storage."""
import http.client
import json
import pathlib
import re
import ssl
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from start_android import adb, assert_installed_apk, RUN, TASK_ID, TEST_API, AVD_NAME, FORWARD_PORT

assert TASK_ID in {'P0-005', 'P0-006', 'P0-007'}
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
assert_installed_apk()
rows = []

def set_selection(value):
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    node = next(n for n in prefs if n.attrib.get('name') == 'shared_folder')
    node.text = value
    adb('shell', 'run-as', 'com.phonebridge', 'sh', '-c', "'cat > shared_prefs/phonebridge_prefs.xml'", data=ET.tostring(prefs))

def try_start_without_server():
    adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity')
    time.sleep(1)
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    ui = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    button = next(n for n in ui.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle')
    x1, y1, x2, y2 = map(int, re.findall(r'\d+', button.attrib['bounds']))
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(7)  # Exceed the foreground-start timeout; a delayed crash must fail this check.
    conn = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=ssl._create_unverified_context(), timeout=2)
    try:
        conn.request('OPTIONS', '/')
        response = conn.getresponse()
    except (OSError, http.client.HTTPException):
        response = None
    finally:
        conn.close()
    assert response is None, 'An unavailable share must not start a server'
    # Independent service state: no server startup after the user's start attempt.
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    ui = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    status = next(n.attrib.get('text') for n in ui.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/tvStatus')
    assert status == 'Tap to start', f'Unexpected stopped UI state: {status}'
    return status

adb('shell', 'am', 'force-stop', 'com.phonebridge')
# Exact synthetic AVD Download directory; preserve its entire contents under a unique sibling name.
backup = '/sdcard/Download-' + TASK_ID.lower() + '-unavailable-' + str(time.time_ns())
original_inode = adb('shell', 'stat', '-c', '%i', '/sdcard/Download').strip()
adb('shell', 'mv', '/sdcard/Download', backup)
try:
    set_selection('downloads')
    status = try_start_without_server()
    rows.append({'name': 'missing-selected-folder', 'result': 'PASS', 'ui_status': status, 'server_unavailable': True})
finally:
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    # API 26 toybox has no -T. Stop the sole test app, reject an existing
    # destination, use no-clobber rename, and verify the preserved directory inode.
    adb('shell', 'test', '!', '-e', '/sdcard/Download')
    adb('shell', 'test', '!', '-L', '/sdcard/Download')
    flags = ['-n'] if TEST_API == 26 else ['-n', '-T']
    adb('shell', 'mv', *flags, backup, '/sdcard/Download')
    assert adb('shell', 'stat', '-c', '%i', '/sdcard/Download').strip() == original_inode
    adb('shell', 'test', '!', '-e', backup)
set_selection('p0-unknown-selection')
try:
    status = try_start_without_server()
    rows.append({'name': 'unknown-selection', 'result': 'PASS', 'ui_status': status, 'server_unavailable': True})
finally:
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    set_selection('downloads')
    subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name('start_android.py'))], check=True, timeout=90)
(RUN / 'storage-scope.json').write_text(json.dumps(rows, indent=2), encoding='utf-8')
print(json.dumps(rows), flush=True)
