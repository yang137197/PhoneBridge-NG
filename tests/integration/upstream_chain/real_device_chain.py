"""P0-010: authorized spare-phone LAN -> rclone -> WinFsp file-chain experiment.

Only new synthetic fixtures are written under a UUID Music subdirectory. No
existing phone data, settings or identity is changed; all fixtures/cache remain.
"""
import base64
from contextlib import contextmanager
import ctypes
import hashlib
import http.client
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import ssl
import subprocess
import threading
import time
import uuid
import xml.etree.ElementTree as ET

from zeroconf import IPVersion, ServiceBrowser, Zeroconf
import tls_runtime as tls

ROOT = tls.ROOT
SERIAL = tls.SERIAL
LOCAL_IP = os.environ['PHONEBRIDGE_TEST_LOCAL_IP']
PHONE_IP = os.environ['PHONEBRIDGE_TEST_PHONE_IP']
RUN_ID = 'P0-010-' + uuid.uuid4().hex[:12]
RUN = ROOT / '.audit/runs/P0-010' / RUN_ID
REMOTE = '/sdcard/Music/' + RUN_ID
DRIVE = Path('P:/')
SIZE = 100_000_000
RC_PORT = 15581
RC_SECRET = secrets.token_urlsafe(32)
ROWS = []
APK_SHA = 'a4383ddac27dccc3f2cc46704f1c389c8ad961d1d8e8205894d8cc8aebb2b88a'
RCLONE_SHA = '033eee51c9ad47c2de2624b6674d355274bcd6cf0027a5f85db4437ba24ae81c'


