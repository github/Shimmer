Set-PSDebug -Strict
$ErrorActionPreference = "Stop"

$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$rootFolder = (Get-Item $scriptPath).Parent.FullName
$srcFolder = "$rootFolder\src"

try {
    git submodule init
} catch {
    Write-Host "Git not found!" -ForegroundColor Red
    Write-Host
    Write-Host "If you have Git installed you should add at least PATH_TO_GIT\cmd to your path"
    Write-Host "For most people, this would be C:\Program Files (x86)\Git\cmd"
    Write-Host
    throw
}

$nuget = "$srcFolder\.nuget\nuget.exe"

. $nuget config -Set Verbosity=quiet
. $nuget restore "$srcFolder\Squirrel.sln" -OutputDirectory "$srcFolder\packages"
