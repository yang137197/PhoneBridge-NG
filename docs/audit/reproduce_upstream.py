"""Offline audit probes for the pinned upstream, not product regression tests.

Usage: python reproduce_upstream.py PATH_TO_UPSTREAM
Network/process launches by upstream code are mocked; only temporary fake data
is written. A daemon thread intentionally reproduces a lock deadlock and ends
when this short-lived Python process exits. Exit 0 means findings reproduced,
NOT that the upstream is safe or that the Android/Explorer chain works.
"""

from __future__ import annotations

import json
import logging
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import traceback
from unittest.mock import MagicMock, patch

EXPECTED_COMMIT = "a378fec40561a4d18be2334f4de92ee02a0e7d0c"
upstream = Path(sys.argv[1]).resolve()
commit = subprocess.check_output(
    ["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True
).strip()
if commit != EXPECTED_COMMIT:
    raise SystemExit(f"Unexpected upstream commit: {commit}")
if subprocess.check_output(
    ["git", "-C", str(upstream), "status", "--porcelain", "--untracked-files=no"],
    text=True,
).strip():
    raise SystemExit("Upstream tracked files have changed")
sys.path.insert(0, str(upstream / "desktop"))
sys.dont_write_bytecode = True
logging.disable(logging.CRITICAL)

from phonebridge.certpin import verify_fingerprint
from phonebridge.config import ConfigManager, PhoneConfig
from phonebridge.discovery import DiscoveredPhone, PhoneDiscoveryListener, PhoneScanner
from phonebridge.mounter import MountInfo, MountManager


def phone():
    return DiscoveredPhone(
        service_name="Audit._phonebridge._tcp.local.", display_name="Audit fixture",
        ip_address="192.0.2.10", port=8273, device_model="fixture", version="2",
    )


results = []


def record(name, evidence):
    results.append({"probe": name, "reproduced": True, "evidence": evidence})


with tempfile.TemporaryDirectory(prefix="phonebridge-audit-") as temp:
    path = Path(temp) / "config.json"
    config = ConfigManager(path)
    config.upsert_phone(PhoneConfig("fixture", "Fixture", auth_password="AUDIT_FAKE_SECRET"))
    assert json.loads(path.read_text(encoding="utf-8"))["phones"]["fixture"]["auth_password"] == "AUDIT_FAKE_SECRET"
    record("plaintext_config", "Synthetic credential survives as plaintext JSON; temp data removed")

with patch("phonebridge.certpin.get_server_fingerprint", return_value=None):
    assert verify_fingerprint("192.0.2.10", 8273, "AA:BB") == (True, None)
    record("fingerprint_failure_allows", "Actual helper returns (True, None) when probe fails")

mock_process = MagicMock()
mock_process.poll.return_value = None
manager = MountManager(rclone_path="AUDIT_MOCK_ONLY")
with patch.object(manager, "is_server_reachable", return_value=True), \
        patch.object(manager, "_obscure_password", return_value="AUDIT_OBSCURED"), \
        patch("phonebridge.mounter.subprocess.Popen", return_value=mock_process) as popen, \
        patch("phonebridge.mounter.time.sleep"):
    mounted = manager.mount(phone(), "P:", "phonebridge", "AUDIT_FAKE_SECRET")
    args = popen.call_args.args[0]
    assert "--no-check-certificate" in args
    assert "--webdav-pass=AUDIT_OBSCURED" in args
    assert mounted.is_alive
    record("mount_without_tls_or_drive_proof", "Mock process alive alone yields mounted state; TLS bypass present in argv; no real rclone launched")

info = MagicMock()
info.parsed_addresses.return_value = ["192.0.2.10"]
info.port = 8273
info.properties = {b"protocol": b"http", b"auth_required": b"true"}
zc = MagicMock()
zc.get_service_info.return_value = info
discovered = PhoneDiscoveryListener()._resolve_service(zc, "_phonebridge._tcp.local.", phone().service_name)
assert discovered.webdav_url == "http://192.0.2.10:8273"
record("mdns_plain_http_accepted", "Mock TXT protocol=http is accepted by actual resolver; no mDNS packets sent")

found = []
scanner = PhoneScanner(on_found=found.append)
first = phone()
scanner._handle_found(first)
changed = phone()
changed.ip_address = "192.0.2.11"
scanner._handle_updated(changed)
assert len(found) == 1 and scanner.get_phones()[first.device_id].ip_address == "192.0.2.11"
record("ip_update_no_found_callback", "Without on_updated wiring, changed IP updates discovery data only")

# Two mount calls cross the initial duplicate check and drive check before
# either process is entered into _mounts. The barriers make this deterministic.
manager = MountManager(rclone_path="AUDIT_MOCK_ONLY")
reachable_barrier = threading.Barrier(2)
spawn_barrier = threading.Barrier(2)
outcomes, errors = [], []


def reachable(*args, **kwargs):
    reachable_barrier.wait(timeout=3)
    return True


def spawn(*args, **kwargs):
    spawn_barrier.wait(timeout=3)
    proc = MagicMock()
    proc.poll.return_value = None
    return proc


def mount_concurrently():
    try:
        outcomes.append(manager.mount(phone(), "P:"))
    except Exception as error:
        errors.append(repr(error))


with patch.object(manager, "is_server_reachable", side_effect=reachable), \
        patch("phonebridge.mounter.subprocess.Popen", side_effect=spawn) as popen, \
        patch("phonebridge.mounter.time.sleep"):
    workers = [threading.Thread(target=mount_concurrently, daemon=True) for _ in range(2)]
    for worker in workers:
        worker.start()
    for worker in workers:
        worker.join(timeout=4)
    assert not errors and not any(worker.is_alive() for worker in workers), errors
    assert len(outcomes) == 2 and popen.call_count == 2 and len(manager._mounts) == 1
    record("duplicate_mount_race", "Two mocked processes started for same device/drive; only one remains tracked")

# Actual non-reentrant lock, actual stale mount path; no mock lock or translated
# implementation. Stack inspection distinguishes deadlock from slow scheduling.
manager = MountManager(rclone_path="AUDIT_MOCK_ONLY")
stale = MagicMock()
stale.poll.return_value = 1
fixture = phone()
manager._mounts[fixture.device_id] = MountInfo(
    fixture.device_id, fixture.display_name, "P:", fixture.webdav_url, process=stale
)
entered = threading.Event()


def stale_mount():
    entered.set()
    manager.mount(fixture, "P:")


worker = threading.Thread(target=stale_mount, daemon=True)
worker.start()
assert entered.wait(timeout=1)
worker.join(timeout=0.5)
stack = traceback.extract_stack(sys._current_frames()[worker.ident]) if worker.is_alive() else []
assert manager._lock.locked() and any(frame.name == "_cleanup_mount" for frame in stack), stack
record("stale_mount_deadlock", "mount holds Lock then _cleanup_mount blocks acquiring the same Lock; confirmed by live thread stack")

print(json.dumps({"commit": commit, "scope": "offline Python only", "results": results}, indent=2))
