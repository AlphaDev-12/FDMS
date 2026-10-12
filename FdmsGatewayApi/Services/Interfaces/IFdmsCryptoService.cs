using FdmsGatewayApi.Models.Dtos;

namespace FdmsGatewayApi.Services.Interfaces;

public interface IFdmsCryptoService
{
    (string privateKeyPem, string csrPem) GenerateKeyPairAndCsr(int deviceId, string deviceSerialNo, string keyAlgorithm = "ECDSA");

    string ComputeReceiptHash(
        int deviceId,
        string receiptType,
        string currency,
        int receiptGlobalNo,
        DateTime receiptDate,
        decimal receiptTotal,
        List<ErpReceiptTaxDto> taxes,
        string? previousReceiptHash);

    SignatureDataDto SignReceipt(string hashBase64, string privateKeyPem);

    string ComputeFiscalDayHash(
        int deviceId,
        int fiscalDayNo,
        DateTime fiscalDayDate,
        List<FiscalDayCounterDto> counters);

    SignatureDataDto SignFiscalDay(string hashBase64, string privateKeyPem);

    bool VerifyServerSignature(
        string serverSignatureBase64,
        string deviceSignatureBase64,
        long receiptId,
        DateTime serverDate,
        string? serverCertPem);

    string GenerateQrCodeUrl(string baseQrUrl, int deviceId, DateTime receiptDate, int receiptGlobalNo, string deviceSignatureBase64);

    string GenerateQrCodeBase64Image(string qrCodeContent);
}
