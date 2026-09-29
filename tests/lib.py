import numpy as np, subprocess, os
from scipy.signal import resample_poly, freqz
FS=32768.0
P=dict(INPUT=0,EQ=1,LOWCUT=2,LOWFREQ=3,LOWGAIN=4,HIGHFREQ=5,HIGHGAIN=6,TILT=7,COMP=8,THRESH=9,RATIO=10,
       ATTACK=11,RELEASE=12,SCHPF=13,MAKEUP=14,MIX=15,LOWMONO=16,WIDTH=17,LIMITER=18,LIMGAIN=19,CEILING=20,LIMREL=21,BYPASS=22)
def run(L,R,sr=48000,blk=256,**kw):
    x=np.empty(2*len(L),np.float32); x[0::2]=L; x[1::2]=R
    x.tofile('/tmp/in.f32')
    args=['./harness','/tmp/in.f32','/tmp/out.f32','/tmp/m.txt',str(sr),str(blk)]
    for k,v in kw.items():
        if k.startswith('at'):   # at=[(frame,name,val),...]
            continue
        args.append(f'{P[k]}={v}')
    for fr,name,val in kw.get('at',[]): args.append(f'@{fr}:{P[name]}={val}')
    out=subprocess.run(args,capture_output=True,text=True).stdout
    lat=int(out.split()[1])
    y=np.fromfile('/tmp/out.f32',np.float32).astype(np.float64)
    rows=[l.split() for l in open('/tmp/m.txt')]
    return y[0::2],y[1::2],lat,rows
def db(x): return 20*np.log10(np.maximum(x,1e-12))
def tp(x,os_=16,edge=2000):
    u=resample_poly(x,os_,1); return np.max(np.abs(u[edge*os_:-edge*os_]))
def sine(f,dbfs,secs,sr=48000,ph=0.0): 
    t=np.arange(int(secs*sr))/sr; return FS*10**(dbfs/20)*np.sin(2*np.pi*f*t+ph)
def gain_at(f,sr=48000,**kw):
    x=sine(f,-20,1.0,sr); L,R,lat,_=run(x,x,sr,**kw)
    a=L[sr//2:]; b=x[sr//2-lat:len(x)-lat]
    return db(np.sqrt(np.mean(a**2))/np.sqrt(np.mean(b**2)))
