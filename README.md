# Uyumsoft e-Fatura C# İstemcisi

Uyumsoft e-Fatura **Integration** SOAP web servisini (.NET 8 / WCF) kullanarak gelen ve giden
e-faturaları listeler, **UBL XML** (isteğe bağlı PDF) dosyalarını indirir.

## Yapı

| Proje | Açıklama |
|---|---|
| `src/UyumsoftEFatura.WinForms` | **Windows Forms uygulaması**: tarih seçip listeleme ve XML/PDF indirme |
| `src/UyumsoftEFatura` | Komut satırı uygulaması |
| `src/UyumsoftEFatura.Core` | Ortak kütüphane: WSDL'den üretilmiş istemci (`Service/IntegrationClient.cs`, 67 metot) ve `UyumsoftClient` sarmalayıcısı |

## Windows Forms uygulaması

Gereksinim: Windows + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (veya Visual Studio 2022).

Visual Studio'da `UyumsoftEFatura.sln` dosyasını açıp `UyumsoftEFatura.WinForms` projesini
başlangıç projesi yapın ve F5 ile çalıştırın. Komut satırından:

```bash
dotnet run --project src/UyumsoftEFatura.WinForms
```

Tek bir `.exe` almak için:

```bash
dotnet publish src/UyumsoftEFatura.WinForms -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Ekran:

- **Bağlantı:** kullanıcı adı, şifre, Test/Canlı ortam, *Bağlantıyı Test Et*
- **Sorgu:** başlangıç/bitiş tarihi, Gelen/Giden seçimi, *Listele*, anlık filtre (fatura no, unvan, VKN, ETTN)
- **İndirme:** kayıt klasörü, *PDF de indir*, *Seçilenleri İndir*, *Tümünü İndir* (filtrelenmiş liste), *Durdur*, *Klasörü Aç*
- **Liste:** fatura no, tarih, karşı taraf, VKN/TCKN, senaryo, fatura tipi, matrah, KDV, ödenecek tutar, döviz, durum, ETTN.
  Sütun başlığına tıklayınca sıralanır; satıra çift tıklayınca o fatura indirilip klasörde gösterilir;
  sağ tık menüsünde *ETTN kopyala* vardır. Alt çubukta kayıt sayısı ve döviz bazında toplam görünür.

Faturalar `<klasör>\Gelen` veya `<klasör>\Giden` altına `{FaturaNo}_{ETTN}.xml` adıyla kaydedilir.
Kullanıcı adı, ortam ve klasör `%APPDATA%\UyumsoftEFatura\ayarlar.json` dosyasında hatırlanır (şifre kaydedilmez).

## Komut satırı uygulaması

Servis adresleri:

- Test: `https://efatura-test.uyumsoft.com.tr/Services/Integration`
- Canlı: `https://efatura.uyumsoft.com.tr/Services/Integration`

Kimlik doğrulama: HTTPS + WS-Security UsernameToken (`TransportWithMessageCredential`).

```bash
cd src/UyumsoftEFatura
export UYUMSOFT_USER=kullanici UYUMSOFT_PASS=sifre   # verilmezse test demo hesabı (Uyumsoft/Uyumsoft)

dotnet run -- whoami
dotnet run -- list inbox  --from 2026-11-01 --to 2026-11-15
dotnet run -- download inbox  --from 2026-11-01 --to 2026-11-15 --out ./faturalar/gelen
dotnet run -- download outbox --from 2026-11-01 --to 2026-11-15 --pdf
dotnet run -- get inbox <ETTN>
```

Canlı ortam için `--prod` ekleyin. Dosya adları `{FaturaNo}_{ETTN}.xml` biçimindedir.

## Kodda kullanım

```csharp
await using var client = new UyumsoftClient("kullanici", "sifre", production: false);

// Tarih aralığındaki tüm gelen faturaların XML'leri
await client.DownloadAllAsync(inbox: true, new DateTime(2026, 11, 1), DateTime.Now, "faturalar/gelen");

// Tek fatura (ETTN ile)
byte[] xml = await client.GetInboxInvoiceXmlAsync("cdd93f99-07ab-409d-8c2b-70d687bf7d20");

// Diğer tüm servis metotları
var status = await client.Service.QueryInboxInvoiceStatusAsync(new[] { "ETTN" });
```

## Notlar

- Liste metotlarında (`GetInboxInvoiceList` vb.) **ETTN `DocumentId`** alanında, fatura
  numarası `InvoiceId` alanındadır. `Get...InvoiceData` / `Get...InvoicePdf` metotları ETTN bekler.
- `DownloadAllAsync`, `GetInboxInvoicesData` ile sayfa sayfa toplu indirme yapar ve
  `SetTaken = false` gönderir; faturaları "alındı" işaretlemek için `SetInvoicesTakenAsync` kullanın.
- İstemciyi yeniden üretmek için:
  `dotnet tool install -g dotnet-svcutil` ardından
  (`src/UyumsoftEFatura.Core` klasöründe) `dotnet-svcutil "https://efatura-test.uyumsoft.com.tr/Services/Integration?wsdl" -n "*,UyumsoftEFatura.Service" -d Service -o IntegrationClient.cs`
