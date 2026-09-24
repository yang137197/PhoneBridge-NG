"""C# DPAPI + real Android Activity/Service over isolated AVD forwarding. No secret logs."""
import argparse
import hashlib
import json
import os
import queue
import re
import socket
import subprocess
import threading
import time
from pathlib import Path

from native import ADB, PACKAGE, ROOT, RUNNER, TARGETS


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--api', type=int, choices=TARGETS, required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    assert re.fullmatch('[a-z0-9-]{1,32}', args.label)
    serial, avd = TARGETS[args.api]
    run = ROOT / f'.audit/p1-007/api{args.api}-{args.label}'
    run.mkdir(parents=True, exist_ok=False)
    forwards, redirections, events = [], [], []
    native_process = fixture = output = None
    native_runs = []
    stage, pin, success = 'guard', None, False
    flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0

    def adb(*items, timeout=45):
        result = subprocess.run([str(ADB), '-s', serial, *items], capture_output=True, timeout=timeout, creationflags=flags)
        if result.returncode:
            raise RuntimeError('adb_failed')
        return result.stdout.decode('utf-8', errors='replace')

    def forward(port):
        local = int(adb('forward', 'tcp:0', f'tcp:{port}').strip())
        forwards.append(local)
        return local

    def redirect(port):
        # Emulator router TCP redirection preserves FIN half-close; ADB streams do not.
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 0))
            local = probe.getsockname()[1]
        assert 'OK' in adb('emu', 'redir', 'add', f'tcp:{local}:{port}')
        redirections.append(local)
        return local

    def control(port, action):
        with socket.create_connection(('127.0.0.1', port), timeout=15) as stream:
            stream.sendall((json.dumps({'action': action}) + '\n').encode())
            result = bytearray()
            while not result.endswith(b'\n'):
                chunk = stream.recv(4097 - len(result))
                if not chunk or len(result) + len(chunk) > 4096:
                    raise RuntimeError('control_closed')
                result.extend(chunk)
            return json.loads(result)

    lines = queue.Queue()

    def read_fixture():
        for line in fixture.stdout:
            lines.put(line)
        lines.put(None)

    def receive(expected):
        line = lines.get(timeout=40)
        if line is None:
            raise RuntimeError('fixture_closed')
        value = json.loads(line)
        # The C# host deliberately exposes only this allowlist of non-secret fields.
        assert set(value) <= {'event_name', 'client_id', 'attempt_id', 'ca_sha256', 'windows_state', 'status', 'sha256', 'windows_can_mount', 'stage', 'error_type', 'closed'}
        events.append(value)
        if value['event_name'] != expected:
            raise RuntimeError('fixture_event')
        return value

    def command(action):
        fixture.stdin.write(json.dumps({'action': action}) + '\n')
        fixture.stdin.flush()
        return receive(action)

    def start_android(resume=False):
        nonlocal native_process, output
        number = len(native_runs)
        output = (run / f'instrumentation-{number}.log').open('wb')
        native_process = subprocess.Popen([str(ADB), '-s', serial, 'shell', 'am', 'instrument', '-w', '-r', '-e', 'bridge', 'true', '-e', 'resume', str(resume).lower(), RUNNER], stdout=output, stderr=subprocess.STDOUT, creationflags=flags)
        until = time.monotonic() + 45
        while True:
            try:
                info = control(control_port, 'info')
                if info['ready'] and (resume or info.get('code')):
                    pid = adb('shell', 'pidof', PACKAGE).strip()
                    assert re.fullmatch('[0-9]+', pid) and pid not in [x['pid'] for x in native_runs]
                    native_runs.append(dict(pid=pid, resume=resume, log=f'instrumentation-{number}.log'))
                    return info
            except (OSError, RuntimeError):
                if native_process.poll() is not None:
                    raise RuntimeError('instrumentation_exited') from None
            if time.monotonic() > until:
                raise RuntimeError('ui_start_timeout')
            time.sleep(.2)

    def stop_android():
        assert control(control_port, 'stop')['stopped']
        assert native_process.wait(timeout=20) == 0
        output.close()
        log = (run / native_runs[-1]['log']).read_text(encoding='utf-8', errors='replace')
        assert 'bridge_result=passed' in log and (pin is None or pin not in log)
        adb('shell', 'am', 'force-stop', PACKAGE)

    try:
        assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == '1'
        assert adb('emu', 'avd', 'name').splitlines()[0] == avd
        assert adb('shell', 'getprop', 'ro.build.version.sdk').strip() == str(args.api)
        assert adb('shell', 'getprop', 'sys.boot_completed').strip() == '1'
        stage = 'install'
        hashes = {}
        for rel, package in [('debug/app-debug.apk', PACKAGE), ('androidTest/debug/app-debug-androidTest.apk', PACKAGE + '.test')]:
            apk = ROOT / 'android/app/build/outputs/apk' / rel
            digest = hashlib.sha256(apk.read_bytes()).hexdigest()
            assert 'Success' in adb('install', '-r', str(apk), timeout=90)
            installed = adb('shell', 'pm', 'path', package).strip().removeprefix('package:')
            assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', installed)
            assert adb('shell', 'sha256sum', installed).split()[0] == digest
            hashes[package] = digest
        adb('shell', 'am', 'force-stop', PACKAGE)
        adb('shell', 'mkdir', '-p', '/sdcard/Music')
        if args.api == 26:
            for permission in ['READ_EXTERNAL_STORAGE', 'WRITE_EXTERNAL_STORAGE']:
                adb('shell', 'pm', 'grant', PACKAGE, 'android.permission.' + permission)
        else:
            adb('shell', 'appops', 'set', PACKAGE, 'MANAGE_EXTERNAL_STORAGE', 'allow')
            adb('shell', 'pm', 'grant', PACKAGE, 'android.permission.POST_NOTIFICATIONS')
        # Dedicated blank emulator only: ensure a visible Activity lifecycle for real button clicks.
        adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        adb('shell', 'wm', 'dismiss-keyguard')
        stage = 'ui_start'
        control_port = forward(18190)
        info = start_android()
        assert info['ready'] and re.fullmatch('[0-9]{8}', info['code'])
        pin = info['code']
        pair_port, https_port = redirect(info['pair_port']), forward(info['https_port'])
        stage = 'csharp_pair'
        dll = ROOT / 'windows/tests/PhoneBridge.PairingNetworkFixture/bin/Debug/net10.0-windows10.0.19041.0/PhoneBridge.PairingNetworkFixture.dll'
        assert dll.is_file()
        fixture = subprocess.Popen([str(ROOT / '.audit/tools/dotnet-10.0.401/dotnet.exe'), str(dll)], cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=flags)
        threading.Thread(target=read_fixture, daemon=True).start()
        setup = dict(code=pin, window=info['window'], pair_port=pair_port, https_port=https_port, device_https_port=info['https_port'], store_root=str(run / 'credentials'))
        fixture.stdin.write(json.dumps(setup) + '\n'); fixture.stdin.flush()
        assert receive('pending')['windows_state'] == 'Pending'
        stage = 'preapproval_denied'
        assert command('session')['status'] == 401
        stage = 'ui_approve'
        assert control(control_port, 'approve')['clicked']
        until = time.monotonic() + 10
        while True:
            session = command('session')
            if session['status'] == 200:
                assert session['windows_state'] == 'Active'
                break
            assert session['status'] == 401 and time.monotonic() < until
            time.sleep(.2)
        stage = 'read'
        assert command('read')['status'] == 200
        stage = 'foreground_exit'
        assert control(control_port, 'pause')['paused']
        assert command('grant')['status'] == 401
        assert command('session')['status'] == 200
        stage = 'active_process_restart'
        stop_android(); resumed = start_android(resume=True)
        assert resumed['https_port'] == info['https_port'] and resumed['window'] is None
        assert command('grant')['status'] == 401
        assert command('session')['status'] == 200
        assert command('read')['status'] == 200
        stage = 'self_revoke'
        revoked = command('revoke')
        assert revoked['status'] == 204 and revoked['windows_can_mount'] is False
        assert command('session')['status'] == 401
        stage = 'revoked_process_restart'
        stop_android(); start_android(resume=True)
        assert command('session')['status'] == 401
        stage = 'service_storage_failure'
        assert control(control_port, 'fault')['injected']
        failure = command('failure')
        assert failure['status'] == 503 or failure['closed']
        until = time.monotonic() + 10
        while control(control_port, 'info')['ready']:
            assert time.monotonic() < until
            time.sleep(.1)
        fixture.stdin.write('{"action":"exit"}\n'); fixture.stdin.flush()
        assert fixture.wait(timeout=15) == 0
        assert not fixture.stderr.read()
        stage = 'ui_stop'
        stop_android()
        # ADB forwarding may accept a host socket after the device listener is gone; it must close immediately.
        for port in [https_port, pair_port, control_port]:
            try:
                with socket.create_connection(('127.0.0.1', port), timeout=5) as stream:
                    assert stream.recv(1) == b''
            except (ConnectionResetError, ConnectionRefusedError):
                pass
        success = True
        (run / 'apk-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n', encoding='utf-8')
    except Exception as error:
        # Exceptions from transport or parsing might contain input: record only the fixed stage/type.
        events.append(dict(event_name='driver_failed', stage=stage, error_type=type(error).__name__))
    finally:
        if fixture is not None and fixture.poll() is None:
            fixture.terminate(); fixture.wait(timeout=10)
        if native_process is not None and native_process.poll() is None:
            adb('shell', 'am', 'force-stop', PACKAGE)
            try:
                native_process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                native_process.terminate(); native_process.wait(timeout=10)
        if native_process is not None:
            output.close()
        for port in forwards:
            adb('forward', '--remove', f'tcp:{port}')
        for port in redirections:
            assert 'OK' in adb('emu', 'redir', 'del', f'tcp:{port}')
        adb('shell', 'am', 'force-stop', PACKAGE)
        summary = dict(api=args.api, avd=avd, passed=success, stage=stage, events=events, native_runs=native_runs, real_phone_access=False, transport='emulator TCP redirection for PAKE; ADB loopback for TLS and test control', secret_log_checked=success)
        (run / 'results.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(dict(api=args.api, passed=success, stage=stage)), flush=True)
    return 0 if success else 1


if __name__ == '__main__':
    raise SystemExit(main())
