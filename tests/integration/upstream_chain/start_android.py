import hashlib, http.client, json, os, pathlib, re, ssl, subprocess, time, xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[3] / '.audit'
TASK_ID = os.environ.get('PHONEBRIDGE_AUDIT_TASK', 'P0-003')
if TASK_ID not in {'P0-003', 'P0-004', 'P0-005', 'P0-006', 'P0-007'}:
    raise ValueError('Unsupported audit task')
TEST_API = int(os.environ.get('PHONEBRIDGE_AUDIT_API', '34'))
if TEST_API not in {26, 34, 36} or (TEST_API == 26 and TASK_ID not in {'P0-006', 'P0-007'}) or (TEST_API == 36 and TASK_ID != 'P0-007'):
    raise ValueError('Unsupported audit API/task combination')
RUN = ROOT / 'runs' / TASK_ID
if TASK_ID in {'P0-006', 'P0-007'}:
    RUN = RUN / f'api{TEST_API}'
VARIANT = os.environ.get('PHONEBRIDGE_AUDIT_VARIANT', 'repair')
if VARIANT not in {'baseline', 'repair'} or (VARIANT == 'baseline' and TASK_ID != 'P0-007'):
    raise ValueError('Unsupported audit variant')
if TASK_ID == 'P0-007':
    RUN = RUN / VARIANT
ADB = ROOT / 'tools/android-sdk/platform-tools/adb.exe'
SERIAL = {26: 'emulator-5558', 34: 'emulator-5556', 36: 'emulator-5560'}[TEST_API]
AVD_NAME = {26: 'PhoneBridgeP0Api26', 34: 'PhoneBridgeP0', 36: 'PhoneBridgeP0Api36'}[TEST_API]
FORWARD_PORT = {26: 18275, 34: 18273, 36: 18277}[TEST_API]
SOURCE_TASK = 'P0-006' if VARIANT == 'baseline' else TASK_ID
APK = ROOT / ('upstream' if SOURCE_TASK == 'P0-003' else SOURCE_TASK.lower() + '-worktree') / 'android/app/build/outputs/apk/debug/app-debug.apk'

def adb(*args, data=None, timeout=45):
    result = subprocess.run([str(ADB), '-s', SERIAL, *args], input=data, capture_output=True, timeout=timeout)
    if result.returncode:
        # Do not include commands/stdout/stderr: a future call may handle a secret.
        raise RuntimeError(f'adb operation failed with exit {result.returncode}')
    return result.stdout

def auth_password():
    prefs = ET.fromstring(adb('exec-out', 'run-as', 'com.phonebridge', 'cat', 'shared_prefs/phonebridge_prefs.xml'))
    return next(node.text for node in prefs if node.attrib.get('name') == 'auth_password')

def assert_installed_apk():
    paths = adb('shell', 'pm', 'path', 'com.phonebridge').decode().splitlines()
    assert len(paths) == 1 and paths[0].startswith('package:'), 'Expected one test APK'
    path = paths[0].removeprefix('package:')
    assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk', path), 'Unexpected APK location'
    actual = adb('shell', 'sha256sum', path).decode().split()[0]
    with APK.open('rb') as stream:
        expected = hashlib.file_digest(stream, 'sha256').hexdigest()
    assert actual == expected, 'Installed APK does not match the selected task; stop'
    return expected

if __name__ == '__main__':
    assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1', 'Disposable emulator required'
    assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode(), 'Dedicated P0 AVD required'
    assert int(adb('shell', 'getprop', 'ro.build.version.sdk').strip()) == TEST_API, 'Unexpected Android version'
    RUN.mkdir(parents=True, exist_ok=True)
    assert APK.is_file(), 'APK missing'
    print(json.dumps({'apk_sha256': hashlib.file_digest(APK.open('rb'), 'sha256').hexdigest()}), flush=True)
    install_args = ['install', '-r'] if TASK_ID != 'P0-003' else ['install']
    print(adb(*install_args, str(APK)).decode().strip(), flush=True)
    assert_installed_apk()
    adb('shell', 'am', 'force-stop', 'com.phonebridge')
    adb('shell', 'mkdir', '-p', '/sdcard/Download')
    adb('shell', 'run-as', 'com.phonebridge', 'mkdir', '-p', 'shared_prefs')
    adb('shell', 'run-as', 'com.phonebridge', 'sh', '-c', "'cat > shared_prefs/phonebridge_prefs.xml'", data=b'<map><string name="shared_folder">downloads</string></map>')
    if TEST_API >= 30:
        adb('shell', 'appops', 'set', 'com.phonebridge', 'MANAGE_EXTERNAL_STORAGE', 'allow')
    else:
        for permission in ['READ_EXTERNAL_STORAGE', 'WRITE_EXTERNAL_STORAGE']:
            adb('shell', 'pm', 'grant', 'com.phonebridge', 'android.permission.' + permission)
    if TEST_API >= 33:
        adb('shell', 'pm', 'grant', 'com.phonebridge', 'android.permission.POST_NOTIFICATIONS')
    adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity')
    time.sleep(2)
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    ui = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    button = next(node for node in ui.iter('node') if node.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle')
    x1, y1, x2, y2 = map(int, re.findall(r'\d+', button.attrib['bounds']))
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    adb('forward', f'tcp:{FORWARD_PORT}', 'tcp:8273')
    context = ssl._create_unverified_context()  # Only this disposable upstream baseline, loopback ADB transport.
    for attempt in range(40):
        try:
            connection = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=context, timeout=3)
            connection.connect()
            fingerprint = hashlib.sha256(connection.sock.getpeercert(binary_form=True)).hexdigest()
            connection.request('OPTIONS', '/')
            response = connection.getresponse()
            response.read()
            status = response.status
            connection.close()
            assert status == 401, f'Unexpected unauthenticated status {status}'
            print(json.dumps({'https': True, 'unauthenticated_status': status, 'certificate_sha256': fingerprint, 'serial': SERIAL, 'transport': 'ADB loopback; not LAN discovery'}), flush=True)
            break
        except (ConnectionError, OSError) as exc:
            if attempt == 39:
                raise RuntimeError(f'HTTPS start failed: {type(exc).__name__}') from None
            time.sleep(0.5)
