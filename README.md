# Uyumsoft e-Fatura C# İstemcisi

Uyumsoft e-Fatura **Integration** SOAP web servisini (.NET 8 / WCF) kullanarak gelen ve giden
e-faturaları listeler, **UBL XML** (isteğe bağlı PDF) dosyalarını indirir.

## Yapı

| Dosya | Açıklama |
|---|---|
| `src/UyumsoftEFatura/Service/IntegrationClient.cs` | WSDL'den `dotnet-svcutil` ile üretilmiş istemci (67 metodun tamamı) |
| `src/UyumsoftEFatura/UyumsoftClient.cs` | Kimlik doğrulama + sık kullanılan metotlar için sarmalayıcı |
| `src/UyumsoftEFatura/Program.cs` | Komut satırı uygulaması |

Servis adresleri:

- Test: `https://efatura-test.uyumsoft.com.tr/Services/Integration`
- Canlı: `https://efatura.uyumsoft.com.tr/Services/Integration`

Kimlik doğrulama: HTTPS + WS-Security UsernameToken (`TransportWithMessageCredential`).

## Kullanım

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
  `dotnet-svcutil "https://efatura-test.uyumsoft.com.tr/Services/Integration?wsdl" -n "*,UyumsoftEFatura.Service" -d Service -o IntegrationClient.cs`
