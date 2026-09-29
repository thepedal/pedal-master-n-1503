from lib import *
def lufs(e): return -0.691+10*np.log10(e)
def integrated(blocks):
    b=np.array(blocks); g=[b[i-3:i+1].mean() for i in range(3,len(b))]
    g=np.array([x for x in g if lufs(x)>-70]); rel=lufs(g.mean())-10
    return lufs(g[lufs(g)>rel].mean())
for sr in (44100,48000):
    res=[]
    for segs in ([(-23,20)],[(-33,20)],[(-36,10),(-23,60),(-36,10)],[(-72,10),(-36,10),(-23,60),(-36,10),(-72,10)],[(-26,20),(-20,20.1),(-26,20)]):
        x=np.concatenate([sine(1000,d,t,sr) for d,t in segs])
        L,R,lat,rows=run(x,x,sr)
        bl=[float(t[1:]) for r in rows for t in r if t.startswith('b')]
        res.append(round(integrated(bl),2))
    print(sr,'EBU 3341 cases 1-5 integrated:',res,' (expect -23,-33,-23,-23,-23)')
sr=48000; x=sine(500,-10,2,sr); rng=np.random.default_rng(3)
for name,(L,R) in {'identical':(x,x),'inverted':(x,-x),'independent noise':tuple(rng.standard_normal((2,2*sr))*3000)}.items():
    _,_,_,rows=run(L,R,sr,LIMITER=0)
    c=[[float(t[1:]) for t in r if t.startswith('c')] for r in rows[5:]]
    c=np.mean(c,axis=0); print(f'correlation {name:18s} {c[1]/np.sqrt(c[0]*c[2]):+.3f}')
