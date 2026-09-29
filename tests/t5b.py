from lib import *
sr=48000
def recovery(burst,rel):
    x=np.concatenate([sine(1000,-6,burst,sr),sine(1000,-40,4,sr)])
    L,R,lat,_=run(x,x,sr,COMP=1,THRESH=40,RATIO=3,ATTACK=4,RELEASE=rel,LIMITER=0)
    y=L[lat:]; x=x[:len(y)]
    w=480; env=np.array([np.max(np.abs(y[i:i+w]))/np.max(np.abs(x[i:i+w])) for i in range(0,len(y)-w,w)])
    start=int(burst*sr/w)+1; gr=db(env[start:])
    ok=gr>-0.5
    return np.argmax(ok)*w/sr*1000 if ok.any() else float("inf")
for rel,name in ((0,'0.1 s'),(3,'1.2 s'),(4,'Auto')):
    print(f'release {name:5s}: back within 0.5 dB after a 50 ms burst: {recovery(0.05,rel):5.0f} ms, after a 3 s burst: {recovery(3.0,rel):5.0f} ms')
