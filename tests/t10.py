# v0.3 features: DC blocker, Match (level-matched A/B), dither.
from lib import *
from scipy.signal import lfilter, welch
sr=48000
def kw(x):
    # BS.1770 K-weighting (48 kHz coefficients)
    x=lfilter([1.53512485958697,-2.69169618940638,1.19839281085285],[1,-1.69065929318241,0.73248077421585],x)
    return lfilter([1,-2,1],[1,-1.99004745483398,0.99007225036621],x)
def lufs(L,R): return -0.691+10*np.log10(np.mean(kw(L/FS)**2)+np.mean(kw(R/FS)**2))
rng=np.random.default_rng(5); n=12*sr
# pink-ish programme: filtered noise with a slow level wobble
w=lfilter([1],[1,-0.98],rng.standard_normal((2,n)),axis=1); w/=np.std(w)
env=10**((6*np.sin(2*np.pi*0.25*np.arange(n)/sr))/20)
L=w[0]*env*FS*0.05; R=(0.7*w[0]+0.3*w[1])*env*FS*0.05

# DC blocker
d=np.full(3*sr,0.1*FS)+sine(1000,-20,3,sr)
Lo,_,lat,_=run(d,d,sr,LIMITER=0)
print('DC: input mean %.0f, output mean over last second %.2f (raw units)'%(np.mean(d),np.mean(Lo[-sr:])))

tail=slice(6*sr,None)
lin=lufs(L[tail],R[tail])
for name,kw_ in (('loud chain (Lim Gain +12, comp)',dict(LIMGAIN=120,COMP=1,THRESH=50)),
                 ('quieter chain (Input -8 dB)',dict(INPUT=160,LIMITER=0))):
    Lo,Ro,lat,rows=run(L,R,sr,**kw_)
    master=lufs(Lo[tail],Ro[tail]); md=float(rows[-1][15])
    Lm,Rm,_,_=run(L,R,sr,MATCH=1,**kw_)
    Lb,Rb,_,_=run(L,R,sr,MATCH=1,BYPASS=1,**kw_)
    print(f'{name}: input {lin:.2f} LUFS, master {master:.2f} (reported match {md:+.2f} LU)')
    print(f'   Match on, master heard: {lufs(Lm[tail],Rm[tail]):.2f}   Match on + Bypass, dry heard: {lufs(Lb[tail],Rb[tail]):.2f}'
          f'   → A/B difference {abs(lufs(Lm[tail],Rm[tail])-lufs(Lb[tail],Rb[tail])):.2f} LU')
    print(f'   peaks with Match: master {db(np.max(np.abs(Lm[tail]))/FS):.2f} dBFS, dry {db(np.max(np.abs(Lb[tail]))/FS):.2f} dBFS (never raised)')

# Dither
t=np.arange(4*sr)/sr; s=FS*10**(-90/20)*np.sin(2*np.pi*1000*t)
for dv,name in ((2,'16-bit'),(3,'16-bit shaped'),(1,'24-bit')):
    Lo,_,lat,_=run(s,s,sr,DITHER=dv,LIMITER=0)
    y=Lo[sr:]; step=1/256 if dv==1 else 1.0
    ongrid=np.max(np.abs(y/step-np.round(y/step)))
    f,P=welch(y/FS,sr,nperseg=8192)
    band=lambda a,b: 10*np.log10(np.sum(P[(f>=a)&(f<b)]))
    fund=band(990,1010); noise=band(20,20000)-10*np.log10(1)  # includes fundamental bin; small
    print(f'dither {name:14s}: on grid (max off {ongrid:.1e}), -90 dBFS sine kept at {10*np.log10(np.mean((y*np.sqrt(2)/FS)**2)):.1f} dB (incl. noise);'
          f' noise 100-900 Hz + 1.1-4 kHz {10*np.log10(10**(band(100,900)/10)+10**(band(1100,4000)/10)):.1f} dB, 15-20k Hz {band(15000,20000):.1f} dB, 3rd harmonic {band(2990,3010):.1f} dB')

# v0.3.1: dither never exceeds the 16/24-bit range. Drive the output past full scale
# (limiter off, a 1.2 FS sine) so the clamp is certainly exercised.
big=np.sin(2*np.pi*50*np.arange(2*sr)/sr)*FS*1.2
for dv,name,hi in ((0,'off',None),(2,'16-bit',32767.0),(3,'16-bit shaped',32767.0),(1,'24-bit',32768-1/256)):
    Lo,Ro,_,_=run(big,big,sr,DITHER=dv,LIMITER=0)
    mx,mn=max(Lo.max(),Ro.max()),min(Lo.min(),Ro.min())
    verdict='(no dither: float output, not clamped)' if hi is None else ('OK' if mx<=hi and mn>=-32768 else 'OVER')
    print(f'dither {name:14s} on a 1.2 FS sine: max {mx:10.4f}, min {mn:10.1f}  {verdict}')
