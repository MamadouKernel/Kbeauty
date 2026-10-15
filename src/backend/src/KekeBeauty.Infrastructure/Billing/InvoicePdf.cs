using System.Globalization;
using System.Text;

namespace KekeBeauty.Infrastructure.Billing;

/// <summary>
/// Minimal one-page PDF (standard Helvetica, no third-party library: QuestPDF's licence is not free for every company
/// and the project avoids paid licences). Text is Latin-1, which covers French accents.
/// </summary>
public static class InvoicePdf
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public static byte[] Build(InvoiceDto invoice, string customerName)
    {
        var lines = new List<(int Size, int Y, string Text)>
        {
            (24, 790, "Keke Beauty"),
            (10, 772, "Annuaire des salons, spas et ongleries - Abidjan, Côte d'Ivoire"),
            (18, 725, $"Facture {invoice.Number}"),
            (11, 698, $"Statut : {StatusLabel(invoice)}"),
            (11, 676, $"Client : {customerName}"),
            (11, 660, $"Établissement : {invoice.EstablishmentName}"),
            (11, 630, $"Abonnement : {invoice.PlanName}"),
            (11, 614, $"Période : du {invoice.PeriodStart:dd/MM/yyyy} au {invoice.PeriodEnd:dd/MM/yyyy}"),
            (11, 598, $"Échéance : {invoice.DueDate:dd/MM/yyyy}"),
            (16, 555, $"Montant à payer : {FormatAmount(invoice.Amount)} {invoice.CurrencyCode}"),
            (9, 90, "Document généré automatiquement par Keke Beauty. Paiement par Mobile Money ou carte bancaire."),
        };

        var content = new StringBuilder("BT\n");
        foreach (var (size, y, text) in lines)
        {
            content.Append(CultureInfo.InvariantCulture, $"/F1 {size} Tf 1 0 0 1 56 {y} Tm ({Escape(text)}) Tj\n");
        }

        content.Append("ET\n");
        var stream = Latin1.GetBytes(content.ToString());

        using var output = new MemoryStream();
        var offsets = new List<long>();

        void Write(string text) => output.Write(Latin1.GetBytes(text));

        void Object(int number, string body)
        {
            offsets.Add(output.Position);
            Write($"{number} 0 obj\n{body}\nendobj\n");
        }

        Write("%PDF-1.4\n");
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>");
        offsets.Add(output.Position);
        Write($"4 0 obj\n<< /Length {stream.Length} >>\nstream\n");
        output.Write(stream);
        Write("\nendstream\nendobj\n");
        Object(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:0000000000} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    public static string FormatAmount(decimal amount) =>
        amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string StatusLabel(InvoiceDto invoice) => invoice.Status switch
    {
        "Paid" => $"payée le {invoice.PaidAt:dd/MM/yyyy}",
        "Overdue" => "en retard de paiement",
        "Void" => "annulée",
        _ => "à payer",
    };

    /// <summary>PDF string escaping, and Latin-1 safety for characters outside that range.</summary>
    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '\\' => "\\\\",
                '(' => "\\(",
                ')' => "\\)",
                > 'ÿ' => "?",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }
}
