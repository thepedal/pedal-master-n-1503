from lib import *
import math
sr=48000
def side_mid(f,**kw):
    # L-only source: M = S = x/2. Report output S and M levels relative to input M/S, and M/S phase agreement.
    x=sine(f,-20,1.0,sr); z=np.zeros_like(x)
    L,R,lat,_=run(x,z,sr,**kw)
    M=(L+R)/2; S=(L-R)/2; a=slice(sr//2,None)
    ref=np.sqrt(np.mean((x/2)**2))
    return round(db(np.sqrt(np.mean(M[a]**2))/ref),2), round(db(np.sqrt(np.mean(S[a]**2))/ref),2), round(db(np.sqrt(np.mean((M[a]-S[a])**2))/ref),1)
v=round(1+99*math.log(120/40)/math.log(300/40))
print('Low Mono 120 Hz (v=%d): f -> (M dB, S dB, |M-S| dB)'%v)
for f in (30,60,120,240,1000,5000): print('  ',f,side_mid(f,LOWMONO=v))
print('Width 0  @1k:',side_mid(1000,WIDTH=0))
print('Width 200 @1k:',side_mid(1000,WIDTH=200))
print('Width 100 @1k:',side_mid(1000))
