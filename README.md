# MyDesktopApp

C# / WPF ile geliştirilen Windows masaüstü uygulaması.

## Gereksinimler

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (".NET masaüstü geliştirme" iş yükü) veya VS Code + C# Dev Kit

## Çalıştırma

```bash
dotnet restore
dotnet run --project src/MyDesktopApp
```

Ya da `MyDesktopApp.sln` dosyasını Visual Studio ile açıp **F5**'e bas.

## Proje yapısı

```
MyDesktopApp/
├── .github/workflows/build.yml   # Her push'ta otomatik derleme
├── src/MyDesktopApp/             # WPF uygulaması
├── MyDesktopApp.sln
└── README.md
```
