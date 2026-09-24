#!/system/bin/sh
set -eu

target="${1:-}"
expected="/storage/emulated/0/Music/PhoneBridge-P1-018/phone-to-pc-10GB.bin"
if [ "$target" != "$expected" ]; then echo "unsafe target" >&2; exit 2; fi
if [ -e "$target" ]; then echo "target exists" >&2; exit 3; fi

temporary="${target}.tmp.$$"
trap 'rm -f "$temporary"' EXIT HUP INT TERM

# Allocate the exact logical length without spending another 10 GB of flash writes.
# Reads of the sparse regions return zeroes; the transfer and final hash still cover all 10 GB.
dd if=/dev/zero of="$temporary" bs=1 count=1 seek=9999999999 2>/dev/null
i=0
while [ "$i" -lt 10000 ]; do
  seek="${i}000000"
  printf 'PBNG-P1-018-PHONE-%05d\n' "$i" | \
    dd of="$temporary" bs=1 seek="$seek" conv=notrunc 2>/dev/null
  i=$((i + 1))
  if [ $((i % 1000)) -eq 0 ]; then echo "markers=$i" >&2; fi
done
sync "$temporary"
size="$(stat -c %s "$temporary")"
if [ "$size" != "10000000000" ]; then echo "wrong size: $size" >&2; exit 4; fi
mv "$temporary" "$target"
trap - EXIT HUP INT TERM
sha256sum "$target"
