from lib import *
for sr in (44100,48000):
    x=sine(997,-12,1.0,sr); y=sine(331,-18,1.0,sr)
    L,R,lat,rows=run(x,y,sr)
    err=max(np.max(np.abs(L[lat:]-x[:len(x)-lat])),np.max(np.abs(R[lat:]-y[:len(y)-lat])))
    print(sr,'latency',lat,'expected',int(0.002*sr+0.5)+15,'null err (raw units)',err)
    # bypass null
    L,R,lat,_=run(x,y,sr,BYPASS=1,LIMGAIN=120,TILT=120,COMP=1)
    print('  bypass null',np.max(np.abs(L[lat:]-x[:len(x)-lat])))
