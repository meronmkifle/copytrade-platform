using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CopyTradePlatform.Core;

namespace CopyTradePlatform.Services
{
    /// <summary>
    /// Main service that orchestrates copy trading between platforms
    /// </summary>
    public class CopyTradingService
    {
        private readonly Dictionary<string, TradeStationClient> _tradeStationClients;
        private readonly Dictionary<string, TradovateExecutor> _tradovateExecutors;
        private readonly HttpClient _httpClient;
        
        public CopyTradingService()
        {
            _tradeStationClients = new Dictionary<string, TradeStationClient>();
            _tradovateExecutors = new Dictionary<string, TradovateExecutor>();
            _httpClient = new HttpClient();
        }
        
        /// <summary>
        /// Initialize TradeStation client for monitoring
        /// </summary>
        public async Task<bool> InitializeTradeStationAccount(TradingAccount account)
        {
            var client = new TradeStationClient(account.ApiKey, account.SecretKey, true);
            
            var success = await client.Authenticate(account.ApiUsername, account.ApiPassword);
            
            if (success)
            {
                _tradeStationClients[account.Id.ToString()] = client;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Initialize Tradovate executor
        /// </summary>
        public async Task<bool> InitializeTradovateAccount(TradingAccount account)
        {
            var executor = new TradovateExecutor(true);
            
            var success = await executor.Authenticate(
                account.ApiUsername,
                account.ApiPassword,
                "CopyTradePlatform",
                "1.0",
                account.ApiKey,
                account.SecretKey
            );
            
            if (success)
            {
                _tradovateExecutors[account.Id.ToString()] = executor;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Start monitoring a TradeStation account for new trades
        /// </summary>
        public async Task StartMonitoring(TradingAccount sourceAccount, 
            List<CopyConfiguration> copyConfigs)
        {
            if (!_tradeStationClients.TryGetValue(sourceAccount.Id.ToString(), out var client))
            {
                throw new Exception($"TradeStation client not initialized for account {sourceAccount.AccountId}");
            }
            
            // Start streaming orders
            await client.StreamOrderUpdates(sourceAccount.AccountId, async (order) =>
            {
                // Only process filled orders
                if (order.Status == "FLL")
                {
                    var signal = client.ConvertToSignal(order, sourceAccount.AccountId);
                    await ProcessTradeSignal(signal, copyConfigs);
                }
            });
        }
        
        /// <summary>
        /// Process a trade signal and distribute to destination accounts
        /// </summary>
        public async Task<List<ExecutedTrade>> ProcessTradeSignal(TradeSignal signal, 
            List<CopyConfiguration> copyConfigs)
        {
            var executedTrades = new List<ExecutedTrade>();
            
            foreach (var config in copyConfigs.Where(c => c.IsActive))
            {
                try
                {
                    // Check filters
                    if (!ShouldCopyTrade(signal, config))
                        continue;
                    
                    // Adjust quantity based on multiplier
                    var adjustedQuantity = (int)(signal.Quantity * config.PositionSizeMultiplier);
                    
                    // Apply max position size
                    if (config.MaxPositionSize.HasValue)
                        adjustedQuantity = Math.Min(adjustedQuantity, (int)config.MaxPositionSize.Value);
                    
                    // Create adjusted signal
                    var adjustedSignal = CloneSignal(signal);
                    adjustedSignal.Quantity = adjustedQuantity;
                    
                    // Reverse signals if configured
                    if (config.ReverseSignals)
                        ReverseSignal(adjustedSignal);
                    
                    // Remove stop/target if not copying
                    if (!config.CopyStopLoss)
                        adjustedSignal.StopLoss = null;
                    
                    if (!config.CopyTakeProfit)
                        adjustedSignal.TakeProfit = null;
                    
                    // Execute on destination platform
                    var executedTrade = await ExecuteOnDestination(
                        adjustedSignal, 
                        config.DestinationAccount
                    );
                    
                    executedTrade.CopyConfigId = config.Id;
                    executedTrade.SignalId = signal.Id;
                    executedTrades.Add(executedTrade);
                    
                    // Update statistics
                    config.TradesCopied++;
                    config.LastTradeAt = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error copying trade for config {config.Id}: {ex.Message}");
                    
                    executedTrades.Add(new ExecutedTrade
                    {
                        Id = Guid.NewGuid(),
                        SignalId = signal.Id,
                        CopyConfigId = config.Id,
                        DestinationAccountId = config.DestinationAccountId,
                        Status = ExecutionStatus.Error,
                        ErrorMessage = ex.Message,
                        ExecutedAt = DateTime.UtcNow
                    });
                }
            }
            
            return executedTrades;
        }
        
        /// <summary>
        /// Execute trade on destination platform
        /// </summary>
        private async Task<ExecutedTrade> ExecuteOnDestination(TradeSignal signal, 
            TradingAccount destinationAccount)
        {
            var executedTrade = new ExecutedTrade
            {
                Id = Guid.NewGuid(),
                Signal = signal,
                DestinationAccountId = destinationAccount.Id,
                ExecutedAt = DateTime.UtcNow
            };
            
            try
            {
                switch (destinationAccount.Platform.ToLower())
                {
                    case "tradovate":
                        await ExecuteOnTradovate(signal, destinationAccount, executedTrade);
                        break;
                    
                    case "ninjatrader":
                        await ExecuteOnNinjaTrader(signal, destinationAccount, executedTrade);
                        break;
                    
                    default:
                        throw new Exception($"Unsupported platform: {destinationAccount.Platform}");
                }
            }
            catch (Exception ex)
            {
                executedTrade.Status = ExecutionStatus.Error;
                executedTrade.ErrorMessage = ex.Message;
            }
            
            return executedTrade;
        }
        
        /// <summary>
        /// Execute trade on Tradovate
        /// </summary>
        private async Task ExecuteOnTradovate(TradeSignal signal, TradingAccount account, 
            ExecutedTrade executedTrade)
        {
            if (!_tradovateExecutors.TryGetValue(account.Id.ToString(), out var executor))
            {
                throw new Exception($"Tradovate executor not initialized for account {account.AccountId}");
            }
            
            var result = await executor.ExecuteTrade(
                signal,
                int.Parse(account.AccountId),
                account.AccountName
            );
            
            executedTrade.DestinationOrderId = result.OrderId.ToString();
            executedTrade.Status = result.OrderStatus.Contains("Accept") 
                ? ExecutionStatus.Filled 
                : ExecutionStatus.Pending;
        }
        
        /// <summary>
        /// Execute trade on NinjaTrader via HTTP
        /// </summary>
        private async Task ExecuteOnNinjaTrader(TradeSignal signal, TradingAccount account, 
            ExecutedTrade executedTrade)
        {
            // NinjaTrader requires local HTTP endpoint
            var endpoint = $"http://localhost:8080/";  // Default port from strategy
            
            var payload = new
            {
                Id = signal.Id.ToString(),
                Symbol = signal.Symbol,
                Action = signal.Action.ToString(),
                OrderType = signal.OrderType.ToString(),
                Quantity = signal.Quantity,
                LimitPrice = signal.LimitPrice,
                StopPrice = signal.StopPrice,
                StopLossTicks = CalculateStopLossTicks(signal),
                ProfitTargetTicks = CalculateProfitTargetTicks(signal)
            };
            
            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.ApiKey);
            
            var response = await _httpClient.PostAsync(endpoint, content);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                var executionResult = JsonConvert.DeserializeObject<NinjaTraderResult>(result);
                
                executedTrade.DestinationOrderId = executionResult.OrderId;
                executedTrade.Status = executionResult.Success 
                    ? ExecutionStatus.Filled 
                    : ExecutionStatus.Error;
                executedTrade.ErrorMessage = executionResult.Message;
            }
            else
            {
                throw new Exception($"NinjaTrader execution failed: {response.StatusCode}");
            }
        }
        
        /// <summary>
        /// Check if trade should be copied based on configuration filters
        /// </summary>
        private bool ShouldCopyTrade(TradeSignal signal, CopyConfiguration config)
        {
            // Check symbol whitelist
            if (config.SymbolWhitelist != null && config.SymbolWhitelist.Any())
            {
                if (!config.SymbolWhitelist.Contains(signal.Symbol))
                    return false;
            }
            
            // Check symbol blacklist
            if (config.SymbolBlacklist != null && config.SymbolBlacklist.Any())
            {
                if (config.SymbolBlacklist.Contains(signal.Symbol))
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Clone a trade signal
        /// </summary>
        private TradeSignal CloneSignal(TradeSignal signal)
        {
            return new TradeSignal
            {
                Id = Guid.NewGuid(),
                SourcePlatform = signal.SourcePlatform,
                SourceAccountId = signal.SourceAccountId,
                SourceOrderId = signal.SourceOrderId,
                Timestamp = signal.Timestamp,
                Symbol = signal.Symbol,
                Action = signal.Action,
                OrderType = signal.OrderType,
                Quantity = signal.Quantity,
                LimitPrice = signal.LimitPrice,
                StopPrice = signal.StopPrice,
                TimeInForce = signal.TimeInForce,
                IsClosing = signal.IsClosing,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                Notes = signal.Notes,
                Status = SignalStatus.Received
            };
        }
        
        /// <summary>
        /// Reverse buy/sell signals
        /// </summary>
        private void ReverseSignal(TradeSignal signal)
        {
            signal.Action = signal.Action switch
            {
                TradeAction.Buy => TradeAction.SellShort,
                TradeAction.SellShort => TradeAction.Buy,
                TradeAction.Sell => TradeAction.BuyToCover,
                TradeAction.BuyToCover => TradeAction.Sell,
                _ => signal.Action
            };
        }
        
        private int CalculateStopLossTicks(TradeSignal signal)
        {
            // Convert stop loss price to ticks (simplified - needs instrument tick size)
            if (signal.StopLoss.HasValue && signal.LimitPrice.HasValue)
            {
                return (int)Math.Abs(signal.LimitPrice.Value - signal.StopLoss.Value);
            }
            return 0;
        }
        
        private int CalculateProfitTargetTicks(TradeSignal signal)
        {
            // Convert take profit price to ticks (simplified - needs instrument tick size)
            if (signal.TakeProfit.HasValue && signal.LimitPrice.HasValue)
            {
                return (int)Math.Abs(signal.TakeProfit.Value - signal.LimitPrice.Value);
            }
            return 0;
        }
        
        private class NinjaTraderResult
        {
            public bool Success { get; set; }
            public string OrderId { get; set; }
            public string Message { get; set; }
        }
    }
}
