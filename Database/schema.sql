-- Copy Trade Platform Database Schema
-- PostgreSQL compatible

-- Users table
CREATE TABLE Users (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    Email VARCHAR(255) NOT NULL UNIQUE,
    Username VARCHAR(100) NOT NULL UNIQUE,
    PasswordHash VARCHAR(255) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    LastLoginAt TIMESTAMP
);

-- Trading Accounts table
CREATE TABLE TradingAccounts (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    UserId UUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    Platform VARCHAR(50) NOT NULL, -- 'TradeStation', 'NinjaTrader', 'Tradovate'
    AccountName VARCHAR(255) NOT NULL,
    AccountId VARCHAR(255) NOT NULL,
    
    -- Encrypted credentials
    ApiUsername VARCHAR(255),
    ApiPassword VARCHAR(500), -- Encrypted
    ApiKey VARCHAR(500),      -- Encrypted
    SecretKey VARCHAR(500),   -- Encrypted
    AccessToken VARCHAR(1000), -- Encrypted
    TokenExpiration TIMESTAMP,
    
    AccountType VARCHAR(20) NOT NULL, -- 'Source' or 'Destination'
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    LastConnectedAt TIMESTAMP,
    
    CONSTRAINT unique_user_account UNIQUE(UserId, AccountId)
);

-- Copy Configurations table
CREATE TABLE CopyConfigurations (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    UserId UUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    SourceAccountId UUID NOT NULL REFERENCES TradingAccounts(Id),
    DestinationAccountId UUID NOT NULL REFERENCES TradingAccounts(Id),
    
    -- Copy Settings
    IsActive BOOLEAN NOT NULL DEFAULT TRUE,
    PositionSizeMultiplier DECIMAL(10,4) NOT NULL DEFAULT 1.0,
    CopyStopLoss BOOLEAN NOT NULL DEFAULT TRUE,
    CopyTakeProfit BOOLEAN NOT NULL DEFAULT TRUE,
    ReverseSignals BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Filters (stored as JSON)
    SymbolWhitelist TEXT, -- JSON array
    SymbolBlacklist TEXT, -- JSON array
    MaxPositionSize DECIMAL(18,2),
    MaxDailyLoss DECIMAL(18,2),
    
    -- Statistics
    TradesCopied INT NOT NULL DEFAULT 0,
    LastTradeAt TIMESTAMP,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    
    CONSTRAINT check_accounts_different CHECK (SourceAccountId != DestinationAccountId)
);

