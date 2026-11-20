# Discord Tunnel Windows 0.2.0

**0.2.0 yayımlandı:** [Windows x64 ZIP'i indir](https://github.com/RandAIThor/DiscordTunnel-Windows/releases/download/v0.2.0/DiscordTunnel-0.2.0-Windows-x64.zip) · [Sürüm açıklaması ve testler](https://github.com/RandAIThor/DiscordTunnel-Windows/releases/tag/v0.2.0). Kullanıcı mevcut paketin bilinen sınırlamalarıyla yayımlanmasını ve geri bildirimlere göre güncellenmesini onayladı. [Yayın kararı](YAYIN-KARARI.md). Teknik test sonuçları ve açık koşullar [incelemede](FINAL-KABUL.md) korunur.

Windows bildirim alanından yönetilen, kurulu Discord masaüstü uygulaması ve kendi güncelleyicisi için bir WireGuard tüneli. Ağ motoru sing-box 1.14.4. Hedef Windows 10 ve 11 x64.

Süreç kuralı sürüm numarasına sabitlenmez. Standart Discord kökündeki ana uygulama ve güncelleyici ile bütün sayısal app-* sürüm klasörlerinin altındaki EXE yardımcıları seçilir. Böylece ses, ekran paylaşımı, modül yardımcıları ve sonraki sürüm klasörleri otomatik kapsanır. Başka dizinlerdeki Discord.exe, Update.exe veya yardımcı dosyalar eşleşmez. Diğer süreçlerin varsayılan çıkışı doğrudandır. TUN modeli bu doğrudan trafiği de işleyebilir.

Windows ZIP'i motoru içerir. ZIP'in tamamını çıkartıp **DiscordTunnel.exe** dosyasını aç ve bildirim alanından tüneli başlat. Kişisel WireGuard profilini kendi bilgisayarında seç. Windows yönetici onayından sonra profil penceresi otomatik açılır. [Kullanım](KURULUM.md)

## Codex ile devam

20 Kasım canlı denemesinde Windows 11 üzerinde gerçek Discord TCP/UDP süreç seçimi ve kullanıcı doğrulamasıyla iki yönlü ses gözlendi. Aktif ses oturumunda iki Wi-Fi arasında gidiş/dönüş tamamlandı. Ofis Wi-Fi'sinde üç eşleştirilmiş boşta/yük altında HTTPS turu çalıştırıldı. Mobil ağdaki tekil ping değerleri performans kabulü sayılmadı. Yeni TUN normal kapanışta kaldırıldı ve önceki bağlantı geri getirildi.

Gerçek adversarial test, süreç kimliği kaybolan datagramın doğrudan çıkabildiğini gösterdi. Bu bulgu ve sistem DNS etkisi bilinen sınırlamalardır; Windows 10 kabulü tamamlanmadı. Kullanıcı mevcut sürümün bu test kapsamıyla yayımlanmasını istedi. WFP ve hazır imzalı sürücü seçeneğinin sınırları [incelemede](FINAL-KABUL.md#mimari) açıklanır. Tarihsel ölçümler [tasarım ve test kaydında](TASARIM-VE-TEST.md) korunur.

## Derleme ve yerel kontrol

Windows üzerinde PowerShell ile depo kökünden çalıştır.

    .\Build.ps1 -Test

Derleme, Windows'un .NET Framework x64 derleyicisini kullanır ve resmî sing-box arşivini sabit SHA-256 değeri ile doğrular. Çalıştırılabilir dosya **Windows-x64** klasöründe oluşur. Bu klasör yerel derleme çıktısıdır. TUN çalıştırmadan Windows DNS, port 53 ve DoH kontrolünü eklemek için **-Diagnose** kullanılabilir. **-Package**, zorunlu kabul koşulları geçmeden ve kaynak parmak izi eşleşmeden paket oluşturmaz. Mevcut durumda bu komut bilerek engellenir.

Canlı deneme için kullanıcı kendi WireGuard profilini seçer ve Windows yönetici onayını verir. Profil, arşivler, motor dosyaları ve yerel test çıktıları depoya alınmaz.
