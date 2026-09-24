"""Only dedicated API 26/36 AVDs. Installs NG plus test APK; does not touch the P0 app."""
import argparse, hashlib, json, re, subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ADB = ROOT / '.audit/tools/android-sdk/platform-tools/adb.exe'
TARGETS = {26: ('emulator-5558','PhoneBridgeP0Api26'), 36: ('emulator-5560','PhoneBridgeP0Api36')}
PACKAGE = 'org.phonebridge.ng'
RUNNER = PACKAGE + '.test/org.phonebridge.ng.BridgeTestRunner'

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--api',type=int,choices=TARGETS,required=True); parser.add_argument('--label',required=True)
    args=parser.parse_args(); assert re.fullmatch('[a-z0-9-]{1,32}',args.label)
    serial,avd=TARGETS[args.api]; run=ROOT/f'.audit/p1-007/api{args.api}-{args.label}';run.mkdir(parents=True,exist_ok=False)
    def adb(*items,timeout=45):
        p=subprocess.run([str(ADB),'-s',serial,*items],capture_output=True,timeout=timeout)
        if p.returncode:raise RuntimeError('ADB exit '+str(p.returncode))
        return p.stdout.decode('utf-8',errors='replace')
    assert adb('shell','getprop','ro.kernel.qemu').strip()=='1'
    assert adb('emu','avd','name').splitlines()[0]==avd
    assert adb('shell','getprop','ro.build.version.sdk').strip()==str(args.api)
    assert adb('shell','getprop','sys.boot_completed').strip()=='1'
    hashes={}
    try:
        for rel,package in [('debug/app-debug.apk',PACKAGE),('androidTest/debug/app-debug-androidTest.apk',PACKAGE+'.test')]:
            apk=ROOT/'android/app/build/outputs/apk'/rel; digest=hashlib.sha256(apk.read_bytes()).hexdigest()
            assert 'Success' in adb('install','-r',str(apk),timeout=90)
            installed=adb('shell','pm','path',package).strip().removeprefix('package:')
            assert re.fullmatch(r'/data/app/[A-Za-z0-9/_.+=~-]+/base\.apk',installed)
            assert adb('shell','sha256sum',installed).split()[0]==digest;hashes[package]=digest
        classes='org.phonebridge.ng.ServiceTests,org.phonebridge.credentials.StoreTests'
        with (run/'instrumentation.log').open('wb') as output:
            result=subprocess.run([str(ADB),'-s',serial,'shell','am','instrument','-w','-r','-e','class',classes,RUNNER],stdout=output,stderr=subprocess.STDOUT,timeout=600)
        text=(run/'instrumentation.log').read_text(encoding='utf-8',errors='replace'); rows=[]; current=None
        for line in text.splitlines():
            if line.startswith('INSTRUMENTATION_STATUS: test='):current=line.split('=',1)[1]
            if line.startswith('INSTRUMENTATION_STATUS_CODE: '):
                code=int(line.split(': ',1)[1])
                if code not in (1,2):rows.append(dict(test=current,code=code,passed=code==0))
        expected=sum(len(re.findall(r'@Test fun ',(ROOT/p).read_text(encoding='utf-8'))) for p in [
            'android/app/src/androidTest/java/org/phonebridge/ng/ServiceTests.kt',
            'android/credentials-store/src/androidTest/java/org/phonebridge/credentials/StoreTests.kt'])
        summary=dict(api=args.api,avd=avd,apk_hashes=hashes,adb_exit=result.returncode,expected=expected,tests=rows,
            passed=sum(x['passed'] for x in rows),total=len(rows),real_phone_access=False)
        (run/'results.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
        print(json.dumps(dict(api=args.api,passed=summary['passed'],total=len(rows),expected=expected)),flush=True)
        assert result.returncode==0 and len(rows)==expected and all(x['passed'] for x in rows) and f'OK ({expected} tests)' in text
    finally:
        adb('shell','am','force-stop',PACKAGE)

if __name__=='__main__':main()
