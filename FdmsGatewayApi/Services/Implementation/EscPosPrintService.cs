using System.Globalization;
using System.Text;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class EscPosPrintService : IEscPosPrintService
{
    private readonly IConfiguration _config;

    public EscPosPrintService(IConfiguration config)
    {
        _config = config;
    }

    public ReceiptPrintLayoutDto GeneratePrintLayout(
        ErpInvoiceRequestDto request,
        DeviceConfigEntity device,
        FiscalDocumentEntity document,
        string qrCodeUrl)
    {
        string headerTitle = _config["FdmsGateway:PrintOptions:HeaderTitle"] ?? "OFFICIAL FISCAL RECEIPT";
        string footerNotice = _config["FdmsGateway:PrintOptions:FooterNotice"] ?? "Thank you for your business!";

        string verificationFallback = $"VERIFY ONLINE: {qrCodeUrl}";

        return new ReceiptPrintLayoutDto
        {
            HeaderTitle = headerTitle,
            TaxPayerName = device.TaxPayerName,
            TaxPayerTIN = device.TaxPayerTIN,
            VatNumber = device.VatNumber,
            DeviceSerialNo = device.DeviceSerialNo,
            DeviceBranchName = device.DeviceBranchName,
            DeviceId = device.DeviceId,
            InvoiceNo = document.InvoiceNo,
            ReceiptType = document.ReceiptType,
            FiscalDayNo = document.FiscalDayNo,
            ReceiptGlobalNo = document.ReceiptGlobalNo,
            ReceiptDate = request.ReceiptDate,
            Currency = request.Currency,
            ReceiptTotal = request.ReceiptTotal,
            Lines = request.Lines,
            Taxes = request.Taxes,
            Payments = request.Payments,
            DeviceSignatureHash = document.DeviceHash,
            DeviceSignature = document.DeviceSignature,
            QrCodeUrl = qrCodeUrl,
            VerificationTextFallback = verificationFallback,
            FooterNotice = footerNotice
        };
    }

    public string GenerateEscPosStreamBase64(ReceiptPrintLayoutDto layout, int paperWidthMm = 80)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int maxCharsPerLine = paperWidthMm <= 58 ? 32 : 48;
        string divider = new string('-', maxCharsPerLine);

        // ESC/POS Commands
        byte[] escInit = new byte[] { 0x1B, 0x40 };
        byte[] alignCenter = new byte[] { 0x1B, 0x61, 0x01 };
        byte[] alignLeft = new byte[] { 0x1B, 0x61, 0x00 };
        byte[] alignRight = new byte[] { 0x1B, 0x61, 0x02 };
        byte[] boldOn = new byte[] { 0x1B, 0x45, 0x01 };
        byte[] boldOff = new byte[] { 0x1B, 0x45, 0x00 };
        byte[] doubleHeightOn = new byte[] { 0x1D, 0x21, 0x01 };
        byte[] fontNormal = new byte[] { 0x1D, 0x21, 0x00 };
        byte[] feedLine = new byte[] { 0x0A };
        byte[] cutPaper = new byte[] { 0x1D, 0x56, 0x42, 0x00 };

        Action<string, byte[]?> writeLine = (text, alignment) =>
        {
            if (alignment != null) writer.Write(alignment);
            byte[] bytes = Encoding.ASCII.GetBytes(text + "\n");
            writer.Write(bytes);
        };

        // 1. Initialize Printer
        writer.Write(escInit);

        // 2. Header
        writeLine(layout.HeaderTitle.ToUpperInvariant(), alignCenter);
        writer.Write(boldOn);
        writeLine(layout.TaxPayerName, alignCenter);
        writer.Write(boldOff);

        writeLine($"TIN: {layout.TaxPayerTIN}", alignCenter);
        if (!string.IsNullOrEmpty(layout.VatNumber))
        {
            writeLine($"VAT NO: {layout.VatNumber}", alignCenter);
        }
        writeLine($"BRANCH: {layout.DeviceBranchName}", alignCenter);
        writeLine($"DEV SN: {layout.DeviceSerialNo} (ID: {layout.DeviceId})", alignCenter);
        writeLine(divider, alignCenter);

        // 3. Receipt Details
        writer.Write(alignLeft);
        writeLine($"INVOICE NO: {layout.InvoiceNo}", alignLeft);
        writeLine($"TYPE: {layout.ReceiptType.ToUpperInvariant()}", alignLeft);
        writeLine($"DATE: {layout.ReceiptDate:yyyy-MM-dd HH:mm:ss}", alignLeft);
        writeLine($"FISCAL DAY: {layout.FiscalDayNo}  GLOBAL NO: {layout.ReceiptGlobalNo}", alignLeft);
        writeLine(divider, alignLeft);

        // 4. Line Items
        writer.Write(boldOn);
        writeLine(PadTwoColumns("ITEM / DESCRIPTION", "QTY x PRICE = TOTAL", maxCharsPerLine), alignLeft);
        writer.Write(boldOff);

        foreach (var item in layout.Lines)
        {
            writeLine(item.ReceiptLineName, alignLeft);
            string qtyStr = $"{item.ReceiptLineQuantity:0.##} x {item.ReceiptLinePrice:N2}";
            string totalStr = $"{item.ReceiptLineTotal:N2} {layout.Currency}";
            writeLine(PadTwoColumns($"  {qtyStr}", totalStr, maxCharsPerLine), alignLeft);
        }
        writeLine(divider, alignLeft);

        // 5. Taxes Summary
        foreach (var tax in layout.Taxes)
        {
            string taxLabel = $"TAX {tax.TaxCode} ({tax.TaxPercent ?? 0:0.0}%)";
            string taxVal = $"{tax.TaxAmount:N2} {layout.Currency}";
            writeLine(PadTwoColumns(taxLabel, taxVal, maxCharsPerLine), alignLeft);
        }
        writeLine(divider, alignLeft);

        // 6. Receipt Total
        writer.Write(boldOn);
        writer.Write(doubleHeightOn);
        writeLine(PadTwoColumns("TOTAL PAID", $"{layout.ReceiptTotal:N2} {layout.Currency}", maxCharsPerLine), alignLeft);
        writer.Write(fontNormal);
        writer.Write(boldOff);
        writeLine(divider, alignLeft);

        // 7. Fiscal Security Signature & QR Verification Fallback
        writeLine("ZIMRA FISCAL VERIFICATION", alignCenter);
        string shortHash = layout.DeviceSignatureHash.Length > 20 ? layout.DeviceSignatureHash[..20] + "..." : layout.DeviceSignatureHash;
        writeLine($"HASH: {shortHash}", alignCenter);
        writeLine(divider, alignCenter);
        writeLine(layout.VerificationTextFallback, alignCenter);

        // 8. Footer & Cut
        writeLine(divider, alignCenter);
        writeLine(layout.FooterNotice, alignCenter);
        writer.Write(feedLine);
        writer.Write(feedLine);
        writer.Write(cutPaper);

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string PadTwoColumns(string left, string right, int width)
    {
        if (left.Length + right.Length >= width)
        {
            int availableLeft = Math.Max(1, width - right.Length - 1);
            left = left.Length > availableLeft ? left[..availableLeft] : left;
        }
        int spaces = width - left.Length - right.Length;
        return left + new string(' ', spaces) + right;
    }
}
