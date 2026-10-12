using System.Security;
using FdmsGatewayApi.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FdmsGatewayApi.Data;

public class ImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateImmutability(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateImmutability(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ValidateImmutability(DbContext? context)
    {
        if (context == null) return;

        // Scan ChangeTracker for unauthorized UPDATE or DELETE states on FiscalDocumentEntity or AuditLogEntity
        var fiscalViolations = context.ChangeTracker.Entries<FiscalDocumentEntity>()
            .Where(e => e.State == EntityState.Deleted ||
                       (e.State == EntityState.Modified && e.OriginalValues.GetValue<string>(nameof(FiscalDocumentEntity.FiscalStatus)) == "Fiscalized"))
            .ToList();

        var auditViolations = context.ChangeTracker.Entries<AuditLogEntity>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        if (fiscalViolations.Count > 0)
        {
            var entry = fiscalViolations.First();
            string operation = entry.State.ToString();
            string invoiceNo = entry.Entity.InvoiceNo;
            Guid entityId = entry.Entity.Id;

            Console.Error.WriteLine($"CRITICAL SECURITY AUDIT ALERT: Malicious {operation} attempted on FiscalDocument ID {entityId} (Invoice: {invoiceNo}) at {DateTime.UtcNow:o} UTC.");

            throw new SecurityException(
                $"SECURITY IMMUTABILITY VIOLATION: Modifying or deleting historical ZIMRA fiscal records is illegal under tax law. " +
                $"Attempted operation: {operation} on FiscalDocument Invoice '{invoiceNo}' (ID: {entityId}). Application context terminated.");
        }

        if (auditViolations.Count > 0)
        {
            var entry = auditViolations.First();
            string operation = entry.State.ToString();
            Guid entityId = entry.Entity.Id;

            Console.Error.WriteLine($"CRITICAL SECURITY AUDIT ALERT: Malicious {operation} attempted on AuditLog ID {entityId} at {DateTime.UtcNow:o} UTC.");

            throw new SecurityException(
                $"SECURITY IMMUTABILITY VIOLATION: Modifying or deleting audit logs is strictly prohibited. " +
                $"Attempted operation: {operation} on AuditLog ID {entityId}. Application context terminated.");
        }
    }
}
