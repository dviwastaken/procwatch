# ProcWatch

Bir programa sağ tıkla → **"System Monitor ile aç"** → program tek kullanımlık bir sanal
makinede çalışır, ProcWatch onu ve doğurduğu her süreci kaydeder. Ana makine dokunulmadan
kalır. ANY.RUN'ın "Processes" sekmesinin yerel karşılığı. .NET 10 + WPF.

## Kurulum

1. **Windows Sandbox'ı bir kez etkinleştir** (yönetici + yeniden başlatma):

   ```powershell
   Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
   ```

2. **Yayımla:** `powershell -File publish.ps1` → `dist\` klasörü oluşur
   (`ProcWatchLauncher.exe` + yanında `tool\` içinde kendine yeten ProcWatch, ~140 MB).

3. **Sağ tık girdisini ekle:** `dist\ProcWatchLauncher.exe` çalıştır, "Sağ tık menüsüne ekle"
   butonuna bas (ya da `ProcWatchLauncher.exe --install`). Kayıt yalnızca `HKCU` altına
   yazılır: yönetici gerekmez, `--uninstall` ile tamamen geri alınır. Girdi `.exe`, `.bat`,
   `.cmd`, `.scr`, `.js`, `.vbs` ve `.msi` dosyalarında görünür.

## Akış

1. Dosyaya sağ tık → "System Monitor ile aç" → launcher açılır: dosya adı, boyut, SHA-256,
   Authenticode imzası. İki seçenek: sanal makinede **ağ erişimi** (varsayılan kapalı) ve
   programın **klasöründeki diğer dosyaları da al** (yanında DLL'i olanlar için).
2. "İzole çalıştır" → `%LOCALAPPDATA%\ProcWatch\runs\<zaman>-<ad>\` altında bir çalışma
   klasörü açılır, hedef oraya kopyalanır ve bir `run.wsb` yazılıp Windows Sandbox başlatılır.
3. Sanal makinede ProcWatch açılır, **önce temel çizgiyi alır**, sonra hedefi çalıştırır.
   Hedef ve tüm alt süreçleri `HEDEF` rozetiyle işaretlenir; "Sadece hedef" kutusu açık gelir.
4. Olaylar `report\events.jsonl` dosyasına satır satır yazılır. Bu klasör hosta bağlı olduğu
   için sandbox kapandıktan sonra da elde kalır.

Sandbox yapılandırması: ağ kapalı (isteğe bağlı açılır), vGPU / ses / kamera / yazıcı / pano
aktarımı kapalı, `ProtectedClient` açık, 4 GB bellek. Hedef ve izleyici klasörleri **salt
okunur** bağlanır.

## Uygulama nasıl çalışıyor

| Parça | Dosya | Yöntem |
|---|---|---|
| Süreç okuma | `ProcessSource.cs` | WMI `Win32_Process` (PID, üst PID, yol, komut satırı, başlangıç, oturum). WMI kapalıysa Toolhelp32'ye düşer — o zaman komut satırı olmaz. |
| Ağaç + olaylar | `ProcessTree.cs` | Periyodik anlık görüntülerin farkı. PID geri dönüşümü başlangıç zamanıyla ayırt edilir. Hedefin alt ağacı ebeveynden miras alınarak işaretlenir. |
| İmza | `Signing.cs` | Parti halinde `Get-AuthenticodeSignature`, sonuç önbelleğe alınır. Windows ikilileri katalog (`.cat`) imzalı olduğu için dosyadan sertifika okumak yetmez; başarısız olursa gömülü sertifikaya düşülür. |
| Gürültü filtresi | `Importance.cs` | Bu oturumda doğduysa → önemli; imza `HashMismatch`/`NotTrusted` → önemli; Windows dizini dışı → önemli; Windows dizininde ve Microsoft imzası geçerli → gürültü; Windows dizininde ama Microsoft imzalı değil → önemli. |
| Kayıt | `RunLog.cs` | JSONL, her satır anında diske yazılır: sandbox aniden kapansa da kayıt durur. |
| Launcher | `Launcher\` | `SandboxRun.cs` çalışma klasörü + `run.wsb` üretir, `ShellMenu.cs` sağ tık girdisini `HKCU` altına yazar. |

Komut satırı: `ProcWatch.exe [--target <exe>] [--args "..."] [--log <dosya>] [--sandbox]`.
Argümansız çalıştırılırsa sadece canlı süreç izleyicisi olur.

Bu makinede ölçüm: 311 süreç, WMI okuması ~130 ms, ilk imza doğrulaması ~6 sn (arka planda,
sonrası önbellekten). "Sadece önemli" 311 → 136. Hedef modunda yoklama aralığı 1,5 sn yerine
0,75 sn.

## Sınırlar ve riskler

- **`report\` klasörü sandbox'a yazılabilir bağlanır** — dışarı tek kanal bu. Analiz edilen
  program oraya dosya bırakabilir. Klasör her çalıştırma için ayrıdır; içeriğine bakarken
  yalnızca `events.jsonl`'a güven.
- Yoklama tabanlı izleme, 0,75 sn'den kısa yaşayan süreçleri kaçırabilir. Kaçırmamak için ETW
  (`Microsoft-Windows-Kernel-Process`) veya Sysmon Event ID 1 gerekir; ikisi de yönetici ister.
- Şu an yalnızca **süreç** olayları kaydediliyor. Dosya/registry/ağ olayları için sandbox
  içine Sysmon + Sigma kurulmalı (ANY.RUN'daki "Actions" sekmesinin karşılığı).
- Sandbox içinde ProcWatch yönetici olarak çalışır, dolayısıyla oradaki tablo tamdır; **hostta**
  argümansız çalıştırıldığında yönetici olmadan başka oturumların süreç yolları okunamaz.
- İmza doğrulaması yayıncıyı ve geçerliliği söyler, dosyanın zararsız olduğunu söylemez.
- Sandbox yolu bu makinede uçtan uca denenmedi: Windows Sandbox özelliği henüz kurulu değil.
  Üretilen `run.wsb` yapısı ve `--target` akışı host üzerinde ayrı ayrı doğrulandı.
