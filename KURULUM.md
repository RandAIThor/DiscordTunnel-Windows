# Discord Tunnel Prototipi

Deneme hedefi Windows 10 ve 11 x64 üzerinde kurulu resmî Discord masaüstü uygulamasıdır. Bu paket, canlı tünel kabul testleri bekleyen bir prototiptir. Geçici sistem rotaları eklediği için ayrı bir Windows deneme ortamında ve başka VPN devrede değilken çalıştır.

1. ZIP’in tamamını yerel bir klasöre çıkart. **Motoru-Hazirla.ps1** dosyasını PowerShell ile çalıştır. Bu tek seferlik adım, yönetici yetkisi istemeden sing-box arşivini resmî sürümden indirip SHA-256 değerini doğrular. Motoru hazırladıktan sonra **DiscordTunnel-Prototype.exe** dosyasını aç.
2. Bildirim alanındaki simgeye sağ tıkla. **Başlatmak için yönetici onayı iste** seçeneğini seç. Windows onayı açıldığında kararı Windows penceresinden ver. Yönetici onayı aynı Windows hesabını yükseltmelidir. Discord kurulumu, profili seçen hesabın standart yerel klasöründe aranır.
3. Yükseltilmiş uygulamanın simgesinden **Profil seç ve deneysel tüneli başlat** seçeneğini aç. Kendi tek eşli WireGuard profilini seç. Profil hem IPv4 hem IPv6 tünel adresleri ve **AllowedIPs** için **0.0.0.0/0, ::/0** içermelidir. Sağlayıcının standart profilini kullan. WireSock’a özgü uygulama alanları ve profil içindeki komutlar desteklenmez. WARP erişimi bu paketle doğrulanmayı bekliyor.
4. Durumu simgeden izle. **TUN hazır** iletisi Windows bağdaştırıcısının açıldığını, **el sıkışma yanıtı** ise WireGuard eşinden yanıt geldiğini bildirir. Discord’u kullanarak bağlantıyı tetikle. Oturum açma ve ses bağlantısını ayrıca dene.
5. Bağlantı takılırsa **Windows DNS, port 53 ve DoH testi** seçeneğini aç. Test Windows çözümleyicisini, 1.1.1.1’e UDP/53 ve HTTPS üzerinden gönderilen Discord alan adı sorgularını karşılaştırır. Windows çözümleyicisi önbellekten veya kendi etkin DoH ayarından cevap verebilir. Bu test Discord’un kullanacağı DNS’i değiştirmez.
6. **Tüneli durdur** veya **Çıkış** ile kapat. Kaldırmak için uygulamadan çıktıktan ve normal bağlantıyı kontrol ettikten sonra çıkarttığın prototip klasörünü kaldır.

Uygulama Discord’u kurmaz, güncellemez veya yamalamaz. Kişisel profil yalnız çalışma sırasında okunur ve motorun standart girdisine aktarılır. Prototip profil kopyası veya günlük dosyası oluşturmaz. Windows TUN, doğrudan çıkan başka uygulamaların trafiğini de işleyebilir.

Motor, sabitlenmiş sing-box 1.14.4 Windows x64 sürümüdür. Motor hazırlanınca lisans bildirimi **engine/LICENSE** dosyasında bulunur. Prototip ve upstream motor EXE dosyaları Authenticode imzası taşımıyor. [Motor sürümü ve kaynak kodu](https://github.com/SagerNet/sing-box/releases/tag/v1.14.4)
