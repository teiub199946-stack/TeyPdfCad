# AutoCAD batch harness

`autocad-batch-run.ps1` runs the plugin against a copy of a DWG fixture. It never opens or saves the supplied source file directly.

The fixture must already contain the PDFIMPORT result. The harness then runs:

```text
TEYPDFDUMPALL
TEYPDFRECONSTRUCTALL
QSAVE
```

Example:

```powershell
pwsh -File .\tools\autocad-batch-run.ps1 `
  -AutoCadPath 'C:\Program Files\Autodesk\AutoCAD 2022\acad.exe' `
  -PluginDll '.\src\TeyPdfCad.AutoCAD\bin\Debug\net48\TeyPdfCad.AutoCAD.dll' `
  -InputDwg '.\fixtures\pdfimport-control.dwg' `
  -OutputDirectory '.\artifacts\autocad-runs'
```

The script returns a JSON object with the run directory, result DWG, fixture JSON, and AutoCAD exit code. A failed timeout or nonzero AutoCAD exit is treated as a failed run.

PDFIMPORT itself remains a separate gate until the prompt sequence is verified against the installed AutoCAD 2022 build. Do not use an unverified command script for production conversion.

For plugin-only health checks, use `autocad-plugin-smoke.ps1`. It uses `accoreconsole.exe` and accepts `-ProfileName` when the installed AutoCAD profile is known. Core Console must start successfully with that profile before the script can load the DLL; a profile setup failure is an AutoCAD installation/profile problem, not a plugin result.

Both harnesses fail before launching AutoCAD when `acad2022.cfg` is missing beside the selected AutoCAD executable. This is the same prerequisite reported by the Web API `/ready` endpoint in AutoCAD mode.
