from lib import *
import math
print('tilt +6: ', [round(gain_at(f,TILT=120),2) for f in (20,100,1000,10000,20000)])
print('tilt -6: ', [round(gain_at(f,TILT=0),2) for f in (20,1000,20000)])
print('lowshelf +6 @100Hz:', [round(gain_at(f,LOWGAIN=180,LOWFREQ=50),2) for f in (25,100,1000,5000)])
print('highshelf -6 @10k:', [round(gain_at(f,HIGHGAIN=60,HIGHFREQ=75),2) for f in (1000,10000,19000)])
v=round(1+99*math.log(30/10)/math.log(250/10))
print('low cut 30 Hz (v=%d), 24 dB/oct:'%v, [round(gain_at(f,LOWCUT=v),2) for f in (7.5,15,30,60,120)])
print('low cut 30 Hz (v=%d), 12 dB/oct:'%v, [round(gain_at(f,LOWCUT=v,SLOPE=0),2) for f in (7.5,15,30,60,120)])
print('DC blocker only (defaults):', [round(gain_at(f),3) for f in (5,10,20,40)])
print('EQ off ignores settings:', round(gain_at(100,EQ=0,LOWGAIN=240,LOWCUT=50),3))
