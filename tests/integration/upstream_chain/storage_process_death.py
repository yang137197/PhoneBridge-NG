"""Kill only the test app on the dedicated AVD during a sacrificial partial upload."""
import base64
import hashlib
import http.client
import json
import pathlib
import ssl
import subprocess
import sys
import time
from start_android import adb, auth_password, assert_installed_apk, RUN, TASK_ID

assert TASK_ID == 'P0-005'
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == b'PhoneBridgeP0'
assert_installed_apk()
context = ssl._create_unverified_context()
folder = 'p0-005-death-' + str(time.time_ns())
target = '/sdcard/Download/' + folder
old = hashlib.shake_256(b'P0-005-process-death-original').digest(32768)
local = RUN / 'process-death-old.bin'
local.write_bytes(old)
adb('shell', 'mkdir', target)
adb('push', str(local), target + '/keep.bin')
auth = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
conn = http.client.HTTPSConnection('127.0.0.1', 18273, context=context, timeout=9)
conn.request('PUT', '/' + folder + '/keep.bin', b'x' * 100,
             {'Authorization': auth, 'Content-Length': '65536'})
for _ in range(30):
    names = adb('shell', 'ls', '-a', target).decode().splitlines()
    temps = [name for name in names if name.startswith('.phonebridge-upload-')]
    if temps:
        break
    time.sleep(0.05)
assert len(temps) == 1, 'Upload staging did not start'
assert adb('exec-out', 'cat', target + '/keep.bin') == old
adb('shell', 'am', 'force-stop', 'com.phonebridge')
conn.close()
after_death = adb('exec-out', 'cat', target + '/keep.bin')
assert after_death == old
retained_before = adb('exec-out', 'cat', target + '/' + temps[0])
assert retained_before == b'x' * 100
# Same dedicated emulator/APK, helper reinstalls without clearing data; test credentials stay in memory.
subprocess.run([sys.executable, str(pathlib.Path(__file__).with_name('start_android.py'))], check=True, timeout=90)
auth = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
conn = http.client.HTTPSConnection('127.0.0.1', 18273, context=context, timeout=9)
conn.request('GET', '/' + folder + '/keep.bin', headers={'Authorization': auth})
response = conn.getresponse()
assert response.status == 200 and response.read() == old
conn.close()
retained_after = adb('exec-out', 'cat', target + '/' + temps[0])
assert retained_after == retained_before
conn = http.client.HTTPSConnection('127.0.0.1', 18273, context=context, timeout=9)
conn.request('PROPFIND', '/' + folder, headers={'Authorization': auth, 'Depth': '1'})
response = conn.getresponse()
assert response.status == 207 and b'.phonebridge-upload-' not in response.read()
conn.close()
result = {'result': 'PASS', 'old_bytes': len(old), 'old_sha256': hashlib.sha256(old).hexdigest(),
          'after_death_sha256': hashlib.sha256(after_death).hexdigest(), 'after_restart_matches': True,
          'retained_temporary_bytes': len(retained_after), 'temporary_not_published': True,
          'scope': 'Android app force-stop before complete body, not physical device power loss'}
(RUN / 'process-death.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result), flush=True)
