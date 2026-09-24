import base64, ctypes, hashlib, http.client, json, os, pathlib, secrets, shutil, socket, subprocess, time
from start_android import adb, auth_password, ROOT, RUN

RCLONE = ROOT / 'tools/rclone-v1.75.1-windows-amd64/rclone.exe'
DRIVE = pathlib.Path('P:/')
RC_PORT = 15579
rc_password = secrets.token_urlsafe(32)
results = {'transport': 'Android 14 emulator via ADB loopback', 'tls_mode': 'unverified isolated upstream experiment', 'checks': []}

def record(name, **kwargs):
    row = {'name': name, **kwargs}
    results['checks'].append(row)
    (RUN / 'chain-results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(json.dumps(row), flush=True)

def sha(path):
    with pathlib.Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def rc(endpoint):
    conn = http.client.HTTPConnection('127.0.0.1', RC_PORT, timeout=10)
    header = base64.b64encode(('p0:' + rc_password).encode()).decode()
    conn.request('POST', '/' + endpoint, body=b'{}', headers={'Authorization': 'Basic ' + header, 'Content-Type': 'application/json'})
    response = conn.getresponse()
    body = response.read()
    conn.close()
    if response.status != 200:
        raise RuntimeError(f'RC {endpoint} failed: {response.status}')
    return json.loads(body)

def android_sha(name):
    # Controlled ASCII filenames only; no shell metacharacters.
    output = adb('shell', 'sha256sum', '/sdcard/Download/' + name).decode()
    return output.split()[0]

def fixture(path, size, seed):
    if path.exists():
        raise RuntimeError('Refusing to overwrite existing fixture')
    with path.open('xb') as stream:
        left = size
        counter = 0
        while left:
            part = hashlib.shake_256(seed + counter.to_bytes(8, 'big')).digest(min(left, 1024*1024))
            stream.write(part)
            left -= len(part)
            counter += 1

if __name__ == '__main__':
    assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1', 'Disposable emulator required'
    assert adb('emu', 'avd', 'name').splitlines()[0] == b'PhoneBridgeP0', 'Dedicated P0 AVD required'
    assert not (ctypes.windll.kernel32.GetLogicalDrives() & (1 << 15)), 'P: already occupied'
    with socket.socket() as check:
        check.bind(('127.0.0.1', RC_PORT))
    run_id = time.strftime('%Y%m%d-%H%M%S')
    data = RUN / ('data-' + run_id)
    data.mkdir()
    files = [data / 'phone-origin-100MB.bin', data / 'pc-origin-100MB.bin']
    fixture(files[0], 100_000_000, b'phone-origin-P0-003')
    fixture(files[1], 100_000_000, b'pc-origin-P0-003')
    text_file = data / 'phone-origin.txt'
    text_file.write_text('PhoneBridge P0-003 synthetic UTF-8: 中文测试\n', encoding='utf-8')
    for path in (files[0], text_file):
        adb('push', str(path), '/sdcard/Download/' + path.name)
        assert android_sha(path.name) == sha(path)
    password = auth_password()
    obscured = subprocess.run([str(RCLONE), 'obscure', '-'], input=(password+'\n').encode(), capture_output=True, check=True, timeout=10).stdout.decode().strip()
    env = {key: value for key, value in os.environ.items() if not key.upper().startswith('RCLONE_')}
    env.update(RCLONE_WEBDAV_PASS=obscured, RCLONE_RC_USER='p0', RCLONE_RC_PASS=rc_password)
    cache = data / 'cache'
    args = [str(RCLONE), 'mount', ':webdav:', 'P:', '--webdav-url', 'https://127.0.0.1:18273/', '--webdav-user', 'phonebridge', '--webdav-vendor', 'other', '--no-check-certificate', '--config', 'NUL', '--cache-dir', str(cache), '--vfs-cache-mode', 'full', '--vfs-cache-max-age', '1h', '--vfs-read-chunk-size', '32M', '--dir-cache-time', '5s', '--poll-interval', '0', '--vfs-write-back', '0s', '--network-mode', '--volname', 'PhoneBridge P0 emulator', '--no-console', '--rc', '--rc-addr', '127.0.0.1:' + str(RC_PORT), '--log-level', 'INFO', '--log-file', str(data / 'rclone.log')]
    process = subprocess.Popen(args, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW)
    results['rclone_pid'] = process.pid
    results['data_directory'] = str(data)
    writes_started = False
    upload_confirmed = False
    try:
        for _ in range(120):
            if process.poll() is not None:
                raise RuntimeError('rclone exited before drive ready; see isolated log')
            if DRIVE.exists() and (DRIVE / text_file.name).is_file():
                break
            time.sleep(0.5)
        else:
            raise RuntimeError('Drive readiness timeout')
        record('mount', result='PASS', drive='P:', directory=sorted(p.name for p in DRIVE.iterdir()))
        for source in (files[0], text_file):
            dest = data / ('downloaded-' + source.name)
            shutil.copyfile(DRIVE / source.name, dest)
            expected = android_sha(source.name)
            actual = sha(dest)
            record('download-hash', result='PASS' if actual == expected else 'FAIL', file=source.name, bytes=dest.stat().st_size, android_sha256=expected, pc_sha256=actual)
            assert actual == expected, 'Download hash mismatch'
            record('phone-to-pc', result='PASS', file=source.name, bytes=dest.stat().st_size, android_sha256=expected, pc_sha256=actual)
        target_name = 'uploaded-' + run_id + '.bin'
        writes_started = True
        with files[1].open('rb') as source, (DRIVE / target_name).open('xb') as target:
            shutil.copyfileobj(source, target, 1024*1024)
        expected = sha(files[1])
        stable = 0
        for _ in range(120):
            stats = rc('vfs/stats')['diskCache']
            if stats['uploadsInProgress'] == 0 and stats['uploadsQueued'] == 0:
                try:
                    actual = android_sha(target_name)
                except RuntimeError:
                    actual = None
                stable = stable + 1 if actual == expected else 0
                if stable >= 3:
                    break
            else:
                stable = 0
            time.sleep(0.5)
        else:
            raise RuntimeError('Upload did not settle; preserve cache')
        actual = android_sha(target_name)
        assert actual == expected, 'Independent Android upload hash mismatch'
        upload_confirmed = True
        record('pc-to-phone', result='PASS', file=target_name, bytes=100_000_000, pc_sha256=expected, android_sha256=actual)
        folder_name = 'new-folder-' + run_id
        (DRIVE / folder_name).mkdir()
        renamed = folder_name + '-renamed'
        (DRIVE / folder_name).rename(DRIVE / renamed)
        assert adb('shell', 'test', '-d', '/sdcard/Download/' + renamed) == b''
        record('mkdir-rename', result='PASS', directory=renamed)
        # Explorer is opened for human visual verification. Launching alone does not pass that case.
        subprocess.Popen(['explorer.exe', 'P:\\'])
        record('explorer', result='UNVERIFIED', detail='Opened P: for user observation; no visual assertion')
        stats = rc('vfs/stats')['diskCache']
        assert stats['uploadsInProgress'] == 0 and stats['uploadsQueued'] == 0 and stats['erroredFiles'] == 0
        record('pending-writes', result='PASS', uploadsInProgress=0, uploadsQueued=0, erroredFiles=0)
        # Keep the drive briefly for independent PowerShell/Explorer verification, then cleanly quit.
        for _ in range(90):
            if (RUN / 'finish-chain').exists():
                break
            time.sleep(1)
        rc('core/quit')
        exit_code = process.wait(timeout=15)
        record('unmount', result='PASS' if not DRIVE.exists() and exit_code == 0 else 'FAIL', exit_code=exit_code, drive_present=DRIVE.exists())
    except Exception as exc:
        record('failure', result='FAIL', kind=type(exc).__name__, detail=str(exc))
        # Stop only when the RC state confirms no pending or errored writes; always preserve cache.
        try:
            state = rc('vfs/stats')['diskCache']
            if (not writes_started or upload_confirmed) and state['uploadsInProgress'] == 0 and state['uploadsQueued'] == 0 and state['erroredFiles'] == 0:
                rc('core/quit')
                record('failure-cleanup', result='PASS', exit_code=process.wait(timeout=15), cache_preserved=True)
            else:
                record('failure-cleanup', result='BLOCKED', cache_preserved=True)
        except Exception:
            record('failure-cleanup', result='BLOCKED', cache_preserved=True)
        raise
