using FdmsGatewayApi.Controllers;
using FdmsGatewayApi.Data;
using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class TenantUserManagementTests
{
    private FdmsDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FdmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new FdmsDbContext(options);
    }

    [Fact]
    public async Task TenantUserController_Login_WithValidCredentials_Succeeds()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        var partner = new SharedPartnerProfileEntity
        {
            PartnerId = 1,
            PartnerKey = "css_serpa_key",
            WholesaleTokenBalance = 100
        };
        db.SharedPartnerProfiles.Add(partner);

        var tenant = new TenantBusinessAccountEntity
        {
            TenantId = 10,
            PartnerId = 1,
            TenantApiKey = "css_tenant_prod_10",
            BusinessName = "Harare Supermarket Ltd",
            IsActive = true
        };
        db.TenantBusinessAccounts.Add(tenant);

        string salt = TenantUserController.GenerateSalt();
        string hash = TenantUserController.HashPassword("Secr3t123!", salt);

        var user = new TenantUserEntity
        {
            TenantId = 10,
            Email = "admin@hararesupermarket.co.zw",
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = "Tendai Moyo",
            Role = "TenantAdmin",
            Permissions = "ViewDashboard,ViewDocuments,ManageDevices,ManageUsers",
            IsActive = true
        };
        db.TenantUsers.Add(user);
        await db.SaveChangesAsync();

        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        tenantRegistryMock.Setup(r => r.GetTenantContextAsync("css_tenant_prod_10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext { TenantId = 10, TenantApiKey = "css_tenant_prod_10", BusinessName = "Harare Supermarket Ltd", PartnerId = 1, IsActive = true });

        var adminAuthMock = new Mock<IAdminAuthService>();
        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var controller = new TenantUserController(
            db, tenantRegistryMock.Object, adminAuthMock.Object, auditMock.Object, configMock.Object, NullLogger<TenantUserController>.Instance);

        var request = new TenantLoginRequestDto
        {
            Email = "admin@hararesupermarket.co.zw",
            Password = "Secr3t123!",
            TenantApiKey = "css_tenant_prod_10"
        };

        // Act
        var result = await controller.Login(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantLoginResponseDto>(okResult.Value);
        Assert.True(response.Success);
        Assert.NotNull(response.Token);
        Assert.Equal(10, response.TenantId);
        Assert.Equal("Harare Supermarket Ltd", response.BusinessName);
        Assert.Equal("Tendai Moyo", response.User?.FullName);
    }

    [Fact]
    public async Task TenantUserController_Login_WithWrongPassword_ReturnsUnauthorized()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        string salt = TenantUserController.GenerateSalt();
        string hash = TenantUserController.HashPassword("CorrectPassword!", salt);

        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = 10,
            Email = "operator@harare.co.zw",
            PasswordHash = hash,
            PasswordSalt = salt,
            FullName = "Operator User",
            Role = "TenantOperator",
            IsActive = true,
            TenantAccount = new TenantBusinessAccountEntity
            {
                TenantId = 10,
                TenantApiKey = "css_tenant_prod_10",
                BusinessName = "Harare Retail",
                IsActive = true
            }
        });
        await db.SaveChangesAsync();

        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        var adminAuthMock = new Mock<IAdminAuthService>();
        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var controller = new TenantUserController(
            db, tenantRegistryMock.Object, adminAuthMock.Object, auditMock.Object, configMock.Object, NullLogger<TenantUserController>.Instance);

        var request = new TenantLoginRequestDto
        {
            Email = "operator@harare.co.zw",
            Password = "WrongPassword123!"
        };

        // Act
        var result = await controller.Login(request, CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task TenantUserController_GetTenantUsers_WithAdminSession_BypassesPermissionCheck()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        db.TenantUsers.Add(new TenantUserEntity
        {
            TenantId = 1,
            Email = "user1@company.co.zw",
            FullName = "First User",
            Role = "TenantOperator",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        var adminAuthMock = new Mock<IAdminAuthService>();
        adminAuthMock.Setup(a => a.ValidateSession("valid_admin_token")).Returns(true);

        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var controller = new TenantUserController(
            db, tenantRegistryMock.Object, adminAuthMock.Object, auditMock.Object, configMock.Object, NullLogger<TenantUserController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.Request.Headers["X-Dashboard-Session"] = "valid_admin_token";

        // Act
        var result = await controller.GetTenantUsers(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var users = Assert.IsAssignableFrom<IEnumerable<TenantUserDto>>(okResult.Value);
        Assert.Single(users);
    }

    [Fact]
    public async Task TenantDashboardController_GetTenantDocuments_StrictlyIsolatesTenantData()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        // Tenant 10 document
        db.MultiTenantFiscalLedger.Add(new MultiTenantFiscalLedgerEntity
        {
            TenantId = 10,
            ZimraDeviceId = "DEV-1001",
            InvoiceUuid = Guid.NewGuid().ToString(),
            InvoiceNumber = "INV-TENANT10-001",
            CurrencyCode = "USD",
            GrossInclusiveAmount = 150.00m,
            CryptographicSignature = "SIG10",
            ZimraSyncStatus = "SYNCED",
            CreatedAt = DateTime.UtcNow
        });

        // Tenant 20 document (Client 2 data wall test)
        db.MultiTenantFiscalLedger.Add(new MultiTenantFiscalLedgerEntity
        {
            TenantId = 20,
            ZimraDeviceId = "DEV-2002",
            InvoiceUuid = Guid.NewGuid().ToString(),
            InvoiceNumber = "INV-TENANT20-999",
            CurrencyCode = "USD",
            GrossInclusiveAmount = 9999.00m,
            CryptographicSignature = "SIG20",
            ZimraSyncStatus = "SYNCED",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        tenantRegistryMock.Setup(r => r.GetTenantContextAsync("css_tenant_prod_10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext { TenantId = 10, TenantApiKey = "css_tenant_prod_10", BusinessName = "Tenant 10 Account", PartnerId = 1, IsActive = true });

        var cryptoMock = new Mock<IFdmsCryptoService>();

        var controller = new TenantDashboardController(
            db, tenantRegistryMock.Object, cryptoMock.Object, NullLogger<TenantDashboardController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Act - Request documents for Tenant 10
        var result = await controller.GetTenantDocuments(tenantApiKey: "css_tenant_prod_10", page: 1, pageSize: 10, cancellationToken: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);

        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var itemsProp = root.TryGetProperty("items", out var it) ? it : root.GetProperty("Items");

        Assert.Equal(1, itemsProp.GetArrayLength());
        var firstItem = itemsProp[0];
        var invNo = firstItem.TryGetProperty("invoiceNumber", out var inv) ? inv.GetString() : firstItem.GetProperty("InvoiceNumber").GetString();
        var tId = firstItem.TryGetProperty("tenantId", out var tid) ? tid.GetInt32() : firstItem.GetProperty("TenantId").GetInt32();

        Assert.Equal("INV-TENANT10-001", invNo);
        Assert.Equal(10, tId);

        // Verify Tenant 20's document is completely absent
        Assert.DoesNotContain("INV-TENANT20-999", json);
    }

    [Fact]
    public async Task TenantUserController_PublicRegister_WithApiKey_AssignsFullAdminPermissions()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var tenant = new TenantBusinessAccountEntity
        {
            TenantId = 15,
            PartnerId = 1,
            TenantApiKey = "css_tenant_prod_15",
            BusinessName = "Bulawayo Retailers Ltd",
            IsActive = true
        };
        db.TenantBusinessAccounts.Add(tenant);
        await db.SaveChangesAsync();

        var tenantRegistryMock = new Mock<ITenantRegistryService>();
        tenantRegistryMock.Setup(r => r.GetTenantContextAsync("css_tenant_prod_15", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantContext { TenantId = 15, TenantApiKey = "css_tenant_prod_15", BusinessName = "Bulawayo Retailers Ltd", PartnerId = 1, IsActive = true });

        var adminAuthMock = new Mock<IAdminAuthService>();
        var auditMock = new Mock<IAuditLogger>();
        var configMock = new Mock<IConfiguration>();

        var controller = new TenantUserController(
            db, tenantRegistryMock.Object, adminAuthMock.Object, auditMock.Object, configMock.Object, NullLogger<TenantUserController>.Instance);

        var request = new PublicTenantRegisterRequestDto
        {
            TenantApiKey = "css_tenant_prod_15",
            Email = "admin@bulawayoretail.co.zw",
            FullName = "Admin User",
            Password = "Password123!"
        };

        // Act
        var result = await controller.PublicRegister(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<TenantLoginResponseDto>(okResult.Value);
        Assert.True(response.Success);

        var createdUser = await db.TenantUsers.FirstOrDefaultAsync(u => u.Email == "admin@bulawayoretail.co.zw");
        Assert.NotNull(createdUser);
        Assert.Equal("TenantAdmin", createdUser.Role);
        Assert.Contains("ManageUsers", createdUser.Permissions);
        Assert.Contains("ManageDevices", createdUser.Permissions);
        Assert.Contains("ViewDashboard", createdUser.Permissions);
        Assert.Contains("ViewDocuments", createdUser.Permissions);
        Assert.Contains("SubmitReceipts", createdUser.Permissions);
    }
}
