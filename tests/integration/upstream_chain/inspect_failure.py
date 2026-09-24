import hashlib, json, pathlib
from start_android import RUN, adb

result = json.loads((RUN / 'chain-results.json').read_text())
data = pathlib.Path(result['data_directory'])
source = data / 'phone-origin-100MB.bin'
download = data / 'downloaded-phone-origin-100MB.bin'
with source.open('rb') as left, download.open('rb') as right:
    position = 0
    while True:
        a, b = left.read(1024*1024), right.read(1024*1024)
        if a != b:
            first = position + next(i for i, (x, y) in enumerate(zip(a, b)) if x != y)
            break
        if not a:
            first = None
            break
        position += len(a)
    left.seek(0)
    initial = left.read(1024)
    right.seek(32*1024*1024)
    at_chunk_boundary = right.read(1024)
report = {
    'source_bytes': source.stat().st_size,
    'download_bytes': download.stat().st_size,
    'android_sha256': adb('shell', 'sha256sum', '/sdcard/Download/phone-origin-100MB.bin').decode().split()[0],
    'source_sha256': hashlib.file_digest(source.open('rb'), 'sha256').hexdigest(),
    'download_sha256': hashlib.file_digest(download.open('rb'), 'sha256').hexdigest(),
    'first_different_byte': first,
    'download_at_32MiB_matches_source_start': at_chunk_boundary == initial,
}
(RUN / 'download-failure.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
