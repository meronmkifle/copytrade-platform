using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using CopyTradePlatform.Core;

namespace CopyTradePlatform.Services
{
    /// <summary>
    /// Executes trades on Tradovate platform
    /// </summary>
    public class TradovateExecutor
    {
        private readonly HttpClient _httpClient;
        private string _accessToken;
        private DateTime _tokenExpiration;
        private readonly bool _useDemoAccount;
        
        public TradovateExecutor(bool useDemoAccount = true)
        {
            _httpClient = new HttpClient();
            _useDemoAccount = useDemoAccount;
            
            _httpClient.BaseAddress = new Uri(useDemoAccount 
                ? "https://demo.tradovateapi.com/v1/" 
                : "https://live.tradovateapi.com/v1/");
        }
        
        /// <summary>
        /// Authenticate with Tradovate
        /// </summary>
        public async Task<bool> Authenticate(string username, string password, string appId, 
            string appVersion, string cid, string secretKey)
        {
            try
            {
                var request = new
                {
                    name = username,
                    password = password,
                    appId = appId,
                    appVersion = appVersion,
                    cid = cid,
                    sec = secretKey,
                    deviceId = Guid.NewGuid().ToString()
                };
                
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("auth/accesstokenrequest", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    var authResult = JsonConvert.DeserializeObject<TradovateAuthResponse>(result);
                    
                    _accessToken = authResult.AccessToken;
                    _tokenExpiration = authResult.ExpirationTime;
                    
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Tradovate auth error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Renew access token before expiration
        /// </summary>
        public async Task<bool> RenewToken()
        {
            try
            {
                var response = await _httpClient.GetAsync("auth/renewaccesstoken");
                
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    var authResult = JsonConvert.DeserializeObject<TradovateAuthResponse>(result);
                    
                    _accessToken = authResult.AccessToken;
                    _tokenExpiration = authResult.ExpirationTime;
                    
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new AuthenticationHeaderValue("Bearer", _accessToken);
                    
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Token renewal error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get Tradovate accounts
        /// </summary>
        public async Task<TradovateAccountList> GetAccounts()
        {
            await EnsureTokenValid();
            
            var response = await _httpClient.GetAsync("account/list");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TradovateAccountList>(content);
        }
        
        /// <summary>
        /// Find contract by symbol name
        /// </summary>
        public async Task<TradovateContract> FindContract(string symbol)
        {
            await EnsureTokenValid();
            
            var response = await _httpClient.GetAsync($"contract/find?name={symbol}");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TradovateContract>(content);
        }
        
        /// <summary>
        /// Execute a trade signal on Tradovate
        /// </summary>
        public async Task<TradovateOrderResponse> ExecuteTrade(TradeSignal signal, 
            int accountId, string accountSpec)
        {
            await EnsureTokenValid();
            
            // Find the contract
            var contract = await FindContract(signal.Symbol);
            
            if (contract == null)
            {
                throw new Exception($"Contract not found for symbol: {signal.Symbol}");
            }
            
            // Build order request
            var orderRequest = new TradovateOrderRequest
            {
                AccountId = accountId,
                AccountSpec = accountSpec,
                Symbol = signal.Symbol,
                OrderQty = signal.Quantity,
                IsAutomated = true
            };
            
            // Map action
            orderRequest.Action = signal.Action switch
            {
                TradeAction.Buy => "Buy",
                TradeAction.Sell => "Sell",
                TradeAction.SellShort => "Sell",
                TradeAction.BuyToCover => "Buy",
                _ => "Buy"
            };
            
            // Map order type
            orderRequest.OrderType = signal.OrderType switch
            {
                OrderType.Market => "Market",
                OrderType.Limit => "Limit",
                OrderType.Stop => "Stop",
                OrderType.StopLimit => "StopLimit",
                _ => "Market"
            };
            
            // Set prices
            if (signal.LimitPrice.HasValue)
                orderRequest.Price = (double)signal.LimitPrice.Value;
            
            if (signal.StopPrice.HasValue)
                orderRequest.StopPrice = (double)signal.StopPrice.Value;
            
            // Time in force
            orderRequest.TimeInForce = signal.TimeInForce ?? "Day";
            
            // Place order
            var json = JsonConvert.SerializeObject(orderRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("order/placeorder", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Order placement failed: {error}");
            }
            
            var result = await response.Content.ReadAsStringAsync();
            var orderResponse = JsonConvert.DeserializeObject<TradovateOrderResponse>(result);
            
            // Place stop loss if specified
            if (signal.StopLoss.HasValue && orderResponse.OrderId > 0)
            {
                await PlaceStopLoss(accountId, accountSpec, signal.Symbol, 
                    signal.Quantity, signal.StopLoss.Value, signal.Action);
            }
            
            // Place take profit if specified
            if (signal.TakeProfit.HasValue && orderResponse.OrderId > 0)
            {
                await PlaceTakeProfit(accountId, accountSpec, signal.Symbol, 
                    signal.Quantity, signal.TakeProfit.Value, signal.Action);
            }
            
            return orderResponse;
        }
        
        /// <summary>
        /// Place a stop loss order
        /// </summary>
        private async Task PlaceStopLoss(int accountId, string accountSpec, string symbol, 
            int quantity, decimal stopPrice, TradeAction originalAction)
        {
            var orderRequest = new TradovateOrderRequest
            {
                AccountId = accountId,
                AccountSpec = accountSpec,
                Symbol = symbol,
                OrderQty = quantity,
                OrderType = "Stop",
                StopPrice = (double)stopPrice,
                TimeInForce = "GTC",
                IsAutomated = true
            };
            
            // Opposite action to close position
            orderRequest.Action = originalAction == TradeAction.Buy ? "Sell" : "Buy";
            
            var json = JsonConvert.SerializeObject(orderRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            await _httpClient.PostAsync("order/placeorder", content);
        }
        
        /// <summary>
        /// Place a take profit order
        /// </summary>
        private async Task PlaceTakeProfit(int accountId, string accountSpec, string symbol, 
            int quantity, decimal limitPrice, TradeAction originalAction)
        {
            var orderRequest = new TradovateOrderRequest
            {
                AccountId = accountId,
                AccountSpec = accountSpec,
                Symbol = symbol,
                OrderQty = quantity,
                OrderType = "Limit",
                Price = (double)limitPrice,
                TimeInForce = "GTC",
                IsAutomated = true
            };
            
            // Opposite action to close position
            orderRequest.Action = originalAction == TradeAction.Buy ? "Sell" : "Buy";
            
            var json = JsonConvert.SerializeObject(orderRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            await _httpClient.PostAsync("order/placeorder", content);
        }
        
        /// <summary>
        /// Get positions
        /// </summary>
        public async Task<TradovatePositionList> GetPositions()
        {
            await EnsureTokenValid();
            
            var response = await _httpClient.GetAsync("position/list");
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TradovatePositionList>(content);
        }
        
        /// <summary>
        /// Close a position
        /// </summary>
        public async Task<TradovateOrderResponse> ClosePosition(int accountId, string accountSpec, 
            string symbol, int quantity, bool isLong)
        {
            await EnsureTokenValid();
            
            var orderRequest = new TradovateOrderRequest
            {
                AccountId = accountId,
                AccountSpec = accountSpec,
                Symbol = symbol,
                OrderQty = Math.Abs(quantity),
                OrderType = "Market",
                Action = isLong ? "Sell" : "Buy",
                TimeInForce = "Day",
                IsAutomated = true
            };
            
            var json = JsonConvert.SerializeObject(orderRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("order/placeorder", content);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TradovateOrderResponse>(result);
        }
        
        private async Task EnsureTokenValid()
        {
            if (DateTime.UtcNow >= _tokenExpiration.AddMinutes(-5))
            {
                await RenewToken();
            }
        }
    }
    
    #region Tradovate Models
    
    public class TradovateAuthResponse
    {
        [JsonProperty("accessToken")]
        public string AccessToken { get; set; }
        
        [JsonProperty("expirationTime")]
        public DateTime ExpirationTime { get; set; }
        
        [JsonProperty("userId")]
        public int UserId { get; set; }
    }
    
    public class TradovateAccountList
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int UserId { get; set; }
        public string AccountType { get; set; }
        public bool Active { get; set; }
    }
    
    public class TradovateContract
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ContractMaturityId { get; set; }
        public string Status { get; set; }
    }
    
    public class TradovateOrderRequest
    {
        [JsonProperty("accountSpec")]
        public string AccountSpec { get; set; }
        
        [JsonProperty("accountId")]
        public int AccountId { get; set; }
        
        [JsonProperty("action")]
        public string Action { get; set; }
        
        [JsonProperty("symbol")]
        public string Symbol { get; set; }
        
        [JsonProperty("orderQty")]
        public int OrderQty { get; set; }
        
        [JsonProperty("orderType")]
        public string OrderType { get; set; }
        
        [JsonProperty("price")]
        public double? Price { get; set; }
        
        [JsonProperty("stopPrice")]
        public double? StopPrice { get; set; }
        
        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; }
        
        [JsonProperty("isAutomated")]
        public bool IsAutomated { get; set; }
    }
    
    public class TradovateOrderResponse
    {
        [JsonProperty("orderId")]
        public int OrderId { get; set; }
        
        [JsonProperty("orderStatus")]
        public string OrderStatus { get; set; }
        
        [JsonProperty("message")]
        public string Message { get; set; }
    }
    
    public class TradovatePositionList
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public int ContractId { get; set; }
        public DateTime Timestamp { get; set; }
        public int NetPos { get; set; }
        public int NetPrice { get; set; }
    }
    
    #endregion
}
