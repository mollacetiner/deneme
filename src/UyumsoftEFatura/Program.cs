using System.Globalization;
using UyumsoftEFatura;

// Kullanım:
//   dotnet run -- whoami
//   dotnet run -- list    [inbox|outbox] [--from 2026-01-01] [--to 2026-01-31]
//   dotnet run -- download [inbox|outbox] [--from ...] [--to ...] [--out ./faturalar] [--pdf]
//   dotnet run -- get     [inbox|outbox] <ETTN> [--out ./faturalar]
//
// Kimlik bilgileri: UYUMSOFT_USER / UYUMSOFT_PASS ortam değişkenleri.
// Canlı ortam için --prod ekleyin (varsayılan: test ortamı).

var argList = args.ToList();
string Opt(string name, string def)
{
    var i = argList.IndexOf(name);
    return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : def;
}
bool Flag(string name) => argList.Contains(name);

var command = argList.FirstOrDefault() ?? "help";
var positional = argList.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
var box = positional.FirstOrDefault() ?? "inbox";
bool inbox = !box.Equals("outbox", StringComparison.OrdinalIgnoreCase);

var user = Environment.GetEnvironmentVariable("UYUMSOFT_USER") ?? "Uyumsoft"; // test ortamı demo kullanıcısı
var pass = Environment.GetEnvironmentVariable("UYUMSOFT_PASS") ?? "Uyumsoft";
bool prod = Flag("--prod");

var today = DateTime.Today;
DateTime from = DateTime.ParseExact(Opt("--from", today.AddDays(-30).ToString("yyyy-MM-dd")), "yyyy-MM-dd", CultureInfo.InvariantCulture);
DateTime to = DateTime.ParseExact(Opt("--to", today.ToString("yyyy-MM-dd")), "yyyy-MM-dd", CultureInfo.InvariantCulture)
    .AddDays(1).AddTicks(-1); // bitiş gününü dahil et
var outDir = Opt("--out", Path.Combine(Environment.CurrentDirectory, "faturalar", inbox ? "gelen" : "giden"));

await using var client = new UyumsoftClient(user, pass, prod);
Console.WriteLine($"Ortam: {(prod ? "CANLI" : "TEST")}  Kullanıcı: {user}");

try
{
    switch (command)
    {
        case "whoami":
        {
            var info = await client.WhoAmIAsync();
            Console.WriteLine($"Firma: {info.Customer?.Name}  VKN/TCKN: {info.Customer?.VkTckNo}");
            break;
        }
        case "list":
        {
            Console.WriteLine($"{(inbox ? "Gelen" : "Giden")} faturalar {from:yyyy-MM-dd} - {to:yyyy-MM-dd}");
            Console.WriteLine("Fatura No         ETTN                                  Tarih             Tutar       Karşı taraf");
            int page = 0, total = 0;
            while (true)
            {
                var (items, totalPages) = inbox
                    ? await ListInbox(page)
                    : await ListOutbox(page);
                foreach (var it in items)
                    Console.WriteLine($"{it.InvoiceId,-17} {it.DocumentId}  {it.ExecutionDate:yyyy-MM-dd}  {it.PayableAmount,14:N2} {it.DocumentCurrencyCode}  {it.TargetTitle}");
                total += items.Length;
                if (++page >= totalPages || items.Length == 0) break;
            }
            Console.WriteLine($"Toplam: {total}");
            break;
        }
        case "download":
        {
            Console.WriteLine($"{(inbox ? "Gelen" : "Giden")} faturalar indiriliyor ({from:yyyy-MM-dd} - {to:yyyy-MM-dd}) -> {outDir}");
            var n = await client.DownloadAllAsync(inbox, from, to, outDir, includePdf: Flag("--pdf"));
            Console.WriteLine($"{n} fatura indirildi.");
            break;
        }
        case "get":
        {
            var id = positional.ElementAtOrDefault(1)
                     ?? throw new ArgumentException("ETTN (InvoiceId) belirtin: get inbox <ETTN>");
            Directory.CreateDirectory(outDir);
            var xml = inbox ? await client.GetInboxInvoiceXmlAsync(id) : await client.GetOutboxInvoiceXmlAsync(id);
            var path = Path.Combine(outDir, id + ".xml");
            await File.WriteAllBytesAsync(path, xml);
            Console.WriteLine($"Kaydedildi: {path}");
            break;
        }
        default:
            Console.WriteLine("Komutlar: whoami | list [inbox|outbox] | download [inbox|outbox] [--pdf] | get [inbox|outbox] <ETTN>");
            Console.WriteLine("Seçenekler: --from yyyy-MM-dd --to yyyy-MM-dd --out <klasör> --prod");
            break;
    }
}
catch (UyumsoftException ex)
{
    Console.Error.WriteLine($"Servis hatası: {ex.Message}");
    return 1;
}
catch (System.ServiceModel.FaultException ex)
{
    Console.Error.WriteLine($"SOAP hatası: {ex.Message}");
    return 1;
}
return 0;

async Task<(UyumsoftEFatura.Service.InvoiceListItemBase[] items, int totalPages)> ListInbox(int page)
{
    var r = await client.GetInboxInvoiceListAsync(from, to, page);
    return (r.Items ?? [], r.TotalPages);
}

async Task<(UyumsoftEFatura.Service.InvoiceListItemBase[] items, int totalPages)> ListOutbox(int page)
{
    var r = await client.GetOutboxInvoiceListAsync(from, to, page);
    return (r.Items ?? [], r.TotalPages);
}
