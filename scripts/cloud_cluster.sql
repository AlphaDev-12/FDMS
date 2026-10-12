-- Core Partner Wholesale Tracking Database File Initialization Parameters
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL; -- Optimized for fast network processing loops in cloud tiers

CREATE TABLE IF NOT EXISTS SharedPartnerProfiles (
    PartnerId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartnerKey TEXT UNIQUE NOT NULL, -- e.g., mj_serpa_master_key
    WholesaleTokenBalance INTEGER DEFAULT 0,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS TenantBusinessAccounts (
    TenantId INTEGER PRIMARY KEY AUTOINCREMENT,
    PartnerId INTEGER NOT NULL,
    TenantApiKey TEXT UNIQUE NOT NULL, -- The unique key handed to the SME Client
    BusinessName TEXT NOT NULL,
    IsActive BOOLEAN DEFAULT 1,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(PartnerId) REFERENCES SharedPartnerProfiles(PartnerId)
);

CREATE TABLE IF NOT EXISTS TenantZimraDevices (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    ZimraDeviceId TEXT NOT NULL,
    EncryptedPfxCertificate BLOB NOT NULL, -- Cryptographically isolated binary store
    CertificateDecryptionSalt TEXT NOT NULL,
    CertificatePassword TEXT NULL,
    LastSequenceNumber INTEGER DEFAULT 0,
    IsActive BOOLEAN DEFAULT 1,
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(TenantId, ZimraDeviceId), -- Ensures strict unique device mapping inside the tenant domain
    FOREIGN KEY(TenantId) REFERENCES TenantBusinessAccounts(TenantId)
);

CREATE TABLE IF NOT EXISTS MultiTenantFiscalLedger (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantId INTEGER NOT NULL,
    ZimraDeviceId TEXT NOT NULL,
    InvoiceUuid TEXT UNIQUE NOT NULL,
    InvoiceNumber TEXT NOT NULL,
    CurrencyCode TEXT NOT NULL, -- Strict multi-currency splitting segregation tracker
    GrossInclusiveAmount REAL NOT NULL,
    CryptographicSignature TEXT NOT NULL,
    ZimraSyncStatus TEXT DEFAULT 'SYNCED',
    CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY(TenantId) REFERENCES TenantBusinessAccounts(TenantId)
);

-- CRITICAL PERFORMANCE RECOVERY INDEXING KEYS
CREATE INDEX IF NOT EXISTS idx_tenant_routing_lookup ON TenantZimraDevices (TenantId, ZimraDeviceId);
CREATE INDEX IF NOT EXISTS idx_ledger_tenant_isolation ON MultiTenantFiscalLedger (TenantId, ZimraDeviceId, CreatedAt);
