# Discord Tunnel Windows 0.2.0

Windows bildirim alanından yönetilen, kurulu Discord masaüstü uygulaması ve kendi güncelleyicisi için bir WireGuard tüneli. Ağ motoru sing-box 1.14.4. Hedef Windows 10 ve 11 x64.

Süreç kuralı sürüm numarasına sabitlenmez. Standart Discord kökündeki ana uygulama ve güncelleyici ile bütün sayısal app-* sürüm klasörlerinin altındaki EXE yardımcıları seçilir. Böylece ses, ekran paylaşımı, modül yardımcıları ve sonraki sürüm klasörleri otomatik kapsanır. Başka dizinlerdeki Discord.exe, Update.exe veya yardımcı dosyalar eşleşmez. Diğer süreçlerin varsayılan çıkışı doğrudandır. TUN modeli bu doğrudan trafiği de işleyebilir.

GitHub sürümlerindeki Windows ZIP’i motoru içerir. ZIP’i çıkartıp **DiscordTunnel.exe** dosyasını aç ve bildirim alanından tüneli başlat. Kişisel WireGuard profilini kendi bilgisayarında seç. Windows yönetici onayından sonra profil penceresi otomatik açılır. [Kullanım](KURULUM.md)

## Codex ile devam

Son canlı denemede Windows 11 ve Superbox üzerinde TUN açıldı, WireGuard eşinden el sıkışma yanıtı geldi ve diğer bir HTTPS isteği başarılı oldu. Yeni TUN normal kapanışta kaldırıldı. Yerel testte motorun süreç yolu eşlemesi ve gerçek WireGuard eşleri üzerinden TCP/UDP ve IPv4/IPv6 doğrulandı. Mobil erişim noktası yeni motorun canlı denemesi sırasında görünmüyordu.

Sıradaki adım gerçek Discord bağlantısının tünel çıkışını doğrulamak. Ardından iki Wi-Fi arasında geçiş, ses, çökme sonrası ağın toparlanması, yük altında tünel açık/kapalı karşılaştırması ve Windows 10 x64 kabulü var. Ölçümler ve yayın ölçütleri [tasarım ve test kaydında](TASARIM-VE-TEST.md). Üretim ZIP'i canlı kabul tamamlanınca hazırlanır.

## Derleme ve yerel kontrol

Windows üzerinde PowerShell ile depo kökünden çalıştır.

    .\Build.ps1 -Test

Derleme, Windows'un .NET Framework x64 derleyicisini kullanır ve resmî sing-box arşivini sabit SHA-256 değeri ile doğrular. Çalıştırılabilir dosya **Windows-x64** klasöründe oluşur. Bu klasör yerel derleme çıktısıdır. TUN çalıştırmadan Windows DNS, port 53 ve DoH kontrolünü eklemek için **-Diagnose** kullanılabilir. Paket oluşturmak için **-Package** ekle. Paket **dist** klasöründe oluşur.

Canlı deneme için kullanıcı kendi WireGuard profilini seçer ve Windows yönetici onayını verir. Profil, arşivler, motor dosyaları ve yerel test çıktıları depoya alınmaz.
