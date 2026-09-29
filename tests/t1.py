# Transparency at defaults, latency, and bypass.
# v0.3: an always-on 5 Hz DC blocker sits at the input, so the defaults are a
# delayed copy of the input above ~20 Hz (tiny phase shift), not bit-exact.
from lib import *
for sr in (44100,48000):
    x=sine(997,-12,2.0,sr); y=sine(331,-18,2.0,sr)
    L,R,lat,rows=run(x,y,sr)
    a=slice(sr//2,None)
    def resid(o,i): d=o[lat:][sr//2:]-i[:len(i)-lat][sr//2:]; return db(np.sqrt(np.mean(d**2))/np.sqrt(np.mean(i**2)))
    print(sr,'latency',lat,'expected',int(0.002*sr+0.5)+15,
          ' residual vs delayed input: 997 Hz %.1f dB, 331 Hz %.1f dB'%(resid(L,x),resid(R,y)))
    L,R,lat,_=run(x,y,sr,BYPASS=1,LIMGAIN=120,TILT=120,COMP=1)
    print('  bypass null (raw units)',np.max(np.abs(L[lat:]-x[:len(x)-lat])))
