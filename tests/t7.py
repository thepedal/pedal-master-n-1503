from lib import *
def lufs(e): return -0.691+10*np.log10(e)
def lra(blocks):
    b=np.array(blocks); st=np.array([b[i-29:i+1].mean() for i in range(29,len(b))])
    st=st[lufs(st)>-70]; rel=lufs(st.mean())-20; st=np.sort(lufs(st[lufs(st)>rel]))
    n=len(st); return st[int(round(0.95*(n-1)))]-st[int(round(0.10*(n-1)))]
for sr in (44100,48000):
    out=[]
    for segs in ([(-20,20),(-30,20)],[(-20,20),(-15,20)],[(-40,20),(-20,20)],[(-50,20),(-35,20),(-20,20),(-35,20),(-50,20)]):
        x=np.concatenate([sine(1000,d,t,sr) for d,t in segs]); L,R,lat,rows=run(x,x,sr,LIMITER=0)
        out.append(round(lra([float(t[1:]) for r in rows for t in r if t.startswith('b')]),2))
    print(sr,'EBU 3342 cases 1-4 LRA:',out,' (expect 10, 5, 20, 15 ±1)')
