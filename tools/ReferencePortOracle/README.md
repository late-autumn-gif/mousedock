# Reference compatibility oracle

This script regenerates synthetic battery fixtures using pure functions from the
user-supplied MouseDock 1.0.2 binary. It requires CPython **3.12** and that exact
binary (SHA256 is checked before any bytecode execution). It does not launch the
reference application. Hardware provider and device-opening entrypoints are
replaced with functions that raise errors.

From the repository root:

```text
python3.12 tools/ReferencePortOracle/generate_vectors.py path/to/MouseDock-1.0.2.exe tests/MouseDock.ProtocolTests/Fixtures/reference-1.0.2-vectors.json
```

The original application, its bytecode and its Python runtime must stay local;
they are not repository dependencies or shipped assets. Only synthetic report
bytes and expected decoding facts are committed. The remaining packet vectors
cover VT9S V2. The fixture also retains the historical receiver comparison
data. ATK name checks use ATK HUB data. COMPX, Scyrox and Sora frame tests run independently.

See `docs/REFERENCE-1.0.2-PORT.md` for scope and `THIRD-PARTY-NOTICES.md` for
attribution. The C# tests also replay complete transactions and instance routing,
which are independent of the pure decoder oracle.

The X23SE regression uses synthetic vectors built from the E HUB protocol fields.