-- Trade Signals table
CREATE TABLE TradeSignals (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    SourcePlatform VARCHAR(50) NOT NULL,
    SourceAccountId VARCHAR(255) NOT NULL,
    SourceOrderId VARCHAR(255),
    Timestamp TIMESTAMP NOT NULL,
    
    -- Order Details
    Symbol VARCHAR(50) NOT NULL,
    Action VARCHAR(20) NOT NULL, -- 'Buy', 'Sell', 'SellShort', 'BuyToCover'
    OrderType VARCHAR(20) NOT NULL, -- 'Market', 'Limit', 'Stop', 'StopLimit'
    Quantity INT NOT NULL,
    LimitPrice DECIMAL(18,8),
    StopPrice DECIMAL(18,8),
    
    -- Order Management
    TimeInForce VARCHAR(10), -- 'Day', 'GTC', 'IOC', 'FOK'
    IsClosing BOOLEAN NOT NULL DEFAULT FALSE,
    
    -- Stop Loss / Take Profit
    StopLoss DECIMAL(18,8),
    TakeProfit DECIMAL(18,8),
    
    -- Additional Info
    Notes TEXT,
    
    -- Status
    Status VARCHAR(20) NOT NULL DEFAULT 'Received', -- 'Received', 'Processing', 'Executed', 'Failed', 'Cancelled'
    ProcessedAt TIMESTAMP,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Executed Trades table
CREATE TABLE ExecutedTrades (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    SignalId UUID NOT NULL REFERENCES TradeSignals(Id),
    CopyConfigId UUID NOT NULL REFERENCES CopyConfigurations(Id),
    DestinationAccountId UUID NOT NULL REFERENCES TradingAccounts(Id),
    
    ExecutedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    DestinationOrderId VARCHAR(255),
    Status VARCHAR(30) NOT NULL, -- 'Pending', 'Filled', 'PartiallyFilled', 'Rejected', 'Cancelled', 'Error'
    ErrorMessage TEXT,
    
    QuantityFilled INT NOT NULL DEFAULT 0,
    FillPrice DECIMAL(18,8),
    
    -- Performance tracking
    ProfitLoss DECIMAL(18,2),
    Commissions DECIMAL(18,2),
    
    CONSTRAINT check_quantity_positive CHECK (QuantityFilled >= 0)
);

-- API Keys table (for authentication)
CREATE TABLE ApiKeys (
    Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    UserId UUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    KeyHash VARCHAR(255) NOT NULL UNIQUE,
    Name VARCHAR(100) NOT NULL,
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
    ExpiresAt TIMESTAMP,
    LastUsedAt TIMESTAMP,
    IsActive BOOLEAN NOT NULL DEFAULT TRUE
);

-- Audit Log table
CREATE TABLE AuditLogs (
    Id BIGSERIAL PRIMARY KEY,
    UserId UUID REFERENCES Users(Id),
    Action VARCHAR(100) NOT NULL,
    EntityType VARCHAR(50),
    EntityId UUID,
    Details TEXT,
    IpAddress VARCHAR(45),
    CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
);

-- Create indexes for performance
CREATE INDEX idx_trading_accounts_user ON TradingAccounts(UserId);
CREATE INDEX idx_trading_accounts_platform ON TradingAccounts(Platform);
CREATE INDEX idx_copy_configs_user ON CopyConfigurations(UserId);
CREATE INDEX idx_copy_configs_source ON CopyConfigurations(SourceAccountId);
CREATE INDEX idx_copy_configs_dest ON CopyConfigurations(DestinationAccountId);
CREATE INDEX idx_copy_configs_active ON CopyConfigurations(IsActive);
CREATE INDEX idx_trade_signals_timestamp ON TradeSignals(Timestamp DESC);
CREATE INDEX idx_trade_signals_symbol ON TradeSignals(Symbol);
CREATE INDEX idx_trade_signals_status ON TradeSignals(Status);
CREATE INDEX idx_executed_trades_signal ON ExecutedTrades(SignalId);
CREATE INDEX idx_executed_trades_config ON ExecutedTrades(CopyConfigId);
CREATE INDEX idx_executed_trades_account ON ExecutedTrades(DestinationAccountId);
CREATE INDEX idx_executed_trades_executed_at ON ExecutedTrades(ExecutedAt DESC);
CREATE INDEX idx_audit_logs_user ON AuditLogs(UserId);
CREATE INDEX idx_audit_logs_created_at ON AuditLogs(CreatedAt DESC);

-- Create views for reporting

-- Active copy configurations with account details
CREATE VIEW vw_ActiveCopyConfigurations AS
SELECT 
    cc.Id,
    cc.UserId,
    u.Username,
    sa.Platform AS SourcePlatform,
    sa.AccountName AS SourceAccountName,
    da.Platform AS DestinationPlatform,
    da.AccountName AS DestinationAccountName,
    cc.PositionSizeMultiplier,
    cc.TradesCopied,
    cc.LastTradeAt,
    cc.CreatedAt
FROM CopyConfigurations cc
JOIN Users u ON cc.UserId = u.Id
JOIN TradingAccounts sa ON cc.SourceAccountId = sa.Id
JOIN TradingAccounts da ON cc.DestinationAccountId = da.Id
WHERE cc.IsActive = TRUE;

-- Trade execution summary
CREATE VIEW vw_TradeExecutionSummary AS
SELECT 
    et.DestinationAccountId,
    ta.AccountName,
    DATE(et.ExecutedAt) AS TradeDate,
    COUNT(*) AS TotalTrades,
    SUM(CASE WHEN et.Status = 'Filled' THEN 1 ELSE 0 END) AS SuccessfulTrades,
    SUM(CASE WHEN et.Status = 'Error' THEN 1 ELSE 0 END) AS FailedTrades,
    SUM(et.ProfitLoss) AS TotalProfitLoss
FROM ExecutedTrades et
JOIN TradingAccounts ta ON et.DestinationAccountId = ta.Id
GROUP BY et.DestinationAccountId, ta.AccountName, DATE(et.ExecutedAt);

-- Recent signals
CREATE VIEW vw_RecentSignals AS
SELECT 
    ts.Id,
    ts.Symbol,
    ts.Action,
    ts.OrderType,
    ts.Quantity,
    ts.Status,
    ts.Timestamp,
    COUNT(et.Id) AS ExecutionCount,
    SUM(CASE WHEN et.Status = 'Filled' THEN 1 ELSE 0 END) AS SuccessfulExecutions
FROM TradeSignals ts
LEFT JOIN ExecutedTrades et ON ts.Id = et.SignalId
WHERE ts.Timestamp > NOW() - INTERVAL '7 days'
GROUP BY ts.Id
ORDER BY ts.Timestamp DESC;

-- Insert sample data (optional for testing)
/*
INSERT INTO Users (Email, Username, PasswordHash) 
VALUES ('demo@example.com', 'demo_user', 'hash_placeholder');

INSERT INTO TradingAccounts (UserId, Platform, AccountName, AccountId, AccountType)
VALUES 
    ((SELECT Id FROM Users WHERE Username = 'demo_user'), 'TradeStation', 'TS Demo', 'TS123456', 'Source'),
    ((SELECT Id FROM Users WHERE Username = 'demo_user'), 'Tradovate', 'Tradovate Demo', 'TDVT789', 'Destination');

INSERT INTO CopyConfigurations (UserId, SourceAccountId, DestinationAccountId)
VALUES (
    (SELECT Id FROM Users WHERE Username = 'demo_user'),
    (SELECT Id FROM TradingAccounts WHERE AccountId = 'TS123456'),
    (SELECT Id FROM TradingAccounts WHERE AccountId = 'TDVT789')
);
*/

-- Grant permissions (adjust as needed)
-- GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO copytrade_user;
-- GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO copytrade_user;
