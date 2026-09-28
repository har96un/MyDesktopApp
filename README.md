# Profil CAD

C# / WPF ile geliştirilen, AutoCAD benzeri basit bir 2B çizim uygulaması.
Asıl amaç **profil kesitlerini** DWG/DXF'ten okuyup **0,0 noktasına taşımak, döndürmek, aynalamak**
ve tekrar DWG/DXF olarak kaydetmektir.

## Özellikler

- **Dosya:** DWG ve DXF açma, içe aktarma (mevcut çizime ekleme), DXF/DWG kaydetme, seçileni dışa aktarma
  ([ACadSharp](https://github.com/DomCR/ACadSharp) ile). Bloklar patlatılarak alınır.
- **Çizim:** Çizgi (L), Polyline (PL), Dikdörtgen (REC), Daire (C), 3 noktadan Yay (A), Yazı (DT)
- **Değiştirme:** Taşı (M), Kopyala (CO), Döndür (RO, referans açılı), Aynala (MI), Ölçekle (SC, referans uzunluklu),
  Ötele/Offset (O), Birleştir (J), Patlat (X), Sil (E / Del), Geri al / Yinele (Ctrl+Z / Ctrl+Y)
- **Profil araçları:**
  - Ağırlık merkezi → 0,0 (OC), Sol alt köşe → 0,0 (OLL), Kutu merkezi → 0,0 (OBC), Seçilen nokta → 0,0 (OR)
  - Hizala (AL): 1. nokta 0,0'a, 2. nokta +X yönüne
  - 0,0 etrafında +90° / −90° / 180° döndürme, X / Y eksenine göre aynalama
  - Asal eksenlere hizalama (ASAL)
  - Kesit özellikleri (MP): alan, çevre, ağırlık merkezi, Ix, Iy, Ixy, I1, I2, W, i — boşluklu kesitler dahil
- **Yardımcılar:** Nesne yakalama (uç, orta, merkez, çeyrek, kesişim, orijin — F3), Orto (F8), Izgara (F7),
  pencere/çapraz seçim, komut satırı ile koordinat girişi

## Koordinat girişi

| Yazım | Anlamı |
|-------|--------|
| `x,y` | Mutlak koordinat |
| `@dx,dy` | Son noktaya göre göreli |
| `@uzunluk<açı` | Kutupsal (açı derece) |
| `25` | İmleç yönünde 25 birim |

## Çalıştırma

```bash
dotnet run --project src/MyDesktopApp
```

Ya da `MyDesktopApp.sln` dosyasını Visual Studio 2022 ile açıp **F5**'e basın.
Gereksinim: Windows 10/11, .NET 8 SDK.
