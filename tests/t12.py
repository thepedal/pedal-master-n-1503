# v0.4: Low Dip and Low Shape, measured through the machine against the analytic
# filter design (the same state-variable shelf and bell, evaluated exactly).
import math
from lib import *

def svf(f,fs,g,k,m0,m1,m2):
    s=1j*np.tan(np.pi*f/fs)/g; D=s*s+k*s+1
    return m0+m1*s/D+m2/D
def shelf(f,fs,fc,dbg,k):
    A=10**(dbg/40); g=np.tan(np.pi*fc/fs)/np.sqrt(A)
    return svf(f,fs,g,k,1,k*(A-1),A*A-1)
def bell(f,fs,fc,dbg,q):
    A=10**(dbg/40); g=np.tan(np.pi*fc/fs); k=1/(q*A)
    return svf(f,fs,g,k,1,k*(A*A-1),0)
def lowfreq(v): return 25.0*(400/25)**(v/100)
KC,KV=math.sqrt(2),1/1.2

F=[20,30,45,60,90,120,180,240,360,500,1000]
def check(name,sr,expect,**kw):
    meas=[gain_at(f,sr,**kw) for f in F]
    exp=[20*np.log10(abs(expect(f))) for f in F]
    err=max(abs(m-e) for m,e in zip(meas,exp))
    print(f'{sr} {name:34s} '+' '.join(f'{m:6.2f}' for m in meas)+f'   max error {err:.3f} dB')
    return err

print('Hz'+' '*41+' '.join(f'{f:6d}' for f in F))
worst=0
for sr in (44100,48000):
    fc=lowfreq(30)                                       # ≈ 57.4 Hz (Low Freq 30)
    worst=max(worst,check('Low Dip 4 dB (Low Freq %.0f Hz)'%fc,sr,lambda f:bell(f,sr,3*fc,-4,0.9),LOWFREQ=30,LOWDIP=40))
    worst=max(worst,check('Vintage shelf +6 dB',sr,lambda f:shelf(f,sr,fc,6,KV),LOWFREQ=30,LOWGAIN=180,LOWSHAPE=1))
    worst=max(worst,check('Clean shelf +6 dB',sr,lambda f:shelf(f,sr,fc,6,KC),LOWFREQ=30,LOWGAIN=180))
    worst=max(worst,check('Vintage +6 dB with Low Dip 4 dB',sr,lambda f:shelf(f,sr,fc,6,KV)*bell(f,sr,3*fc,-4,0.9),LOWFREQ=30,LOWGAIN=180,LOWSHAPE=1,LOWDIP=40))
print('worst deviation from the design: %.3f dB'%worst)
print('EQ off ignores Low Dip and Low Shape: %.3f dB at 180 Hz'%gain_at(180,48000,EQ=0,LOWFREQ=30,LOWDIP=60,LOWSHAPE=1,LOWGAIN=240))
print('Low Dip follows Low Freq: centre at Low Freq 100 Hz →',
      max(((gain_at(f,48000,LOWDIP=60),f) for f in (200,250,300,350,400)))[1] and
      min((gain_at(f,48000,LOWDIP=60),f) for f in (200,250,280,300,320,350,400))[1],'Hz (expected 300)')
