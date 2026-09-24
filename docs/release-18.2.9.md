# GestureSign V2 18.2.9

- Based on developer preview 0.4; includes local intent learning and the optional AI veto controls.
- AMD NPU uses a normalized 1x1 Conv linear core through VitisAI. CPU performs normalization, sigmoid and full precision verification; the published score remains the original CPU reference. This is hybrid execution, not a claim of end-to-end NPU acceleration or improved performance.
- Strictly disables ONNX CPU execution-provider fallback inside accelerated sessions. Initialization/runtime failure falls back to the next available backend. Raw AMD score error over 0.05 rejects the NPU backend.
- Prepares already-installed certified providers at startup without downloading. The hardware preparation button can acquire missing certified providers through Windows ML.
- Copies the installed AMD provider to a version-specific local application cache to avoid the WindowsApps compiler VFS issue. No machine-specific AMD driver binaries or personal training data are distributed.
- Uses a writable local working directory and model/provider-specific compilation cache.
- MSI and portable ZIP target Windows x64. Main application requires .NET 10 Desktop Runtime and the Windows App SDK runtime, as in 18.2.8; the bundled intent component is self-contained.
- Tested on AMD Ryzen AI Z2 Extreme: 1,559 vectors (including 1,359 local samples); 1,562 NPU executions including initialization; final maximum error against CPU ONNX: 0.0000008941. NPU raw maximum error: 0.0294035. Other NPU generations are not yet hardware-validated.
