"""Generate synthetic design vectors. This is an encoding/KDF reference, not pairing service code."""
import base64, hashlib, hmac, json, ssl, struct
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
def frame(kind,body): return b'PBP1'+bytes([kind])+struct.pack('>I',len(body))+body
def hkdf(ikm,salt,info,length):
    prk=hmac.digest(salt,ikm,'sha256'); previous=b''; output=b''
    for index in range(1,(length+31)//32+1):
        previous=hmac.digest(prk,previous+info+bytes([index]),'sha256');output+=previous
    return output[:length]
ca=ssl.PEM_cert_to_DER_cert((ROOT/'tests/integration/windows_mount/wrong-ca.pem').read_text())
window=bytes(range(16));client=bytes(range(16,32));attempt=bytes(range(32,48))
first=frame(1,b'\x01'+window+client)
second=frame(2,b'\x01'+window+client+attempt+struct.pack('>HH',8273,len(ca))+ca)
context=hashlib.sha256(first+second).hexdigest()
ikm=bytes(range(256))+bytes(range(128));salt=bytes(range(32));info=b'PhoneBridge NG|pairing-grant|v1'
requirements=[
 ('PAIR-01','positive','Real Windows and Android same 8-digit code, including leading zeros, approve and mount with new token'),
 ('PAIR-02','negative','Wrong code and forged/mutated CA or Hello field cannot create trust or send a long-term token'),
 ('PAIR-03','negative','Invalid group elements, proofs, MAC, reflection and cross-window replay terminate once'),
 ('PAIR-04','negative','Oversized, duplicate, reordered, truncated or fragmented frames obey exact bounds and deadline'),
 ('PAIR-05','lifecycle','Five global attempts, one concurrent handshake, 120-second monotonic expiry, no automatic reopen'),
 ('PAIR-06','lifecycle','Stop, leave pairing page, restart, interface/IP change invalidate pending code and grants'),
 ('PAIR-07','negative','Grant cannot authorize files, another attempt or another client; duplicate Authorization rejected'),
 ('PAIR-08','negative','Strict HTTPS rejects wrong CA, expired cert, wrong IP SAN, redirect and identity replacement'),
 ('PAIR-09','storage','DPAPI write failure sends no token; mutation, wrong user and decryption failure prohibit mount'),
 ('PAIR-10','storage','Keystore key loss or corrupted/missing registry fails closed; no SharedPreferences fallback'),
 ('PAIR-11','lifecycle','Same POST is idempotent; changed token/name/client conflicts without replacement'),
 ('PAIR-12','lifecycle','Approval and cancel race leaves one observable state; lost response reconciles using saved token'),
 ('PAIR-13','lifecycle','Crash before send, before Android commit, after Android commit and before Windows Active commit'),
 ('PAIR-14','negative','Unapproved or expired attempt never grants file access, and old experimental Basic is rejected'),
 ('PAIR-15','lifecycle','Two computers have independent tokens; revoking one preserves the other; duplicate client cannot overwrite'),
 ('PAIR-16','lifecycle','Revoke blocks new requests and active write commit; old file remains intact; storage failure stops sharing without claiming durable revocation; acknowledged revocation survives restart'),
 ('PAIR-17','storage','Offline revoke stays pending; forgetting locally does not claim remote revocation'),
 ('PAIR-18','storage','No cloud/D2D restoration of pairing materials; reboot first-unlock and app reinstallation boundaries'),
 ('PAIR-19','negative','Config, URL, command line, logs, dumps and diagnostics do not contain synthetic secrets'),
 ('PAIR-20','authorization','Server enforces safe/default and per-device permission independently of rclone flags'),
 ('PAIR-21','limits','At most 16 Active clients, 64 KiB encrypted record set and strict JSON/UTF-8 limits'),
 ('PAIR-22','compatibility','Version 2 discovery stays experimental; v3 cannot silently downgrade or import old credentials'),
]
contract=dict(profile='PBNG-PAIR-1',status='design-not-product-implementation',suite=1,group='BC-NIST_3072',digest='SHA-256',
    libraries=dict(csharp='BouncyCastle.Cryptography 2.7.0',java='org.bouncycastle:bcprov-jdk18on:1.86'),
    limits=dict(window_seconds=120,attempts=5,concurrent_handshakes=1,handshake_seconds=30,frame_io_seconds=5,
                frame_payload_bytes=8192,session_bytes=32768,ca_der_bytes=4096,active_clients=16,encrypted_record_bytes=65536),
    frames=[dict(type=t,direction=d,bytes=n) for t,d,n in [(1,'W-A',33),(2,'A-W','53+ca_der'),(17,'W-A',1600),
        (18,'A-W',1600),(33,'W-A',800),(34,'A-W',800),(49,'W-A',32),(50,'A-W',32)]],
    hello_vector=dict(synthetic=True,ca_der_base64=base64.b64encode(ca).decode(),client_frame_hex=first.hex(),
        server_frame_hex=second.hex(),context_sha256=context,windows_participant_id='pbng-pair-v1:W:'+context,
        android_participant_id='pbng-pair-v1:A:'+context),
    signed_mac_vectors=[dict(bytes_hex=b.hex(),signed_integer=str(int.from_bytes(b,'big',signed=True)))
                        for b in [bytes(32),b'\x7f'+b'\xff'*31,b'\x80'+bytes(31),b'\xff'*32]],
    rfc5869_case1=dict(ikm_hex='0b'*22,salt_hex='000102030405060708090a0b0c',info_hex='f0f1f2f3f4f5f6f7f8f9',length=42,
        expected_okm_hex='3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865'),
    grant_kdf_vector=dict(scope='KDF only; synthetic IKM is not a valid PAKE session',ikm_hex=ikm.hex(),salt_hex=salt.hex(),
        info_ascii=info.decode(),length=32,expected_okm_hex=hkdf(ikm,salt,info,32).hex()),
    acceptance=[dict(id=i,category=c,requirement=r,status='required-not-run-on-product') for i,c,r in requirements])
# Keep observed task evidence separate from deterministic protocol vectors.
previous = json.loads((HERE/'contract.json').read_text(encoding='utf-8')) if (HERE/'contract.json').exists() else {}
for item in contract['acceptance']:
    old = next((x for x in previous.get('acceptance', []) if x['id'] == item['id']), {})
    if old.get('status') == 'partial-android-app-tested' and old.get('evidence'):
        item.update(status=old['status'], evidence=old['evidence'], remaining=old['remaining'])
if any(x['status'] == 'partial-android-app-tested' for x in contract['acceptance']):
    contract['status'] = 'partial-android-app-integration'
(HERE/'contract.json').write_text(json.dumps(contract,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(vector_file='tests/contracts/pairing/contract.json',acceptance_cases=len(requirements),context_sha256=context)))
