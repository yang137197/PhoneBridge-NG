"""Sacrificial P0-004 APK fixtures only. Never deletes a shared root or personal files."""
import base64
import hashlib
import http.client
import json
import ssl
from start_android import adb, auth_password, assert_installed_apk, ROOT, TASK_ID

assert TASK_ID == 'P0-004'
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == b'PhoneBridgeP0'
assert_installed_apk()
run = ROOT / 'runs/P0-005'
auth = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
context = ssl._create_unverified_context()
old = hashlib.shake_256(b'p0-005-preserve-original').digest(32768)
outside = b'P0-005 sacrificial sibling sentinel\n'
for name, data, target in [('upload-preserve.bin', old, '/sdcard/Download'),
                           ('p0-outside.txt', outside, '/sdcard/Download-sibling')]:
    (run / name).write_bytes(data)
    adb('shell', 'mkdir', '-p', target)
    adb('push', str(run / name), target + '/' + name)
results = {'baseline': 'P0-004 repair APK; P0-005 not applied', 'probes': []}
for path in ['/../Download-sibling/p0-outside.txt', '/%252e%252e/Download-sibling/p0-outside.txt', '/../../outside-missing']:
    conn = http.client.HTTPSConnection('127.0.0.1', 18273, context=context, timeout=9)
    conn.request('GET', path, headers={'Authorization': auth})
    response = conn.getresponse()
    body = response.read()
    results['probes'].append({'path': path, 'status': response.status,
                             'sibling_sentinel_returned': body == outside,
                             'root_listing_returned': b'phone-origin.txt' in body})
    conn.close()
conn = http.client.HTTPSConnection('127.0.0.1', 18273, context=context, timeout=9)
conn.request('PUT', '/upload-preserve.bin', body=b'x' * 100,
             headers={'Authorization': auth, 'Content-Length': '65536'})
response = conn.getresponse()
response.read()
conn.close()
actual = adb('exec-out', 'cat', '/sdcard/Download/upload-preserve.bin')
results['incomplete_overwrite'] = {'status': response.status, 'declared_bytes': 65536,
    'sent_bytes': 100, 'old_bytes': len(old), 'actual_bytes': len(actual),
    'old_sha256': hashlib.sha256(old).hexdigest(), 'actual_sha256': hashlib.sha256(actual).hexdigest(),
    'old_file_preserved': actual == old}
(run / 'unsafe-baseline.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results, indent=2))
