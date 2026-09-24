"""P0-008 read-only network probe on the explicitly authorized spare phone.

No file transfers or authentication over LAN. USB obtains only the public TLS
certificate; rclone must validate that certificate before any HTTP request.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import ssl
import subprocess
import threading
import time
import xml.etree.ElementTree as ET

from zeroconf import IPVersion, ServiceBrowser, Zeroconf

ROOT = Path(__file__).resolve().parents[3]
RUN = ROOT / '.audit/runs/P0-008'
ADB = ROOT / '.audit/tools/android-sdk/platform-tools/adb.exe'
RCLONE = ROOT / '.audit/tools/rclone-v1.75.1-windows-amd64/rclone.exe'
APK = ROOT / '.audit/p0-007-worktree/android/app/build/outputs/apk/debug/app-debug.apk'
SERIAL = os.environ['PHONEBRIDGE_TEST_SERIAL']
LOCAL_IP = os.environ['PHONEBRIDGE_TEST_LOCAL_IP']
PHONE_IP = os.environ['PHONEBRIDGE_TEST_PHONE_IP']
FORWARD = 18279
EXPECTED_APK = '3d96a0c4f5aecf1f68cedc522392e06f7e5197db78fba97e891e0f71c0e86100'


def adb(*args, data=None, timeout=45):
    r = subprocess.run([str(ADB), '-s', SERIAL, *args], input=data, capture_output=True, timeout=timeout)
    if r.returncode:
        raise RuntimeError(f'ADB operation failed: {r.returncode}')
    return r.stdout


def save(name, data):
    (RUN / name).write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def guard():
    with socket.socket() as available:
        available.bind(('127.0.0.1', FORWARD))
    assert adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
    assert adb('shell', 'getprop', 'ro.build.version.sdk').strip() == b'36'
    assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() != b'1'
    assert PHONE_IP in adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
    assert hashlib.sha256(APK.read_bytes()).hexdigest() == EXPECTED_APK
    apk_path = adb('shell', 'pm', 'path', 'com.phonebridge').decode().strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', apk_path)
    assert adb('shell', 'sha256sum', apk_path).decode().split()[0] == EXPECTED_APK
    # Read-only test may expose no user files. Preserve the two existing Android
    # thumbnail metadata files; neither is opened, downloaded, or deleted.
    files = set(adb('shell', 'find', '/sdcard/Music', '-type', 'f').decode().splitlines())
    assert files <= {'/sdcard/Music/.thumbnails/.database_uuid', '/sdcard/Music/.thumbnails/.nomedia'}
    assert not adb('shell', 'find', '/sdcard/Music', '-type', 'l').strip()
    save('preflight.json', {'apk_sha256': EXPECTED_APK, 'device': 'alioth', 'api': 36,
        'phone_wifi_ip': PHONE_IP, 'pc_lan_ip': LOCAL_IP, 'shared_root': '/sdcard/Music',
        'existing_regular_file_count': len(files), 'existing_files_are_thumbnail_metadata_only': True,
        'existing_files_modified': False, 'transport': 'Phone Wi-Fi to PC Ethernet on same LAN'})


def main():
    RUN.mkdir(parents=True, exist_ok=True)
    guard()
    # App was installed/setup under this task's authorization. The user starts
    # sharing via the normal phone UI; Xiaomi rejects shell notification grants.
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    assert next(n.text for n in prefs if n.attrib.get('name') == 'shared_folder') == 'music'
    assert next(n.attrib.get('value') for n in prefs if n.attrib.get('name') == 'auto_start_on_boot') == 'false'
    service = adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
    assert 'isForeground=true' in service, 'Start sharing using the authorized phone UI first'
    seen = []
    discovered = threading.Event()
    started = time.monotonic()

    class Listener:
        def add_service(self, zc, kind, name):
            info = zc.get_service_info(kind, name)
            if info and PHONE_IP in info.parsed_addresses():
                seen.append({'name': name, 'addresses': info.parsed_addresses(), 'port': info.port,
                    'protocol': (info.properties.get(b'protocol') or b'').decode(),
                    'elapsed_seconds': round(time.monotonic() - started, 3)})
                discovered.set()

        def update_service(self, zc, kind, name):
            self.add_service(zc, kind, name)

        def remove_service(self, zc, kind, name):
            pass

    forwarded = False
    try:
        with Zeroconf(interfaces=[LOCAL_IP], ip_version=IPVersion.V4Only) as zc:
            browser = ServiceBrowser(zc, '_phonebridge._tcp.local.', Listener())
            discovered.wait(max(0, 30 - (time.monotonic() - started)))
            browser.cancel()
        save('mdns.json', {'observation_limit_seconds': 30, 'matching_services': seen,
            'discovered_within_30_seconds': any(row['elapsed_seconds'] <= 30 for row in seen)})
        print(json.dumps({'mdns_matches': len(seen)}), flush=True)
        adb('forward', '--no-rebind', f'tcp:{FORWARD}', 'tcp:8273')
        forwarded = True
        # Public certificate bootstrap over user-authorized USB, not LAN TOFU.
        # No HTTP request, password, or data is sent through this bootstrap.
        with socket.create_connection(('127.0.0.1', FORWARD), timeout=5) as sock:
            with ssl._create_unverified_context().wrap_socket(sock) as tls:
                der = tls.getpeercert(binary_form=True)
        pem = RUN / 'phone-public-certificate.pem'
        pem.write_text(ssl.DER_cert_to_PEM_cert(der), encoding='ascii')
        details = ssl._ssl._test_decode_cert(str(pem))
        save('certificate.json', {'sha256': hashlib.sha256(der).hexdigest(), 'subject': details['subject'],
            'subject_alt_names': details.get('subjectAltName', []), 'bootstrap': 'Public certificate only, USB ADB',
            'private_key_exported': False})
        # The actual LAN rclone operation verifies TLS, and has no credentials.
        env = {k: v for k, v in os.environ.items() if not k.upper().startswith('RCLONE_') and k.upper() not in {'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY'}}
        command = [str(RCLONE), 'lsd', ':webdav:', '--webdav-url', f'https://{PHONE_IP}:8273/',
            '--webdav-vendor', 'other', '--ca-cert', str(pem), '--config', 'NUL',
            '--retries', '1', '--low-level-retries', '1', '--contimeout', '5s', '--timeout', '10s']
        result = subprocess.run(command, env=env, capture_output=True, timeout=30)
        (RUN / 'rclone-tls-probe.log').write_bytes(result.stderr)
        message = result.stderr.decode(errors='replace')
        save('tls-probe.json', {'rclone_exit_code': result.returncode, 'certificate_verification_enabled': True,
            'credentials_sent': False, 'tls_identity_rejected': 'doesn\'t contain any IP SANs' in message,
            'status': 'FAIL' if result.returncode else 'PASS', 'file_transfer_attempted': False})
        print(json.dumps({'rclone_exit_code': result.returncode, 'error': message}), flush=True)
    finally:
        adb('shell', 'am', 'force-stop', 'com.phonebridge')
        stopped = b'ServiceRecord{' not in adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge')
        if forwarded:
            adb('forward', '--remove', f'tcp:{FORWARD}')
        save('probe-cleanup.json', {'service_absent': stopped, 'test_forward_removed': forwarded,
            'app_installed_but_force_stopped': True, 'shared_storage_writes_performed': False})
        assert stopped


if __name__ == '__main__':
    main()
