"""P0-009 real service/rclone verification. Never writes shared storage.

Explicit experiment migration removes only the P0-created legacy PKCS12; it is
not an automatic product migration. USB bootstraps public CA, not a LAN probe.
"""
import base64
from contextlib import contextmanager
import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import socket
import ssl
import subprocess
import time
import xml.etree.ElementTree as ET
from tls_instrumentation import ADB, ROOT, SERIAL, TARGETS

API, AVD, NAME = TARGETS[SERIAL]
RUN = ROOT / '.audit/runs/P0-009' / NAME
PORT = {'api26': 18275, 'api36': 18277, 'phone36': 18279}[NAME]
RCLONE = ROOT / '.audit/tools/rclone-v1.75.1-windows-amd64/rclone.exe'
APK = ROOT / '.audit/p0-009-worktree/android/app/build/outputs/apk/debug/app-debug.apk'
CERT = 'no_backup/tls-identity-v1/identity.der'
LEGACY = 'files/phonebridge_keystore.p12'
ROWS = []

def adb(*args, data=None, timeout=45, check=True):
    p = subprocess.run([str(ADB), '-s', SERIAL, *args], input=data, capture_output=True, timeout=timeout)
    if check and p.returncode:
        raise RuntimeError(f'ADB operation failed: {p.returncode}')
    return p.stdout if check else p

def exists(path):
    assert re.fullmatch(r'[a-zA-Z0-9_./-]+', path)
    result = adb('shell', 'run-as', 'com.phonebridge', 'sh', '-c', f"'test -f {path}'", check=False)
    assert result.returncode in (0, 1) and not result.stderr.strip(), 'File preflight could not run'
    return result.returncode == 0

def write_private(path, content):
    assert path in {CERT, 'cache/p0-009-stop'}
    # ADB shell on older devices can apply terminal processing to binary stdin.
    # Send ASCII and decode on-device, then verify exact bytes before proceeding.
    adb('shell', '-T', 'run-as', 'com.phonebridge', 'sh', '-c', f"'base64 -d > {path}'",
        data=base64.b64encode(content) + b'\n')
    assert adb('exec-out', 'run-as', 'com.phonebridge', 'cat', path) == content, 'Fixture write verification failed'

