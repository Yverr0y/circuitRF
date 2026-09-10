@echo off
rem Windows counterpart of build.sh -- same contract, same guarantees.
rem
rem THIS SCRIPT MUST NEVER FAIL A BUILD. The worker is optional: circuitRF builds and runs without
rem it, and a design that places no compiled model never notices. Every failure is reported and
rem swallowed, and the exit code stays 0.
rem
rem TWO BINARIES, AND THE REASON IS NOT circuitRF'S OWN ARCHITECTURE.
rem
rem     osdi-worker-x64.exe      loads an x86-64 .osdi
rem     osdi-worker-arm64.exe    loads an arm64 .osdi
rem     osdi-worker.exe          a copy of whichever matches THE MACHINE BEING BUILT FOR, for the
rem                              bare-command route a kit's device-provider.json uses
rem
rem THE FLAT COPY FOLLOWS --arch, NOT THIS MACHINE, and that is a fix. It used to be chosen from
rem %PROCESSOR_ARCHITECTURE%, which is right for a developer build and wrong for every release: one
rem run of packaging\windows\build-windows.ps1 publishes x86, x64 and arm64 from a single machine,
rem so all three payloads of 1.0.0-beta.16 shipped the arm64 flat worker. The suffixed pair was
rem correct throughout and VerilogAFileResolver prefers it, so the .osdi route never noticed - only
rem the bare-command route did, on a user's machine, as a binary the processor cannot execute.
rem
rem This worker LoadLibrary()s a model the user compiled, and a process holds exactly one
rem instruction set -- so the worker's architecture has to match THE MODEL'S, not circuitRF's. That
rem is not a hypothetical split on Windows: an arm64 machine very often runs an x64 Verilog-A
rem compiler under translation, and that compiler emits x64 .osdi files. Both are therefore built
rem where the toolchain can target both, and VerilogAFileResolver reads the model's own PE header to
rem pick between them.
rem
rem It differs from senior-worker deliberately. That one is x86-64 ALWAYS, because the vendor model
rem libraries it hosts only ever ship as x64. Here the model is the user's own build output, so its
rem architecture is a fact to be read rather than assumed.
setlocal EnableDelayedExpansion
set "here=%~dp0"
set "build=%here%build"
set "dest="
set "targetarch="

rem Parenthesised, not chained with &. In cmd, `if COND a & b` runs b UNCONDITIONALLY -- which in an
rem argument loop silently consumes arguments in pairs whether they matched or not.
:args
if "%~1"=="" goto after
if /I "%~1"=="--dest" (
    set "dest=%~2"
    shift
    shift
    goto args
)
rem --arch names the TARGET, in any of the spellings build.sh accepts, and decides which of the pair
rem becomes the flat osdi-worker.exe. It never narrows what is BUILT: both are built whenever the
rem toolchain can reach both, because the pair is what the .osdi route picks between.
if /I "%~1"=="--arch" (
    set "targetarch=%~2"
    shift
    shift
    goto args
)
rem --os travels with --arch from the same .csproj property. Here it is always windows, so it is
rem accepted and ignored rather than refused: build.sh needs it to tell a Mach-O from an ELF.
if /I "%~1"=="--os" (
    shift
    shift
    goto args
)
shift
goto args
:after

rem This machine's own architecture, in the spelling used for the file names. It decides what a
rem single-target compiler can emit, and nothing else.
set "hostarch=x64"
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "hostarch=arm64"
if /I "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "hostarch=arm64"

rem ...and the architecture the flat copy is FOR: the target when one was named, this machine
rem otherwise, so a plain `dotnet build` behaves exactly as it always has.
set "flatarch=%hostarch%"
if defined targetarch (
    if /I "%targetarch%"=="x64"     set "flatarch=x64"
    if /I "%targetarch%"=="x86_64"  set "flatarch=x64"
    if /I "%targetarch%"=="amd64"   set "flatarch=x64"
    if /I "%targetarch%"=="arm64"   set "flatarch=arm64"
    if /I "%targetarch%"=="aarch64" set "flatarch=arm64"
    rem NO 32-BIT WORKER IS BUILT, so an x86 target takes the x64 one. A worker is a separate
    rem process, so circuitRF's own architecture never had to match it - and an x86 install of
    rem circuitRF is almost always sitting on 64-bit Windows, where a 32-bit process launches a
    rem 64-bit executable perfectly well. On a genuinely 32-bit Windows nothing here can run, and
    rem that is unchanged by this: it was true of the arm64 copy that used to be shipped too.
    if /I "%targetarch%"=="x86"  set "flatarch=x64"
    if /I "%targetarch%"=="i386" set "flatarch=x64"
    if /I "%targetarch%"=="i686" set "flatarch=x64"
)

