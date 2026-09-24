"""Full wire interop of the product cores via pipes, using only fixed synthetic short codes."""
import base64, hashlib, json, queue, subprocess, threading, time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'.audit/p1-004';OUT.mkdir(exist_ok=True)
DOTNET=ROOT/'.audit/tools/dotnet-10.0.401/dotnet.exe'
JAVA=ROOT/'.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110/bin/java.exe'
DLL=ROOT/'windows/tests/PhoneBridge.PairingFixture/bin/Release/net10.0-windows10.0.19041.0/PhoneBridge.PairingFixture.dll'
CP=(ROOT/'android/pairing-core/build/test-classpath.txt').read_text()
CA=ROOT/'tests/integration/windows_mount/wrong-ca.pem'
ORDER=[1,2,17,18,33,34,49,50]
cases=[dict(name=f'full-handshake-{i}',code='00123456') for i in range(3)]
cases += [dict(name='leading-zero',code='00000000'),dict(name='wrong-code',code='00123456',wrong=True)]
for index in range(8):
    for mutation in ['magic','type','length','trailing','truncated']:
        cases.append(dict(name=f'{mutation}-frame-{index+1}',code='00123456',index=index,mutation=mutation))
for name,index,mutation in [('ca-bound',1,'ca'),('port-bound',1,'port'),('attempt-bound',1,'attempt'),
    ('suite-unknown',0,'suite'),('window-mismatch',0,'window'),('client-mismatch',1,'client'),
    ('reflection',3,'reflection'),('replay',2,'replay'),('gx2-zero',2,'zero'),('gx2-one',2,'one'),
    ('scalar-out-of-range',2,'scalar'),('round2-proof',4,'bit'),('client-mac',6,'bit'),('server-mac',7,'bit')]:
    cases.append(dict(name=name,code='00123456',index=index,mutation=mutation))

rows=[];sample=[]
for case in cases:
    started=time.monotonic(); children=[]; queues=[]; threads=[]; frames=[]; rejection=False; forced=False; success=False
    commands=[[str(DOTNET),str(DLL),case['code']],[str(JAVA),'-cp',CP,'org.phonebridge.pairing.InteropHost',
              '00123457' if case.get('wrong') else case['code'],str(CA)]]
    try:
        for cmd in commands:
            child=subprocess.Popen(cmd,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
            q=queue.Queue()
            def read(c=child,d=q):
                for line in c.stdout:d.put(line.strip())
                d.put('EOF')
            thread=threading.Thread(target=read);thread.start();children.append(child);queues.append(q);threads.append(thread)
        for index in range(8):
            sender=index%2; line=queues[sender].get(timeout=15)
            if line=='REJECT':rejection=True;break
            raw=bytearray(base64.b64decode(line,validate=True));assert raw[:4]==b'PBP1' and raw[4]==ORDER[index]
            frames.append(bytes(raw))
            if index==case.get('index'):
                mutation=case['mutation']
                if mutation=='magic':raw[0]^=1
                elif mutation=='type':raw[4]^=1
                elif mutation=='length':raw[5:9]=b'\xff'*4
                elif mutation=='trailing':raw+=b'\0'
                elif mutation=='truncated':raw=raw[:-1]
                elif mutation=='ca':raw[62]^=1
                elif mutation=='port':raw[58]^=1
                elif mutation=='attempt':raw[42]^=1
                elif mutation=='suite':raw[9]=2
                elif mutation=='window':raw[10]^=1
                elif mutation=='client':raw[26]^=1
                elif mutation=='reflection':raw=bytearray(frames[2]);raw[4]=18
                elif mutation=='replay':raw=bytearray(base64.b64decode(sample[2]))
                elif mutation in ('zero','one'):raw[393:777]=bytes(384);raw[776]=int(mutation=='one')
                elif mutation=='scalar':raw[1161:1193]=b'\xff'*32
                elif mutation=='bit':raw[-1]^=1
                else:raise AssertionError('unknown mutation')
            children[1-sender].stdin.write(base64.b64encode(raw).decode()+'\n');children[1-sender].stdin.flush()
        if not rejection:
            results=[q.get(timeout=15) for q in queues]
            success=results[0].startswith('OK:') and results[0]==results[1]
            rejection='REJECT' in results
        positive=not case.get('wrong') and 'mutation' not in case
        assert (success and not rejection) if positive else (rejection and not success),case['name']
    finally:
        for child in children:
            child.stdin.close()
            try:child.wait(timeout=3)
            except subprocess.TimeoutExpired:forced=True;child.kill();child.wait(timeout=3)
        for thread in threads:thread.join(timeout=3)
        for child in children:
            assert not child.stderr.read(),'unexpected stderr'
            child.stdout.close();child.stderr.close()
    exits=[p.returncode for p in children]
    assert not forced and ((exits==[0,0]) if positive else (2 in exits and all(c in (0,2) for c in exits)))
    rows.append(dict(test=case['name'],passed=True,expected='confirmed' if positive else 'rejected',
                     exit_codes=exits,forced_cleanup=forced,seconds=round(time.monotonic()-started,3)))
    if case['name']=='full-handshake-0':sample=[base64.b64encode(f).decode() for f in frames]
    print(json.dumps(rows[-1]),flush=True)
(OUT/'core-interop-results.json').write_text(json.dumps(dict(scope='full-core-wire-over-pipes-not-network',
    cases=rows),indent=2),encoding='utf-8')
(OUT/'core-public-frames.json').write_text(json.dumps(dict(synthetic=True,frames_base64=sample),indent=2),encoding='utf-8')
print(json.dumps(dict(passed=len(rows),failures=0)))
