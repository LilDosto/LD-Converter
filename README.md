# LD Converter 🚀

> **Yeni Nesil, Optimize & Akıcı Dosya Dönüştürme Aracı**  
> .NET 8, WPF ve modern asenkron mimari ile tasarlanmış yüksek performanslı masaüstü dosya dönüştürücü.

---

## 🌟 Öne Çıkan Özellikler

1. **Sıfır Tıklama (Zero-Click) Çarkı (Radial Wheel):**
   - Masaüstünde veya klasörde herhangi bir dosyayı (veya dosya grubunu) sürüklerken **Shift** (veya **Ctrl**) tuşuna basılı tuttuğunuz anda fare imlecinizin etrafında dairesel işlem çarkı açılır.
   - Dosyayı istediğiniz dilimin üzerine bırakın; dönüştürme anında başlar.

2. **Dinamik ve Akıllı Format Eşleme:**
   - Görseller (PNG, JPG, WebP, AVIF, BMP, TIFF, GIF, ICO, HEIC)
   - Belgeler (DOCX, PPTX, XLSX, ODT, RTF, TXT, HTML, EPUB)
   - PDF İşlemleri (Sayfalara ayırma, PDF & Görsel birleştirme, metin çıkarma, DOCX'e çevirme, sayfaları PNG/JPG olarak dışa aktarma)
   - Medya (MP4, MKV, AVI, MOV, WebM -> MP3, WAV, AAC, FLAC, OGG, GIF)
   - Arşivleme (ZIP, 7Z, TAR.GZ, RAR)
   - Karekod (QR Code) okuma ve tek tıklamayla yüksek çözünürlüklü QR üretme.

3. **Atomik Evreleme (ExportTransaction):**
   - Dosyalar önce güvenli bir geçici alanda işlenir; işlem başarıyla tamamlandığında hedef klasöre aktarılır. Yarım kalmış veya bozuk dosyalar sisteminizi kirletmez.

4. **Fluent UI / Dark Theme & Tekil Örnek (Single Instance):**
   - Windows 11 Fluent tasarım dili, koyu tema, özel LD Converter simgesi, donanım hızlandırmalı WPF vektörel çizim motoru.
   - Aynı anda birden fazla örnek açılmasını engeller; pencere kapatıldığında arka planda gereksiz süreç bırakmaz.

---

## ⚡ Çalıştırma

### Hazır Derlenmiş Sürümden Başlatma:
`publish/LDConverter.exe` dosyasını çift tıklayarak doğrudan çalıştırabilirsiniz.

### Kaynak Koddan Çalıştırma:
```bash
dotnet run --project src/Donustur.UI/Donustur.UI.csproj
```

### Testleri Çalıştırma:
```bash
dotnet test
```
