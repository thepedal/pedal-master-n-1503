from lib import *
sr=48000
# static curve: 1 kHz sine, RMS = peak - 3.01. thresh -20 (v=40), ratio 4 (v=3), attack 10ms, release 0.3 s, knee 6
for pk in (-30,-23,-20,-17,-10,-3):
    x=sine(1000,pk,1.5,sr)
    L,R,lat,rows=run(x,x,sr,COMP=1,THRESH=40,RATIO=3,ATTACK=4,RELEASE=1,LIMITER=0)
    a=slice(sr,None); g=db(np.sqrt(np.mean(L[a]**2))/np.sqrt(np.mean(x[a]**2)))
    rms=pk-3.0103; over=rms+20
    exp_=0 if 2*over<-6 else (over*0.75 if 2*over>6 else 0.75*(over+3)**2/12)
    print(f'peak {pk:4d}: GR {-g:5.2f} dB  expected {exp_:5.2f}  meter {max(float(r[13]) for r in rows[-5:]):5.2f}')
# parallel mix 50% + makeup 6 dB at -10 dBFS
x=sine(1000,-10,1.5,sr)
L,R,lat,rows=run(x,x,sr,COMP=1,THRESH=40,RATIO=3,ATTACK=4,RELEASE=1,LIMITER=0,MIX=50,MAKEUP=60)
a=slice(sr,None); g=db(np.sqrt(np.mean(L[a]**2))/np.sqrt(np.mean(x[a]**2)))
gc=10**(-(6.9897*0.75)/20); print('mix 50 + makeup 6: gain %.2f expected %.2f'%(g, 6+db(0.5+0.5*gc)))