rem A COMPILER THAT IS INSTALLED BUT NOT ON PATH is the ordinary way to arrive here and it looks
rem exactly like having none -- PATH is read when a terminal starts, so one installed a minute ago
rem is invisible until a new terminal opens. So it can be named outright.
set "zigexe="
if defined CRF_ZIG if exist "%CRF_ZIG%" set "zigexe=%CRF_ZIG%"
if not defined zigexe where zig >nul 2>&1 && set "zigexe=zig"

if not exist "%build%" mkdir "%build%" >nul 2>&1
set "log=%TEMP%\crf-osdi-worker-build.log"
set "src=%here%osdi_worker.c"
set "built="

if defined zigexe goto viazig
where clang >nul 2>&1 && goto vianative
where gcc   >nul 2>&1 && goto vianative

echo osdi-worker: no C toolchain found on PATH (zig, clang or an MSYS2/MinGW gcc); skipping the OSDI worker.
echo osdi-worker: if you just installed one, open a NEW terminal -- PATH is read when a terminal starts.
echo osdi-worker: or name it outright:  set CRF_ZIG=C:\path\to\zig.exe
echo osdi-worker: circuitRF runs normally; compiled Verilog-A models need it.
exit /b 0

:viazig
rem zig targets both from either machine, which is what makes one build produce the pair.
echo osdi-worker: building osdi-worker-x64.exe and osdi-worker-arm64.exe
type nul > "%log%"
call :zigtarget x86_64-windows-gnu  x64
call :zigtarget aarch64-windows-gnu arm64
goto built

:zigtarget
"%zigexe%" cc -target %1 -O2 -std=gnu11 -Wall -Wextra ^
    "%src%" -o "%build%\osdi-worker-%2.exe" >>"%log%" 2>&1
if exist "%build%\osdi-worker-%2.exe" set "built=!built! %2"
exit /b 0

:vianative
rem One compiler, one target: whatever this machine is. A model built for the other architecture
rem will then be refused BY NAME rather than loaded and crashed into -- see VerilogAFileResolver.
echo osdi-worker: building osdi-worker-%hostarch%.exe (this toolchain targets one architecture)
set "cc=gcc"
where clang >nul 2>&1 && set "cc=clang"
"%cc%" -O2 -std=gnu11 -Wall -Wextra "%src%" -o "%build%\osdi-worker-%hostarch%.exe" >"%log%" 2>&1
if exist "%build%\osdi-worker-%hostarch%.exe" set "built=%hostarch%"
goto built

:built
if "%built%"=="" goto buildfailed

rem The flat name, for the target's architecture. A kit's device-provider.json names the worker by
rem bare command, and that route has no model file to read an architecture out of - so this copy is
rem the one decision here that cannot be corrected later by reading a header.
rem
rem It falls back to this machine's own build when the target's is absent, which is what a
rem single-target compiler leaves behind. Naming the fallback out loud matters: the file is present
rem and plausible either way, and the difference is one no `dir` will show.
set "flatsrc=%build%\osdi-worker-%flatarch%.exe"
if not exist "%flatsrc%" (
    echo osdi-worker: no %flatarch% worker was built; the flat osdi-worker.exe falls back to %hostarch%.
    set "flatsrc=%build%\osdi-worker-%hostarch%.exe"
)
if exist "%flatsrc%" copy /Y "%flatsrc%" "%build%\osdi-worker.exe" >nul 2>&1
goto publish

:buildfailed
echo osdi-worker: the OSDI worker did not build; the compiler's own output is here:
echo osdi-worker:   %log%
echo osdi-worker: circuitRF will run normally, but compiled Verilog-A models will not be available.
exit /b 0

:publish
if "%dest%"=="" goto done
if not exist "%dest%" mkdir "%dest%" >nul 2>&1
for %%F in (osdi-worker.exe osdi-worker-x64.exe osdi-worker-arm64.exe) do (
    if exist "%build%\%%F" copy /Y "%build%\%%F" "%dest%\" >nul 2>&1
)

:done
echo osdi-worker: ok (%built%)
exit /b 0
