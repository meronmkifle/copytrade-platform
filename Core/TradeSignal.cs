using System;

namespace CopyTradePlatform.Core
{
    /// <summary>
    /// Universal trade signal model used for copy trading across platforms
    /// </summary>
    public class TradeSignal
    {
        public Guid Id { get; set; }
        public string SourcePlatform { get; set; }  // "TradeStation"
        public string SourceAccountId { get; set; }
        public DateTime Timestamp { get; set; }
        
        // Order Details
        public string Symbol { get; set; }
        public TradeAction Action { get; set; }  // Buy, Sell, BuyToCover, SellShort
        public OrderType OrderType { get; set; }   // Market, Limit, Stop, StopLimit
        public int Quantity { get; set; }
        public decimal? LimitPrice { get; set; }
        public decimal? StopPrice { get; set; }
        
        // Order Management
        public string TimeInForce { get; set; }  // Day, GTC, IOC, FOK
        public bool IsClosing { get; set; }       // True if closing existing position
        
        // Stop Loss / Take Profit
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        
        // Additional Info
        public string SourceOrderId { get; set; }
        public string Notes { get; set; }
        
        // Status
        public SignalStatus Status { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
    
    public enum TradeAction
    {
        Buy,
        Sell,
        SellShort,
        BuyToCover
    }
    
    public enum OrderType
    {
        Market,
        Limit,
        Stop,
        StopLimit
    }
    
    public enum SignalStatus
    {
        Received,
        Processing,
        Executed,
        Failed,
        Cancelled
    }
}
