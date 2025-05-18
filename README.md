# SharpiReg
Invisible registry manipulation tool written in C#. Works as a bof on C2 frameworks such as Cobaltstrike.

Originality credits go to [nukingdragons](https://github.com/NukingDragons/invisreg)!

## How to Use
1. Load the `.cna` script in Cobaltstrike, or use functionality such as `execute-assembly` in your C2 framework to run the C# payload in-memory. If using Cobaltstrike, make sure the `.cna` script and the `sharpireg.exe` are in the same directory.
![alt text](<media/C2 framework load cna.png>)

2. Create, list, or delete registry keys and values using `sharpireg`.
![alt text](<media/sharpireg usage and value creation.png>)
![alt text](<media/sharpireg usage and value creation 2.png>)

## Video Showcase
https://github.com/user-attachments/assets/a6ae0d65-3c65-4f6e-ae2c-f7822b720991

## Detection              
Utilize ETW, or forensic tools other than `Regedit.exe` or `reg` to triage and examine registry manipulation. 
