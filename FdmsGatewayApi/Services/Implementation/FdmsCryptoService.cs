using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class FdmsCryptoService : IFdmsCryptoService
{
    private readonly ILogger<FdmsCryptoService>? _logger;

    public FdmsCryptoService(ILogger<FdmsCryptoService>? logger = null)
    {
        _logger = logger;
    }

    public (string privateKeyPem, string csrPem) GenerateKeyPairAndCsr(int deviceId, string deviceSerialNo, string keyAlgorithm = "ECDSA")
    {
        string zeroPaddedDeviceId = deviceId.ToString("D10", CultureInfo.InvariantCulture);
        string cn = $"ZIMRA-{deviceSerialNo}-{zeroPaddedDeviceId}";
        var subject = new X500DistinguishedName($"CN={cn}, C=ZW, O=Zimbabwe Revenue Authority, S=Zimbabwe");

        if (string.Equals(keyAlgorithm, "RSA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyAlgorithm, "RSA_2048", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyAlgorithm, "RSA2048", StringComparison.OrdinalIgnoreCase))
        {
            using var rsa = RSA.Create(2048);
            var csr = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string csrPem = csr.CreateSigningRequestPem();
            string privateKeyPem = rsa.ExportRSAPrivateKeyPem();
            return (privateKeyPem, csrPem);
        }
        else
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
            string csrPem = csr.CreateSigningRequestPem();
            string privateKeyPem = ecdsa.ExportECPrivateKeyPem();
            return (privateKeyPem, csrPem);
        }
    }

    public string ComputeReceiptHash(
        int deviceId,
        string receiptType,
        string currency,
        int receiptGlobalNo,
        DateTime receiptDate,
        decimal receiptTotal,
        List<ErpReceiptTaxDto> taxes,
        string? previousReceiptHash)
    {
        var sb = new StringBuilder();

        // 1. Device ID
        sb.Append(deviceId.ToString(CultureInfo.InvariantCulture));

        // 2. Receipt Type UPPERCASE
        sb.Append(receiptType.Trim().ToUpperInvariant());

        // 3. Receipt Currency UPPERCASE
        sb.Append(currency.Trim().ToUpperInvariant());

        // 4. Receipt Global No
        sb.Append(receiptGlobalNo.ToString(CultureInfo.InvariantCulture));

        // 5. Receipt Date (YYYY-MM-DDTHH:mm:ss)
        sb.Append(receiptDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));

        // 6. Receipt Total in Cents (Represented as raw cents integer per ZIMRA spec)
        long totalInCents = ConvertToCents(receiptTotal);
        sb.Append(totalInCents.ToString(CultureInfo.InvariantCulture));

        // 7. Receipt Taxes (Ordered by taxID ascending, then taxCode alphabetical)
        var sortedTaxes = taxes
            .OrderBy(t => t.TaxID)
            .ThenBy(t => t.TaxCode ?? string.Empty)
            .ToList();

        foreach (var tax in sortedTaxes)
        {
            sb.Append(tax.TaxCode ?? string.Empty);

            if (!tax.TaxPercent.HasValue)
            {
                // Exempt - empty value
            }
            else
            {
                decimal taxPct = tax.TaxPercent.Value;
                sb.Append(taxPct.ToString("0.00", CultureInfo.InvariantCulture));
            }

            long taxAmountInCents = ConvertToCents(tax.TaxAmount);
            sb.Append(taxAmountInCents.ToString(CultureInfo.InvariantCulture));

            long salesAmountInCents = ConvertToCents(tax.SalesAmountWithTax);
            sb.Append(salesAmountInCents.ToString(CultureInfo.InvariantCulture));
        }

        // 8. Previous Receipt Hash (Concatenated directly without spaces per ZIMRA spec §13.1)
        if (!string.IsNullOrEmpty(previousReceiptHash))
        {
            sb.Append(previousReceiptHash);
        }

        string canonicalString = sb.ToString();

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalString));
        return Convert.ToBase64String(hashBytes);
    }

    public SignatureDataDto SignReceipt(string hashBase64, string privateKeyPem)
    {
        byte[] hashBytes = Convert.FromBase64String(hashBase64);
        byte[] signatureBytes;

        if (privateKeyPem.Contains("RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
            (privateKeyPem.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase) && !privateKeyPem.Contains("EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase)))
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            signatureBytes = rsa.SignHash(hashBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        else
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);
            signatureBytes = ecdsa.SignHash(hashBytes);
        }

        return new SignatureDataDto
        {
            Hash = hashBase64,
            Signature = Convert.ToBase64String(signatureBytes)
        };
    }

    public string ComputeFiscalDayHash(
        int deviceId,
        int fiscalDayNo,
        DateTime fiscalDayDate,
        List<FiscalDayCounterDto> counters)
    {
        var sb = new StringBuilder();

        // 1. Device ID
        sb.Append(deviceId.ToString(CultureInfo.InvariantCulture));

        // 2. Fiscal Day No
        sb.Append(fiscalDayNo.ToString(CultureInfo.InvariantCulture));

        // 3. Fiscal Day Date (YYYY-MM-DD)
        sb.Append(fiscalDayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // 4. Concatenated Fiscal Day Counters (Ordered by type, currency, taxID/moneyType per §13.3.1)
        var activeCounters = counters
            .Where(c => c.FiscalCounterValue != 0)
            .OrderBy(c => c.FiscalCounterType.Trim().ToUpperInvariant())
            .ThenBy(c => c.FiscalCounterCurrency.Trim().ToUpperInvariant())
            .ThenBy(c => c.FiscalCounterTaxID ?? 0)
            .ThenBy(c => c.FiscalCounterMoneyType?.Trim().ToUpperInvariant() ?? string.Empty)
            .ToList();

        foreach (var counter in activeCounters)
        {
            sb.Append(counter.FiscalCounterType.Trim().ToUpperInvariant());
            sb.Append(counter.FiscalCounterCurrency.Trim().ToUpperInvariant());

            if (!string.IsNullOrEmpty(counter.FiscalCounterMoneyType))
            {
                sb.Append(counter.FiscalCounterMoneyType.Trim().ToUpperInvariant());
            }
            else if (counter.FiscalCounterTaxPercent.HasValue)
            {
                decimal taxPct = counter.FiscalCounterTaxPercent.Value;
                sb.Append(taxPct.ToString("0.00", CultureInfo.InvariantCulture));
            }
            else
            {
                // Exempt tax line
            }

            long valueInCents = ConvertToCents(counter.FiscalCounterValue);
            sb.Append(valueInCents.ToString(CultureInfo.InvariantCulture));
        }

        string canonicalStr = sb.ToString();
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalStr));
        return Convert.ToBase64String(hashBytes);
    }

    public SignatureDataDto SignFiscalDay(string hashBase64, string privateKeyPem)
    {
        return SignReceipt(hashBase64, privateKeyPem);
    }

    public bool VerifyServerSignature(
        string serverSignatureBase64,
        string deviceSignatureBase64,
        long receiptId,
        DateTime serverDate,
        string? serverCertPem)
    {
        if (string.IsNullOrEmpty(serverCertPem))
        {
            _logger?.LogWarning("VerifyServerSignature: Server certificate PEM is null or empty for Receipt ID {ReceiptID}. Verification bypassed.", receiptId);
            return true;
        }

        try
        {
            string canonicalStr = $"{deviceSignatureBase64}{receiptId}{serverDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalStr));
            byte[] signatureBytes = Convert.FromBase64String(serverSignatureBase64);

            using var cert = X509Certificate2.CreateFromPem(serverCertPem);
            using var rsa = cert.GetRSAPublicKey();

            if (rsa != null)
            {
                return rsa.VerifyHash(hashBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return ecdsa.VerifyHash(hashBytes, signatureBytes);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates ZIMRA deep link QR code URL per FDMS v7.2 Section 11:
    /// {qrUrl}/{deviceID:10 digits}{receiptDate:ddMMyyyy}{receiptGlobalNo:10 digits}{receiptQrData:first 16 chars of MD5 hash of hex signature}
    /// </summary>
    public string GenerateQrCodeUrl(
        string baseQrUrl,
        int deviceId,
        DateTime receiptDate,
        int receiptGlobalNo,
        string deviceSignatureBase64)
    {
        if (string.IsNullOrWhiteSpace(baseQrUrl))
        {
            baseQrUrl = "https://invoice.zimra.co.zw";
        }

        string trimmedUrl = baseQrUrl.TrimEnd('/');

        // 1. Device ID (10 digits, zero-padded)
        string deviceIdStr = deviceId.ToString("D10", CultureInfo.InvariantCulture);

        // 2. Receipt Date (ddMMyyyy, 8 digits)
        string dateStr = receiptDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

        // 3. Receipt Global No (10 digits, zero-padded)
        string globalNoStr = receiptGlobalNo.ToString("D10", CultureInfo.InvariantCulture);

        // 4. Receipt QR Data: First 16 hex chars of MD5 hash of Device Signature
        byte[] sigBytes = string.IsNullOrEmpty(deviceSignatureBase64) ? Array.Empty<byte>() : Convert.FromBase64String(deviceSignatureBase64);
        string sigHex = Convert.ToHexString(sigBytes);
        byte[] md5Hash = MD5.HashData(Encoding.UTF8.GetBytes(sigHex));
        string md5Hex = Convert.ToHexString(md5Hash).ToUpperInvariant();
        string receiptQrData = md5Hex.Length >= 16 ? md5Hex[..16] : md5Hex.PadRight(16, '0');

        return $"{trimmedUrl}/{deviceIdStr}{dateStr}{globalNoStr}{receiptQrData}";
    }

    /// <summary>
    /// Renders a QR code content string as a Base64-encoded PNG image using QRCoder.
    /// </summary>
    public string GenerateQrCodeBase64Image(string qrCodeContent)
    {
        if (string.IsNullOrWhiteSpace(qrCodeContent)) return string.Empty;

        try
        {
            using var qrGenerator = new QRCoder.QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrCodeContent, QRCoder.QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCoder.PngByteQRCode(qrCodeData);
            byte[] qrCodeGraphic = qrCode.GetGraphic(20);
            return Convert.ToBase64String(qrCodeGraphic);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long ConvertToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }
}
