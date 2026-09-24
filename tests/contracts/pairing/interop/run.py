"""Relay only synthetic J-PAKE public payloads; no phone, sockets, user keys or credentials."""
import base64, hashlib, json, queue, subprocess, threading, time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[4]
AUDIT=ROOT/'.audit/p1-003'
DOTNET=ROOT/'.audit/tools/dotnet-10.0.401/dotnet.exe'
JAVA=ROOT/'.audit/tools/jdk/jbrsdk_jcef-21.0.10-windows-x64-b1163.110/bin/java.exe'
DLL=Path(__file__).parent/'dotnet/bin/Release/net10.0/PairingInterop.dll'
CASES=[('same-code-'+str(i),'00123456','00123456',None) for i in range(8)]+[
 ('leading-zero-code','00000000','00000000',None),('wrong-code','00123456','00123457',None),
 ('context-ca-or-window-altered','00123456','00123456','context'),
 ('zero-group-element','00123456','00123456','gx2'),('modified-proof','00123456','00123456','proof'),
 ('modified-server-confirmation','00123456','00123456','mac')]
rows=[];negative_tags=0;raw_samples=[]
for name,win_code,android_code,mutation in CASES:
    start=time.monotonic()
    context=hashlib.sha256(('synthetic:'+name).encode()).hexdigest()
    peer_context=('0'*64) if mutation=='context' else context
    commands=[[str(DOTNET),str(DLL),win_code,context], [str(JAVA),'-cp',
        str(AUDIT/'java-classes')+';'+str(AUDIT/'bcprov-jdk18on-1.86.jar'),'PairingInterop',android_code,peer_context]]
    children=[];queues=[];threads=[];stages=[];confirmed=False;rejected=False;forced=False
    try:
        for command in commands:
            child=subprocess.Popen(command,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                text=True,creationflags=subprocess.CREATE_NO_WINDOW)
            lines=queue.Queue()
            def read(c=child,q=lines):
                for line in c.stdout:
                    q.put(line.strip())
                q.put('EOF')
            thread=threading.Thread(target=read);thread.start()
            children.append(child);queues.append(lines);threads.append(thread)
        for stage,length in [(1,1600),(2,800),(3,32)]:
            payloads=[q.get(timeout=15) for q in queues]
            if 'REJECT' in payloads:
                rejected=True;break
            decoded=[base64.b64decode(p,validate=True) for p in payloads]
            assert all(len(p)==length for p in decoded), 'unexpected-wire-size'
            if stage==3: negative_tags+=sum(p[0]>=128 for p in decoded)
            if mutation=='gx2' and stage==1: decoded[0]=decoded[0][:384]+bytes(384)+decoded[0][768:]
            if mutation=='proof' and stage==2:
                changed=bytearray(decoded[0]);changed[-1]^=1;decoded[0]=bytes(changed)
            if mutation=='mac' and stage==3:
                changed=bytearray(decoded[1]);changed[0]^=1;decoded[1]=bytes(changed)
            stages.append(stage)
            for i,child in enumerate(children):
                child.stdin.write(base64.b64encode(decoded[1-i]).decode()+'\n');child.stdin.flush()
            if name=='same-code-0': raw_samples.append(dict(stage=stage,windows=payloads[0],android=payloads[1]))
        if not rejected:
            results=[q.get(timeout=15) for q in queues]
            confirmed=results[0].startswith('OK:') and results[0]==results[1]
            rejected='REJECT' in results
        expected_success=mutation is None and win_code==android_code
        passed=confirmed if expected_success else rejected and not confirmed
        rows.append(dict(test=name,passed=passed,expected='confirmed' if expected_success else 'rejected',
                         completed_rounds=stages,seconds=round(time.monotonic()-start,3)))
    finally:
        for child in children:
            child.stdin.close()
            try:child.wait(timeout=3)
            except subprocess.TimeoutExpired:forced=True;child.kill();child.wait(timeout=3)
        for thread in threads:thread.join(timeout=3)
        for child in children:
            assert not child.stderr.read(), 'unexpected-child-stderr'
            child.stdout.close();child.stderr.close()
    exit_codes=[child.returncode for child in children]
    exits_valid=(exit_codes==[0,0]) if expected_success else (2 in exit_codes and all(code in (0,2) for code in exit_codes))
    rows[-1].update(exit_codes=exit_codes,forced_cleanup=forced)
    rows[-1]['passed']=rows[-1]['passed'] and exits_valid and not forced
    print(json.dumps(rows[-1]),flush=True)
    assert rows[-1]['passed'], name
result=dict(scope='offline-library-interop-not-network-or-product',java_bc='1.86',csharp_bc='2.7.0',
            cases=rows,signed_negative_mac_values=negative_tags)
assert negative_tags>0
(AUDIT/'interop-results.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
(AUDIT/'synthetic-public-payloads.json').write_text(json.dumps(dict(context=hashlib.sha256(b'synthetic:same-code-0').hexdigest(),
    code='00123456',public_payloads=raw_samples),indent=2),encoding='utf-8')
print(json.dumps(dict(passed=len(rows),negative_mac_tags=negative_tags)),flush=True)
