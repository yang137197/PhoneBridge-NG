"""P0-005 real HTTPS tests; sacrificial fixture names on PhoneBridgeP0 only."""
import base64
import concurrent.futures
import hashlib
import http.client
import json
import socket
import ssl
import time
import urllib.parse
from start_android import adb, auth_password, assert_installed_apk, RUN, TASK_ID

assert TASK_ID == 'P0-005'
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == b'PhoneBridgeP0'
assert_installed_apk()
AUTH = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
CONTEXT = ssl._create_unverified_context()  # Isolated ADB loopback, not product TLS acceptance.
PREFIX = '/p0-005-' + str(time.time_ns())
OLD = hashlib.shake_256(b'p0-005-preserve-original').digest(32768)
NEW = hashlib.shake_256(b'p0-005-new-complete-body').digest(65536)
results = []

def connection():
    return http.client.HTTPSConnection('127.0.0.1', 18273, context=CONTEXT, timeout=12)

def request(method, path, body=None, headers=None):
    conn = connection()
    try:
        conn.request(method, path, body, {'Authorization': AUTH, **(headers or {})})
        res = conn.getresponse()
        data = res.read()
        return res.status, data, res.will_close
    finally:
        conn.close()

def expect(status, method, path, body=None, headers=None):
    actual, data, closed = request(method, path, body, headers)
    assert actual == status, f'{method} {path}: expected {status}, got {actual}'
    return data, closed

