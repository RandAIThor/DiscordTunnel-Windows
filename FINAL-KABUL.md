# Final kabul incelemesi — 20 Kasım 2026

**Teknik inceleme sonucu: final kabulü başarısız.** İnceleme sırasında yeni ZIP oluşturulmadı ve sürüm yayımlanmadı. Kullanıcının daha sonra mevcut paketi bilinen sınırlamalarıyla yayımlama kararı [YAYIN-KARARI.md](YAYIN-KARARI.md) içindedir; aşağıdaki test sonuçları bu kararla değişmez.

Uygulamanın veri yolunda `5cc4dce` sürümündeki Engine.cs, TunnelConfig.cs ve Tray.cs kullanıldı. Motor sing-box 1.14.4. Bu inceleme üretim motorundaki açıkları giderdiğini iddia etmez. Yeni kaynaklar, tekrar üretilebilir test araçları ve eksik kabul kaydıyla paket oluşturmayı engelleyen kontroldür. Ham günlükler, profil ve kişisel ağ bilgileri depoya alınmaz.

<a id="canli-test"></a>
## Canlı test

Windows 11 Pro x64 build 26300 üzerinde kurulu Discord 1.0.9269 kullanıldı. Mevcut WireSock geçici durduruldu; bağımsız uygulamanın motoru kendi TUN bağdaştırıcısını açtı. Kullanıcı UAC onaylarını kendisi verdi. Discord kurulmadı, güncellenmedi veya dosyaları değiştirilmedi.

- İki kayıtlı Wi-Fi arasında A → B → A geçişi aktif ses oturumunda tamamlandı. Fiziksel Wi-Fi bağlantısı geçişleri 1.457 ve 1.172 saniye sürdü. Bu sayılar sesin toparlanma süresi değildir; ses kesintisinin süresi ayrıca ölçülmedi.
- Kullanıcı her iki geçişten sonra iki yönlü sesin çalıştığını bildirdi. Motorun süreç kimliği ve seçilen çıkış kayıtlarının eşleştirilmesinde Discord'a ait 5 UDP ve 13 TCP akışının WireGuard yoluna seçildiği görüldü. Bu, fiziksel arayüzde bütün olası kaçışların yokluğuna ilişkin paket yakalama kanıtı değildir.
- Kullanıcının mobil ağda bildirdiği 148 ve 84 ms, eşleştirilmiş performans testi sayılmadı. Kullanıcı aynı bağlantıda başka telefon kullanımı olabileceğini belirtti. Discord'un yeşil bağlantı göstergesinden son ping ve giden paket kayıp oranı okunabildiği ayrıca doğrulandı. Önceki WireSock bağlantısına dönüldükten sonraki 94 ms ve %0.0 değeri yeni motor sonucu olarak kullanılmadı.
- Önceki ayrı bir geçiş turunda Wi-Fi bağlantıları 1.358 ve 1.140 saniye sürdü. Yeni motor açıkken diğer uygulamayı temsil eden HTTPS isteği üç aşamada da HTTP 200 döndü. Tek başına bu test tüm diğer uygulamaların izolasyonunu kanıtlamaz.
- Sonraki bir denemede ikinci ağa bağlantı 14 saniyelik test sınırını aştı. Daha sonraki 28.5 saniyelik girişim de tamamlanamadı. Eski tarama listesi görünürlük kanıtı sayılmadı; etkin WLAN taraması sonrasında ağ tekrar bağlanılabilir oldu. Başarısız girişimler sonuçlardan çıkarılmadı.
- Normal kapanışta motor sıfır koduyla çıktı, yeni TUN kaldırıldı, arayüzlerin WeakHostSend/Forwarding ve DNS sunucusu ayarları başlangıçla eşleşti. Önceki WireSock profiline dönüş ve bağımsız HTTPS yanıtı kontrol edildi. DNS önbelleği eşitliği ölçülmedi.

Canlı harness, üretim Discord kurallarını koruyup yalnız geri döngüde dinleyen ek bir tanılama SOCKS girişi kullanır. Bu girişin istekleri açıkça WireGuard'a yönlendirilir. Tanılama isteğinin başarısı ile gerçek Discord sürecinin kapsamı birbirinin yerine kullanılmaz.

<a id="dns"></a>
## DNS

