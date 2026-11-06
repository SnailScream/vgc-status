@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Не найден csc.exe - установите .NET Framework 4.x
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:VgcStatus.exe /win32manifest:app.manifest ^
  /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.ServiceProcess.dll Program.cs Diagnostics.cs DiagnosticsForm.cs Localization.cs

if errorlevel 1 (
  echo Сборка не удалась
  pause
  exit /b 1
)

echo Готово: VgcStatus.exe
pause
