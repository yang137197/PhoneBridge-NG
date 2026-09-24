"""Real Android HTTPS regressions for P0-004. Only the dedicated disposable emulator is allowed."""
import base64
import hashlib
import http.client
import json
import socket
import ssl
import time
from start_android import adb, auth_password, RUN, TASK_ID

if TASK_ID not in {'P0-004', 'P0-005'}:
    raise RuntimeError('Set PHONEBRIDGE_AUDIT_TASK=P0-004 or P0-005')
assert adb('emu', 'avd', 'name').splitlines()[0] == b'PhoneBridgeP0'
AUTH = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
CONTEXT = ssl._create_unverified_context()  # Isolated upstream experiment; never formal TLS acceptance.
DATA = hashlib.shake_256(b'P0-004-byte-range-fixture').digest(16384)
fixture = RUN / 'range-fixture.txt'
fixture.write_bytes(DATA)
empty = RUN / 'range-empty.bin'
empty.write_bytes(b'')
adb('push', str(fixture), '/sdcard/Download/range-fixture.txt')
adb('push', str(empty), '/sdcard/Download/range-empty.bin')
results = []

def connect():
    return http.client.HTTPSConnection('127.0.0.1', 18273, context=CONTEXT, timeout=9)

def request(method='GET', path='/range-fixture.txt', headers=None, body=None, authenticated=True):
    conn = connect()
    try:
        conn.putrequest(method, path)
        if authenticated:
            conn.putheader('Authorization', AUTH)
        for key, value in (headers or {}).items():
            conn.putheader(key, value)
        if body is not None and 'Content-Length' not in (headers or {}):
            conn.putheader('Content-Length', str(len(body)))
        conn.endheaders(body)
        response = conn.getresponse()
        data = response.read()
        return response.status, response.headers, data, response.will_close
    finally:
        conn.close()

