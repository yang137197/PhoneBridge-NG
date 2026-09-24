"""Check fixed design vectors and document/contract agreement; not an implementation security proof."""
import argparse, hashlib, hmac, json, re, struct
from pathlib import Path
root=Path(__file__).resolve().parents[3];here=Path(__file__).parent
c=json.loads((here/'contract.json').read_text());doc=(root/'docs/PAIRING.md').read_text(encoding='utf-8')
checks=[]
def check(name,value): checks.append(dict(test=name,passed=bool(value)));assert value,name
def kdf(v):
    ikm=bytes.fromhex(v['ikm_hex']);salt=bytes.fromhex(v['salt_hex'])
    info=v['info_ascii'].encode() if 'info_ascii' in v else bytes.fromhex(v['info_hex'])
    prk=hmac.new(salt,ikm,hashlib.sha256).digest();out=b'';block=b'';counter=1
    while len(out)<v['length']:
        block=hmac.new(prk,block+info+bytes([counter]),hashlib.sha256).digest();out+=block;counter+=1
    return out[:v['length']].hex()
check('rfc5869-independent-known-answer',kdf(c['rfc5869_case1'])==c['rfc5869_case1']['expected_okm_hex'])
check('profile-kdf-vector',kdf(c['grant_kdf_vector'])==c['grant_kdf_vector']['expected_okm_hex'])
v=c['hello_vector'];frames=[bytes.fromhex(v[k]) for k in ['client_frame_hex','server_frame_hex']]
check('hello-frame-bounds',all(f[:4]==b'PBP1' and int.from_bytes(f[5:9],'big')==len(f)-9<=8192 for f in frames))
check('hello-field-binding',frames[0][4]==1 and frames[1][4]==2 and frames[0][9]==frames[1][9]==1 and frames[0][10:42]==frames[1][10:42])
check('hello-context',hashlib.sha256(b''.join(frames)).hexdigest()==v['context_sha256'])
check('role-separation',v['windows_participant_id']=='pbng-pair-v1:W:'+v['context_sha256'] and v['android_participant_id']=='pbng-pair-v1:A:'+v['context_sha256'])
check('fixed-signed-mac',all(int(x['signed_integer']).to_bytes(32,'big',signed=True).hex()==x['bytes_hex'] for x in c['signed_mac_vectors']))
check('wire-round-sizes', [f['bytes'] for f in c['frames'][2:]]==[1600,1600,800,800,32,32] and all(str(n) in doc for n in [1600,800,8192,32768]))
check('scope-status-not-product-pass',len(c['acceptance'])==22 and all(
    r['status']=='required-not-run-on-product' or
    (r['status']=='partial-android-app-tested' and bool(r.get('evidence')) and bool(r.get('remaining')))
    for r in c['acceptance']))
check('case-identifiers-unique',len({r['id'] for r in c['acceptance']})==22)
check('required-lifecycle-coverage',{'negative','positive','storage','lifecycle','authorization','limits','compatibility'}<={r['category'] for r in c['acceptance']})
parser=argparse.ArgumentParser();parser.add_argument('--output',default='.audit/p1-003/contract-results.json')
out=root/parser.parse_args().output;out.parent.mkdir(exist_ok=True,parents=True)
out.write_text(json.dumps(dict(scope='design-vector-consistency-only',checks=checks),indent=2),encoding='utf-8')
print(json.dumps(dict(passed=len(checks),failures=0,product_pairing_verified=False)))
