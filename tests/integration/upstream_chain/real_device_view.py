"""P0-010 bounded, read-only Explorer recheck of an already verified fixture run."""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import ssl
import subprocess
import time

import real_device_chain as chain
import tls_runtime as tls


def main():
    run_id = os.environ['PHONEBRIDGE_CHAIN_RUN']
    assert re.fullmatch(r'P0-010-[0-9a-f]{12}', run_id)
    original = chain.ROOT / '.audit/runs/P0-010' / run_id
    previous = json.loads((original / 'chain-results.json').read_text(encoding='utf-8'))
    assert previous['run_id'] == run_id
    required = {'CHAIN-01-discovery', 'CHAIN-02-https-auth', 'CHAIN-04-phone-to-pc',
                'CHAIN-05-pc-to-phone', 'upload_fresh_lan_readback', 'mkdir_and_rename', 'CHAIN-06-phone-cleanup'}
    assert required <= {r['test'] for r in previous['checks'] if r['result'] == 'PASS'}
    assert tls.SERIAL == '61c9a964' and tls.adb('shell', 'getprop', 'ro.product.device').strip() == b'alioth'
    assert tls.adb('shell', 'getprop', 'ro.build.version.sdk').strip() == b'36'
    assert not (ctypes.windll.kernel32.GetLogicalDrives() & (1 << 15))
    with socket.socket() as available:
        available.bind(('127.0.0.1', chain.RC_PORT))
    assert f'inet {chain.PHONE_IP}/24' in tls.adb('shell', 'ip', '-4', 'addr', 'show', 'wlan0').decode()
    installed = tls.adb('shell', 'pm', 'path', 'com.phonebridge').decode().strip().removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
    assert tls.adb('shell', 'sha256sum', installed).decode().split()[0] == chain.APK_SHA
    assert chain.digest(tls.RCLONE) == chain.RCLONE_SHA
    chain.RUN_ID = run_id
    chain.REMOTE = '/sdcard/Music/' + run_id
    chain.RUN = original / ('explorer-check-' + time.strftime('%H%M%S'))
    chain.RUN.mkdir(exist_ok=False)
    chain.ROWS = []
    for test, name in [('CHAIN-04-phone-to-pc', 'phone-100MB.bin'), ('CHAIN-05-pc-to-phone', 'pc-upload-100MB.bin')]:
        row = next(r for r in previous['checks'] if r['test'] == test and r['file'] == name)
        assert chain.phone_info(name) == {'bytes': row['bytes'], 'sha256': row['sha256']}
    der = tls.adb('exec-out', 'run-as', 'com.phonebridge', 'cat', tls.CERT)
    assert hashlib.sha256(der).hexdigest() == previous['checks'][0]['identity_certificate_sha256']
    ca = chain.RUN / 'paired-identity.pem'
    ca.write_text(ssl.DER_cert_to_PEM_cert(der), encoding='ascii')
    tls.RUN = chain.RUN / 'host'; tls.RUN.mkdir(); tls.ROWS = []
    before = chain.metadata_snapshot()
    expected_names = ['pc-upload-100MB.bin', 'phone-100MB.bin', 'phone-origin.txt', 'renamed-folder']
    try:
        with chain.phone_service_host('explorer-readonly'):
            env = tls.clean_env()
            obscure = subprocess.run([str(tls.RCLONE), 'obscure', '-'], input=tls.password().encode(),
                                     capture_output=True, timeout=10, check=True, env=env).stdout.decode().strip()
            env.update(RCLONE_WEBDAV_URL=f'https://{chain.PHONE_IP}:8273/{run_id}/', RCLONE_WEBDAV_USER='phonebridge',
                       RCLONE_WEBDAV_PASS=obscure, RCLONE_RC_USER='p0', RCLONE_RC_PASS=chain.RC_SECRET)
            args = [str(tls.RCLONE), 'mount', ':webdav:', 'P:', '--read-only', '--config', 'NUL', '--ca-cert', str(ca),
                    '--webdav-vendor', 'other', '--network-mode', '--volname', 'PhoneBridge P0-010', '--no-console',
                    '--rc', '--rc-addr', f'127.0.0.1:{chain.RC_PORT}', '--contimeout', '5s', '--timeout', '15s',
                    '--log-level', 'INFO', '--log-file', str(chain.RUN / 'rclone.log')]
            process = subprocess.Popen(args, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                       creationflags=subprocess.CREATE_NO_WINDOW)
            try:
                deadline = time.monotonic() + 20
                while not (chain.DRIVE / 'phone-origin.txt').is_file():
                    assert process.poll() is None and time.monotonic() < deadline
                    time.sleep(0.25)
                assert sorted(p.name for p in chain.DRIVE.iterdir()) == expected_names
                chain.record('readonly_explorer_ready', drive='P:', names=expected_names, readonly=True,
                             observation_file=str(chain.RUN / 'explorer-observation.json'), pid=process.pid)
                deadline = time.monotonic() + 120
                while not (chain.RUN / 'explorer-observation.json').exists() and time.monotonic() < deadline:
                    time.sleep(0.5)
                assert (chain.RUN / 'explorer-observation.json').exists(), 'Visual confirmation not received within 120 seconds'
                observation = json.loads((chain.RUN / 'explorer-observation.json').read_text(encoding='utf-8'))
                assert observation['observed'] is True and observation['drive'] == 'P:'
                chain.record('CHAIN-03-explorer', **observation)
            finally:
                if process.poll() is None:
                    chain.rc('core/quit')
                code = process.wait(timeout=15)
                assert code == 0 and not Path('P:/').exists()
                chain.record('CHAIN-06-readonly-unmount', exit_code=code, drive_absent=True)
    finally:
        assert chain.metadata_snapshot() == before
        assert 'isForeground=true' not in tls.adb('shell', 'dumpsys', 'activity', 'services', 'com.phonebridge').decode()
        assert not tls.adb('forward', '--list').strip()
        chain.record('CHAIN-06-readonly-phone-cleanup', original_metadata_unchanged=True, sharing_stopped=True, adb_forwards=0)


if __name__ == '__main__':
    main()
