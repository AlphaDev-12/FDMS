using FdmsGatewayApi.Exceptions;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class FdmsValidationService : IFdmsValidationService
{
    public void ValidateInvoiceRequest(ErpInvoiceRequestDto dto, DeviceConfigEntity deviceConfig)
    {
        ValidateCommonRules(dto, deviceConfig);

        if (dto.ReceiptTotal < 0)
        {
            throw new ZimraValidationException("RCPT040", "Receipt total must be greater than or equal to 0 for FiscalInvoice.");
        }
    }

    public void ValidateCreditNoteRequest(ErpCreditNoteRequestDto dto, DeviceConfigEntity deviceConfig, FiscalDocumentEntity? originalDoc)
    {
        ValidateCommonRules(dto, deviceConfig);

        if (string.IsNullOrWhiteSpace(dto.ReceiptNotes))
        {
            throw new ZimraValidationException("RCPT034", "Receipt notes are mandatory for CreditNote.");
        }

        if (dto.ReceiptTotal > 0)
        {
            throw new ZimraValidationException("RCPT040", "Receipt total must be less than or equal to 0 for CreditNote.");
        }

        if (dto.CreditDebitNoteRef == null && (originalDoc == null))
        {
            throw new ZimraValidationException("RCPT030", "Original receipt reference is mandatory for CreditNote.");
        }

        if (dto.CreditDebitNoteRef?.OriginalReceiptID.HasValue == true && originalDoc == null)
        {
            throw new ZimraValidationException("RCPT031", "Referenced original receipt was not found in fiscal device system.");
        }

        if (originalDoc != null)
        {
            // RCPT032: Currency matching
            if (!string.Equals(dto.Currency, originalDoc.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ZimraValidationException("RCPT032", $"CreditNote currency ({dto.Currency}) does not match original receipt currency ({originalDoc.Currency}).");
            }

            // RCPT033: Must be issued within 12 months
            if (dto.ReceiptDate > originalDoc.CreatedAt.AddMonths(12))
            {
                throw new ZimraValidationException("RCPT033", "Credited receipt was issued earlier than 12 months before credit note date.");
            }

            // RCPT035: Total credit note amount must not exceed original receipt amount
            if (Math.Abs(dto.ReceiptTotal) > Math.Abs(originalDoc.TotalAmount))
            {
                throw new ZimraValidationException("RCPT035", "Total credit note amount exceeds original receipt amount.");
            }
        }
    }

    public void ValidateDebitNoteRequest(ErpDebitNoteRequestDto dto, DeviceConfigEntity deviceConfig, FiscalDocumentEntity? originalDoc)
    {
        ValidateCommonRules(dto, deviceConfig);

        if (string.IsNullOrWhiteSpace(dto.ReceiptNotes))
        {
            throw new ZimraValidationException("RCPT034", "Receipt notes are mandatory for DebitNote.");
        }

        if (dto.ReceiptTotal < 0)
        {
            throw new ZimraValidationException("RCPT040", "Receipt total must be greater than or equal to 0 for DebitNote.");
        }

        if (dto.CreditDebitNoteRef == null && (originalDoc == null))
        {
            throw new ZimraValidationException("RCPT030", "Original receipt reference is mandatory for DebitNote.");
        }

        if (dto.CreditDebitNoteRef?.OriginalReceiptID.HasValue == true && originalDoc == null)
        {
            throw new ZimraValidationException("RCPT031", "Referenced original receipt was not found in fiscal device system.");
        }

        if (originalDoc != null)
        {
            // RCPT032: Currency matching
            if (!string.Equals(dto.Currency, originalDoc.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ZimraValidationException("RCPT032", $"DebitNote currency ({dto.Currency}) does not match original receipt currency ({originalDoc.Currency}).");
            }
        }
    }

    private void ValidateCommonRules(ErpInvoiceRequestDto dto, DeviceConfigEntity deviceConfig)
    {
        if (!string.Equals(deviceConfig.FiscalDayStatus, "FiscalDayOpened", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(deviceConfig.FiscalDayStatus, "FiscalDayCloseFailed", StringComparison.OrdinalIgnoreCase))
        {
            throw new ZimraValidationException("RCPT001", $"Fiscal day status '{deviceConfig.FiscalDayStatus}' does not permit receipt submission. Status must be FiscalDayOpened or FiscalDayCloseFailed.");
        }

        int maxHrs = deviceConfig.TaxPayerDayMaxHrs > 0 ? deviceConfig.TaxPayerDayMaxHrs : 24;
        if (deviceConfig.FiscalDayOpenedAt.HasValue && (DateTime.Now - deviceConfig.FiscalDayOpenedAt.Value).TotalHours > maxHrs)
        {
            throw new ZimraValidationException("RCPT041", $"Fiscal day open duration exceeds maximum allowed limit of {maxHrs} hours.");
        }

        if (string.IsNullOrWhiteSpace(dto.Currency) || dto.Currency.Length != 3)
        {
            throw new ZimraValidationException("RCPT010", "Invalid ISO 4217 currency code.");
        }

        if (dto.Lines == null || dto.Lines.Count == 0)
        {
            throw new ZimraValidationException("RCPT016", "At least one receipt line must be provided.");
        }

        // RCPT020: Maximum line count rule
        if (dto.Lines.Count > 500)
        {
            throw new ZimraValidationException("RCPT020", $"Receipt line count ({dto.Lines.Count}) exceeds maximum allowed limit of 500 lines.");
        }

        if (dto.Taxes == null || dto.Taxes.Count == 0)
        {
            throw new ZimraValidationException("RCPT017", "At least one tax line must be provided.");
        }

        if (dto.Payments == null || dto.Payments.Count == 0)
        {
            throw new ZimraValidationException("RCPT018", "At least one payment method line must be provided.");
        }

        // RCPT015: Mandatory Buyer TIN validation
        if (dto.BuyerData != null)
        {
            if (!string.IsNullOrWhiteSpace(dto.BuyerData.VATNumber) && string.IsNullOrWhiteSpace(dto.BuyerData.BuyerTIN))
            {
                throw new ZimraValidationException("RCPT015", "Buyer TIN is mandatory when Buyer VAT number is supplied.");
            }
            if (dto.ReceiptTotal >= 100000m && string.IsNullOrWhiteSpace(dto.BuyerData.BuyerTIN))
            {
                throw new ZimraValidationException("RCPT015", "Buyer TIN is mandatory for high-value transactions exceeding threshold.");
            }
        }

        // Validate line pricing, quantities, and tax rates (RCPT014, RCPT022, RCPT023, RCPT024)
        bool isCreditNote = dto is ErpCreditNoteRequestDto;
        foreach (var line in dto.Lines)
        {
            if (line.ReceiptLineQuantity <= 0)
            {
                throw new ZimraValidationException("RCPT023", $"Receipt line quantity ({line.ReceiptLineQuantity}) must be greater than 0.");
            }

            // RCPT014: Tax code / percent verification
            if (line.TaxPercent.HasValue && dto.Taxes != null)
            {
                var matchingTaxHeader = dto.Taxes.FirstOrDefault(t => t.TaxID == line.TaxID);
                if (matchingTaxHeader != null && matchingTaxHeader.TaxPercent.HasValue && Math.Abs(matchingTaxHeader.TaxPercent.Value - line.TaxPercent.Value) > 0.01m)
                {
                    throw new ZimraValidationException("RCPT014", $"Line #{line.ReceiptLineNo} TaxPercent ({line.TaxPercent.Value}%) does not match declared tax header rate ({matchingTaxHeader.TaxPercent.Value}%).");
                }
            }

            if (line.ReceiptLinePrice.HasValue)
            {
                if (isCreditNote && string.Equals(line.ReceiptLineType, "Sale", StringComparison.OrdinalIgnoreCase) && line.ReceiptLinePrice.Value > 0)
                {
                    throw new ZimraValidationException("RCPT022", "Receipt line price must be less than 0 for CreditNote Sale lines.");
                }
                if (!isCreditNote && string.Equals(line.ReceiptLineType, "Sale", StringComparison.OrdinalIgnoreCase) && line.ReceiptLinePrice.Value < 0)
                {
                    throw new ZimraValidationException("RCPT022", "Receipt line price must be greater than 0 for FiscalInvoice/DebitNote Sale lines.");
                }

                decimal calcLineTotal = line.ReceiptLinePrice.Value * line.ReceiptLineQuantity;
                if (Math.Abs(line.ReceiptLineTotal - calcLineTotal) > 0.05m)
                {
                    throw new ZimraValidationException("RCPT024", $"Line #{line.ReceiptLineNo} total ({line.ReceiptLineTotal}) does not match price * quantity ({calcLineTotal}).");
                }
            }
        }

        // Validate tax amount calculation formulas (RCPT026, RCPT027)
        foreach (var tax in dto.Taxes)
        {
            var matchingLines = dto.Lines.Where(l => l.TaxID == tax.TaxID).ToList();
            if (matchingLines.Count > 0)
            {
                decimal linesSum = matchingLines.Sum(l => l.ReceiptLineTotal);
                if (tax.TaxPercent.HasValue && tax.TaxPercent.Value > 0)
                {
                    decimal pct = tax.TaxPercent.Value / 100m;
                    decimal expectedTax = dto.ReceiptLinesTaxInclusive
                        ? linesSum * (pct / (1m + pct))
                        : linesSum * pct;

                    decimal expectedSalesWithTax = dto.ReceiptLinesTaxInclusive
                        ? linesSum
                        : linesSum * (1m + pct);

                    if (Math.Abs(tax.TaxAmount - expectedTax) > 0.10m)
                    {
                        throw new ZimraValidationException("RCPT026", $"Tax amount ({tax.TaxAmount}) for TaxID {tax.TaxID} does not match ZIMRA formula ({expectedTax}).");
                    }
                    if (Math.Abs(tax.SalesAmountWithTax - expectedSalesWithTax) > 0.10m)
                    {
                        throw new ZimraValidationException("RCPT027", $"Sales amount with tax ({tax.SalesAmountWithTax}) for TaxID {tax.TaxID} does not match ZIMRA formula ({expectedSalesWithTax}).");
                    }
                }
            }
        }

        // Validate payment amount signs (RCPT028)
        foreach (var payment in dto.Payments)
        {
            if (isCreditNote && payment.PaymentAmount > 0)
            {
                throw new ZimraValidationException("RCPT028", $"Payment amount ({payment.PaymentAmount}) must be less than or equal to 0 for CreditNote.");
            }
            if (!isCreditNote && payment.PaymentAmount < 0)
            {
                throw new ZimraValidationException("RCPT028", $"Payment amount ({payment.PaymentAmount}) must be greater than or equal to 0 for FiscalInvoice/DebitNote.");
            }
        }

        // Validate line sums
        decimal totalLinesSum = dto.Lines.Sum(l => l.ReceiptLineTotal);
        decimal totalTaxesSum = dto.Taxes.Sum(t => t.TaxAmount);
        decimal totalPaymentsSum = dto.Payments.Sum(p => p.PaymentAmount);

        decimal expectedTotal = dto.ReceiptLinesTaxInclusive ? totalLinesSum : totalLinesSum + totalTaxesSum;

        if (Math.Abs(dto.ReceiptTotal - expectedTotal) > 0.05m)
        {
            throw new ZimraValidationException("RCPT019", $"Receipt total ({dto.ReceiptTotal}) does not match calculated total ({expectedTotal}).");
        }

        if (Math.Abs(dto.ReceiptTotal - totalPaymentsSum) > 0.05m)
        {
            throw new ZimraValidationException("RCPT039", $"Receipt total ({dto.ReceiptTotal}) does not match total payment amount ({totalPaymentsSum}).");
        }
    }
}

