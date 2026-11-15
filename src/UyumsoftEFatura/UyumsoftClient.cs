using System.IO.Compression;
using System.ServiceModel;
using System.Xml.Linq;
using UyumsoftEFatura.Service;

namespace UyumsoftEFatura;

/// <summary>
/// Uyumsoft e-Fatura Integration web servisi için ince bir sarmalayıcı.
/// WSDL'den üretilmiş <see cref="IntegrationClient"/> sınıfını kullanır.
/// </summary>
public sealed class UyumsoftClient : IAsyncDisposable
{
    public const string TestUrl = "https://efatura-test.uyumsoft.com.tr/Services/Integration";
    public const string ProdUrl = "https://efatura.uyumsoft.com.tr/Services/Integration";

    private readonly IntegrationClient _client;

    public UyumsoftClient(string userName, string password, bool production = false)
    {
        // WSDL: TransportWithMessageCredential (HTTPS + WS-Security UsernameToken)
        var binding = new BasicHttpBinding(BasicHttpSecurityMode.TransportWithMessageCredential)
        {
            MaxReceivedMessageSize = int.MaxValue,
            MaxBufferSize = int.MaxValue,
            ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max,
            SendTimeout = TimeSpan.FromMinutes(5),
            ReceiveTimeout = TimeSpan.FromMinutes(5),
        };
        binding.Security.Message.ClientCredentialType = BasicHttpMessageCredentialType.UserName;

        _client = new IntegrationClient(binding, new EndpointAddress(production ? ProdUrl : TestUrl));
        _client.ClientCredentials.UserName.UserName = userName;
        _client.ClientCredentials.UserName.Password = password;
    }

    /// <summary>Doğrudan üretilmiş istemciye erişim (diğer 60+ metot için).</summary>
    public IIntegration Service => _client;

    // ---------- Yardımcı metotlar ----------

    public async Task<WhoAmIInfo> WhoAmIAsync()
        => Check(await _client.WhoAmIAsync()).Value;

    public async Task<PagedResponseOfInboxInvoiceListItem> GetInboxInvoiceListAsync(
        DateTime start, DateTime end, int pageIndex = 0, int pageSize = 100)
    {
        var query = new InboxInvoiceListQueryModel
        {
            ExecutionStartDate = start,
            ExecutionEndDate = end,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
        return Check(await _client.GetInboxInvoiceListAsync(query)).Value;
    }

    public async Task<PagedResponseOfOutboxInvoiceListItem> GetOutboxInvoiceListAsync(
        DateTime start, DateTime end, int pageIndex = 0, int pageSize = 100)
    {
        var query = new OutboxInvoiceListQueryModel
        {
            ExecutionStartDate = start,
            ExecutionEndDate = end,
            PageIndex = pageIndex,
            PageSize = pageSize,
        };
        return Check(await _client.GetOutboxInvoiceListAsync(query)).Value;
    }

    /// <summary>Gelen faturanın UBL XML içeriği (ETTN / InvoiceId ile).</summary>
    public async Task<byte[]> GetInboxInvoiceXmlAsync(string invoiceId)
        => Unwrap(Check(await _client.GetInboxInvoiceDataAsync(invoiceId)).Value.Data);

    /// <summary>Giden faturanın UBL XML içeriği (ETTN / InvoiceId ile).</summary>
    public async Task<byte[]> GetOutboxInvoiceXmlAsync(string invoiceId)
        => Unwrap(Check(await _client.GetOutboxInvoiceDataAsync(invoiceId)).Value.Data);

    public async Task<byte[]> GetInboxInvoicePdfAsync(string invoiceId)
        => Check(await _client.GetInboxInvoicePdfAsync(invoiceId)).Value.Data;

    public async Task<byte[]> GetOutboxInvoicePdfAsync(string invoiceId)
        => Check(await _client.GetOutboxInvoicePdfAsync(invoiceId)).Value.Data;

    /// <summary>
    /// Tarih aralığındaki tüm gelen/giden faturaların XML'lerini klasöre indirir.
    /// Dosya adı: {FaturaNo}_{ETTN}.xml
    /// </summary>
    public async Task<int> DownloadAllAsync(
        bool inbox, DateTime start, DateTime end, string outputDir,
        bool includePdf = false, int pageSize = 50, Action<string>? log = null)
    {
        Directory.CreateDirectory(outputDir);
        log ??= Console.WriteLine;
        int count = 0, pageIndex = 0;

        while (true)
        {
            InvoicesDataResponse response;
            if (inbox)
            {
                response = await _client.GetInboxInvoicesDataAsync(new InboxInvoiceQueryModel
                {
                    ExecutionStartDate = start,
                    ExecutionEndDate = end,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    SetTaken = false, // true verilirse faturalar "alındı" olarak işaretlenir
                });
            }
            else
            {
                response = await _client.GetOutboxInvoicesDataAsync(new InvoiceQueryModel
                {
                    ExecutionStartDate = start,
                    ExecutionEndDate = end,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                });
            }

            var page = Check(response).Value;
            var items = page?.Items ?? [];
            foreach (var item in items)
            {
                var xml = Unwrap(item.Data);
                var invoiceNo = TryGetInvoiceNumber(xml) ?? "fatura";
                var baseName = SafeFileName($"{invoiceNo}_{item.InvoiceId}");
                await File.WriteAllBytesAsync(Path.Combine(outputDir, baseName + ".xml"), xml);

                if (includePdf)
                {
                    var pdf = inbox
                        ? await GetInboxInvoicePdfAsync(item.InvoiceId)
                        : await GetOutboxInvoicePdfAsync(item.InvoiceId);
                    await File.WriteAllBytesAsync(Path.Combine(outputDir, baseName + ".pdf"), pdf);
                }

                count++;
                log($"  [{count}] {baseName}.xml");
            }

            pageIndex++;
            if (items.Length == 0 || pageIndex >= page!.TotalPages) break;
        }

        return count;
    }

    // ---------- İç yardımcılar ----------

    private static T Check<T>(T response) where T : Response
    {
        if (response is null || !response.IsSucceded)
            throw new UyumsoftException(response?.Message ?? "Servisten boş yanıt döndü.");
        return response;
    }

    /// <summary>Veri ZIP olarak gelirse içindeki ilk dosyayı çıkarır, değilse olduğu gibi döner.</summary>
    private static byte[] Unwrap(byte[] data)
    {
        if (data is { Length: > 4 } && data[0] == 'P' && data[1] == 'K')
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        ?? zip.Entries.First();
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        return data;
    }

    private static string? TryGetInvoiceNumber(byte[] xml)
    {
        try
        {
            XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
            var doc = XDocument.Load(new MemoryStream(xml));
            return doc.Root?.Element(cbc + "ID")?.Value;
        }
        catch { return null; }
    }

    private static string SafeFileName(string name)
        => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.State == CommunicationState.Faulted) _client.Abort();
            else await _client.CloseAsync();
        }
        catch { _client.Abort(); }
    }
}

public sealed class UyumsoftException(string message) : Exception(message);
