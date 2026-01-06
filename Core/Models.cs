using System;
using System.Collections.Generic;

namespace CopyTradePlatform.Core
{
    /// <summary>
    /// User account in the copy trading system
    /// </summary>
    public class User
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string Username { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        
        public List<TradingAccount> TradingAccounts { get; set; }
        public List<CopyConfiguration> CopyConfigurations { get; set; }
    }
    
    /// <summary>
    /// Represents a trading account on any platform
    /// </summary>
    public class TradingAccount
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Platform { get; set; }  // "TradeStation", "NinjaTrader", "Tradovate"
        public string AccountName { get; set; }
        public string AccountId { get; set; }
        
        // Credentials (encrypted in production)
        public string ApiUsername { get; set; }
        public string ApiPassword { get; set; }
        public string ApiKey { get; set; }
        public string SecretKey { get; set; }
        public string AccessToken { get; set; }
        public DateTime? TokenExpiration { get; set; }
        
        public AccountType AccountType { get; set; }  // Source or Destination
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastConnectedAt { get; set; }
    }
    
    /// <summary>
    /// Configuration for copying trades from source to destination
    /// </summary>
    public class CopyConfiguration
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid SourceAccountId { get; set; }
        public Guid DestinationAccountId { get; set; }
        
        public TradingAccount SourceAccount { get; set; }
        public TradingAccount DestinationAccount { get; set; }
        
        // Copy Settings
        public bool IsActive { get; set; }
        public decimal PositionSizeMultiplier { get; set; }  // 1.0 = same size, 0.5 = half size
        public bool CopyStopLoss { get; set; }
        public bool CopyTakeProfit { get; set; }
        public bool ReverseSignals { get; set; }  // Invert buy/sell signals
        
        // Filters
        public List<string> SymbolWhitelist { get; set; }  // Only copy these symbols
        public List<string> SymbolBlacklist { get; set; }  // Never copy these symbols
        public decimal? MaxPositionSize { get; set; }
        public decimal? MaxDailyLoss { get; set; }
        
        // Statistics
        public int TradesCopied { get; set; }
        public DateTime? LastTradeAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
    
    public enum AccountType
    {
        Source,      // Account being monitored (TradeStation)
        Destination  // Account receiving copied trades (NinjaTrader/Tradovate)
    }
    
    /// <summary>
    /// Log of executed trades
    /// </summary>
    public class ExecutedTrade
    {
        public Guid Id { get; set; }
        public Guid SignalId { get; set; }
        public Guid CopyConfigId { get; set; }
        public Guid DestinationAccountId { get; set; }
        
        public TradeSignal Signal { get; set; }
        
        public DateTime ExecutedAt { get; set; }
        public string DestinationOrderId { get; set; }
        public ExecutionStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        
        public int QuantityFilled { get; set; }
        public decimal? FillPrice { get; set; }
    }
    
    public enum ExecutionStatus
    {
        Pending,
        Filled,
        PartiallyFilled,
        Rejected,
        Cancelled,
        Error
    }
}
