#!/bin/bash

python -m pip install uavcan em empy --user

python ./canard_dsdlc/canard_dsdlc.py dsdl/uavcan out

"C:\Python27\python.exe" -m pip install uavcan em empy --user

mkdir out

"C:\Python27\python.exe" ./canard_dsdlc/canard_dsdlc.py dsdl\uavcan\ out

pause