Yeni motorun tünel içindeki gerçek DoH isteği A yanıtı verdi. Bu adres `curl --resolve` ile aynı tünel üzerinden Discord gateway HTTPS isteğine verildiğinde geçerli `wss://` gateway yanıtı alındı. İki ayrı turda DoH/Discord HTTPS süreleri sırasıyla 385/527 ve 420/330 ms idi.

Aynı turlarda normal isim çözümlemesini kullanan tünel içi HTTPS isteklerinde zaman aşımı ve bir TLS doğrulama hatası görüldü. Sertifika kontrolü kapatılmadı. Bu sonuç, tanılama isteğinde gerçek DoH ile erişimin mümkün olduğunu gösterir. Kurulu Discord'un paylaşılan Windows DNS istemcisindeki sorgularını süreç bazında DoH'ye geçiren bir özellik eklenmedi.

Bu günkü ağda normal DNS yanıtları da alındı. Port 53'ün kontrollü olarak yanıtsız bırakıldığı yeni bir canlı kabul yapılmadı; 30 saniyelik DNS arızası kabulü açık kalır. Eski mobil DNS arızası ölçümü bu koşulun yerine geçirilmez.

<a id="gecikme"></a>
## Gecikme

Mobil ağ kullanılmadı. Tek ofis Wi-Fi'sinde aynı `https://1.1.1.1/cdn-cgi/trace` hedefiyle üç eşleştirilmiş tur çalıştırıldı. Her tur, koşul ve akış başına 20 yeni HTTPS isteği gönderildi. Ölçüm TCP/TLS ve HTTPS tamamlanma süresidir; ICMP/UDP RTT veya Discord ses pingi değildir. Zaman aşımı bağlantı için 2, tüm istek için 3 saniyeydi.

Yük koşulunda başka bir süreç, Cloudflare'ın [resmî hız testi yükleme ucuna](https://github.com/cloudflare/speedtest) 12 MiB sıfır bayt gönderdi; kullanıcı verisi gönderilmedi. Altı yüklemenin tamamı HTTP 200 ile bitti. Gerçek hız 490,396–520,374 bayt/sn, toplam süre 24.18–25.66 saniyeydi. Yük altında sayılan bütün örneklerde yükleme işlemi çalışıyordu. Bu, yaklaşık 4 Mbit/sn sabit yük testidir; hattın tamamının doyurulduğu iddia edilmez.

| Tur | Akış | Boşta p50/p95 ms | Başarısız/N | Yük altında p50/p95 ms | Başarısız/N |
| --- | --- | --- | --- | --- | --- |
| 1 | Tünel kapalı, kontrol | 241.83 / 1906.42 | 4/20 | 253.35 / 557.30 | 0/20 |
| 1 | Tünel açık, Discord dışı kontrol | 257.25 / 323.45 | 0/20 | 234.93 / 300.96 | 0/20 |
| 1 | Tünel açık, zorlanmış WireGuard tanılama akışı | 238.77 / 273.55 | 0/20 | 254.35 / 370.74 | 0/20 |
| 2 | Tünel kapalı, kontrol | 245.98 / 305.11 | 0/20 | 287.75 / 462.44 | 0/20 |
| 2 | Tünel açık, Discord dışı kontrol | 262.43 / 381.79 | 0/20 | 248.38 / 412.19 | 0/20 |
| 2 | Tünel açık, zorlanmış WireGuard tanılama akışı | 239.91 / 340.99 | 0/20 | 272.47 / 451.83 | 0/20 |
| 3 | Tünel kapalı, kontrol | 246.80 / 283.56 | 0/20 | 250.29 / 406.83 | 0/20 |
| 3 | Tünel açık, Discord dışı kontrol | 254.42 / 268.11 | 0/20 | 264.00 / 463.98 | 0/20 |
| 3 | Tünel açık, zorlanmış WireGuard tanılama akışı | 232.32 / 263.16 | 0/20 | 238.65 / 351.93 | 0/20 |

Yüzdelikler başarılı örnekler üzerinde nearest-rank yöntemiyle hesaplandı. Başarısız istekler kayıp RTT örneği gibi yüzdeliklere katılmadı; ayrı sütunda tutuldu. İlk tur kapalı/boşta koşulunun 2–5. isteklerinde curl 28 bağlantı zaman aşımı oluştu. Bu dört başarısızlık korunur.

