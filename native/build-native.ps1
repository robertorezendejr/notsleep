$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'NoTSleep.cpp'
$output = Join-Path $PSScriptRoot 'NoTSleepNative.exe'
g++ -std=c++20 -O2 -s -mwindows -municode $source -o $output -luser32 -lshell32 -lpowrprof -lgdi32
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Criado: $output"
