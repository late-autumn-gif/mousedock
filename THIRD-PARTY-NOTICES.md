# Third-party notices

## MouseDock 1.0.2 reference (a different author's program)

The six verified battery profiles, request/response rules, and ATK CID/MID names
were adapted into new battery-only C# code in ReferenceProfiles,
ReferenceBatteryProtocol, VendorProtocol.Reference and ReferenceIdentityNames.
The supplied reference binary identifies its license as follows. The original
application and its binary/runtime are not included in this distribution.

MIT License

Copyright (c) 2026 MouseDock contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Halo Battery protocol references

The 1.0.2 distribution also attributes battery protocol references to
https://github.com/HeyOkay/HaloBattery and includes this MIT notice.
No HeadsetControl code or helper is incorporated by this six-profile port.

MIT License

Copyright (c) 2026 Halo Battery contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Synthetic compatibility fixtures

`tests/MouseDock.ProtocolTests/Fixtures/reference-1.0.2-vectors.json` contains
synthetic report bytes and expected facts obtained from the reference's pure
packet functions. It contains no application bytecode, user settings, device
paths, firmware, or hardware captures. The regeneration script requires the
user's own exact reference binary and disables hardware entrypoints.
