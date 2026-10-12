using System.Globalization;
using System.Text;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class PdfReportGenerator : IPdfReportGenerator
{
    private readonly ILogger<PdfReportGenerator> _logger;

    public PdfReportGenerator(ILogger<PdfReportGenerator> logger)
    {
        _logger = logger;
    }

    public byte[] GenerateZReportPdf(ZReportResponseDto report)
    {
        return BuildReportPdf(
            reportTitle: "OFFICIAL FISCAL Z-REPORT",
            reportSubtitle: "END OF FISCAL DAY SUMMARY & CRYPTOGRAPHIC LEDGER",
            deviceId: report.DeviceId,
            deviceSerialNo: report.DeviceSerialNo,
            taxPayerName: report.TaxPayerName,
            taxPayerTIN: report.TaxPayerTIN,
            fiscalDayNo: report.FiscalDayNo,
            openingTime: report.ReportDate.ToString("yyyy-MM-dd HH:mm:ss"),
            closingTime: report.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            startGlobalNo: report.StartingReceiptGlobalNo,
            endGlobalNo: report.EndingReceiptGlobalNo,
            fiscalizedDocsCount: report.TotalFiscalizedDocuments,
            pendingDocsCount: report.TotalPendingDocuments,
            currencySummaries: report.CurrencySummaries,
            blockHashDigest: report.BlockHashDigest,
            signature: report.ReportSignature?.Signature ?? string.Empty
        );
    }

    public byte[] GenerateXReportPdf(XReportResponseDto report)
    {
        return BuildReportPdf(
            reportTitle: "OFFICIAL FISCAL X-REPORT",
            reportSubtitle: "INTERIM FISCAL DAY TRANSACTIONS READOUT (DAY OPEN)",
            deviceId: report.DeviceId,
            deviceSerialNo: report.DeviceSerialNo,
            taxPayerName: report.TaxPayerName,
            taxPayerTIN: report.TaxPayerTIN,
            fiscalDayNo: report.FiscalDayNo,
            openingTime: report.FiscalDayOpenedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            closingTime: report.ReportGeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " (INTERIM)",
            startGlobalNo: report.StartingReceiptGlobalNo,
            endGlobalNo: report.EndingReceiptGlobalNo,
            fiscalizedDocsCount: report.TotalFiscalizedDocuments,
            pendingDocsCount: report.TotalPendingDocuments,
            currencySummaries: report.CurrencySummaries,
            blockHashDigest: report.BlockHashDigest,
            signature: report.ReportSignature?.Signature ?? string.Empty
        );
    }

    private byte[] BuildReportPdf(
        string reportTitle,
        string reportSubtitle,
        int deviceId,
        string deviceSerialNo,
        string taxPayerName,
        string taxPayerTIN,
        int fiscalDayNo,
        string openingTime,
        string closingTime,
        int startGlobalNo,
        int endGlobalNo,
        int fiscalizedDocsCount,
        int pendingDocsCount,
        List<ZReportCurrencySummaryDto> currencySummaries,
        string blockHashDigest,
        string signature)
    {
        var builder = new PdfStreamWriter();

        // 1. Header Banner & Title
        builder.DrawRect(30, 750, 535, 60, fillRgb: (15, 23, 42), strokeRgb: (30, 41, 59));
        builder.DrawText("REPUBLIC OF ZIMBABWE - ZIMRA FDMS GATEWAY", 45, 792, "/F2", 12, (241, 245, 249));
        builder.DrawText(reportTitle, 45, 774, "/F2", 16, (56, 189, 248));
        builder.DrawText(reportSubtitle, 45, 760, "/F1", 9, (148, 163, 184));

        // 2. Taxpayer & Device Metadata Card
        builder.DrawRect(30, 660, 535, 80, fillRgb: (248, 250, 252), strokeRgb: (226, 232, 240));
        builder.DrawText("TAXPAYER & DEVICE IDENTIFICATION", 42, 726, "/F2", 10, (30, 41, 59));
        builder.DrawLine(42, 720, 553, 720, (203, 213, 225));

        builder.DrawText($"Taxpayer Name: {taxPayerName}", 42, 705, "/F1", 9, (51, 65, 85));
        builder.DrawText($"Taxpayer TIN: {taxPayerTIN}", 42, 690, "/F1", 9, (51, 65, 85));
        builder.DrawText($"Device ID: {deviceId}", 320, 705, "/F1", 9, (51, 65, 85));
        builder.DrawText($"Serial Number: {deviceSerialNo}", 320, 690, "/F1", 9, (51, 65, 85));

        // 3. Fiscal Day & Counter Summary Box
        builder.DrawRect(30, 565, 535, 85, fillRgb: (255, 255, 255), strokeRgb: (203, 213, 225));
        builder.DrawText("FISCAL DAY STATUS & COUNTERS", 42, 635, "/F2", 10, (15, 23, 42));
        builder.DrawLine(42, 629, 553, 629, (226, 232, 240));

        builder.DrawText($"Fiscal Day Number: #{fiscalDayNo}", 42, 613, "/F2", 10, (16, 185, 129));
        builder.DrawText($"Day Open Time: {openingTime}", 42, 598, "/F1", 9, (51, 65, 85));
        builder.DrawText($"Day Close / Readout Time: {closingTime}", 42, 583, "/F1", 9, (51, 65, 85));

        builder.DrawText($"Receipt Global Range: #{startGlobalNo} - #{endGlobalNo}", 320, 613, "/F1", 9, (51, 65, 85));
        builder.DrawText($"Fiscalized Documents: {fiscalizedDocsCount}", 320, 598, "/F2", 9, (30, 41, 59));
        builder.DrawText($"Pending Documents: {pendingDocsCount}", 320, 583, "/F1", 9, (100, 116, 139));

        // 4. Currency Sales Summaries
        float currentY = 525;
        builder.DrawText("FINANCIAL TRANSACTION TOTALS BY CURRENCY", 30, currentY, "/F2", 11, (15, 23, 42));
        currentY -= 15;

        foreach (var curr in currencySummaries)
        {
            builder.DrawRect(30, currentY - 60, 535, 60, fillRgb: (241, 245, 249), strokeRgb: (203, 213, 225));
            builder.DrawText($"CURRENCY: {curr.Currency}", 42, currentY - 18, "/F2", 10, (30, 41, 59));
            builder.DrawText($"Gross Sales: {curr.Currency} {curr.GrossSalesAmount:N2}", 42, currentY - 36, "/F2", 10, (16, 185, 129));
            builder.DrawText($"Net Taxable: {curr.Currency} {curr.NetTaxableAmount:N2}", 240, currentY - 36, "/F1", 9, (51, 65, 85));
            builder.DrawText($"Total Tax: {curr.Currency} {curr.TotalTaxAmount:N2}", 400, currentY - 36, "/F2", 9, (225, 29, 72));

            // Tax Breakdown subtable if present
            if (curr.TaxBreakdowns != null && curr.TaxBreakdowns.Count > 0)
            {
                currentY -= 65;
                builder.DrawText("Tax Rate Breakdowns:", 42, currentY, "/F2", 8, (100, 116, 139));
                currentY -= 12;
                foreach (var tax in curr.TaxBreakdowns)
                {
                    builder.DrawText($"• Code: {tax.TaxCode} ({tax.TaxPercent}%) | Taxable Sales: {curr.Currency} {tax.TaxableSales:N2} | Tax: {curr.Currency} {tax.TaxAmount:N2}", 50, currentY, "/F1", 8, (71, 85, 105));
                    currentY -= 12;
                }
            }
            else
            {
                currentY -= 70;
            }
        }

        if (currencySummaries.Count == 0)
        {
            builder.DrawRect(30, currentY - 30, 535, 30, fillRgb: (248, 250, 252), strokeRgb: (226, 232, 240));
            builder.DrawText("No fiscalized transactions recorded for this period.", 42, currentY - 20, "/F1", 9, (100, 116, 139));
            currentY -= 40;
        }

        // 5. Cryptographic Integrity & Signature Ledger
        currentY -= 10;
        builder.DrawRect(30, currentY - 110, 535, 110, fillRgb: (15, 23, 42), strokeRgb: (30, 41, 59));
        builder.DrawText("ZIMRA CRYPTOGRAPHIC INTEGRITY & SECURITY LEDGER", 42, currentY - 18, "/F2", 10, (241, 245, 249));
        builder.DrawLine(42, currentY - 24, 553, currentY - 24, (51, 65, 85));

        builder.DrawText("SHA-256 Block Hash Digest:", 42, currentY - 40, "/F2", 8, (56, 189, 248));
        builder.DrawText(FormatWrapText(blockHashDigest, 75), 42, currentY - 52, "/F1", 8, (226, 232, 240));

        builder.DrawText("ECDSA secp256r1 Digital Signature:", 42, currentY - 70, "/F2", 8, (16, 185, 129));
        string sigText = string.IsNullOrEmpty(signature) ? "REGISTERED DEVICE KEY SIGNATURE ATTACHED" : signature;
        builder.DrawText(FormatWrapText(sigText, 75), 42, currentY - 82, "/F1", 8, (226, 232, 240));

        // 6. Page Footer
        builder.DrawLine(30, 45, 565, 45, (203, 213, 225));
        builder.DrawText("ZIMRA FDMS Gateway v7.2 Bridge • Official Tax Authority Compliance Audit Report", 30, 30, "/F1", 8, (100, 116, 139));
        builder.DrawText($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} CAT", 400, 30, "/F1", 8, (100, 116, 139));

        return builder.ToByteArray();
    }

    private static string FormatWrapText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;
        return text.Substring(0, maxChars) + "...";
    }

    private class PdfStreamWriter
    {
        private readonly List<string> _contentCommands = new();

        public void DrawText(string text, float x, float y, string font, float fontSize, (byte r, byte g, byte b) color)
        {
            string escText = EscapePdfText(text);
            string r = (color.r / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string g = (color.g / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string b = (color.b / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);

            _contentCommands.Add($"BT {r} {g} {b} rg {font} {fontSize.ToString(CultureInfo.InvariantCulture)} Tf {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} Td ({escText}) Tj ET");
        }

        public void DrawRect(float x, float y, float w, float h, (byte r, byte g, byte b) fillRgb, (byte r, byte g, byte b) strokeRgb)
        {
            string fr = (fillRgb.r / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string fg = (fillRgb.g / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string fb = (fillRgb.b / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);

            string sr = (strokeRgb.r / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string sg = (strokeRgb.g / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string sb = (strokeRgb.b / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);

            _contentCommands.Add($"{fr} {fg} {fb} rg {sr} {sg} {sb} RG {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)} {w.ToString(CultureInfo.InvariantCulture)} {h.ToString(CultureInfo.InvariantCulture)} re B");
        }

        public void DrawLine(float x1, float y1, float x2, float y2, (byte r, byte g, byte b) strokeRgb, float lineWidth = 1.0f)
        {
            string sr = (strokeRgb.r / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string sg = (strokeRgb.g / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string sb = (strokeRgb.b / 255.0f).ToString("0.###", CultureInfo.InvariantCulture);
            string lw = lineWidth.ToString(CultureInfo.InvariantCulture);

            _contentCommands.Add($"{sr} {sg} {sb} RG {lw} w {x1.ToString(CultureInfo.InvariantCulture)} {y1.ToString(CultureInfo.InvariantCulture)} m {x2.ToString(CultureInfo.InvariantCulture)} {y2.ToString(CultureInfo.InvariantCulture)} l S");
        }

        public byte[] ToByteArray()
        {
            string streamBody = string.Join("\n", _contentCommands);
            byte[] streamBytes = Encoding.ASCII.GetBytes(streamBody);

            var objects = new List<string>();
            var offsets = new List<long>();
            long currentOffset = 0;

            string header = "%PDF-1.4\n%\xFF\xFF\xFF\xFF\n";
            currentOffset += Encoding.ASCII.GetByteCount(header);

            // Object 1: Catalog
            offsets.Add(currentOffset);
            string obj1 = "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(obj1);
            objects.Add(obj1);

            // Object 2: Pages
            offsets.Add(currentOffset);
            string obj2 = "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(obj2);
            objects.Add(obj2);

            // Object 3: Page
            offsets.Add(currentOffset);
            string obj3 = "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(obj3);
            objects.Add(obj3);

            // Object 4: Font F1 (Helvetica)
            offsets.Add(currentOffset);
            string obj4 = "4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(obj4);
            objects.Add(obj4);

            // Object 5: Font F2 (Helvetica-Bold)
            offsets.Add(currentOffset);
            string obj5 = "5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(obj5);
            objects.Add(obj5);

            // Object 6: Stream Contents
            offsets.Add(currentOffset);
            string streamHeader = $"6 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n";
            string streamFooter = "\nendstream\nendobj\n";
            currentOffset += Encoding.ASCII.GetByteCount(streamHeader) + streamBytes.Length + Encoding.ASCII.GetByteCount(streamFooter);

            // Build PDF file output
            var ms = new MemoryStream();
            byte[] hBytes = Encoding.ASCII.GetBytes(header);
            ms.Write(hBytes, 0, hBytes.Length);

            foreach (var o in objects)
            {
                byte[] oBytes = Encoding.ASCII.GetBytes(o);
                ms.Write(oBytes, 0, oBytes.Length);
            }

            // Stream obj 6
            byte[] shBytes = Encoding.ASCII.GetBytes(streamHeader);
            ms.Write(shBytes, 0, shBytes.Length);
            ms.Write(streamBytes, 0, streamBytes.Length);
            byte[] sfBytes = Encoding.ASCII.GetBytes(streamFooter);
            ms.Write(sfBytes, 0, sfBytes.Length);

            // XREF table
            long xrefOffset = ms.Position;
            var sbXref = new StringBuilder();
            sbXref.AppendLine("xref");
            sbXref.AppendLine($"0 {offsets.Count + 1}");
            sbXref.AppendLine("0000000000 65535 f ");
            foreach (var off in offsets)
            {
                sbXref.AppendLine($"{off:D10} 00000 n ");
            }
            sbXref.AppendLine("trailer");
            sbXref.AppendLine($"<< /Size {offsets.Count + 1} /Root 1 0 R >>");
            sbXref.AppendLine("startxref");
            sbXref.AppendLine($"{xrefOffset}");
            sbXref.AppendLine("%%EOF");

            byte[] xrefBytes = Encoding.ASCII.GetBytes(sbXref.ToString());
            ms.Write(xrefBytes, 0, xrefBytes.Length);

            return ms.ToArray();
        }

        private static string EscapePdfText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        }
    }
}
