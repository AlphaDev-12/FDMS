namespace FdmsBridgeManager.Models.Dtos;

// --- White Label DTOs ---
public class WhiteLabelConfigDto
{
    public string FirmName { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string CopyrightText { get; set; } = string.Empty;
    public string EmailFooterText { get; set; } = string.Empty;
}

public class UpdateWhiteLabelRequest
{
    public string FirmName { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public string PrimaryColor { get; set; } = string.Empty;
    public string SecondaryColor { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
    public string SupportPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string CopyrightText { get; set; } = string.Empty;
    public string EmailFooterText { get; set; } = string.Empty;
}

// --- Plan DTOs ---
public class PlanDto
{
    public Guid Id { get; set; }
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int DurationMonths { get; set; }
    public int MaxDevices { get; set; }
    public int GracePeriodDays { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class UpsertPlanRequest
{
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = "Monthly";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int DurationMonths { get; set; } = 1;
    public int MaxDevices { get; set; } = 10;
    public int GracePeriodDays { get; set; } = 7;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
}

// --- Client DTOs ---
public class ClientDto
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TenantApiKey { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public int ActiveSubscriptionsCount { get; set; }
}

public class PagedClientResultDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public List<ClientDto> Items { get; set; } = new();
}

public class UpsertClientRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? TenantApiKey { get; set; }
    public string Status { get; set; } = "Active";
}

// --- Subscription DTOs ---
public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int MaxDevices { get; set; }
    public int TokensConsumed { get; set; }
    public string MaskedApiKey { get; set; } = string.Empty;
    public string FullApiKey { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int DaysRemaining { get; set; }
    public string Status { get; set; } = string.Empty; // Active, Expired, GracePeriod
    public bool IsActive { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string TransactionRef { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public string ActivatedBy { get; set; } = string.Empty;
}

public class PagedSubscriptionResultDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public List<SubscriptionDto> Items { get; set; } = new();
}

public class GenerateCashKeyRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public int DeviceCount { get; set; } = 1;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick";
    public string CashReceiptRef { get; set; } = string.Empty;
    public bool SendEmailReceipt { get; set; } = true;
}

public class GenerateDeviceUpgradeKeyRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick"; // TillStick or BranchBox
    public int SlotsToAdd { get; set; } = 1;
    public int CurrentMaxDevices { get; set; } = 1;
    public string CashReceiptRef { get; set; } = string.Empty;
}

public class GenerateDeviceUpgradeKeyResponse
{
    public string UpgradeKey { get; set; } = string.Empty;
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = string.Empty;
    public int SlotsAdded { get; set; }
    public int NewMaxDevices { get; set; }
    public int HardCap { get; set; }
    public decimal PricePerSlotUSD { get; set; }
    public decimal PricePaidUSD { get; set; }
    public int UpgradeTokensConsumed { get; set; }
}

// --- Hardware Tier Pricing DTOs ---
public class HardwareTierConfigDto
{
    public Guid Id { get; set; }
    public string TierName { get; set; } = string.Empty;
    public decimal PricePerSlotUSD { get; set; }
    public int TokensPerSlot { get; set; }
    public int HardCap { get; set; }
    public int DefaultDevices { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}

public class UpdateHardwareTierPricesRequest
{
    public List<HardwareTierConfigDto> Tiers { get; set; } = new();
}

// --- Checkout DTOs ---
public class PublicCheckoutRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public int DeviceCount { get; set; } = 1;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick";
}

public class PublicUpgradeCheckoutRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DriveInstanceId { get; set; } = string.Empty;
    public string HardwareTier { get; set; } = "TillStick";
    public int SlotsToAdd { get; set; } = 1;
    public int CurrentMaxDevices { get; set; } = 1;
}

public class ValidateTinRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
}

public class ValidateTinResponse
{
    public bool IsRegistered { get; set; }
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ConsultationRequest
{
    public string TaxPayerTIN { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class PublicCheckoutResponse
{
    public Guid TransactionId { get; set; }
    public string PayNowRedirectUrl { get; set; } = string.Empty;
    public string PayNowReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string PlanName { get; set; } = string.Empty;
}

public class SimulatePayNowRequest
{
    public Guid TransactionId { get; set; }
}

// --- User & Auth DTOs ---
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Staff";
    public List<string> Permissions { get; set; } = new();
}

public class UpdateUserPermissionsRequest
{
    public List<string> Permissions { get; set; } = new();
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public bool IsActive { get; set; } = true;
}
