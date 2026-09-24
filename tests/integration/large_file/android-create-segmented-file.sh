#!/system/bin/sh
set -eu

target="${1:-}"
case "$target" in
  /storage/emulated/0/DCIM/PhoneBridge-P1-016/*.bin) ;;
  *) echo "unsafe target" >&2; exit 2 ;;
esac

if [ -e "$target" ]; then echo "target exists" >&2; exit 3; fi
temporary="${target}.tmp.$$"
trap 'rm -f "$temporary"' EXIT HUP INT TERM

dd if=/dev/zero of="$temporary" bs=1000000 count=1000 2>/dev/null
i=0
while [ "$i" -lt 1000 ]; do
  printf 'PBNG-P1-016-PHONE-%04d\n' "$i" | \
    dd of="$temporary" bs=1 seek=$((i * 1000000)) conv=notrunc 2>/dev/null
  i=$((i + 1))
done
sync "$temporary"
size="$(stat -c %s "$temporary")"
if [ "$size" != "1000000000" ]; then echo "wrong size: $size" >&2; exit 4; fi
mv "$temporary" "$target"
trap - EXIT HUP INT TERM
sha256sum "$target"
