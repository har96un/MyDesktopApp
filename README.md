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
- **Yakalama izi (F11):** Bir yakalama noktasında imleci ~0,5 sn bekletince nokta alınır (yeşil +). İmleç o noktanın
  yatay/dikey hizasına, çizgi uzantısına ya da dikine gelince hizalanır; iki noktanın izlerinin kesişimine yakalanır.
  Temel noktadan, alınan çizgiye paralel ve dik yönler de izlenir.
- **Katmanlar:** Yeni katman, silme (nesneleri silerek veya "0"a taşıyarak), yeniden adlandırma, renk, görünürlük,
  geçerli katman, seçili nesneleri katmana taşıma
- **Gruplar:** Grupla (G) / Grubu çöz (UG). Gruba tıklamak tümünü seçer, Ctrl+tık tek nesne. DWG/DXF'e grup olarak yazılır.
- **Buda / Uzat / Yuvarla / Pah:** TR, EX, F, CHA. Yuvarla ve Pah iki çizgide veya polyline köşelerinde (P: tüm köşeler) çalışır.
- **Ölçülendirme:** Doğrusal (DLI), Paralel (DAL), Yarıçap (DRA), Çap (DDI), Açı (DAN). DWG/DXF'e gerçek ölçü nesnesi olarak yazılır.
- **Tarama (H):** Kapalı alanın içine tıklayın; SOLID, ANSI31, ANSI37, LINE, NET desenleri. Delikler otomatik bulunur.
- **Özellikler paneli:** Seçili nesnenin katmanı, rengi ve geometrisi (koordinat, uzunluk, açı, yarıçap, metin, ölçü/tarama ayarları) doğrudan düzenlenir.
- **Profil kütüphanesi (Ctrl+L):** Kesitleri küçük resimlerle listeler; arama, çift tıkla ekleme ya da çizime sürükle-bırak.
  Seçili kesit ağırlık merkezi 0,0 olacak şekilde kütüphaneye kaydedilir.
- **Toplu işlem:** Çok sayıda DWG/DXF'i asal eksene hizalar, döndürür, aynalar, ölçekler, 0,0'a taşır, DXF/DWG olarak kaydeder;
  isteğe bağlı her dosya için PDF raporu ve tüm kesitlerin özellik tablosu (CSV).
- **PDF kesit raporu (Ctrl+P):** A4, firma logosu ve adı, ölçekli çizim, ağırlık merkezi ve asal eksenler, genişlik/yükseklik ölçüleri, özellik tablosu.
- **Sekmeler:** Her DWG/DXF kendi sekmesinde açılır (çoklu seçimle ya da sürükle-bırakla birden fazla dosya). Ctrl+Tab sekmeler arası geçiş, Ctrl+W kapatır, orta tık da kapatır. Her sekmenin kendi geri alma geçmişi ve görünümü vardır.
- **Kayıt sürümleri:** DXF 2018 / 2013 / 2010 / 2007 / 2004 / 2000 / R14, **R12 (CNC/lazer/abkant uyumlu)**, ikili (binary) DXF; DWG 2018 / 2013 / 2010 / 2004 / 2000 / R14. Kaydet, dosyanın açıldığı sürümü korur.
- **Çizim denetimi (DENETLE):** Açılışta şüpheli nesneleri (çizimden çok uzak, çizime göre aşırı büyük daire/yay, neredeyse tam daire yay, sıfır boylu, kopya) bulur; seç / gizli katmana taşı / sil seçenekleri sunar.
- **Blok açma:** Aynalanmış, eşit olmayan ölçekli, döndürülmüş ve dizi (MINSERT) bloklar doğru açılır.
- **Son açılanlar, otomatik kayıt ve kurtarma:** Beklenmedik kapanmada bir sonraki açılışta çizim kurtarılır.
- **Sağ tık menüsü:** Komut yokken sağ tık bağlam menüsü açar; komut sırasında Enter görevi görür.
- **Ayarlar:** Firma/logo, kütüphane klasörü, otomatik kayıt aralığı, ölçü ve tarama varsayılanları,
  komutlara ek kısa adlar ve klavye kısayolları (ör. Ctrl+Shift+T → TRIM).

## Koordinat girişi

| Yazım | Anlamı |
|-------|--------|
| `x,y` | Mutlak koordinat |
| `@dx,dy` | Son noktaya göre göreli |
| `@uzunluk<açı` | Kutupsal (açı derece) |
| `25` | İmleç yönünde 25 birim |

## Kurulum

Her `main` gönderiminde GitHub Actions **ProfilCAD-Setup.exe** kurulum dosyasını üretir ve
**Releases → latest** altına yükler (ayrıca Actions sayfasında artifact olarak da bulunur).
Kurulum .NET gerektirmez (self-contained), yönetici yetkisi istemeden kullanıcı klasörüne kurulabilir.

## Çalıştırma (geliştirme)

```bash
dotnet run --project src/MyDesktopApp
```

Ya da `MyDesktopApp.sln` dosyasını Visual Studio 2022 ile açıp **F5**'e basın.
Gereksinim: Windows 10/11, .NET 8 SDK.