Toplam 360 istekten tünel içindeki 120'si ve tünel açık Discord dışı 120'si başarılıydı. Kontrol akışının eşleştirilmiş p50 farkı boşta +7.62…+16.46 ms, yük altında -39.37…+13.71 ms idi. Sıra her tur kapalı → açık oldu; sıranın rastgeleleştirildiği iddia edilmez. Ölçümü yürüten ilk araç yalnız tünel yolunu önceden ısıttı. Depodaki tekrar aracı artık her iki yolu ısıtır; bu düzeltilmiş araçla canlı ölçüm henüz tekrarlanmadı. Uçtan uca UDP paket kaybı ve yeterli bağımsız tekrar olmadan “gecikme sorunu çözüldü” sonucu verilmez.

<a id="surec-kimligi-kaybi"></a>
## Süreç kimliği kaybı

`Reproduce-AttributionFailure.ps1` gerçek sing-box motoruyla, yalnız yerel ve sentetik anahtar/soketlerle çalışır; TUN açmaz. Gerçek Discord dosyaları yerine ayrı geçici kökte eşleşen bir EXE fixture'ı kullanır.

1. Gönderen canlıyken üretim süreç deseni bu yolu seçti. Yerel WireGuard eş soketinde 148 baytlık handshake initiation görüldü; doğrudan hedef datagramı almadı.
2. Aynı motor, Windows debugger API'siyle yalnız test motoruna bağlanılarak geçici bekletildi. Eşleşen fixture UDP datagramını gönderip kapandı. Motor bırakıldığında süreç tablosunda gönderen kalmamıştı.
3. Datagram doğrudan hedefe ulaştı. Program `FAIL` ve çıkış 1 üretti; dış test komutu da başarısız döndü.

Bu, süreç kimliği çözümlenemeyen durumda üretimdeki `final: direct` yolunun kaçışı mümkün kıldığının tekrar üretimidir. Tam TUN veya gerçek Discord kapanış testi olarak sunulmaz. Tüm bilinmeyen süreçleri reddetmek de çözüm sayılmaz; başka uygulamaların ve paylaşılan DNS hizmetinin trafiğini engelleyebilir.

<a id="mimari"></a>
## Mimari

Mevcut TUN adayı üç kritik koşulu karşılamıyor:

