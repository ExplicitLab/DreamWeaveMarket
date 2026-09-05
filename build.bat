@echo off
rem ---------------------------------------------------------------------------
rem  Builds DreamweaveMarket. Finds MSBuild itself; no project setup needed.
rem
rem    build.bat                    build Release
rem    build.bat Debug              build Debug instead
rem    build.bat Release 1.0.4      set the version to 1.0.4 first, then build
rem
rem  The window stays open at the end - and on any failure - so the messages can
rem  be read.
rem
rem  NOTE FOR EDITING: this file must keep Windows (CRLF) line endings. With Unix
rem  line endings cmd cannot find the :fail label below and the window closes
rem  instantly with no message. Notepad++/VS Code: check the status bar says CRLF.
rem ---------------------------------------------------------------------------
setlocal EnableDelayedExpansion
cd /d "%~dp0"

echo ============================================================
echo  DreamweaveMarket build
echo ============================================================
echo.

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Release"
set "NEWVER=%~2"

rem "Program Files (x86)" contains a closing bracket, which ends a parenthesised
rem block early if it is expanded inside one. Captured here, at the top level,
rem where that cannot happen; used as !PF86! from here on.
set "PF86=%ProgramFiles(x86)%"
set "PF64=%ProgramFiles%"

rem --- Optional version bump --------------------------------------------------
rem AssemblyInfo.cs is the single source of truth; everything downstream reads
rem the version back out of the compiled DLL.
if not "%NEWVER%"=="" call :bump || goto :fail

rem --- Find MSBuild -----------------------------------------------------------
rem Visual Studio's MSBuild specifically: WebView2 is a NuGet PackageReference
rem and the bare .NET Framework MSBuild cannot restore those.
set "MSBUILD="
set "VSWHERE=!PF86!\Microsoft Visual Studio\Installer\vswhere.exe"

if exist "!VSWHERE!" for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"

if not defined MSBUILD (
  echo Could not find Visual Studio's MSBuild.
  echo.
  echo Install Visual Studio ^(Community is fine^) or the Visual Studio Build
  echo Tools, with the ".NET desktop development" workload, then run this again.
  echo.
  echo The plain .NET Framework MSBuild in C:\Windows\Microsoft.NET is not
  echo enough here - it cannot restore the WebView2 NuGet package.
  goto :fail
)

rem --- Prefer a real Decal install over the reference-only DLLs ----------------
rem reference-only\ is enough to compile in most cases, but does not include
rem every Decal.Interop.* assembly. If Decal is installed, build against it.
set "DECAL="
call :findfile DECAL "!PF86!\Decal 3.0" Decal.Adapter.dll
call :findfile DECAL "!PF64!\Decal 3.0" Decal.Adapter.dll
call :findfile DECAL "C:\Games\Decal 3.0" Decal.Adapter.dll
call :findfile DECAL "C:\Decal 3.0" Decal.Adapter.dll

set "VVS="
call :findfile VVS "!PF86!\Decal Plugins\VirindiViewService" VirindiViewService.dll
call :findfile VVS "!PF86!\VirindiPlugins\VirindiViewService" VirindiViewService.dll
call :findfile VVS "C:\Games\VirindiPlugins\VirindiViewService" VirindiViewService.dll

rem --- Make sure we can compile against something ----------------------------
rem The reference assemblies are not in the repository - they belong to Decal and
rem to Virindi. Either an install is found above, or they have been copied into
rem reference-only\ by hand. Say so plainly rather than letting MSBuild fail with
rem a hint-path error that explains nothing.
if not defined DECAL if not exist "reference-only\Decal.Adapter.dll" (
  echo Cannot find Decal.
  echo.
  echo Either install Decal 3.0, or copy these files into reference-only\ :
  echo     Decal.Adapter.dll, Decal.Interop.Core.dll, Decal.Interop.Inject.dll
  echo.
  echo See reference-only\README.txt.
  goto :fail
)

if not defined VVS if not exist "reference-only\VirindiViewService.dll" (
  echo Cannot find Virindi View Service.
  echo.
  echo Either install it, or copy VirindiViewService.dll into reference-only\ .
  echo.
  echo See reference-only\README.txt.
  goto :fail
)

echo MSBuild:       !MSBUILD!
echo Configuration: !CONFIG!

