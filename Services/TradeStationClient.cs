using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CopyTradePlatform.Core;

namespace CopyTradePlatform.Services
{
    /// <summary>
    /// TradeStation API client for monitoring and retrieving trade data
    /// </summary>
    public class TradeStationClient
    {
        private readonly HttpClient _httpClient;
        private string _accessToken;
        private DateTime _tokenExpiration;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly bool _useSandbox;
        
        public TradeStationClient(string clientId, string clientSecret, bool useSandbox = true)
        {
            _httpClient = new HttpClient();
            _clientId = clientId;
            _clientSecret = clientSecret;
            _useSandbox = useSandbox;
            
            _httpClient.BaseAddress = new Uri(useSandbox 
                ? "https://sim-api.tradestation.com/v3/" 
                : "https://api.tradestation.com/v3/");
        }
        
        /// <summary>
        /// Authenticate with TradeStation using OAuth2
        /// </summary>
        public async Task<bool> Authenticate(string username, string password)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://signin.tradestation.com/oauth/token");
                
                var credentials = new Dictionary<string, string>
                {
                    { "grant_type", "password" },
                    { "username", username },
                    { "password", password },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret },
                    { "scope", "openid profile MarketData ReadAccount Trade" }
                };
                
                request.Content = new FormUrlEncodedContent(credentials);
                
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);
                    
                    _accessToken = tokenResponse.AccessToken;
                    _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                    
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TradeStation auth error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get list of accounts for the authenticated user
        /// </summary>
        public async Task<List<AccountInfo>> GetAccounts()
        {
            await EnsureTokenValid();
            
            var response = await _httpClient.GetAsync("brokerage/accounts");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var accounts = JsonConvert.DeserializeObject<AccountsResponse>(content);
            
            return accounts.Accounts;
        }
        
        /// <summary>
        /// Get orders for a specific account
        /// </summary>
        public async Task<List<TSOrder>> GetOrders(string accountId, DateTime? since = null)
        {
            await EnsureTokenValid();
            
            var url = $"brokerage/accounts/{accountId}/orders";
            if (since.HasValue)
            {
                url += $"?since={since.Value:yyyy-MM-ddTHH:mm:ssZ}";
            }
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var orders = JsonConvert.DeserializeObject<OrdersResponse>(content);
            
            return orders.Orders;
        }
        
        /// <summary>
        /// Get positions for a specific account
        /// </summary>
        public async Task<List<TSPosition>> GetPositions(string accountId)
        {
            await EnsureTokenValid();
            
            var response = await _httpClient.GetAsync($"brokerage/accounts/{accountId}/positions");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var positions = JsonConvert.DeserializeObject<PositionsResponse>(content);
            
            return positions.Positions;
        }
        
        /// <summary>
        /// Stream order updates in real-time
        /// </summary>
        public async Task StreamOrderUpdates(string accountId, Action<TSOrder> onOrderUpdate)
        {
            await EnsureTokenValid();
            
            // TradeStation uses Server-Sent Events (SSE) for streaming
            var streamUrl = $"brokerage/stream/accounts/{accountId}/orders";
            
            var request = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var stream = await response.Content.ReadAsStreamAsync();
            var reader = new System.IO.StreamReader(stream);
            
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                
                if (line?.StartsWith("data:") == true)
                {
                    var json = line.Substring(5).Trim();
                    var order = JsonConvert.DeserializeObject<TSOrder>(json);
                    onOrderUpdate?.Invoke(order);
                }
            }
        }
        
        /// <summary>
        /// Convert TradeStation order to universal TradeSignal
        /// </summary>
        public TradeSignal ConvertToSignal(TSOrder order, string sourceAccountId)
        {
            var signal = new TradeSignal
            {
                Id = Guid.NewGuid(),
                SourcePlatform = "TradeStation",
                SourceAccountId = sourceAccountId,
                SourceOrderId = order.OrderID,
                Timestamp = order.OpenedDateTime,
                Symbol = order.Symbol,
                Quantity = order.Quantity,
                TimeInForce = order.Duration,
                Status = SignalStatus.Received
            };
            
            // Map order action
            signal.Action = order.TradeAction switch
            {
                "BUY" => TradeAction.Buy,
                "SELL" => TradeAction.Sell,
                "SELLSHORT" => TradeAction.SellShort,
                "BUYTOCOVER" => TradeAction.BuyToCover,
                _ => TradeAction.Buy
            };
            
            // Map order type
            signal.OrderType = order.OrderType switch
            {
                "Market" => OrderType.Market,
                "Limit" => OrderType.Limit,
                "StopMarket" => OrderType.Stop,
                "StopLimit" => OrderType.StopLimit,
                _ => OrderType.Market
            };
            
            signal.LimitPrice = order.LimitPrice;
            signal.StopPrice = order.StopPrice;
            
            return signal;
        }
        
        private async Task EnsureTokenValid()
        {
            if (DateTime.UtcNow >= _tokenExpiration.AddMinutes(-5))
            {
                // Token expired or expiring soon - need to refresh
                // In production, implement refresh token flow
                throw new InvalidOperationException("Token expired. Please re-authenticate.");
            }
        }
        
        #region Response Models
        
        private class TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }
            
            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }
            
            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }
        }
        
        #endregion
    }
    
    #region TradeStation Models
    
    public class AccountInfo
    {
        public string AccountID { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
    }
    
    public class AccountsResponse
    {
        public List<AccountInfo> Accounts { get; set; }
    }
    
    public class TSOrder
    {
        public string OrderID { get; set; }
        public string Symbol { get; set; }
        public string TradeAction { get; set; }  // BUY, SELL, SELLSHORT, BUYTOCOVER
        public string OrderType { get; set; }     // Market, Limit, StopMarket, StopLimit
        public int Quantity { get; set; }
        public decimal? LimitPrice { get; set; }
        public decimal? StopPrice { get; set; }
        public string Duration { get; set; }      // DAY, GTC
        public string Status { get; set; }        // FLL (Filled), OPN (Open), etc
        public DateTime OpenedDateTime { get; set; }
        public decimal? FilledPrice { get; set; }
        public int? FilledQuantity { get; set; }
    }
    
    public class OrdersResponse
    {
        public List<TSOrder> Orders { get; set; }
    }
    
    public class TSPosition
    {
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public string PositionType { get; set; }  // Long, Short
        public decimal UnrealizedProfitLoss { get; set; }
    }
    
    public class PositionsResponse
    {
        public List<TSPosition> Positions { get; set; }
    }
    
    #endregion
}