- Motor, Discord dışındaki tarayıcı, dosya eşitleme ve uygulama trafiğini de işler. Canlı gözlem bunu doğruladı; `direct` çıkış seçilmesi motoru atlamak değildir.
- Süreç atfı kaybolunca yukarıdaki yerel testte doğrudan çıkış gerçekleşti.
- Sabitlenen [sing-tun Windows kaynağı](https://github.com/SagerNet/sing-tun/blob/040db71fcd33/tun_windows.go) açılış/rota güncellemesi/kapanış sırasında DNS önbelleğini temizliyor. [Arayüz bağlama kodu](https://github.com/SagerNet/sing-tun/blob/040db71fcd33/tun_bind_windows.go), başka arayüzlerdeki WeakHostSend değerlerini değiştirebiliyor ve normal kapanışta geri alıyor. Test makinesinde başlangıçta bu değerler kapalıydı. Önceden açık değerle çökme kurtarması ve temiz kaldırma tamamlanmadı.

Üretim alternatifi, [WFP ALE connect/bind redirection](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/using-bind-or-connect-redirection) tabanında uygulama kimliği, bağlı/bağlantısız UDP, IPv4/IPv6, süreç ömrü, özgün hedef ve hizmet çökmesini birlikte ele almalı. Bu bir ayar değişikliği değil, yeni sürücü/hizmet veri yoludur. WFP'ye geçiş de paylaşılan Windows DNS atfını kendiliğinden çözmez.

[WinDivert 2.2'nin resmî belgeleri](https://reqrypt.org/windivert-doc.html), hazır sürücülerin imzalı olduğunu; fakat NETWORK katmanında süreç kimliğinin bulunmadığını, FLOW olaylarının engellenemediğini ve SOCKET olaylarının değiştirilip enjekte edilemediğini belirtir. Süreç kimliği/işlem ömrü yarışı da belgelenmiştir. Bu nedenle WinDivert mevcut kodun yerine doğrulanmış bir çözüm gibi eklenmedi. Her hazır imzalı alternatif için seçici yakalama ve ilk paket/kimlik kaybı kabulü yeniden gerekir.

Yeni özel WFP sürücüsünün olağan Windows güvenlik ayarlarında dağıtımı için Microsoft imzalama yolu gerekir. [Microsoft'un sürücü imzalama gereksinimleri](https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/code-signing-reqs), ilgili Hardware Dev Center hesabına bağlı geçerli EV sertifikasını açıklar. Bu oturumda kullanılabilir yayıncı hesabı/sertifikası bulunmadığı kullanıcı tarafından bildirildi. Secure Boot, HVCI, sertifika doğrulaması veya sürücü imza denetimi kapatılmadı. Yeni WFP sürücüsü yazıldığı, imzalandığı veya test edildiği iddia edilmez.

<a id="isletim-sistemi"></a>
## İşletim sistemi

Erişilebilir fiziksel makine Windows 11 x64. Hazır Windows 10 VM veya Sandbox yok. Hyper-V ve ilgili yönetim bileşenleri kurulu değil. Yaklaşık 15.6 GiB bellek ve 641 GiB boş disk bir deneme ortamına elverişli; fakat Hyper-V kurulumu/yeniden başlatma ve resmî Windows 10 kurulum ortamı/lisans adımı tamamlanmadı. Windows Server CI, WSL veya Windows 11 testi Windows 10 kabulü yerine sayılmadı.

<a id="kapsam"></a>
## Kapsam ve açık kabul

Gerçek 1.0.9269 süreci ve canlı ses artık gözlendi. Önceki/gelecek sürüm ve iç içe modül örnekleri desen testidir; gerçek güncelleme sırasında yeni yardımcıların paket kapsamı ve tam sürüm geçişi hâlâ açık. Stable dışı PTB/Canary ve özel kurulum yerleri kabul edilmedi. Mevcut oturumla çalışma denendi; sıfırdan oturum açma yapılmadı. Yerel TCP/UDP IPv4/IPv6 testleri tekrar geçti; canlı sağlayıcıda bütün IPv6 yollarının kabulü açık.

## Tekrar çalıştırma ve yayın kontrolü

Yerel regresyonlar:

```powershell
.\Build.ps1 -Test -Diagnose
.\ReleaseGate.Tests.ps1
.\Reproduce-AttributionFailure.ps1  # Mevcut motorda başarısız olması beklenen gerçek koruma testi.
```

Canlı test için `Build-LiveAcceptance.ps1` ayrı, pakete alınmayan harness'ı derler. Harness'a özel yerel sonuç klasörü ve yerel profil yolu verilir. `go` dosyası TUN'u başlatır; `stop` dosyası durdurur; otomatik üst süre beş dakikadır. `Invoke-PairedBenchmark.ps1` profil yolu ve hedef Wi-Fi profilini parametreyle alır. Gerekirse yalnız test sırasında mevcut WireSock bağlantısını durdurup geri çağıran isteğe bağlı parametre vardır; uygulamanın WireSock bağımlılığı yoktur. Diğer VPN'ler önceden durdurulmalıdır. UAC onayı ve gerçek ses doğrulaması kullanıcıya aittir. Araç kişisel profil veya ham motor günlüğünü yayınlamaz.

`Build.ps1 -Package`, `RELEASE-ACCEPTANCE.json` içindeki zorunlu bütün kriterler geçmeden arşiv oluşturmaz. Kaynak parmak izi değiştiğinde eski kayıt geçersizdir; başarısız satırı silmek veya atlamak kabulü açmaz. Kayıt incelemeye yardımcıdır; elle yazılan “passed” ifadesi tek başına test kanıtı değildir. Bu kontrolün 15 sentetik politika testi, uygulamanın 15 kabul testini geçtiği anlamına gelmez.

İnceleme sırasında GitHub v0.2.0 taslağındaki ZIP korundu: 32,935,917 bayt, SHA256 `F8B1A319B22CB5CA1DE3D51A4FB05893108FF25CB6CB086CF7BCF06C7F05DD20`. Bu ZIP teknik kabulü tamamlanmış paket değildir; sonraki yayın onayı ayrı karardır. Eski ayrı Windows ZIP'inin SHA256 değeri `2D1637B248909190149957CDB89DD9AA0BCEE8CBD9A7BB8B3454CBBE603B3ED8` olarak korundu. Bu inceleme anında yeni anonim indirme doğrulaması yapılmamıştı.