set "EXTRA="
if defined DECAL (
  echo Decal:         !DECAL!
  set "EXTRA=!EXTRA! /p:DecalPath=!DECAL!\"
) else (
  echo Decal:         reference-only\  [compile only - never install those DLLs]
)
if defined VVS (
  echo VVS:           !VVS!
  set "EXTRA=!EXTRA! /p:VVSPath=!VVS!\"
) else (
  echo VVS:           reference-only\
)
echo.

rem --- Build ------------------------------------------------------------------
"!MSBUILD!" DreamweaveMarket.sln -restore /p:Configuration=!CONFIG! /p:Platform=x86 !EXTRA! /v:minimal /nologo
if errorlevel 1 (
  echo.
  echo If the failure mentions NuGet or Microsoft.Web.WebView2, this machine
  echo could not reach nuget.org. Restore once while connected, then rebuild.
  echo.
  echo If it mentions CS0012 and a Decal.Interop.* assembly, Decal was not found
  echo in the usual places. Edit DecalPath near the top of
  echo DreamweaveMarket.csproj to point at your Decal folder.
  goto :fail
)

rem --- Package ----------------------------------------------------------------
rem The build renamed the DLL to carry its version - DreamweaveMarket-1.0.3.dll.
rem That name is the authority for the folder and zip names below, so the three
rem can never disagree with each other or with the assembly.
set "OUT=bin\x86\!CONFIG!"

set "DLL="
for %%f in ("!OUT!\DreamweaveMarket-*.dll") do set "DLL=%%~nxf"
if not defined DLL (
  echo ERROR: no built DreamweaveMarket-*.dll found in !OUT!
  goto :fail
)

set "VER=!DLL:DreamweaveMarket-=!"
set "VER=!VER:.dll=!"

set "DISTNAME=DreamweaveMarket-!VER!"
set "DIST=dist\!DISTNAME!"

if exist dist rmdir /s /q dist
mkdir "!DIST!"

copy /y "!OUT!\!DLL!" "!DIST!\" >nul
copy /y "!OUT!\Microsoft.Web.WebView2.Core.dll" "!DIST!\" >nul
copy /y "!OUT!\Microsoft.Web.WebView2.WinForms.dll" "!DIST!\" >nul
if exist "!OUT!\WebView2Loader.dll" copy /y "!OUT!\WebView2Loader.dll" "!DIST!\" >nul
if exist "!OUT!\runtimes\win-x86\native\WebView2Loader.dll" copy /y "!OUT!\runtimes\win-x86\native\WebView2Loader.dll" "!DIST!\" >nul
copy /y README.md "!DIST!\" >nul
copy /y CHANGELOG.md "!DIST!\" >nul

if not exist "!DIST!\WebView2Loader.dll" (
  echo.
  echo WARNING: WebView2Loader.dll [x86] was not in the build output. The plugin
  echo          loads without it, but the market window will not open.
)

powershell -NoProfile -Command "Compress-Archive -Path '!DIST!' -DestinationPath 'dist\!DISTNAME!.zip' -Force"

echo.
echo ============================================================
echo  Done.
echo    Assembly:      !DLL!
echo    Plugin folder: !DIST!
echo    Package:       dist\!DISTNAME!.zip
echo ============================================================
echo.
echo Register !DIST!\!DLL! in the Decal Agent [Plugins ^> Add], then restart
echo the client. The window is in the Virindi bar, or type /market.
echo.
pause
endlocal
exit /b 0


rem ===========================================================================
rem  Subroutines
rem ===========================================================================

rem :findfile VAR "folder" filename
rem Sets VAR to the folder if it is not already set and the file is in there.
:findfile
if defined %~1 exit /b 0
if exist "%~2\%~3" set "%~1=%~2"
exit /b 0

rem :bump   rewrites the version in Properties\AssemblyInfo.cs
:bump
echo %NEWVER%| findstr /r "^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo ERROR: version must look like 1.2.3, not "%NEWVER%"
  exit /b 1
)
powershell -NoProfile -Command "$p='Properties\AssemblyInfo.cs'; $t=Get-Content $p -Raw; $t=$t -replace 'AssemblyVersion\(\"[^\"]*\"\)','AssemblyVersion(\"%NEWVER%.0\")'; $t=$t -replace 'AssemblyFileVersion\(\"[^\"]*\"\)','AssemblyFileVersion(\"%NEWVER%.0\")'; Set-Content $p $t -NoNewline"
if errorlevel 1 exit /b 1
echo Version set to %NEWVER%
echo.
exit /b 0


:fail
echo.
echo ============================================================
echo  *** BUILD FAILED - see the messages above ***
echo ============================================================
echo.
pause
endlocal
exit /b 1
