using System.ComponentModel;
using UyumsoftEFatura.Service;

namespace UyumsoftEFatura;

/// <summary>Listeleme ekranları için sade fatura satırı.</summary>
public sealed class InvoiceRow
{
    [DisplayName("Fatura No")] public string InvoiceNo { get; init; } = "";
    [DisplayName("Tarih")] public DateTime? Date { get; init; }
    [DisplayName("Karşı Taraf")] public string Title { get; init; } = "";
    [DisplayName("VKN/TCKN")] public string TaxId { get; init; } = "";
    [DisplayName("Senaryo")] public string Type { get; init; } = "";
    [DisplayName("Fatura Tipi")] public string Profile { get; init; } = "";
    [DisplayName("Matrah")] public decimal TaxExclusiveAmount { get; init; }
    [DisplayName("KDV")] public decimal TaxTotal { get; init; }
    [DisplayName("Ödenecek")] public decimal PayableAmount { get; init; }
    [DisplayName("Döviz")] public string Currency { get; init; } = "";
    [DisplayName("Durum")] public string Status { get; init; } = "";
    [DisplayName("ETTN")] public string Ettn { get; init; } = "";

    public static InvoiceRow From(InvoiceListItemBase it) => new()
    {
        // Not: liste metotlarında ETTN DocumentId alanında, fatura no InvoiceId alanındadır.
        InvoiceNo = it.InvoiceId ?? "",
        Ettn = it.DocumentId ?? "",
        Date = it.ExecutionDate,
        Title = it.TargetTitle ?? "",
        TaxId = it.TargetTcknVkn ?? "",
        Type = Tr(it.Type.ToString(), Scenarios),
        Profile = Tr(it.InvoiceTipType.ToString(), Kinds),
        TaxExclusiveAmount = it.TaxExclusiveAmount,
        TaxTotal = it.TaxTotal,
        PayableAmount = it.PayableAmount,
        Currency = it.DocumentCurrencyCode ?? "",
        Status = Tr(it.Status.ToString(), Statuses),
    };

    private static string Tr(string key, Dictionary<string, string> map) => map.TryGetValue(key, out var v) ? v : key;

    private static readonly Dictionary<string, string> Scenarios = new()
    {
        ["BaseInvoice"] = "Temel", ["ComercialInvoice"] = "Ticari", ["InvoiceWithPassanger"] = "Yolcu Beraber",
        ["Export"] = "İhracat", ["eArchive"] = "e-Arşiv", ["Hks"] = "HKS", ["PublicAdministration"] = "Kamu",
        ["Energy"] = "Enerji", ["MedicineAndMedicalDevices"] = "İlaç/Tıbbi Cihaz", ["InvestmentIncentive"] = "Yatırım Teşvik",
        ["IdisSystem"] = "İDİS",
    };

    private static readonly Dictionary<string, string> Kinds = new()
    {
        ["Sales"] = "Satış", ["Return"] = "İade", ["Tax"] = "Tevkifat", ["Exception"] = "İstisna",
        ["TaxBase"] = "Özel Matrah", ["ExportSaved"] = "İhraç Kayıtlı", ["Sgk"] = "SGK", ["Broker"] = "Komisyoncu",
        ["HksSales"] = "HKS Satış", ["HksBroker"] = "HKS Komisyoncu", ["WithholdingReturn"] = "Tevkifat İade",
        ["Accomodation"] = "Konaklama Vergisi", ["Charge"] = "Şarj", ["ChargeInstant"] = "Şarj Anlık",
        ["TechSupport"] = "Teknoloji Destek",
    };

    private static readonly Dictionary<string, string> Statuses = new()
    {
        ["NotPrepared"] = "Hazırlanmadı", ["NotSend"] = "Gönderilmedi", ["Draft"] = "Taslak", ["Canceled"] = "İptal",
        ["Queued"] = "Kuyrukta", ["Processing"] = "İşleniyor", ["SentToGib"] = "GİB'e Gönderildi",
        ["Approved"] = "Onaylandı", ["WaitingForAprovement"] = "Onay Bekliyor", ["Declined"] = "Reddedildi",
        ["Return"] = "İade", ["EArchivedCanceled"] = "e-Arşiv İptal", ["Error"] = "Hata",
    };
}