def record(name, **details):
    ROWS.append({'test': name, 'result': 'PASS', **details})
    (RUN / 'tls-runtime.json').write_text(json.dumps({'target': NAME, 'tests': ROWS}, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(ROWS[-1]), flush=True)

@contextmanager
def host(label, expected):
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    adb('shell', 'run-as', 'com.phonebridge', 'rm', '-f', 'cache/p0-009-stop', 'cache/p0-009-ready')
    with (RUN / f'host-{label}.log').open('wb') as log:
        p = subprocess.Popen([str(ADB), '-s', SERIAL, 'shell', 'am', 'instrument', '-w', '-r',
            '-e', 'class', 'com.phonebridge.TlsServiceHost', '-e', 'p0_tls_host', 'P0-009',
            'com.phonebridge.test/androidx.test.runner.AndroidJUnitRunner'], stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 35
            foreground_launch = False
            while not exists('cache/p0-009-ready'):
                assert p.poll() is None, 'Integration host ended before readiness'
                assert time.monotonic() < deadline, 'Service host readiness timeout'
                if not AVD and not foreground_launch:
                    # This ROM rejects the instrumentation process's background
                    # launcher intent (START_ABORTED=102). Launch the normal public
                    # activity through the authorized ADB session, without changing
                    # any app-op, permission, lock-screen or background policy.
                    pid = adb('shell', 'pidof', 'com.phonebridge', check=False).stdout.decode().strip()
                    if pid.isdigit():
                        events = adb('logcat', '-d', '-b', 'system', '-v', 'brief',
                                     'ActivityTaskManager:I', '*:S').decode(errors='replace')
                        denied = [line for line in events.splitlines()
                                  if f'from pid {pid} callingPackage com.phonebridge ' in line
                                  and 'result code=102' in line and 'com.phonebridge/.MainActivity' in line]
                        if denied:
                            launch = adb('shell', 'am', 'start', '-W', '-a', 'android.intent.action.MAIN',
                                         '-c', 'android.intent.category.LAUNCHER',
                                         '-n', 'com.phonebridge/.MainActivity', timeout=15).decode()
                            assert 'Status: ok' in launch, 'Explicit foreground launch did not complete'
                            foreground_launch = True
                            (RUN / f'host-{label}-foreground-launch.log').write_text(
                                denied[-1] + '\n' + launch, encoding='utf-8')
                            record('explicit_foreground_test_launch', host=label,
                                   background_start_result=102, system_policy_changed=False)
                time.sleep(0.25)
            ready = adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'cache/p0-009-ready').decode().strip()
            assert ready == expected, f'Expected service {expected}, got {ready}'
            yield
        finally:
            if p.poll() is None:
                write_private('cache/p0-009-stop', b'stop')
                try:
                    p.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    p.terminate(); p.wait(timeout=5)
            adb('shell', 'am', 'force-stop', 'com.phonebridge')
    output = (RUN / f'host-{label}.log').read_text(encoding='utf-8')
    assert p.returncode == 0 and 'OK (1 test)' in output and 'FAILURES!!!' not in output

def refused_without_http(label):
    # ADB's listening forward itself remains open even when the Android target is closed.
    with socket.create_connection(('127.0.0.1', PORT), timeout=4) as sock:
        sock.settimeout(4)
        try:
            sock.sendall(b'GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n')
            response = sock.recv(1024)
        except (ConnectionResetError, ConnectionAbortedError):
            response = b''
        assert not response, 'TLS failure exposed an HTTP listener'
    services = adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
    assert 'isForeground=true' not in services
    record(label, plaintext_http_response_bytes=0, foreground_service=False)

def password():
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    return next(n.text for n in prefs if n.attrib.get('name') == 'auth_password')

def clean_env():
    return {k: v for k, v in os.environ.items() if not k.upper().startswith('RCLONE_')
            and not k.upper().endswith('_PROXY') and k.upper() != 'SSLKEYLOGFILE'}

def status(ca, secret):
    context = ssl.create_default_context(cafile=str(ca))
    conn = http.client.HTTPSConnection('127.0.0.1', PORT, context=context, timeout=5)
    try:
        conn.connect()
        tls_version = conn.sock.version()
        header = 'Basic ' + base64.b64encode(('phonebridge:' + secret).encode()).decode()
        conn.request('GET', '/phonebridge/status', headers={'Authorization': header})
        response = conn.getresponse()
        data = json.loads(response.read())
        assert response.status == 200
        data['_probe_tls_version'] = tls_version
        return data
    finally:
        conn.close()

def rclone(ca, url, secret, expected_error=None):
    env = clean_env()
    obscure = subprocess.run([str(RCLONE), 'obscure', '-'], input=secret.encode(), capture_output=True,
                             env=env, timeout=10, check=True).stdout.decode().strip()
    env.update(RCLONE_WEBDAV_URL=url, RCLONE_WEBDAV_USER='phonebridge', RCLONE_WEBDAV_PASS=obscure)
    p = subprocess.run([str(RCLONE), 'lsd', ':webdav:', '--config', 'NUL', '--ca-cert', str(ca),
                        '--retries', '1', '--low-level-retries', '1', '--contimeout', '5s', '--timeout', '10s',
                        '--log-level', 'ERROR'], env=env, capture_output=True, timeout=30)
    error = p.stderr.decode(errors='replace')
    assert secret not in error and 'Authorization:' not in error
    if expected_error:
        assert p.returncode != 0 and expected_error in error, 'Expected TLS rejection was not observed'
    else:
        assert p.returncode == 0, error[-1500:]
    return {'exit_code': p.returncode, 'tls_verification_disabled': False,
            'error': error.strip() if expected_error else None, 'url': url}

def main():
    RUN.mkdir(parents=True, exist_ok=True)
    assert int(adb('shell', 'getprop', 'ro.build.version.sdk')) == API
    if AVD:
        assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
        assert adb('emu', 'avd', 'name').splitlines()[0] == AVD.encode()
    else:
        assert SERIAL == '61c9a964' and adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
        assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() != b'1'
        files = set(adb('shell', 'find', '/sdcard/Music', '-type', 'f').decode().splitlines())
        assert files <= {'/sdcard/Music/.thumbnails/.database_uuid', '/sdcard/Music/.thumbnails/.nomedia'}
        assert not adb('shell', 'find', '/sdcard/Music', '-type', 'l').strip()
        prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
        assert next(n.text for n in prefs if n.attrib.get('name') == 'shared_folder') == 'music'
        assert next(n.attrib.get('value') for n in prefs if n.attrib.get('name') == 'auto_start_on_boot') == 'false'
    installed = adb('shell', 'pm', 'path', 'com.phonebridge').decode().strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
    apk_hash = hashlib.sha256(APK.read_bytes()).hexdigest()
    assert adb('shell', 'sha256sum', installed).decode().split()[0] == apk_hash
    record('preflight', apk_sha256=apk_hash, shared_storage_writes=False)
    adb('forward', '--no-rebind', f'tcp:{PORT}', 'tcp:8273')
    cert_bytes = None
    try:
        if exists(LEGACY):
            assert not exists(CERT), 'Unexpected simultaneous legacy and new identity'
            old_hash = adb('shell', 'run-as', 'com.phonebridge', 'sha256sum', LEGACY).decode().split()[0]
            with host('legacy', 'rejected'):
                refused_without_http('legacy_identity_stops_sharing')
            assert os.environ.get('PHONEBRIDGE_EXPERIMENTAL_TLS_MIGRATION') == 'P0-009', 'Explicit experiment migration required'
            adb('shell', 'run-as', 'com.phonebridge', 'rm', LEGACY)
            record('explicit_experiment_identity_migration', retired_pkcs12_sha256=old_hash,
                   reset_formal_pairing=False, removed_only=LEGACY)
        with host('trusted', 'running'):
            cert_bytes = adb('exec-out', 'run-as', 'com.phonebridge', 'cat', CERT)
            assert 0 < len(cert_bytes) <= 65536
            fingerprint = hashlib.sha256(cert_bytes).hexdigest()
            ca = RUN / 'paired-identity.pem'
            ca.write_text(ssl.DER_cert_to_PEM_cert(cert_bytes), encoding='ascii')
            secret = password()
            record('rclone_correct_identity_loopback', **rclone(ca, f'https://127.0.0.1:{PORT}/', secret))
            observed = status(ca, secret)
            assert observed['identity_certificate_sha256'] == fingerprint
            assert observed['device_id'] == 'pbng-' + fingerprint and observed['cert_fingerprint'] is None
            record('stable_identity_metadata', identity_certificate_sha256=fingerprint,
                   verified_status_tls_version=observed['_probe_tls_version'])
            previous = observed['total_requests']
            wrong_ca = ROOT / '.audit/runs/P0-008/phone-public-certificate.pem'
            record('rclone_wrong_identity_rejected', **rclone(wrong_ca, f'https://127.0.0.1:{PORT}/', secret, 'x509:'))
            record('rclone_wrong_address_rejected', **rclone(ca, f'https://localhost:{PORT}/', secret, 'x509:'))
            after = status(ca, secret)['total_requests']
            assert after == previous + 1, 'Rejected TLS reached HTTP authentication/request handling'
            record('negative_tls_sent_no_http_requests', request_count_difference=after - previous,
                   expected_status_probe_requests=1)
            if not AVD:
                phone_ip = os.environ['PHONEBRIDGE_TEST_PHONE_IP']
                assert re.fullmatch(r'192\.168\.100\.\d{1,3}', phone_ip)
                assert phone_ip in adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
                record('rclone_correct_identity_real_lan', **rclone(ca, f'https://{phone_ip}:8273/', secret))
        with host('restart', 'running'):
            assert adb('exec-out', 'run-as', 'com.phonebridge', 'cat', CERT) == cert_bytes
            record('process_restart_preserves_paired_identity', **rclone(ca, f'https://127.0.0.1:{PORT}/', secret))
        write_private(CERT, b'P0-009 corrupt public certificate fixture')
        try:
            with host('corrupt', 'rejected'):
                refused_without_http('corrupt_identity_stops_sharing')
        finally:
            write_private(CERT, cert_bytes)
        with host('restore', 'running'):
            record('same_identity_recovers_after_public_metadata_restore', **rclone(ca, f'https://127.0.0.1:{PORT}/', secret))
    finally:
        adb('shell', 'am', 'force-stop', 'com.phonebridge')
        adb('forward', '--remove', f'tcp:{PORT}')
        assert 'isForeground=true' not in adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
        assert not any(parts[0] == SERIAL and parts[1] == f'tcp:{PORT}'
                       for row in adb('forward', '--list').decode().splitlines() if (parts := row.split()))
        record('cleanup', sharing_stopped=True, adb_forward_removed=True, shared_storage_writes=False)

if __name__ == '__main__':
    main()
