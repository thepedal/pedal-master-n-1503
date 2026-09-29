from lib import *
sr=48000; x=sine(220,-12,1.0,sr); y=sine(330,-12,1.0,sr)
def click(changes,**kw):
    L,R,lat,_=run(x,y,sr,at=changes,**kw)
    d2=np.abs(np.diff(L,2)); base=np.max(d2[lat+2000:24000-100])
    return np.max(d2[24000:])/base
cases={'Bypass on':[(24000,'BYPASS',1)],'Limiter off (at 6 dB GR)':[(24000,'LIMITER',0)],'EQ off (low +12 dB)':[(24000,'EQ',0)],
       'Comp on (10 dB GR)':[(24000,'COMP',1)],'Low Mono on 300 Hz':[(24000,'LOWMONO',100)],'Width 100→200':[(24000,'WIDTH',200)],
       'Low gain 0→+12 dB':[(24000,'LOWGAIN',240)],'Input 0→+12 dB':[(24000,'INPUT',360)],'Low cut on 250 Hz':[(24000,'LOWCUT',100)]}
extra={'Limiter off (at 6 dB GR)':dict(LIMGAIN=180),'EQ off (low +12 dB)':dict(LOWGAIN=240,LIMITER=0),'Comp on (10 dB GR)':dict(THRESH=20,RATIO=5,LIMITER=0)}
for k,c in cases.items(): print(f'{k:28s} max |Δ²| after change / steady = {click(c,**extra.get(k,{})):.2f}')
