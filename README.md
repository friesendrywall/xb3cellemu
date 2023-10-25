# Digi XB3-C-A2-UT-001 Emulator
At present, this emulates a handful of core
actions necessary for basic socket and time testing.
The device emulates API mode.
## Usage
Open the exe with com port and baud rate.<br>
ConsoleAppXb3.exe COM15 230400
## Implemented commands
### AT commands
PH (Pseudo phone number)<br>
MV (Firmware version)<br>
DT (Time)<br>
DB (RSSI)<br>
SQ (Signal quality)<br>
SW (Signal quality)<br>
MY (IP address)<br>
00 (Restart server) *Not a stock command
### API commands
0x08 AT command<br>
0x40 Socket create<br>
0x42 Socket connect<br>
0x43 Socket close<br>
0x44 Socket send<br>
## Notes
This was somewhat of a quick and dirty tool as a
companion tool for debugging an MCU API.
