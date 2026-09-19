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

For plugin-only health checks, use `autocad-plugin-smoke.ps1`. It uses
`accoreconsole.exe`, accepts `-ProfileName`, and accepts
`-ConfigurationFile` when `acad2022.cfg` is stored in the user's Autodesk
profile rather than beside `acad.exe`.

Both harnesses fail before launching AutoCAD when the selected configuration
file is missing. The Web API uses the equivalent
`TEYPDFCAD_AUTOCAD_CONFIG_FILE` setting.
