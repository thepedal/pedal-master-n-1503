# The GUI's C# LoudnessMeter (compiled with Mono) against EBU 3341 (I) and 3342 (LRA),
# fed with the machine's own output blocks.
from lib import *
import subprocess, os
def cs(segs,sr):
    x=np.concatenate([sine(1000,d,t,sr) for d,t in segs]); L,R,lat,rows=run(x,x,sr,LIMITER=0)
    open('/tmp/blocks.txt','w').write('\n'.join(t[1:] for r in rows for t in r if t.startswith('b')))
    return [float(v) for v in subprocess.run(['mono','/tmp/lt.exe','/tmp/blocks.txt'],capture_output=True,text=True).stdout.split()]
for sr in (44100,48000):
    I=[cs(s,sr)[0] for s in ([(-23,20)],[(-33,20)],[(-36,10),(-23,60),(-36,10)],[(-72,10),(-36,10),(-23,60),(-36,10),(-72,10)],[(-26,20),(-20,20.1),(-26,20)])]
    R=[cs(s,sr)[1] for s in ([(-20,20),(-30,20)],[(-20,20),(-15,20)],[(-40,20),(-20,20)],[(-50,20),(-35,20),(-20,20),(-35,20),(-50,20)])]
    print(sr,'C# I (EBU 3341 1-5):',I,' LRA (EBU 3342 1-4):',R)
