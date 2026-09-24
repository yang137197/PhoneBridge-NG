"""No phone: bounded stdin and sanitized error checks for the experimental diagnostic."""
import json
from pathlib import Path
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
run = ROOT / '.audit/runs/P1-002' / ('input-' + uuid.uuid4().hex[:12])
run.mkdir(parents=True)
command = [str(ROOT / '.audit/tools/dotnet-10.0.401/dotnet.exe'), str(ROOT /
    'windows/src/PhoneBridge.Mounting.Diagnostics/bin/Release/net10.0-windows10.0.19041.0/PhoneBridge.Mounting.Diagnostics.dll')]
checks = []
for name, value, expected in [('eof', '', 'input-missing'), ('invalid-json', 'synthetic-secret\n', 'diagnostic-failed'),
                               ('oversized', 'x' * 32768, 'input-too-long')]:
    child = subprocess.run(command, input=value, capture_output=True, text=True, timeout=10,
                           creationflags=subprocess.CREATE_NO_WINDOW)
    events = [json.loads(line) for line in child.stdout.splitlines()]
    passed = child.returncode == 2 and not child.stderr and events[0]['result']['code'] == expected
    assert 'synthetic-secret' not in child.stdout + child.stderr
    checks.append(dict(test=name, passed=passed)); assert passed
print(json.dumps(dict(input_cases=checks, deadline_test_started=True)), flush=True)
started = time.monotonic()
child = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                         text=True, creationflags=subprocess.CREATE_NO_WINDOW)
try:
    child.wait(timeout=135)  # deliberately leave stdin open without a line
    elapsed = time.monotonic() - started
    events = [json.loads(line) for line in child.stdout.read().splitlines()]
    passed = child.returncode == 2 and not child.stderr.read() and 119 <= elapsed <= 130 and events[-1]['result']['State'] == 'Idle'
    checks.append(dict(test='silent-stdin-deadline', passed=passed, elapsed_seconds=round(elapsed, 3)))
finally:
    if child.poll() is None: child.kill(); child.wait(timeout=5)
    child.stdin.close(); child.stdout.close(); child.stderr.close()
(run / 'results.json').write_text(json.dumps(dict(checks=checks), indent=2), encoding='utf-8')
assert all(c['passed'] for c in checks)
print(json.dumps(dict(run=str(run), checks=checks)), flush=True)
