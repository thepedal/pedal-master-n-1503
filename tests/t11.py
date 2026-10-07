# Output against the previous release, for settings that release also had.
# usage: python3 t11.py <harness built from the previous release> [label]
# (build one with: git show v0.3.1:native/PedalMasterN.cpp > /tmp/old.cpp, then compile harness.cpp against it.
#  Run from this folder: the harness needs ../native/sdk/MachineInterface.h, which run_all.sh fetches.)
import numpy as np, subprocess, time, sys
from lib import *
ref_bin=sys.argv[1] if len(sys.argv)>1 else None
label=sys.argv[2] if len(sys.argv)>2 else 'previous release'
sr=48000; rng=np.random.default_rng(7); n=10*sr
t=np.arange(n)/sr
L=FS*0.3*(np.sin(2*np.pi*55*t)+0.5*rng.standard_normal(n)*np.exp(-((t*4)%1)*20)); R=np.roll(L,37)*0.9
cases={'defaults':{}, 'limiting +12':dict(LIMGAIN=120), 'everything on':dict(LIMGAIN=60,COMP=1,THRESH=40,LOWMONO=40,LOWCUT=20,TILT=70,SCHPF=2,MATCH=1),
       'limiter off, comp off':dict(LIMITER=0), 'comp switched on mid-way':dict(at=[(5*sr,'COMP',1)],THRESH=40),
       'limiter switched off mid-way':dict(at=[(5*sr,'LIMITER',0)],LIMGAIN=120),
       'limiter switched on mid-way':dict(at=[(5*sr,'LIMITER',1)],LIMITER=0,LIMGAIN=120),
       'EQ: shelves, tilt, low cut':dict(LOWGAIN=180,HIGHGAIN=80,TILT=90,LOWCUT=30,LOWFREQ=30),
       'EQ: low shelf swept mid-way':dict(LOWGAIN=200,at=[(5*sr,'LOWFREQ',90)])}
def runbin(binary,L,R,kw):
    import lib; old=lib.run
    x=np.empty(2*len(L),np.float32); x[0::2]=L; x[1::2]=R; x.tofile('/tmp/in.f32')
    args=[binary,'/tmp/in.f32','/tmp/out.f32','/tmp/m.txt',str(sr),'256']
    for k,v in kw.items():
        if k!='at': args.append(f'{P[k]}={v}')
    for fr,name,val in kw.get('at',[]): args.append(f'@{fr}:{P[name]}={val}')
    t0=time.time(); subprocess.run(args,capture_output=True); dt=time.time()-t0
    y=np.fromfile('/tmp/out.f32',np.float32).astype(np.float64); return y, dt
for name,kw in cases.items():
    a,ta=runbin('./harness',L,R,kw)
    if ref_bin:
        b,tb=runbin(ref_bin,L,R,kw)
        d=np.max(np.abs(a-b)); print(f'{name:30s} max difference vs {label}: {d:.2e} raw units ({db(d/FS) if d>0 else -999:.0f} dBFS)   time {tb:.2f}s → {ta:.2f}s')
