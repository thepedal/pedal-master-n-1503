#!/bin/sh
# Sandbox tests for Pedal Master N (Linux: g++, Mono mcs/mono, Python 3 with numpy/scipy).
# Builds the machine source against the real MachineInterface.h and drives Work() directly.
set -e
cd "$(dirname "$0")"
[ -f ../native/sdk/MachineInterface.h ] || { mkdir -p ../native/sdk; curl -s -o ../native/sdk/MachineInterface.h \
  https://raw.githubusercontent.com/wasteddesign/ReBuzz/a87079863d904d928456c0ca29057c208af65a32/3rdParty/Buzz/MachineInterface.h; }
g++ -O2 -std=c++17 -Wall -msse2 -I../native/sdk -o harness harness.cpp
# GUI: compile check against WPF / BuzzGUI stand-ins (Mono has no WPF), and the
# loudness class as a runnable test program.
mcs -target:library -out:/tmp/pmn_gui_check.dll cs/Stubs.cs ../gui/*.cs && echo "GUI compile check OK"
mcs -out:/tmp/lt.exe ../gui/Loudness.cs cs/LoudnessTest.cs
for t in t1 t2 t3 t4 t5 t5b t6 t7 t8 t9; do echo "── $t"; python3 $t.py; done
