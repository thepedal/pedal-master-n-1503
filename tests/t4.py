from lib import *
rng=np.random.default_rng(1)
def sigs(sr):
    n=2*sr; t=np.arange(n)/sr
    out={}
    out['sine 1k']=(sine(1000,-6,2,sr,0.3),)*2
    out['sine fs/4 45deg']=(sine(sr/4,-6,2,sr,np.pi/4),)*2
    out['sine 0.4fs']=(sine(0.4*sr,-6,2,sr,0.7),)*2
    w=rng.standard_normal((2,n))*FS*0.15; out['white noise']=(w[0],w[1])
    # kick-like transients + bass + hats
    k=np.zeros(n); 
    for s0 in range(0,n,sr//2):
        m=min(n-s0,sr//4); tt=np.arange(m)/sr
        k[s0:s0+m]+=np.sin(2*np.pi*(50+200*np.exp(-tt*40))*tt)*np.exp(-tt*12)
    h=rng.standard_normal(n)*np.exp(-((t*8)%1)*30)*0.3
    mixL=FS*0.5*(k+0.4*np.sin(2*np.pi*55*t)+h); mixR=FS*0.5*(k+0.4*np.sin(2*np.pi*55*t+0.5)-h)
    out['drums+bass']=(mixL,mixR)
    sq=np.sign(np.sin(2*np.pi*441*t))*FS*0.5; out['square 441']=(sq,sq)
    return out
for sr in (44100,48000):
    for ceil_v,ceil_db in ((50,-1.0),(60,0.0),(30,-3.0)):
        worst=-99; 
        for name,(L,R) in sigs(sr).items():
            for gain in (0,120,240):
                Lo,Ro,lat,rows=run(L,R,sr,LIMGAIN=gain,CEILING=ceil_v)
                a=slice(lat+sr//10,None)
                t=db(max(tp(Lo[a]),tp(Ro[a]))/FS); worst=max(worst,t-ceil_db)
                if ceil_v==60 and sr==48000 and t-ceil_db>0.03: print(f'{sr} {name:16s} gain+{gain/10:4.1f}  out TP {t:7.2f} dBTP  (ceiling {ceil_db})  sample pk {db(max(np.max(np.abs(Lo[a])),np.max(np.abs(Ro[a])))/FS):6.2f}  limGR max {max(float(r[14]) for r in rows):5.2f}')
        print(f'== sr {sr} ceiling {ceil_db}: worst overshoot {worst:+.3f} dB')
