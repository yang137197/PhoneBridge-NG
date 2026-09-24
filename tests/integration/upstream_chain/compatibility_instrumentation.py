"""Run and parse instrumented tests; adb exit 0 alone never means the tests passed."""
import hashlib
import json
from start_android import adb, assert_installed_apk, ROOT, RUN, TASK_ID, TEST_API, AVD_NAME

assert TASK_ID in {'P0-006', 'P0-007'}
assert adb('shell', 'getprop', 'ro.kernel.qemu').strip() == b'1'
assert adb('emu', 'avd', 'name').splitlines()[0] == AVD_NAME.encode()
apk_hash = assert_installed_apk()
test_apk = ROOT / (TASK_ID.lower() + '-worktree/android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk')
adb('install', '-r', str(test_apk))
# Preserve earlier crash evidence. API 26 logcat -c can fail at clearing the main
# buffer; clearing device logs is not a prerequisite for instrumented tests.
before = adb('logcat', '-d', '-s', 'AndroidRuntime:E').decode(errors='replace')
(RUN / 'pre-instrumentation-crash-log.txt').write_text(before, encoding='utf-8')
assert 'FATAL EXCEPTION' not in before, 'Investigate existing emulator crash evidence before running tests'
output = adb('shell', 'am', 'instrument', '-w', '-r', 'com.phonebridge.test/androidx.test.runner.AndroidJUnitRunner', timeout=180).decode(errors='replace')
(RUN / 'instrumentation.log').write_text(output, encoding='utf-8')
rows = []
name = suite = None
for line in output.splitlines():
    if line.startswith('INSTRUMENTATION_STATUS: class='):
        suite = line.split('=', 1)[1]
    if line.startswith('INSTRUMENTATION_STATUS: test='):
        name = line.split('=', 1)[1]
    if line.startswith('INSTRUMENTATION_STATUS_CODE: '):
        code = int(line.split(': ', 1)[1])
        if code != 1:
            rows.append({'suite': suite, 'name': name, 'result': {0: 'PASS', -4: 'SKIP'}.get(code, 'FAIL'), 'code': code})
result = {'api': TEST_API, 'apk_sha256': apk_hash, 'test_apk_sha256': hashlib.sha256(test_apk.read_bytes()).hexdigest(), 'tests': rows}
(RUN / 'instrumentation-results.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2), flush=True)
assert len(rows) == (17 if TASK_ID == 'P0-007' else 15) and 'FAILURES!!!' not in output and 'INSTRUMENTATION_FAILED' not in output
assert all(row['result'] == 'PASS' or (row['name'] == 'hardLinkedFilesAreRejected' and row['result'] == 'SKIP') for row in rows)
