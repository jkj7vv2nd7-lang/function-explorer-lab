# 開発者向けビルド手順

## 必要なもの

- Windows 11 64 ビット
- .NET 10 SDK
- Git（ソース取得に使う場合）
- Windows デスクトップ開発に必要な .NET SDK コンポーネント

## ソース

WPF プロジェクトは `src/` にあります。アプリは .NET 10 Windows Desktop を使い、グラフ描画や画面部品は WPF で実装しています。

## ビルド

PowerShell でリポジトリのフォルダーを開き、次を実行します。

```powershell
dotnet restore .\src\FunctionExplorer.csproj
dotnet build .\src\FunctionExplorer.csproj -c Release
```

## Windows 11 向け単体 EXE の発行

```powershell
dotnet publish .\src\FunctionExplorer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:RuntimeFrameworkVersion=10.0.12 `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\publish
```

発行後、`publish\関数探究ラボ.exe` ができます。ランタイムを含めるためファイルサイズは大きくなります。

## 注意

- インターネットなしで発行する場合、必要な公式 NuGet パッケージを事前に用意する必要があります。
- `obj/` や `bin/` はビルド時に作られるため、Git には登録しません。
- 実際の生徒記録や個人情報をソース管理へ登録しないでください。
