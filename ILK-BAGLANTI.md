# İlk bağlantı ve yayın durumu

## WARP cihaz kaydı

23 Kasım 2026 incelemesinde Cloudflare’ın tüketici Windows kılavuzu resmî WARP istemcisini kurmayı ve istemcide onay vermeyi tarif ediyor. Yayımlanan Zero Trust API’sindeki kayıt işlemleri bir Cloudflare hesabına bağlı mevcut kayıtları listeler, getirir, siler veya iptal eder. Bu referansta üçüncü taraf Windows uygulamasına tüketici cihazı oluşturan veya kullanılabilir WireGuard profili veren bir uç nokta bulunamadı. Zero Trust’ın kılavuzundaki kayıt, kurum kurulumu ve yetkilendirmeyle Cloudflare One Client üzerinden yürür.

- [Tüketici Windows kurulumu](https://developers.cloudflare.com/warp-client/get-started/windows/)
- [Zero Trust kayıt API’si](https://developers.cloudflare.com/api/resources/zero_trust/subresources/devices/subresources/registrations/)
- [Cloudflare One kayıt kılavuzu](https://developers.cloudflare.com/cloudflare-one/team-and-resources/devices/cloudflare-one-client/deployment/manual-deployment/)

Cloudflare uygulama koşullarının 2. bölümü kişisel, devredilemez bir uygulama lisansı tanımlar ve uygulamanın tersine mühendisliğini sınırlar. Belgelenmemiş bir uç noktanın üçüncü taraf dağıtımına izin verildiğini veya erişimin kalıcı olacağını doğrulayamadık. Yerel WireGuard anahtarı üretmek tek başına bu anahtarı Cloudflare ağına kaydetmez. [Cloudflare uygulama koşulları](https://www.cloudflare.com/application/terms/)

Önizleme mevcut çift yığın WireGuard profilini kullanır. Çift tıklama kurulum penceresini açar. Sing-box 1.14.4, Discord süreç kuralları ve TUN yapılandırması mevcut koddan kullanılır. Cloudflare’ın yerel proxy modu resmî istemciyi ve proxy yapılandırmasını gerektirir. Discord sesinin uçtan uca çalışması bu yolla doğrulanmadı. [Cloudflare WARP modları](https://developers.cloudflare.com/warp-client/warp-modes/)

## Teknik kabul

Bu dalın canlı kabulü açık. Tarihsel v0.2.0 matrisi **RELEASE-ACCEPTANCE.json** ve **FINAL-KABUL.md** dosyalarındadır. Windows 10 temiz kurulum, DNS izolasyonu, süreç kimliği kaybolan UDP’nin doğrudan çıkışı ve TUN çökmesi sonrası toparlanma üretim kabulünü engellemeye devam eder. Bu sürümde canlı Discord oturumu, ses, gerçek TCP/UDP ve TUN kapanışı yeniden ölçülmeden önceki canlı sonuçlar bu derlemeye taşınamaz.

| Kontrol | Önizleme sonucu |
| --- | --- |
| Windows 11 Pro x64 26300, mevcut kullanıcı | ZIP’ten çıkartılan EXE kurulum penceresini açtı. İkinci EXE işlemi sıfır koduyla kapandı. Yönetici onayı sonrası profil seçimi bu turda denenmedi. |
| Motor ve trafik | Sabit motor hash’i, profil ve süreç kuralları, iki yerel WireGuard ucu arasında IPv4/IPv6 TCP/UDP ve Discord dışı doğrudan çıkış testleri geçti. Testler Windows TUN kullanmadı. |
| Motor kapanışı | TUN olmadan kontrollü kapanış ve denetleyici zorla kapatıldığında motorun sonlanması geçti. |
| DNS | Windows, UDP/53 ve DoH teşhis sorguları A kaydı aldı. Önizlemenin canlı TUN altında sistem DNS izolasyonu ve Discord DoH sınanmadı. |
| Yayın kontrolü | 15 paketleme politikası testi geçti. Üretim paketleme komutu eski kaynak parmak izi ve tamamlanmamış kabul ölçütleri nedeniyle engellendi. |
