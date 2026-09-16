# FRLG FFI plugin prototype

This private project turns the existing FRLG scene reader into a Windows native library. Its game-specific dictionaries, model assets, scene rules and digit templates stay here; none of them belong in the generic EasyCon repository. The current EasyCon `EXTERN FUNC`/FFI mechanism can call it without changing the built-in `OCR()` backend or opening a separate OCR window.

## Build and package

From this repository root:

```powershell
dotnet publish tools/FrlgFfi/FrlgFfi.csproj -c Release -r win-x64 -o tools/FrlgFfi/dist/win-x64
```

Keep the published `FrlgFfi.dll`, `models/frlg`, `ezcv_native.dll`, `opencv_world500.dll`, `onnxruntime.dll`, Tesseract/leptonica DLLs and remaining native dependencies together in one plugin directory. The `dist/` directory is ignored by Git; it is a local test artifact, not game content to upload to EasyCon upstream.

Current EasyCon resolves native DLL paths relative to its application, not relative to a loaded script. The installer therefore generates absolute DLL and model paths into a small ECS module, and refuses to overwrite an existing module:

```powershell
& tools/FrlgFfi/Install-FrlgFfi.ps1 `
    -PluginRoot 'D:\path\to\plugin' `
    -ScriptLibDirectory 'D:\path\to\your-script\lib'
```

Loading that `lib/FrlgFfi.ecs` module provides `FRLG_OCR(scene,x,y,w,h)`, `FRLG_LastError()`, `FRLG_LastDebug()` and `FRLG_Shutdown()`. Replace only the central Japanese OCR wrapper, not capture, RNG or battle control flow. For example:

```ecs
RETURN FRLG_OCR($日版场景键[$场景号], $日版区域X[$场景号], $日版区域Y[$场景号], $日版区域W[$场景号], $日版区域H[$场景号])
```

`FRLG_OCR` takes a fresh full-frame `FRAME()` snapshot and applies the supplied ROI in capture-frame pixels. `frlg_read` uses the Cdecl ABI and UTF-8 zero-terminated strings. It returns a per-thread UTF-8 buffer that EasyCon copies immediately; a failed or unconfirmed read returns an empty string, and `FRLG_LastError()` gives the reason. Call `$closed = FRLG_Shutdown()` when the script is done to dispose OCR sessions.

## Verified locally

Against `upstream/dev` with two generic FFI fixes applied locally, actual ECS compilation and VM execution returned the expected Japanese summary name, nature, level, HP and five stats on the included public fixtures (12/12). A mismatched target was rejected with an empty result and nonempty error. Both process and disk-cache FFI signature regression tests passed. Read-only ECS scripts additionally returned `ニドラン♀ / 22` from a cached wild battle capture and `45345` from a trainer-card fixture. This validates the plugin-to-FFI integration, but does **not** yet validate live capture or every species.

The two EasyCon fixes contain no FRLG data: free UTF-8 arguments with their matching allocator (`FreeCoTaskMem`), and recompile cached modules containing external native calls until ECM artifacts retain FFI signatures. Without the second fix, repeated compilation can produce an image containing FFI calls but no native symbol table, causing `原生函数未实现` at runtime.

The optional `FrlgFfiDevSmokeTest.cs.sample` preserves the out-of-tree ECS regression harness. Copy it as `test/EasyCon.Tests/FrlgFfiDevSmokeTest.cs` in a local EasyCon dev worktree only, then set `FRLG_FFI_DIST`, `FRLG_FFI_FIXTURES`, `FRLG_FFI_READONLY`, `FRLG_FFI_REGION_MODULE` and `FRLG_FFI_WILD_SAMPLE` to your local paths before running its test filter. Never add that game-specific test file to the generic EasyCon PR. The full 2.0 script compile case is marked explicit because its legacy Seed table currently exceeds the dev VM's 255-slot limit; the read-only scripts and central OCR wrapper are separately verified.
