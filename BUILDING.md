# Building

Requirements:

- Windows x64
- .NET SDK 10 or a compatible SDK capable of building `netstandard2.1`
- A legally installed Sephiria 1.0.30 Mono build
- BepInEx 5 installed in that game directory

Build the mod without copying game assemblies into this repository:

```powershell
dotnet build SephiriaAutoRetry.csproj -c Release /p:SephiriaDir="C:\Path\To\Sephiria"
```

Output:

```text
bin/Release/SephiriaAutoRetry.dll
```

The offline installer additionally expects the official unmodified BepInEx x64 archive at:

```text
vendor/BepInEx_win_x64_5.4.23.5.zip
```

Then publish it with:

```powershell
dotnet publish installer/SephiriaAutoRetry.Installer.csproj -c Release -r win-x64 --self-contained true -o artifacts/installer-v0.2.1
```

The game DLLs, BepInEx archive, build outputs, saves, and logs are intentionally ignored and must not be committed.
