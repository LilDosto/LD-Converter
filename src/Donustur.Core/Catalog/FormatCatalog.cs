using Donustur.Core.Models;

namespace Donustur.Core.Catalog;

public static class FormatCatalog
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "webp", "avif", "bmp", "tiff", "tif", "gif", "ico", "svg", "heic", "heif"
    };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mkv", "avi", "mov", "webm", "flv", "wmv", "m4v"
    };

    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "wav", "aac", "m4a", "flac", "ogg", "opus", "wma"
    };

    private static readonly HashSet<string> DocumentExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "doc", "pptx", "ppt", "xlsx", "xls", "odt", "rtf", "txt", "md", "html", "htm", "epub"
    };

    private static readonly HashSet<string> ArchiveExts = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "tar", "gz", "tgz", "rar"
    };

    public static string NormalizeExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (Directory.Exists(path)) return "folder";

        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpeg" or "jpe" => "jpg",
            "heif" => "heic",
            "markdown" => "md",
            "tgz" => "gz",
            "htm" => "html",
            "tif" => "tiff",
            _ => ext
        };
    }

    public static bool IsDirectory(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    public static bool IsImage(string path) => !IsDirectory(path) && ImageExts.Contains(NormalizeExtension(path));
    public static bool IsVideo(string path) => !IsDirectory(path) && VideoExts.Contains(NormalizeExtension(path));
    public static bool IsAudio(string path) => !IsDirectory(path) && AudioExts.Contains(NormalizeExtension(path));
    public static bool IsDocument(string path) => !IsDirectory(path) && DocumentExts.Contains(NormalizeExtension(path));
    public static bool IsPdf(string path) => !IsDirectory(path) && NormalizeExtension(path) == "pdf";
    public static bool IsArchive(string path) => !IsDirectory(path) && ArchiveExts.Contains(NormalizeExtension(path));

    /// <summary>
    /// Gelen dosya listesine göre çark üzerinde gösterilecek en uygun seçenekleri üretir.
    /// 6 ila 8 dilim ideal bir çark deneyimi sağlar.
    /// </summary>
    public static IReadOnlyList<ToolChoice> GetChoicesForFiles(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
        {
            return GetDefaultChoices();
        }

        bool anyDirs = filePaths.Any(IsDirectory);
        if (anyDirs)
        {
            return new List<ToolChoice>
            {
                new("zip", "ZIP Arşivi Oluştur", "Arşiv", "Archive", "Seçili klasör ve dosyaları ZIP arşivine sıkıştır"),
                new("7z", "7Z Arşivi Oluştur", "Arşiv", "Archive", "Ultra sıkıştırma ile 7Z arşivi oluştur"),
                new("tar_gz", "TAR.GZ Yap", "Arşiv", "Archive", "Linux uyumlu .tar.gz paketi oluştur"),
                new("copy_folder", "Yeni Klasöre Topla", "Dosya", "Folder", "İçeriği yeni bir klasörde organize et")
            };
        }

        bool allImages = filePaths.All(IsImage);
        bool allVideos = filePaths.All(IsVideo);
        bool allAudios = filePaths.All(IsAudio);
        bool allPdfs = filePaths.All(IsPdf);
        bool allDocs = filePaths.All(IsDocument);
        bool allArchives = filePaths.All(IsArchive);

        // Görseller
        if (allImages)
        {
            var list = new List<ToolChoice>
            {
                new("png", "PNG", "Görsel", "Image", "Kayıpsız PNG formatına dönüştür"),
                new("jpg", "JPG", "Görsel", "Image", "Yüksek uyumlu JPG formatına dönüştür"),
                new("webp", "WebP", "Görsel", "Web", "Modern, hafif WebP formatına dönüştür"),
                new("avif", "AVIF", "Görsel", "Sparkle", "Yeni nesil ultra sıkıştırılmış AVIF"),
                new("pdf", "PDF", "Belge", "Pdf", "Görsellerden PDF belgesi oluştur"),
                new("ico", "ICO (İkon)", "Görsel", "Icon", "Windows simgesi (.ico) oluştur")
            };

            if (filePaths.Count == 1)
            {
                list.Add(new("qr_read", "QR Oku", "Araç", "QrCode", "Görseldeki QR kodu tara ve çöz"));
                list.Add(new("compress_image", "Sıkıştır", "Optimizasyon", "Compress", "Kalite kaybı olmadan boyutu küçült"));
            }
            else
            {
                list.Add(new("gif", "Animasyonlu GIF", "Görsel", "Sparkle", "Görsellerden hareketli GIF oluştur"));
                list.Add(new("zip", "ZIP Arşivi", "Arşiv", "Archive", "Tüm görselleri tek ZIP dosyası yap"));
                list.Add(new("compress_image", "Hepsini Sıkıştır", "Optimizasyon", "Compress", "Tüm görselleri optimize et"));
            }
            return list;
        }

        // PDF'ler
        if (allPdfs)
        {
            var list = new List<ToolChoice>
            {
                new("pdf_to_images", "Sayfaları PNG Yap", "Görsel", "Image", "PDF sayfalarını yüksek çözünürlüklü PNG'ye aktar"),
                new("pdf_to_jpg", "Sayfaları JPG Yap", "Görsel", "Image", "PDF sayfalarını JPG resimleri olarak kaydet"),
                new("pdf_to_txt", "Metin Çıkar (TXT)", "Belge", "Text", "PDF içindeki tüm metinleri TXT olarak kaydet"),
                new("pdf_to_docx", "DOCX Yap", "Belge", "Document", "Word belgesine (.docx) dönüştür"),
                new("qr_read", "QR Oku", "Araç", "QrCode", "PDF sayfalarındaki QR kodları tara")
            };

            if (filePaths.Count > 1)
            {
                list.Insert(0, new("pdf_merge", "PDF'leri Birleştir", "Belge", "PdfMerge", "Tüm PDF'leri tek bir dosyada birleştir"));
            }
            else
            {
                list.Add(new("pdf_split", "Sayfaları Ayır", "Belge", "Split", "Her sayfayı ayrı bir PDF yap"));
            }
            list.Add(new("zip", "ZIP Arşivi", "Arşiv", "Archive", "PDF'leri ZIP olarak arşivle"));
            return list;
        }

        // Belgeler (DOCX, PPTX, XLSX, TXT, HTML, MD...)
        if (allDocs)
        {
            return new List<ToolChoice>
            {
                new("pdf", "PDF Yap", "Belge", "Pdf", "Ofis belgesini PDF formatına dönüştür"),
                new("docx", "DOCX Yap", "Belge", "Document", "Modern Word belgesine çevir"),
                new("txt", "Düz Metin (TXT)", "Belge", "Text", "Yalnızca metin içeriğini kaydet"),
                new("html", "HTML Web Sayfası", "Belge", "Web", "Web formatında HTML çıktısı al"),
                new("zip", "ZIP Paketle", "Arşiv", "Archive", "Belgeleri sıkıştırılmış ZIP yap")
            };
        }

        // Videolar
        if (allVideos)
        {
            return new List<ToolChoice>
            {
                new("mp4", "MP4 Yap", "Video", "Video", "Evrensel MP4 (H.264 / AAC) dönüştür"),
                new("mp3", "Sesi MP3 Yap", "Ses", "Audio", "Videodan sesi ayırıp MP3 olarak kaydet"),
                new("gif", "Animasyonlu GIF", "Görsel", "Sparkle", "Videoyu hafif ve canlı GIF yap"),
                new("webm", "WebM Yap", "Video", "Web", "Web uyumlu VP9/WebM formatına dönüştür"),
                new("mkv", "MKV Yap", "Video", "Video", "Yüksek kaliteli MKV konteyneri"),
                new("compress_video", "Video Sıkıştır", "Optimizasyon", "Compress", "Kaliteyi koruyarak dosya boyutunu düşür")
            };
        }

        // Sesler
        if (allAudios)
        {
            return new List<ToolChoice>
            {
                new("mp3", "MP3 Yap", "Ses", "Audio", "Yüksek uyumlu 192kbps MP3"),
                new("wav", "WAV Yap", "Ses", "Audio", "Kayıpsız stüdyo WAV formatı"),
                new("aac", "AAC / M4A", "Ses", "Audio", "Modern yüksek kaliteli AAC"),
                new("flac", "FLAC Yap", "Ses", "Audio", "Kayıpsız FLAC sıkıştırması"),
                new("ogg", "OGG Vorbis", "Ses", "Audio", "Açık kaynak OGG formatı"),
                new("compress_audio", "Sesi Sıkıştır", "Optimizasyon", "Compress", "Ses boyutunu küçült")
            };
        }

        // Arşivler
        if (allArchives)
        {
            return new List<ToolChoice>
            {
                new("extract_folder", "Klasöre Çıkar", "Arşiv", "Unpack", "Arşiv içeriğini klasöre çıkar"),
                new("zip", "ZIP Formatına Çevir", "Arşiv", "Archive", "Arşivi standart ZIP yap"),
                new("7z", "7Z Formatına Çevir", "Arşiv", "Archive", "Ultra sıkıştırmalı 7-Zip yap"),
                new("tar_gz", "TAR.GZ Yap", "Arşiv", "Archive", "Linux uyumlu .tar.gz yap")
            };
        }

        // Karışık dosyalar
        return new List<ToolChoice>
        {
            new("zip", "ZIP Arşivi Oluştur", "Arşiv", "Archive", "Tüm seçili dosyaları ZIP dosyasına topla"),
            new("7z", "7Z Arşivi Oluştur", "Arşiv", "Archive", "Ultra sıkıştırma ile 7Z arşivi oluştur"),
            new("tar_gz", "TAR.GZ Oluştur", "Arşiv", "Archive", "Dosyaları .tar.gz olarak arşivle"),
            new("pdf_merge", "Görsel & PDF Birleştir", "Belge", "PdfMerge", "Desteklenen belgeleri tek PDF yap"),
            new("copy_folder", "Yeni Klasöre Topla", "Dosya", "Folder", "Dosyaları organize bir klasöre kopyala")
        };
    }

    private static IReadOnlyList<ToolChoice> GetDefaultChoices()
    {
        return new List<ToolChoice>
        {
            new("png", "PNG", "Görsel", "Image", "PNG dönüştür"),
            new("jpg", "JPG", "Görsel", "Image", "JPG dönüştür"),
            new("webp", "WebP", "Görsel", "Web", "WebP dönüştür"),
            new("pdf", "PDF", "Belge", "Pdf", "PDF oluştur"),
            new("mp3", "MP3", "Ses", "Audio", "MP3 ses çıkar"),
            new("zip", "ZIP Arşivi", "Arşiv", "Archive", "Dosyaları ZIP arşivine sıkıştır")
        };
    }
}
