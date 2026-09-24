# Large-file acceptance helpers

`New-DeterministicFile.ps1` writes an exact-length PC sample in 1,000,000-byte blocks using a distinct deterministic seed per block and reports the SHA-256 computed during streaming. It writes to a same-directory temporary file and only moves the complete, flushed file into place.

`android-create-segmented-file.sh` is deliberately restricted to the disposable `DCIM/PhoneBridge-P1-016` directory. It creates an exact 1,000,000,000-byte phone sample and writes a distinct marker at the start of every 1,000,000-byte segment before computing the hash on the phone.

`android-create-segmented-file-5gb.sh` is separately restricted to the disposable `Music/PhoneBridge-P1-017/phone-to-pc-5GB.bin` target. It creates an exact 5,000,000,000-byte phone sample with 5,000 distinct one-megabyte segment markers and computes SHA-256 on the phone. The separate fixed target keeps the P1-016 evidence helper immutable and avoids a general-purpose phone writer.

`android-create-segmented-file-10gb.sh` is restricted to the disposable `Music/PhoneBridge-P1-018/phone-to-pc-10GB.bin` target. It applies the same sparse allocation and per-megabyte marker strategy to an exact 10,000,000,000-byte sample, with all offsets expressed as decimal strings to avoid Android shell integer overflow.

`android-create-segmented-file-20gb.sh` is restricted to the disposable `Music/PhoneBridge-P1-020/phone-to-pc-20GB.bin` target. It creates the exact decimal 20,000,000,000-byte boundary sample with 20,000 distinct one-megabyte segment markers and computes SHA-256 on the phone.

These helpers generate test data only. They do not start sharing, mount a drive, change permissions, use real user files, or treat a Windows cache hash as proof of phone persistence.
