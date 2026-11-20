# Discord Tunnel 0.2.0

ZIP’in tamamını yerel bir klasöre çıkart ve **DiscordTunnel.exe** dosyasını aç. Ağ motoru pakete dahildir.

1. Bildirim alanındaki simgeye sağ tıkla ve **Tüneli başlat** seçeneğini seç. Windows yönetici onayını ver.
2. Açılan pencereden kendi WireGuard profilini seç. Eski Discord-WARP profili de kullanılabilir. Profildeki uygulama listeleri yerine bu uygulamanın Discord kapsamı uygulanır.
3. Profil hem IPv4 hem IPv6 tünel adresleri ve **AllowedIPs** için **0.0.0.0/0, ::/0** içermelidir.
4. **TUN hazır** mesajı Windows bağdaştırıcısının açıldığını bildirir. Discord’u kullanınca alınan **WireGuard eşinden yanıt** mesajı, tünel sunucusunun yanıt verdiğini gösterir.
5. Çıkmak için simgeden **Tüneli durdur** veya **Çıkış** seç. Uygulama kapandıktan sonra klasörünü kaldırarak silebilirsin.

Kapsam, bu Windows hesabının standart **%LOCALAPPDATA%\Discord** kurulumudur. Kök dizindeki Discord ve Update uygulamaları, **app-*** sürüm klasörleri ve onların altındaki bütün EXE yardımcıları kapsanır. Ses ve ekran paylaşımı yardımcıları, birden fazla kurulu sürüm ve uygulama açıkken oluşan yeni sürüm yolları bu kuralla seçilir.

Diğer süreçler doğrudan çıkışa yönlendirilir. Windows TUN çalışırken bu trafik de motor tarafından işlenebilir. PTB, Canary ve taşınabilir Discord kurulumları bu sürümün kapsamına dahil değildir.

Başka bir VPN aynı anda açıksa rotalar çakışabilir. WireSock bağlantısını bu uygulamayla canlı test yapmadan önce durdur. Uygulama Discord dosyalarını değiştirmez.

Paket kişisel profil veya anahtar içermez. Seçilen profil çalışma sırasında okunur ve bellekte motora aktarılır. Uygulama profil kopyası veya günlük oluşturmaz.

**Windows DNS, port 53 ve DoH testi** yalnız teşhis içindir. Windows DNS sunucularını değiştirmez. TUN motoru açılış ve normal kapanışta DNS önbelleğini temizleyebilir.

Windows 11 üzerinde motor testleri yapılmıştır. Windows 10, gerçek Discord ses bağlantısı, iki ağ arasında yeni motorun geçişi ve yük altında karşılaştırmalı gecikme kabulü sürmektedir. EXE dosyalarının Authenticode imzası yoktur. Güncel sonuçlar **TASARIM-VE-TEST.md** dosyasındadır.
