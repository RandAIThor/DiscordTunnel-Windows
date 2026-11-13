# Discord Tunnel prototipi

## Mimari seçenekleri ve karar

İlk aday sing-box 1.14.4 ve WireGuard. Windows TUN, IPv4 ve IPv6 TCP/UDP bağlantılarını yakalar. Motor, standart Discord kökü altındaki Discord ve güncelleyici yollarını WireGuard eşine yönlendirir. Diğer trafik fiziksel arayüz üzerinden doğrudan çıkışa yönlendirilir. Sistem genelindeki rotalar TUN çalışırken etkilenebileceği için diğer uygulamaların motoru tamamen atlaması bu modelde garanti edilemez. `dns_mode: disabled` ve `strict_route: false` ile TUN için yerel DNS ataması ve genel DNS engellemesi kapatılır. [TUN seçenekleri](https://sing-box.sagernet.org/configuration/inbound/tun/) · [Süreç yolu eşleme](https://sing-box.sagernet.org/configuration/route/rule/) · [WireGuard endpoint](https://sing-box.sagernet.org/configuration/endpoint/wireguard/)

WFP seçeneği, uygulama kimliğine göre ALE katmanında uygun TCP/UDP bağlantılarını bir kullanıcı modu hizmetine yönlendirebilir. Diğer uygulamaların paketleri kullanıcı modu ağ motoruna uğramadan devam edebilir. Bağlı ve bağlantısız UDP, özgün hedefin saklanması, IPv6 ve hizmet çökmesi ayrı ayrı doğrulanmalıdır. Üretim için Microsoft imzalama süreci, sürücü kurulumu ve geri alma, Driver Verifier, Secure Boot/HVCI ve Windows güncellemelerinde regresyon testi gerekir. TUN izolasyon, gecikme, toparlanma veya DNS ölçütlerinden kalırsa WFP için önce bu dar kapsamı doğrula. [Microsoft WFP yönlendirmesi](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/using-bind-or-connect-redirection) · [Sürücü imzalama](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-)

Uygulama içi HTTP/SOCKS vekili Windows rotalarına dokunmadan TCP için denenebilir. Discord’un kendi güncelleyicisini ve ses UDP’sini şeffaf biçimde kapsaması gösterilmediğinden ana aday değildir.

## Riskler ve prototip sınırları

Sabitlenen Windows TUN bağımlılığı, DNS modu kapalı olsa da otomatik rotaları açarken, rota seçenekleri güncellendiğinde ve normal kapanışta Windows DNS önbelleğini temizler. Canlı DNS testinde fiziksel bağdaştırıcıların ayarlarıyla birlikte önbellek davranışı da ölçülmeli. Motor, başka arayüzlerde etkin olan WeakHostSend ayarını geçici olarak kapatabilir ve normal kapanışta geri yükler. Motor zorla sonlandırılırsa bu geri yükleme kodu çalışmaz. Test bilgisayarında sorgulanan 14 arayüz/aile kaydında WeakHostSend kapalıydı. Üretim kabulü için önceden açık olduğu bir deneme sistemi de kullanılmalı. [Windows TUN kaynak kodu](https://github.com/SagerNet/sing-tun/blob/040db71fcd33/tun_windows.go) · [Arayüz bağlama kaynak kodu](https://github.com/SagerNet/sing-tun/blob/040db71fcd33/tun_bind_windows.go)

| Risk | Uygulama ve doğrulama |
| --- | --- |
| Discord güncellenirken iki sürüm bir arada | Kullanıcının standart `%LOCALAPPDATA%\Discord` köküne sabitlenmiş, sürüm numarası desenli tam dosya yollarını eşleştir. Diskte sonradan beliren sürüm için motoru yeniden yapılandırmak gerekmez. Başka bir klasördeki `Discord.exe` ve `Update.exe` doğrudan kalmalıdır. Standart kurulum dışındaki dağıtımlar bu deneye dahil değildir. |
| Yanlış süreç tespiti veya IPv6 kaçışı | Her iki ailede süreç yolu ve fiziksel arayüz/tünel eşi üzerinden paketin geçtiği yer ölçülür. Windows DNS istemcisi gibi paylaşılan süreçler Discord’a güvenilir biçimde atfedilemez. Yerel ağ rotaları, önceden kurulmuş soketler ve süreç eşlemesinin başarısız olduğu durum ayrıca kontrol edilir. Bu tasarım Discord için bir kill switch değildir. |
| Normal DNS yanıtsız | Windows resolver testi ve HTTPS DNS testi bağımsız gösterilir. Genel NRPT, bağdaştırıcı DNS’i ve sistem vekili değiştirilmez. Bu prototip Discord’un DNS sorgularını otomatik olarak DoH’ye geçirmez. |
| WARP veya başka WireGuard eşi erişilemiyor | Geçerli yapılandırma, TUN başlangıcı ve tünelden Discord HTTPS yanıtı farklı durumlardır. WARP ulaşılabilirliği, UDP izinleri ve çift yığın ayrı doğrulanır. |
| Eşten istenmeyen yeni bağlantılar | WireGuard eşinden başlayan yeni bağlantılar için açık ret kuralı var. Mevcut TCP ve UDP bağlantılarının yanıtları çift yığın yerel eş testiyle ayrıca doğrulanır. |
| Profil ifşası | Profil kullanıcı tarafından seçilir. Üretilen yapılandırma motorun standart girdisinden iletilir. Kalıcı profil, çalışma günlüğü ve profil içeren süreç komut satırı kullanılmaz. |
| Kapanış, çökme veya paralel VPN | Motor kontrollü kapanış için sinyal alır. Windows Job Object, denetleyici kaybolduğunda motoru sonlandırır. Bu koruma tek başına geçici rotaların kaldırıldığını kanıtlamaz. Mevcut WireSock kurulumu canlı prototip için kullanılmaz. |

## Kabul planı

Her çalıştırmada OS sürümü/build, motor sürümü, ağ A/B, tarih, tünel ve test durumu yazılır. Özel anahtar, profil, oturum bilgisi, paket içeriği veya kişisel IP’ler ortak kayda konmaz.

| Test | Yöntem ve kabul |
| --- | --- |
| Windows 10 ve 11 x64 | Her iki OS’de temiz standart kullanıcı oturumunda aç. Bildirim alanı ve UAC akışını, iptal/kapatmayı ve Windows’un hâlihazırda kurulu Discord’una değişiklik yapılmadığını doğrula. |
| Süreç ve trafik izolasyonu | Bilinen WireGuard eşinde ve fiziksel arayüzde eşzamanlı gözlemle. Discord ve kendi güncelleyicisi için TCP/UDP, IPv4/IPv6. Aynı isimli harici EXE, tarayıcı ve ikinci uygulamanın dış IP’si doğrudan kalmalı. İki gerçek Discord sürümü ve motor başladıktan sonra eklenen sürüm için tekrar et. Güncelleme olağan Discord akışıyla ve ayrı deneme makinesinde yapılır. |
| Wi-Fi A → B → A | Aktif ses oturumunda iki yönde geçiş yap. Fiziksel çıkış/varsayılan rota ve WireGuard yeniden erişim süresi, DNS ve Discord oturum süresi ölçülür. DHCP ve ağ değişimi 30 saniyeyi aşarsa bekleyen adım görünür olmalı. |
| Geleneksel DNS ve DoH | Kontrollü ağda UDP/TCP 53’ü yanıtsız bırak. Sistem DNS’i ve bağımsız HTTPS DNS sonucunu süreleriyle kaydet. DoH’nin Discord’u düzelttiği ancak Discord açılışı, yeni sorgu ve ses aynı koşulda başarılıysa söylenebilir. Diğer uygulamaların DNS sunucularını ve sorgu yolunu karşılaştır. |
| Discord oturum açma ve ses | Kullanıcı test hesabıyla giriş yapar. En az bir konuşma kanalında iki yönlü ses, bağlantı yeniden kurulumu ve fiziksel arayüzde Discord UDP kaçışı kontrol edilir. Tünelden Discord HTTPS yanıtı bu testin yerine geçmez. |
| Gecikme | Tünel açık/kapalı aynı hedefe, aynı ağ ve aynı upload hızıyla ölç. Discord dışı kontrol akışı ve tünelden giden akışı ayrı ölç. Boşta ve upload sırasında en az üç eşleştirilmiş turda p50, p95, kayıp, upload hızı ve örnek sayısını kaydet. TUN açıkken normal trafiğin artışını aynı rotanın tekrarlı temel dağılımıyla karşılaştır. Eski 402 ms değeri nedensellik kanıtı değildir. |
| Kapanış, zorla sonlandırma ve kaldırma | Normal durdurma, motorun öldürülmesi, denetleyicinin öldürülmesi, yeniden başlatma ve taşınabilir klasörün kaldırılması. Her aşamada internet/DNS ve arayüz/rotalar başlangıçla karşılaştırılır. Başlangıç ve Wi-Fi değişiminde bekleyen veya başarısız ağ adımı en geç 30 saniyede görünür olmalıdır. |
| Dağıtım | Üretim ZIP’i yalnız iki işletim sistemi ve canlı ağ kabul matrisi tamamlandığında oluşturulur. Motor sürümü ve hash’i, dağıtım lisansları, sürücü ve paket imzaları kontrol edilir. Paket izin listesinde profil, anahtar, hesap verisi veya günlük bulunmaz. |

Eski ölçümler ayrı referanstır. WireSock yeniden bağlanması 0,57 sn, Discord açılışı yaklaşık 4 sn ve ses kanalına giriş yaklaşık 0,87 sn. DoH ile Wi-Fi geçişi önceki çalışmada tamamlanmamıştı.

## 13 Kasım 2026 doğrulama kaydı

| Durum | Sonuç |
| --- | --- |
| Geçti · Windows 11 Pro x64 build 26300, standart kullanıcı | Arayüz derlendi, ana pencere açmadan çalıştı, ikinci kopya kapandı. UAC ve bildirim alanı etkileşiminin tamamı elle doğrulanmadı. |
| Geçti · süreç yolu ve motor yapılandırması | Yerel Discord 1.0.9267 ve 1.0.9268 ile güncelleyiciler dahil altı dosya tespit edildi. Örnek yeni sürüm ve benzer isimli olumsuz örnekler desenle sınandı. Sabit sürüm sing-box, kişisel veri içermeyen çift yığın yapılandırmasını 'check' komutuyla kabul etti. Bu test gerçek paketlere süreç kimliği atanmasını ölçmedi. |
| Geçti · TUN olmadan motor yaşam döngüsü | Gerçek sing-box işlemi başladı, Ctrl+C ile sıfır koduyla çıktı ve ayrı bir denemede denetleyici zorla sonlandırılınca Windows Job Object motoru sonlandırdı. Canlı TUN rota temizliği sınanmadı. |
| Geçti · mevcut Wi-Fi’de DNS teşhisi | Bir denemede Windows DNS istemcisi 90 ms, Cloudflare UDP/53 86 ms, Cloudflare DoH 372 ms içinde A yanıtı verdi. Bu ağda UDP/53 yanıtsızlığı gözlenmedi. |
| Çalıştırılmadı · mevcut kurulumu korumak için canlı TUN başlatılmadı | Yönetici onayı, gerçek WireGuard/WARP erişimi, Discord TCP ve ses UDP’si, IPv4/IPv6 trafik izolasyonu, DNS etkisi, canlı zaman aşımı, kapanış/çökme ve kaldırmadan sonra normal internet açık. |
| Çalıştırılmadı · ikinci ağ ve Windows 10 x64 deneme ortamı kullanılmadı | A → B → A Wi-Fi testi, DNS arızasında Discord’a etkisi, Windows 10, oturum açma, ses ve eşleştirilmiş boşta/upload gecikmesi açık. |
| Paketleme | Yalnız prototip ZIP’i. Motor, alıcı tarafından sabit SHA-256 ile resmî adresten indirilir. Canlı kabul matrisi açık olduğu için üretim ZIP’i oluşturulmadı. |

### Ek doğrulama

- İki gerçek sing-box WireGuard ucu arasında geçici test anahtarlarıyla dört akış çalıştırıldı. Windows üzerinde motorun gerçek süreç yolu tespitiyle seçilen TCP/IPv4, TCP/IPv6, UDP/IPv4 ve UDP/IPv6 istek ve yanıtlarının içerikleri doğrulandı. Test, yerel geri döngü dinleyicileriyle yapıldı ve eşleşmeyen trafiğin reddedildiği bir test kuralı kullandı. Windows TUN, gerçek sağlayıcı, Discord süreç seçimi ve Wi-Fi kapsamı için canlı kabul gerekir.
- Yerel testlerin öncesinde ve sonrasında IPv4/IPv6 rota tablosu ve DNS sunucusu ayarları birebir eşleşti. TUN bağdaştırıcısı oluşmadı. Eski uygulamanın beş dosyasının ve eski ZIP’in SHA-256 değerleri ilk okuma ile eşleşti.
- Erişim kontrolünde bu bilgisayarda ayrı bir Windows sanal makinesi bulunamadı. Fiziksel bağlantı olarak yalnız bir Wi-Fi arayüzü bağlıydı.

### Windows 11 Wi-Fi geçişi ve DNS arızası

- İkinci ağ ilk listede görünmedi. Etkin Wi-Fi taraması sonrasında kayıtlı Ozgur ağı göründü. İki ölçümlü turda Ozgur bağlantısı ve IPv4 varsayılan rotası 1,51–1,52 sn, ofis ağına dönüş ve rota 1,12–1,23 sn sürdü. Ağ yeniden bağlanınca erişim ayrıca kontrol edildi.
- Prototip TUN kapalıyken ve mevcut WireSock ayarlarına müdahale edilmeden Ozgur ağında Windows DNS istemcisi ve Cloudflare port 53 sorgusu A kaydı vermedi. Cloudflare DoH, HTTP 200 ile A kayıtları döndürdü. DoH adresini Windows çözümleyicisini atlayarak aynı Discord HTTPS isteğine vermek bağlantı sıfırlanmasıyla sonuçlandı. Bağımsız HTTPS isteği HTTP 200 döndü. Bu koşulda DoH ile Discord erişiminin geri geldiği doğrulanmadı. HTTPS istekleri Discord istemcisi yerine curl üzerinden yapıldı.
- Ofis ağına dönüşte Windows DNS ve Cloudflare port 53 başarılıydı. Bir turda DoH ilk IPv4 varsayılan rotası oluşur oluşmaz denendiğinde ulaşılmadı. Bağlantı oturduktan sonra HTTP 200, 0,225 sn olarak tekrar doğrulandı. Discord HTTPS isteği ofis ağında da bağlantı sıfırlanmasıyla bitti.
- Ayrı bir geçişte mevcut WireSock başlangıçta bağlı raporladı. Wi-Fi geçiş talebinden sonra WireSock Ozgur’da 7,34 sn, ofis ağına dönünce 3,47 sn içinde yeniden bağlı raporladı. Durum sorguları yaklaşık yarım saniye aralıkla yapıldı. WireSock profili değiştirilmedi. Bu ölçüm mevcut kurulumun raporladığı durumu gösterir.
- Yeni uygulamanın canlı WireGuard yeniden bağlanması, Discord oturumu, ses ve yük altındaki açık/kapalı tünel karşılaştırması açık kalır. Başlangıçtaki Wi-Fi profiline dönüş ve eski ZIP’in SHA-256 değeri doğrulandı.

### Superbox üzerinde yeni motorun canlı testi

- Yönetici onayıyla uygulamanın kendi motor denetleyicisi ve yapılandırma üreticisi çalıştırıldı. Mevcut tünel geçici durduruldu. Aynı WireGuard bağlantı değerlerine sahip yerel standart profil doğrudan okundu. Profil değerleri rapora veya pakete yazılmadı.
- Gerçek Windows TUN açıldı ve WireGuard eşinden el sıkışma yanıtı gözlendi. Yeni motor çalışırken bağımsız HTTPS isteği HTTP 200 döndü ve Discord'a ait bir kurulu TCP soketi görüldü. Bu soketin çıkış yolu, yeni oturum açma ve ses trafiği ayrıca ölçülmeli.
- Bu turda Ozgur ağı görünmedi. Canlı tünel altında geçiş ölçümü tamamlanamadı.
- Yeni motor sıfır çıkış koduyla kapandı. Son kontrolde yeni TUN bağdaştırıcısı yoktu, ilk Wi-Fi profili bağlıydı, önceki tünel bağlı raporladı ve bağımsız HTTPS yeniden HTTP 200 döndü.