def run(name, action):
    started = time.monotonic()
    try:
        action()
        row = {'name': name, 'result': 'PASS'}
    except Exception as error:
        row = {'name': name, 'result': 'FAIL', 'error': type(error).__name__, 'detail': str(error)[:300]}
    row['seconds'] = round(time.monotonic() - started, 3)
    results.append(row)
    (RUN / 'stream-regression.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(row), flush=True)

def range_case(header, expected_status, expected_data, content_range=None, extra=None, path='/range-fixture.txt'):
    headers = {'Range': header, **(extra or {})} if header is not None else (extra or {})
    status, response_headers, data, _ = request(path=path, headers=headers)
    assert status == expected_status, f'Expected {expected_status}, got {status}'
    assert data == expected_data, f'Representation mismatch: expected {len(expected_data)}, got {len(data)} bytes'
    assert response_headers.get('Content-Range') == content_range
    assert response_headers.get('Content-Encoding') is None
    assert int(response_headers['Content-Length']) == len(data)

cases = [
    ('full', None, 200, DATA, None),
    ('middle', 'bytes=100-199', 206, DATA[100:200], 'bytes 100-199/16384'),
    ('first-byte', 'bytes=0-0', 206, DATA[:1], 'bytes 0-0/16384'),
    ('last-byte', 'bytes=16383-', 206, DATA[-1:], 'bytes 16383-16383/16384'),
    ('open-end', 'bytes=16000-', 206, DATA[16000:], 'bytes 16000-16383/16384'),
    ('suffix', 'bytes=-512', 206, DATA[-512:], 'bytes 15872-16383/16384'),
    ('oversized-suffix', 'bytes=-20000', 206, DATA, 'bytes 0-16383/16384'),
    ('clipped-end', 'bytes=16000-99999', 206, DATA[16000:], 'bytes 16000-16383/16384'),
    ('past-end', 'bytes=16384-', 416, b'', 'bytes */16384'),
    ('reversed', 'bytes=20-10', 416, b'', 'bytes */16384'),
    ('zero-suffix', 'bytes=-0', 416, b'', 'bytes */16384'),
    ('malformed', 'bytes=abc', 416, b'', 'bytes */16384'),
    ('huge-first', 'bytes=' + '9'*80 + '-', 416, b'', 'bytes */16384'),
    ('huge-end', 'bytes=16000-' + '9'*80, 206, DATA[16000:], 'bytes 16000-16383/16384'),
    ('huge-suffix', 'bytes=-' + '9'*80, 206, DATA, 'bytes 0-16383/16384'),
    ('unsupported-unit', 'items=1-2', 200, DATA, None),
    ('unsupported-multiple', 'bytes=1-2,4-5', 200, DATA, None),
]
for name, *values in cases:
    run('range-' + name, lambda values=values: range_case(*values))
run('if-range-without-validator-full', lambda: range_case('bytes=0-3', 200, DATA, extra={'If-Range': '"unknown"'}))
run('gzip-does-not-change-byte-offsets', lambda: range_case('bytes=400-499', 206, DATA[400:500], 'bytes 400-499/16384', {'Accept-Encoding': 'gzip'}))
run('empty-full', lambda: range_case(None, 200, b'', path='/range-empty.bin'))
run('empty-range', lambda: range_case('bytes=0-', 416, b'', 'bytes */0', path='/range-empty.bin'))

def head_case():
    status, headers, data, _ = request('HEAD', headers={'Range': 'bytes=0-1'})
    assert status == 200 and not data
    assert set(headers.get_all('Content-Length')) == {'16384'}
run('head-ignores-range', head_case)

def keepalive_case():
    conn = connect()
    conn.connect()
    original_socket = conn.sock
    try:
        for _ in range(3):
            body = b'<?xml version="1.0"?><d:propfind xmlns:d="DAV:"><d:allprop/></d:propfind>'
            conn.request('PROPFIND', '/', body, {'Authorization': AUTH, 'Depth': '1', 'Content-Type': 'application/xml'})
            response = conn.getresponse()
            response.read()
            assert response.status == 207 and not response.will_close
            conn.request('GET', '/range-fixture.txt', headers={'Authorization': AUTH})
            response = conn.getresponse()
            assert response.status == 200 and response.read() == DATA
            assert conn.sock is original_socket, 'Connection silently replaced'
    finally:
        conn.close()
run('propfind-get-same-connection-three-times', keepalive_case)

def pipeline_case():
    with socket.create_connection(('127.0.0.1', 18273), timeout=9) as raw, CONTEXT.wrap_socket(raw, server_hostname='localhost') as tls:
        one = f'PROPFIND / HTTP/1.1\r\nHost: localhost\r\nAuthorization: {AUTH}\r\nDepth: 0\r\nContent-Length: 4\r\n\r\n<x/>'
        two = f'OPTIONS / HTTP/1.1\r\nHost: localhost\r\nAuthorization: {AUTH}\r\nConnection: close\r\n\r\n'
        tls.sendall((one + two).encode())
        with tls.makefile('rb') as stream:
            for expected in (207, 200):
                status_line = stream.readline(8193)
                assert int(status_line.split()[1]) == expected
                headers = {}
                while (line := stream.readline(8193)) not in (b'\r\n', b''):
                    key, value = line.decode().split(':', 1)
                    headers[key.lower()] = value.strip()
                assert 'transfer-encoding' not in headers
                stream.read(int(headers['content-length']))
run('pipelined-body-does-not-consume-next-request', pipeline_case)

def rejection(headers, expected, method='PROPFIND', body=None, authenticated=True):
    status, _, _, closing = request(method, '/', headers, body, authenticated)
    assert status == expected, f'Expected {expected}, got {status}'
    assert closing, 'Unread or invalid body must close connection'
run('oversized-body', lambda: rejection({'Content-Length': '1048577'}, 413))
run('negative-length', lambda: rejection({'Content-Length': '-1'}, 400))
run('invalid-length', lambda: rejection({'Content-Length': 'xyz'}, 400))
run('overflow-length', lambda: rejection({'Content-Length': '9'*80}, 400))
run('chunked-body-rejected', lambda: rejection({'Transfer-Encoding': 'chunked'}, 501))
run('ambiguous-transfer-encoding-and-length', lambda: rejection({'Transfer-Encoding': 'chunked', 'Content-Length': '0'}, 501))
run('missing-put-length', lambda: rejection({}, 411, method='PUT'))
run('unauthenticated-body-closes', lambda: rejection({}, 401, body=b'XML body', authenticated=False))
run('stalled-short-body-times-out', lambda: rejection({'Content-Length': '100'}, 408, body=b'short'))

def bounded_large_body():
    status, _, _, closing = request('PROPFIND', '/', {'Depth': '0'}, b'x'*65536)
    assert status == 207 and not closing
run('multi-buffer-body', bounded_large_body)
failed = sum(row['result'] == 'FAIL' for row in results)
print(json.dumps({'passed': len(results)-failed, 'failed': failed}), flush=True)
raise SystemExit(1 if failed else 0)
