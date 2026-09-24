# Documentation Source Registry

Cache of canonical doc URLs for what this repo references. **Not a pin** — Phase 1 falls through to live lookup (NuGet registration `projectUrl`, then `WebSearch`) for anything not listed here, and a URL here that 404s should be replaced, not worked around.

Add an entry only when:

- The package/library appears in this repo, AND
- The live lookup gave a worse URL than what you found manually.

Keyed by name + major version. Update entries when the repo upgrades.

## Lookup order (Phase 1)

1. **This file** — exact match on `<name>@<major>`
2. NuGet: `https://api.nuget.org/v3/registration5-gz-semver2/<lowercase-id>/index.json` → `projectUrl`
3. `WebSearch "<name> <major-version> documentation"` — first official-looking result
4. Mark as `unresolved` in the findings; do not block the audit

## Registry format

```yaml
'<name>@<major>':
  primary: 'https://...'
  pages:
    - 'https://...'
  notes: 'anything quirky'
```

## Entries

```yaml
'dotnet@10':
  primary: 'https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview'
  pages:
    - 'https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0'
    - 'https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core'
  notes: 'breaking changes + support window'

'dotnet-security':
  primary: 'https://learn.microsoft.com/en-us/dotnet/standard/security/secure-coding-guidelines'
  pages:
    - 'https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/security-warnings'
    - 'https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-security-guide'
    - 'https://learn.microsoft.com/en-us/dotnet/standard/io/file-path-formats'

'dotnet-interop':
  primary: 'https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices'
  pages:
    - 'https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation'
    - 'https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle'
    - 'https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code'

'dotnet-async':
  primary: 'https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/'
  pages:
    - 'https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming'

'wpf@10':
  primary: 'https://learn.microsoft.com/en-us/dotnet/desktop/wpf/'
  pages:
    - 'https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-wpf-application-performance'
    - 'https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-direct3d9-interoperation'
    - 'https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/'
    - 'https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100'
  notes: 'the Direct3D9 interop page covers D3DImage Lock/Unlock and front-buffer loss'

'LiteDB@5':
  primary: 'https://www.litedb.org/docs/'
  pages:
    - 'https://www.litedb.org/docs/connection-string/'
    - 'https://www.litedb.org/docs/indexes/'
    - 'https://www.litedb.org/docs/object-mapping/'
  notes: 'GitHub issues (mbdavid/LiteDB) are the only place some corruption/concurrency caveats are documented'

'Newtonsoft.Json@13':
  primary: 'https://www.newtonsoft.com/json/help/html/Introduction.htm'
  pages:
    - 'https://www.newtonsoft.com/json/help/html/SerializationSettings.htm'
    - 'https://www.newtonsoft.com/json/help/html/T_Newtonsoft_Json_TypeNameHandling.htm'

'Serilog@4':
  primary: 'https://github.com/serilog/serilog/wiki'
  pages:
    - 'https://github.com/serilog/serilog/wiki/Writing-Log-Events'
    - 'https://github.com/serilog/serilog-sinks-file'
  notes: 'check Log.CloseAndFlush on exit'

'Vortice.Direct3D9@3':
  primary: 'https://github.com/amerkoleci/Vortice.Windows'
  notes: 'thin COM wrappers — Direct3D 9 semantics come from the Microsoft D3D9 docs'

'direct3d9':
  primary: 'https://learn.microsoft.com/en-us/windows/win32/direct3d9/dx9-graphics'
  pages:
    - 'https://learn.microsoft.com/en-us/windows/win32/direct3d9/lost-devices'

'SlimDX':
  primary: 'https://github.com/SlimDX/slimdx'
  notes: 'unmaintained since ~2012; relevant only as an AcTools.Render dependency'

'AcTools':
  primary: 'https://github.com/gro-ove/actools'
  notes: 'Content Manager source; referenced as local DLLs from a sibling checkout (..\..\actools)'

'fmod':
  primary: 'https://www.fmod.com/docs/2.02/api/welcome.html'
  pages:
    - 'https://www.fmod.com/docs/2.02/api/white-papers-handle-system.html'
    - 'https://www.fmod.com/docs/2.02/api/white-papers-dsp-plugin-api.html'
  notes: 'AC ships an older FMOD Studio; check the DLL version in the AC install and pick the matching docs version'

'csp-lua-sdk':
  primary: 'https://github.com/ac-custom-shaders-patch/acc-lua-sdk'
  notes: 'the lib.lua / common definitions are the API reference'

'cwe-top-25':
  primary: 'https://cwe.mitre.org/top25/'
```
