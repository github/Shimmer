## Scenarios

At the end of the day, here's how a developer will use Shimmer:

1. Add the **Shimmer** package to your application
1. As part of the install for Shimmer, NuGet Package Build  is enabled in the csproj file
1. The user edits the generated `.nuspec` to specify some details about their app
1. From the NuGet package console, run `New-Release` - this builds the
   world, and you end up with a `$SolutionDir/Releases` folder that has both a
   Shimmer release package as well as a `Setup.exe`

## How does this work:

1. Call `$DTE` to build the current project, including the NuGet packages
1. Look at all of the projects which have references to `Shimmer.Client`
1. Look up the build output directory for those projects, run
   `CreateReleasePackage.exe` on all of the .nupkg files
1. Using the generated NuGet package, fill in the `Template.wxs` file
1. Create a temporary directory for the contents of the Setup.exe, copy in the
   `Shimmer.WiXUi.dll` as well as any DLL Project that references
   `Shimmer.Client.dll`
1. Run `Candle` and `Light` to generate a `Setup.exe`, which contains
   Shimmer.WiXUi.dll and friends, any custom UI DLLs, and the latest full
   `nupkg` file.