def run(name, action):
    started = time.monotonic()
    try:
        action()
        row = {'name': name, 'result': 'PASS'}
    except Exception as error:
        row = {'name': name, 'result': 'FAIL', 'kind': type(error).__name__, 'detail': str(error)[:300]}
    row['seconds'] = round(time.monotonic() - started, 3)
    results.append(row)
    (RUN / 'storage-regression.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(row), flush=True)

def file_equals(path, data):
    actual, _ = expect(200, 'GET', path)
    assert actual == data, f'File hash mismatch {hashlib.sha256(actual).hexdigest()}'

expect(201, 'MKCOL', PREFIX)
expect(201, 'PUT', PREFIX + '/preserve.bin', OLD)
outside_before = adb('exec-out', 'cat', '/sdcard/Download-sibling/p0-outside.txt')

for method in ['GET', 'HEAD', 'PROPFIND', 'PUT', 'DELETE', 'MKCOL', 'MOVE', 'COPY']:
    for label, path in [('parent', '/../Download-sibling/p0-outside.txt'),
                        ('encoded-parent', '/%2e%2e/Download-sibling/p0-outside.txt'),
                        ('root-fallback', '/../../outside-missing'), ('backslash', '/a%5cb')]:
        run(f'{method}-{label}-rejected', lambda m=method, p=path:
            expect(403, m, p, b'x' if m == 'PUT' else None,
                   {'Destination': PREFIX + '/unused'} if m in ['MOVE', 'COPY'] else None))
for method in ['PUT', 'DELETE', 'MKCOL', 'MOVE', 'COPY']:
    run(f'{method}-root-rejected', lambda m=method: expect(403, m, '/', b'x' if m == 'PUT' else None,
        {'Destination': PREFIX + '/unused'} if m in ['MOVE', 'COPY'] else None))
run('double-encoding-is-literal-not-traversal', lambda: expect(404, 'GET', '/%252e%252e/Download-sibling/p0-outside.txt'))

for raw in ['/', '/../Download-sibling/new', '/%2e%2e/Download-sibling/new', '//host/name',
            'https://elsewhere/name', 'https://user@127.0.0.1:18273/name', '/name?query', '/name#fragment', '/bad%']:
    for method in ['MOVE', 'COPY']:
        def bad_destination(raw=raw, method=method):
            status, _, _ = request(method, PREFIX + '/preserve.bin', headers={'Destination': raw})
            assert status in [400, 403], f'Unexpected status {status}'
            file_equals(PREFIX + '/preserve.bin', OLD)
        run(f'{method}-destination-{raw}', bad_destination)

def names_roundtrip():
    for name in ['中文 + %.txt', '%2e%2e', "apostrophe' &.txt", '.ordinary-hidden']:
        encoded = urllib.parse.quote(name, safe='')
        target = PREFIX + '/' + encoded
        expect(201, 'PUT', target, NEW)
        file_equals(target, NEW)
        moved = PREFIX + '/' + urllib.parse.quote('renamed-' + name, safe='')
        expect(201, 'MOVE', target, headers={'Destination': 'https://127.0.0.1:18273' + moved})
        file_equals(moved, NEW)
    xml, _ = expect(207, 'PROPFIND', PREFIX, headers={'Depth': '1'})
    assert b'.ordinary-hidden' in xml and b'%252e%252e' in xml
    html, _ = expect(200, 'GET', PREFIX)
    assert b'&amp;' in html and b'&apos;' in html
run('unicode-percent-plus-hidden-and-escaped-listing', names_roundtrip)

def incomplete_overwrite():
    status, _, closed = request('PUT', PREFIX + '/preserve.bin', b'x' * 100, {'Content-Length': '65536'})
    assert status == 408 and closed
    file_equals(PREFIX + '/preserve.bin', OLD)
    actual = adb('exec-out', 'cat', '/sdcard/Download' + PREFIX + '/preserve.bin')
    assert actual == OLD
run('timeout-preserves-existing-independent-android-bytes', incomplete_overwrite)

def disconnected_new_upload():
    conn = connection()
    conn.request('PUT', PREFIX + '/disconnected.bin', b'x' * 100, {'Authorization': AUTH, 'Content-Length': '65536'})
    conn.sock.shutdown(socket.SHUT_RDWR)
    conn.close()
    time.sleep(0.3)
    expect(404, 'GET', PREFIX + '/disconnected.bin')
run('disconnected-new-upload-does-not-create-target', disconnected_new_upload)

def concurrent_visibility():
    conn = connection()
    conn.request('PUT', PREFIX + '/preserve.bin', NEW[:100], {'Authorization': AUTH, 'Content-Length': str(len(NEW))})
    time.sleep(0.25)
    assert adb('exec-out', 'cat', '/sdcard/Download' + PREFIX + '/preserve.bin') == OLD
    with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
        pending = pool.submit(request, 'GET', PREFIX + '/preserve.bin')
        conn.send(NEW[100:])
        response = conn.getresponse()
        response.read()
        assert response.status == 204
        status, body, _ = pending.result(timeout=12)
        assert status == 200 and body in [OLD, NEW]
    conn.close()
    file_equals(PREFIX + '/preserve.bin', NEW)
run('concurrent-read-sees-complete-version-not-partial-upload', concurrent_visibility)

def retained_staging():
    name = '.phonebridge-upload-retained.part'
    local = RUN / 'retained-staging-fixture'
    local.write_bytes(b'unknown state - preserve')
    adb('push', str(local), '/sdcard/Download' + PREFIX + '/' + name)
    for method in ['GET', 'PUT', 'DELETE', 'MOVE', 'COPY', 'PROPFIND']:
        expect(403, method, PREFIX + '/' + name, b'x' if method == 'PUT' else None,
               {'Destination': PREFIX + '/unused'} if method in ['MOVE', 'COPY'] else None)
    xml, _ = expect(207, 'PROPFIND', PREFIX, headers={'Depth': '1'})
    assert name.encode() not in xml
    expect(409, 'DELETE', PREFIX)
    assert adb('exec-out', 'cat', '/sdcard/Download' + PREFIX + '/' + name) == local.read_bytes()
run('retained-staging-hidden-inaccessible-and-not-recursively-deleted', retained_staging)

def ordinary_operations():
    folder = PREFIX + '/operations'
    expect(201, 'MKCOL', folder)
    expect(201, 'MKCOL', folder + '/sub')
    expect(201, 'PUT', folder + '/sub/file', NEW)
    expect(201, 'COPY', folder, headers={'Destination': PREFIX + '/copied'})
    file_equals(PREFIX + '/copied/sub/file', NEW)
    expect(201, 'MOVE', PREFIX + '/copied', headers={'Destination': PREFIX + '/moved'})
    expect(412, 'COPY', folder + '/sub/file', headers={'Destination': PREFIX + '/preserve.bin', 'Overwrite': 'F'})
    expect(204, 'COPY', folder + '/sub/file', headers={'Destination': PREFIX + '/preserve.bin'})
    expect(403, 'MOVE', folder, headers={'Destination': folder + '/sub/inside'})
    expect(204, 'DELETE', PREFIX + '/moved')
    expect(404, 'GET', PREFIX + '/moved')
    file_equals(folder + '/sub/file', NEW)
    expect(201, 'PUT', folder + '/empty', b'')
    file_equals(folder + '/empty', b'')
run('mkdir-recursive-copy-move-delete-overwrite-and-empty-upload', ordinary_operations)

def final_sentinel_check():
    assert adb('exec-out', 'cat', '/sdcard/Download-sibling/p0-outside.txt') == outside_before
    file_equals(PREFIX + '/preserve.bin', NEW)
run('outside-sentinel-and-shared-root-survive-all-probes', final_sentinel_check)
print(json.dumps({'passed': sum(row['result'] == 'PASS' for row in results), 'total': len(results), 'fixture': PREFIX}), flush=True)
raise SystemExit(0 if all(row['result'] == 'PASS' for row in results) else 1)