def record(name, result='PASS', **details):
    ROWS.append({'test': name, 'result': result, **details})
    (RUN / 'chain-results.json').write_text(json.dumps({
        'run_id': RUN_ID, 'checks': ROWS, 'fixtures_retained': True,
        'transport': 'Phone Wi-Fi to PC Ethernet on same LAN',
    }, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(ROWS[-1]), flush=True)


@contextmanager
def phone_service_host(label):
    with tls.host(label, 'running'):
        try:
            yield
        finally:
            # ActivityScenario.close launches a framework EmptyActivity before
            # finishing its target. This ROM rejects that launch while the app
            # is backgrounded (verified thread stack + START_ABORTED=102).
            # Return the authorized test UI to foreground before requesting
            # teardown. Do not alter background-start permissions or app-ops.
            launch = tls.adb('shell', 'am', 'start', '-W', '-a', 'android.intent.action.MAIN',
                             '-c', 'android.intent.category.LAUNCHER',
                             '-n', 'com.phonebridge/.MainActivity', timeout=15).decode()
            assert 'Status: ok' in launch, 'Test UI could not return to foreground for teardown'
            record('test_teardown_foreground', system_policy_changed=False)


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def phone_info(name):
    assert re.fullmatch(r'[a-zA-Z0-9_.-]+', name)
    path = REMOTE + '/' + name
    return {'sha256': tls.adb('shell', 'sha256sum', path).decode().split()[0],
            'bytes': int(tls.adb('shell', 'stat', '-c', '%s', path))}


def metadata_snapshot():
    return {name: tls.adb('shell', 'stat', '-c', '%s:%Y:%i', '/sdcard/Music/.thumbnails/' + name).decode().strip()
            for name in ['.database_uuid', '.nomedia']}


def rc(endpoint):
    conn = http.client.HTTPConnection('127.0.0.1', RC_PORT, timeout=5)
    try:
        header = 'Basic ' + base64.b64encode(('p0:' + RC_SECRET).encode()).decode()
        conn.request('POST', '/' + endpoint, body='{}',
                     headers={'Authorization': header, 'Content-Type': 'application/json'})
        response = conn.getresponse()
        data = response.read()
        assert response.status == 200, f'RC {endpoint} returned {response.status}'
        return json.loads(data)
    finally:
        conn.close()


def lan_request(ca, path, secret=None, hash_body=False):
    conn = http.client.HTTPSConnection(PHONE_IP, 8273, context=ssl.create_default_context(cafile=str(ca)), timeout=15)
    try:
        headers = {}
        if secret:
            headers['Authorization'] = 'Basic ' + base64.b64encode(('phonebridge:' + secret).encode()).decode()
        conn.request('GET', path, headers=headers)
        response = conn.getresponse()
        if hash_body:
            count = 0
            sha = hashlib.sha256()
            while part := response.read(1024 * 1024):
                sha.update(part); count += len(part)
            return response.status, {'bytes': count, 'sha256': sha.hexdigest()}
        return response.status, response.read()
    finally:
        conn.close()


def preflight():
    assert SERIAL == '61c9a964' and tls.adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
    assert tls.adb('shell', 'getprop', 'ro.build.version.sdk').strip() == b'36'
    assert tls.adb('shell', 'getprop', 'ro.kernel.qemu').strip() != b'1'
    assert ipaddress.ip_address(PHONE_IP) in ipaddress.ip_network('192.168.100.0/24')
    assert ipaddress.ip_address(LOCAL_IP) in ipaddress.ip_network('192.168.100.0/24')
    assert f'inet {PHONE_IP}/24' in tls.adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
    with socket.socket() as check:
        check.bind((LOCAL_IP, 0))
    assert not (ctypes.windll.kernel32.GetLogicalDrives() & (1 << 15)), 'P: is occupied'
    with socket.socket() as check:
        check.bind(('127.0.0.1', RC_PORT))
    assert not tls.adb('forward', '--list').strip(), 'Unexpected ADB forwarding exists'
    assert digest(tls.APK) == APK_SHA and digest(tls.RCLONE) == RCLONE_SHA
    for package, sha in [('com.phonebridge', APK_SHA), ('com.phonebridge.test',
                         'e6c62d4fd05367feecb61f8640cf5263bc14c8c4d3bef648b1a3c34178587a5d')]:
        installed = tls.adb('shell', 'pm', 'path', package).decode().strip().removeprefix('package:')
        assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
        assert tls.adb('shell', 'sha256sum', installed).decode().split()[0] == sha
    files = set(tls.adb('shell', 'find', '/sdcard/Music', '-type', 'f').decode().splitlines())
    assert files == {'/sdcard/Music/.thumbnails/.database_uuid', '/sdcard/Music/.thumbnails/.nomedia'}, 'Review existing fixtures before another run'
    assert not tls.adb('shell', 'find', '/sdcard/Music', '-type', 'l').strip()
    assert 'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
    prefs = ET.fromstring(tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    assert next(n.text for n in prefs if n.attrib.get('name') == 'shared_folder') == 'music'
    assert next(n.attrib.get('value') for n in prefs if n.attrib.get('name') == 'auto_start_on_boot') == 'false'
    assert shutil.disk_usage(ROOT).free > 1_000_000_000
    free_kib = int(tls.adb('shell', 'df', '-k', '/sdcard/Music').decode().splitlines()[-1].split()[3])
    assert free_kib * 1024 > 500_000_000
    der = tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', tls.CERT)
    fingerprint = hashlib.sha256(der).hexdigest()
    previous = json.loads((ROOT / 'docs/audit/p0-009/phone36-final-state.json').read_text(encoding='utf-8'))
    assert fingerprint == previous['identity_certificate_sha256'], 'Device identity changed; stop'
    ca = RUN / 'paired-identity.pem'
    ca.write_text(ssl.DER_cert_to_PEM_cert(der), encoding='ascii')
    record('preflight', apk_sha256=APK_SHA, rclone_sha256=RCLONE_SHA, identity_certificate_sha256=fingerprint,
           phone_wifi_ip=PHONE_IP, pc_ethernet_ip=LOCAL_IP, adb_file_transfer=False)
    return ca, fingerprint


def mount_and_copy(ca, secret, source_info, host_started):
    env = tls.clean_env()
    obscure = subprocess.run([str(tls.RCLONE), 'obscure', '-'], input=secret.encode(),
                             capture_output=True, timeout=10, check=True, env=env).stdout.decode().strip()
    env.update(RCLONE_WEBDAV_URL=f'https://{PHONE_IP}:8273/{RUN_ID}/', RCLONE_WEBDAV_USER='phonebridge',
               RCLONE_WEBDAV_PASS=obscure, RCLONE_RC_USER='p0', RCLONE_RC_PASS=RC_SECRET)
    args = [str(tls.RCLONE), 'mount', ':webdav:', 'P:', '--config', 'NUL', '--ca-cert', str(ca),
            '--webdav-vendor', 'other', '--cache-dir', str(RUN / 'cache'), '--vfs-cache-mode', 'full',
            '--vfs-cache-max-age', '1h', '--vfs-read-chunk-size', '32M', '--dir-cache-time', '5s',
            '--poll-interval', '0', '--vfs-write-back', '0s', '--network-mode', '--volname', 'PhoneBridge P0-010',
            '--no-console', '--rc', '--rc-addr', f'127.0.0.1:{RC_PORT}', '--contimeout', '5s', '--timeout', '15s',
            '--log-level', 'INFO', '--log-file', str(RUN / 'rclone.log')]
    process = subprocess.Popen(args, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                               creationflags=subprocess.CREATE_NO_WINDOW)
    confirmed = False
    writes_started = False
    record('rclone_started', pid=process.pid, tls_verification_disabled=False, mount_scope=REMOTE)
    try:
        deadline = time.monotonic() + 20
        while not (DRIVE / 'phone-origin.txt').is_file():
            assert process.poll() is None, 'rclone exited before mount became ready'
            assert time.monotonic() < deadline, 'Mount readiness timeout'
            time.sleep(0.25)
        record('CHAIN-03-mount', drive='P:', names=sorted(p.name for p in DRIVE.iterdir()))
        for name in ['phone-100MB.bin', 'phone-origin.txt']:
            destination = RUN / ('downloaded-' + name)
            start = time.monotonic()
            with (DRIVE / name).open('rb') as source, destination.open('xb') as target:
                shutil.copyfileobj(source, target, 1024 * 1024)
            actual = {'bytes': destination.stat().st_size, 'sha256': digest(destination)}
            assert actual == source_info[name], 'Phone to PC content mismatch'
            record('CHAIN-04-phone-to-pc', file=name, **actual, android=source_info[name],
                   elapsed_seconds=round(time.monotonic() - start, 3), through='P: / WinFsp / rclone / LAN')
        start = time.monotonic()
        writes_started = True
        with (RUN / 'pc-100MB.bin').open('rb') as source, (DRIVE / 'pc-upload-100MB.bin').open('xb') as target:
            shutil.copyfileobj(source, target, 1024 * 1024)
        expected = {'bytes': SIZE, 'sha256': digest(RUN / 'pc-100MB.bin')}
        deadline = time.monotonic() + 45
        stable = 0
        while stable < 2:
            state = rc('vfs/stats')['diskCache']
            assert state['erroredFiles'] == 0, 'VFS reports write errors; preserve cache'
            if state['uploadsInProgress'] == 0 and state['uploadsQueued'] == 0:
                try:
                    actual = phone_info('pc-upload-100MB.bin')
                except RuntimeError:
                    actual = None
                stable = stable + 1 if actual == expected else 0
            else:
                stable = 0
            assert time.monotonic() < deadline, 'Independent phone persistence verification timeout'
            time.sleep(0.3)
        confirmed = True
        record('CHAIN-05-pc-to-phone', file='pc-upload-100MB.bin', **expected, android=actual,
               elapsed_seconds=round(time.monotonic() - start, 3), through='P: / WinFsp / rclone / LAN')
        code, readback = lan_request(ca, f'/{RUN_ID}/pc-upload-100MB.bin', secret, hash_body=True)
        assert code == 200 and readback == expected, 'Fresh authenticated LAN readback mismatch'
        record('upload_fresh_lan_readback', **readback, pc_vfs_cache_used=False)
        (DRIVE / 'new-folder').mkdir()
        (DRIVE / 'new-folder').rename(DRIVE / 'renamed-folder')
        tls.adb('shell', 'sh', '-c', f"'test -d {REMOTE}/renamed-folder && test ! -e {REMOTE}/new-folder'")
        record('mkdir_and_rename', android_directory_verified=True)
        (RUN / 'explorer-ready.json').write_text(json.dumps({'drive': 'P:', 'names': sorted(p.name for p in DRIVE.iterdir())}), encoding='utf-8')
        record('explorer_ready', evidence_file=str(RUN / 'explorer-observation.json'))
        deadline = min(time.monotonic() + 60, host_started + 150)
        while not (RUN / 'explorer-observation.json').exists() and time.monotonic() < deadline:
            time.sleep(0.5)
        assert (RUN / 'explorer-observation.json').exists(), 'Explorer observation not completed within bounded host window'
        observation = json.loads((RUN / 'explorer-observation.json').read_text(encoding='utf-8'))
        assert observation['drive'] == 'P:' and observation['observed'] is True
        record('CHAIN-03-explorer', **observation)
        state = rc('vfs/stats')['diskCache']
        assert state['uploadsInProgress'] == state['uploadsQueued'] == state['erroredFiles'] == 0
        record('pending_writes', uploads_in_progress=0, uploads_queued=0, errored_files=0)
        rc('core/quit')
        code = process.wait(timeout=15)
        assert code == 0 and not DRIVE.exists()
        record('CHAIN-06-unmount', exit_code=code, drive_absent=True, cache_retained=True)
    except Exception:
        if process.poll() is None:
            try:
                state = rc('vfs/stats')['diskCache']
                if (not writes_started or confirmed) and state['uploadsInProgress'] == state['uploadsQueued'] == state['erroredFiles'] == 0:
                    rc('core/quit')
                    record('failure_unmount', exit_code=process.wait(timeout=15), cache_retained=True)
                else:
                    record('failure_unmount', result='BLOCKED', pid=process.pid, cache_retained=True)
            except Exception:
                record('failure_unmount', result='BLOCKED', pid=process.pid, cache_retained=True)
        raise


def main():
    RUN.mkdir(parents=True, exist_ok=False)
    ca, fingerprint = preflight()
    before = metadata_snapshot()
    tls.adb('shell', 'mkdir', REMOTE)  # New UUID directory only; no -p or overwrite.
    tls.adb('shell', 'dd', 'if=/dev/urandom', f'of={REMOTE}/phone-100MB.bin', 'bs=1000000', 'count=100', timeout=45)
    payload = 'PhoneBridge P0-010 synthetic UTF-8: 中文测试\n'.encode()
    tls.adb('shell', '-T', 'sh', '-c', f"'base64 -d > {REMOTE}/phone-origin.txt'", data=base64.b64encode(payload) + b'\n')
    with (RUN / 'pc-100MB.bin').open('xb') as stream:
        for index in range(100):
            stream.write(hashlib.shake_256(RUN_ID.encode() + index.to_bytes(4, 'big')).digest(1_000_000))
    source_info = {name: phone_info(name) for name in ['phone-100MB.bin', 'phone-origin.txt']}
    assert source_info['phone-100MB.bin']['bytes'] == SIZE
    assert source_info['phone-origin.txt'] == {'bytes': len(payload), 'sha256': hashlib.sha256(payload).hexdigest()}
    record('fixtures', phone_directory=REMOTE, phone=source_info, pc={'bytes': SIZE, 'sha256': digest(RUN / 'pc-100MB.bin')})
    tls.RUN = RUN / 'host'
    tls.RUN.mkdir()
    tls.ROWS = []
    seen = []
    found = threading.Event()
    started = time.monotonic()

    class Listener:
        def add_service(self, zc, kind, name):
            info = zc.get_service_info(kind, name)
            if info and PHONE_IP in info.parsed_addresses() and info.port == 8273 and info.properties.get(b'protocol') == b'https':
                seen.append({'name': name, 'addresses': info.parsed_addresses(), 'port': info.port,
                             'elapsed_seconds': round(time.monotonic() - started, 3)})
                found.set()

        def update_service(self, zc, kind, name):
            self.add_service(zc, kind, name)

        def remove_service(self, zc, kind, name):
            pass

    try:
        with Zeroconf(interfaces=[LOCAL_IP], ip_version=IPVersion.V4Only) as zc:
            browser = ServiceBrowser(zc, '_phonebridge._tcp.local.', Listener())
            try:
                with phone_service_host('file-chain'):
                    host_started = time.monotonic()
                    assert found.wait(max(0, 30 - (time.monotonic() - started))), 'No matching mDNS within 30 seconds'
                    assert seen[0]['elapsed_seconds'] <= 30
                    record('CHAIN-01-discovery', observation_limit_seconds=30, service=seen[0], listener_started_before_service=True)
                    secret = tls.password()
                    code, _ = lan_request(ca, '/phonebridge/status')
                    assert code == 401
                    code, body = lan_request(ca, '/phonebridge/status', secret)
                    assert code == 200 and json.loads(body)['identity_certificate_sha256'] == fingerprint
                    record('CHAIN-02-https-auth', unauthenticated=401, authenticated=200, identity_unchanged=True, tls_verification_disabled=False)
                    mount_and_copy(ca, secret, source_info, host_started)
            finally:
                browser.cancel()
    except Exception as exc:
        # Do not write exception text: third-party messages may contain secrets.
        record('failure', result='FAIL', kind=type(exc).__name__, fixtures_and_cache_retained=True)
        raise
    finally:
        tls.adb('shell', 'am', 'force-stop', 'com.phonebridge')
        unchanged = metadata_snapshot() == before
        stopped = 'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
        assert unchanged and stopped and not tls.adb('forward', '--list').strip()
        record('CHAIN-06-phone-cleanup', sharing_stopped=stopped, existing_metadata_unchanged=unchanged,
               adb_forwards=0, fixtures_retained=True)
    record('overall', required_chain_cases_passed=6, actual_explorer_observation=True,
           phone_reboot_or_network_recovery_tested=False)


if __name__ == '__main__':
    main()
