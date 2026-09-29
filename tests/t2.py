from lib import *
print('tilt +6: ', [round(gain_at(f,TILT=120),2) for f in (20,100,1000,10000,20000)])
print('tilt -6: ', [round(gain_at(f,TILT=0),2) for f in (20,1000,20000)])
print('lowshelf +6 @100Hz:', [round(gain_at(f,LOWGAIN=180,LOWFREQ=50),2) for f in (25,100,1000,5000)])
print('highshelf -6 @10k:', [round(gain_at(f,HIGHGAIN=60,HIGHFREQ=75),2) for f in (1000,10000,19000)])
print('lowcut 30Hz (v=?):')
import math
v=round(1+99*math.log(30/20)/math.log(250/20)); print('  v',v,[round(gain_at(f,LOWCUT=v),2) for f in (15,30,60,200)])
print('EQ off ignores settings:', round(gain_at(100,EQ=0,LOWGAIN=240,LOWCUT=50),3))
