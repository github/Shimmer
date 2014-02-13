param(
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]
    $increment = "Patch"
)

Set-StrictMode -Version Latest

function Die([string]$message, [object[]]$output) {
    if ($output) {
        Write-Output $output
        $message += ". See output above."
    }
    Write-Error $message
    exit 1
}

function Write-VersionAssemblyInfo {
    Param(
        [string]
        $version, 

        [string]
        $assemblyInfo
    )

    $numberOfReplacements = 0
    $newContent = Get-Content $assemblyInfo | %{
        $regex = "(Assembly(?:File|Informational)?Version)\(`"\d+\.\d+\.\d+`"\)"
        $newString = $_
        if ($_ -match $regex) {
            $numberOfReplacements++
            $newString = $_ -replace $regex, "`$1(`"$version`")"
        }
        $newString
    }

    if ($numberOfReplacements -ne 3) {
        Die "Expected to replace the version number in 3 places in $assemblyInfo (AssemblyVersion, AssemblyFileVersion, AssemblyInformationalVersion) but actually replaced it in $numberOfReplacements"
    }

    $newContent | Set-Content $assemblyInfo -Encoding UTF8
}

function Read-VersionAssemblyInfo {
    Param(
        [string]
        $assemblyInfo
    )

    Get-Content $assemblyInfo | %{
        $regex = "AssemblyInformationalVersion\(`"\d+\.\d+\.\d+"
        $version = ""
        if ($_ -match $regex) {
            $version = $matches[0] -replace "AssemblyInformationalVersion\(`"",  ""
        }
    }

    if ($version -eq "") {
        Die "Could not find an AssemblyInformationalVersion entry in this file: $assemblyInfo"
    }

    return $version
}


$scriptPath = split-path -parent $MyInvocation.MyCommand.Definition
$rootFolder = (Get-Item $scriptPath).Parent.FullName
$srcFolder = "$rootFolder\src"

$items = Get-ChildItem -Path "$srcFolder" -Filter "AssemblyInfo.cs" -Recurse

# Get AssemblyInfo.cs files of projects that contain "Squirrel." or are "CreateReleasePackage"
# However the solution itself might be in a folder that contains "Squirrel." so only look at the last bit
$items = $items | Where-Object { ($_.FullName -replace "^.*(?=\\(.*?)\\Properties\\AssemblyInfo\.cs)", "").Contains("Squirrel.") -or $_.FullName.Contains("CreateReleasePackage") }

$currentVersion = [System.Version](Read-VersionAssemblyInfo $items[0].FullName)

Write-Host "Current version: $currentVersion"

$newVersion = ""
$major = $currentVersion.Major
$minor = $currentVersion.Minor
$patch = $currentVersion.Build

if ($increment -eq "Patch") {
   $patch = $patch + 1
} elseif ($increment -eq "Minor") {
   $minor = $minor + 1
   $patch = 0
} elseif ($increment -eq "Major") {
   $major = $major + 1
   $minor = 0
   $patch = 0
}

$newVersion = "$major.$minor.$patch"
Write-Host "New version: $newVersion"

foreach ($item in $items) {
    Write-VersionAssemblyInfo -assemblyInfo $item.FullName -version $newVersion
}
