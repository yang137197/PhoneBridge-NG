import http.client, json, re, ssl, time, xml.etree.ElementTree as ET
from start_android import adb, assert_installed_apk, RUN, TASK_ID, AVD_NAME, FORWARD_PORT

assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode(), 'Dedicated P0 AVD required'
if TASK_ID in {'P0-006', 'P0-007'}:
    assert_installed_apk()
adb('shell', 'am', 'start', '-n', 'com.phonebridge/.MainActivity')
time.sleep(1)
adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
ui = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
status = next(n.attrib.get('text') for n in ui.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/tvStatus')
if status == 'Running':
    button = next(n for n in ui.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/btnToggle')
    x1, y1, x2, y2 = map(int, re.findall(r'\d+', button.attrib['bounds']))
    adb('shell', 'input', 'tap', str((x1+x2)//2), str((y1+y2)//2))
    time.sleep(1)
if TASK_ID in {'P0-006', 'P0-007'}:
    adb('shell', 'uiautomator', 'dump', '/sdcard/p0-window.xml')
    stopped_ui = ET.fromstring(adb('exec-out', 'cat', '/sdcard/p0-window.xml'))
    assert next(n.attrib.get('text') for n in stopped_ui.iter('node') if n.attrib.get('resource-id') == 'com.phonebridge:id/tvStatus') == 'Tap to start'
    connection = http.client.HTTPSConnection('127.0.0.1', FORWARD_PORT, context=ssl._create_unverified_context(), timeout=2)
    try:
        connection.request('OPTIONS', '/')
        connection.getresponse()
    except (OSError, http.client.HTTPException):
        pass
    else:
        raise AssertionError('Server still listening after UI stop; keep emulator for investigation')
    finally:
        connection.close()
log = adb('logcat', '-d', '-s', 'PhoneBridgeService:I', 'TlsHelper:I', 'NsdAdvertiser:I', 'WebDavServer:W', 'AndroidRuntime:E').decode(errors='replace')
(RUN / 'android-service.log').write_text(log, encoding='utf-8')
if TASK_ID in {'P0-006', 'P0-007'}:
    assert 'FATAL EXCEPTION' not in log
adb('forward', '--remove', f'tcp:{FORWARD_PORT}')
adb('emu', 'kill')
result = {'previous_ui_status': status, 'stop_button_pressed': status == 'Running', 'adb_forward_removed': True, 'emulator_shutdown_acknowledged': True, 'cache_and_avd_preserved': True}
if TASK_ID in {'P0-006', 'P0-007'}:
    result.update({'stopped_ui_confirmed': True, 'https_unavailable_after_stop': True, 'no_fatal_exception_in_service_log': True})
    (RUN / 'cleanup.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
