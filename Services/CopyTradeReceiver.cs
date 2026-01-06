using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using Newtonsoft.Json;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// NinjaTrader strategy that receives trade signals from cloud copy trading platform
    /// and executes them locally
    /// </summary>
    public class CopyTradeReceiver : Strategy
    {
        private HttpListener _listener;
        private System.Threading.Thread _listenerThread;
        private bool _isRunning;
        private Dictionary<string, Order> _activeOrders;
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Receives trade signals from cloud copy trading platform";
                Name = "CopyTradeReceiver";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                // User parameters
                ListenPort = 8080;
                ApiKey = "";
                MaxPositionSize = 10;
                EnableLogging = true;
            }
            else if (State == State.Configure)
            {
                _activeOrders = new Dictionary<string, Order>();
            }
            else if (State == State.DataLoaded)
            {
                // Start HTTP listener in a separate thread
                StartHttpListener();
            }
            else if (State == State.Terminated)
            {
                StopHttpListener();
            }
        }
        
        protected override void OnBarUpdate()
        {
            // This strategy is event-driven via HTTP, but OnBarUpdate is required
        }
        
        /// <summary>
        /// Start HTTP listener to receive trade signals
        /// </summary>
        private void StartHttpListener()
        {
            try
            {
                _isRunning = true;
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{ListenPort}/");
                _listener.Start();
                
                if (EnableLogging)
                    Print($"HTTP Listener started on port {ListenPort}");
                
                _listenerThread = new System.Threading.Thread(ListenForRequests);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();
            }
            catch (Exception ex)
            {
                Print($"Error starting HTTP listener: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Stop HTTP listener
        /// </summary>
        private void StopHttpListener()
        {
            _isRunning = false;
            
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            
            if (EnableLogging)
                Print("HTTP Listener stopped");
        }
        
        /// <summary>
        /// Listen for incoming HTTP requests
        /// </summary>
        private void ListenForRequests()
        {
            while (_isRunning && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                        Print($"Listener error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Handle incoming HTTP request
        /// </summary>
        private async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            
            try
            {
                // Verify API key
                var authHeader = request.Headers["Authorization"];
                if (string.IsNullOrEmpty(authHeader) || !authHeader.Equals($"Bearer {ApiKey}"))
                {
                    response.StatusCode = 401;
                    var errorBytes = Encoding.UTF8.GetBytes("Unauthorized");
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    response.Close();
                    return;
                }
                
                // Read request body
                string body;
                using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
                {
                    body = await reader.ReadToEndAsync();
                }
                
                if (EnableLogging)
                    Print($"Received signal: {body}");
                
                // Parse trade signal
                var signal = JsonConvert.DeserializeObject<TradeSignalDTO>(body);
                
                // Execute trade
                var result = ExecuteTradeSignal(signal);
                
                // Send response
                response.StatusCode = result.Success ? 200 : 400;
                var responseJson = JsonConvert.SerializeObject(result);
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            }
            catch (Exception ex)
            {
                Print($"Request handling error: {ex.Message}");
                response.StatusCode = 500;
                var errorBytes = Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            }
            finally
            {
                response.Close();
            }
        }
        
        /// <summary>
        /// Execute a trade signal
        /// </summary>
        private ExecutionResult ExecuteTradeSignal(TradeSignalDTO signal)
        {
            var result = new ExecutionResult { SignalId = signal.Id };
            
            try
            {
                // Validate quantity
                if (signal.Quantity > MaxPositionSize)
                {
                    result.Success = false;
                    result.Message = $"Quantity {signal.Quantity} exceeds max position size {MaxPositionSize}";
                    return result;
                }
                
                // Set stop loss and profit target if provided
                if (signal.StopLossTicks > 0)
                    SetStopLoss(CalculationMode.Ticks, signal.StopLossTicks);
                
                if (signal.ProfitTargetTicks > 0)
                    SetProfitTarget(CalculationMode.Ticks, signal.ProfitTargetTicks);
                
                Order order = null;
                string signalName = $"Copy_{signal.Id}";
                
                // Execute based on action
                switch (signal.Action)
                {
                    case "Buy":
                        if (signal.OrderType == "Market")
                            order = EnterLong(signal.Quantity, signalName);
                        else if (signal.OrderType == "Limit" && signal.LimitPrice.HasValue)
                            order = EnterLongLimit(signal.Quantity, signal.LimitPrice.Value, signalName);
                        else if (signal.OrderType == "Stop" && signal.StopPrice.HasValue)
                            order = EnterLongStopMarket(signal.Quantity, signal.StopPrice.Value, signalName);
                        break;
                    
                    case "Sell":
                        if (Position.MarketPosition == MarketPosition.Long)
                        {
                            // Closing long position
                            order = ExitLong(signal.Quantity, signalName, "");
                        }
                        break;
                    
                    case "SellShort":
                        if (signal.OrderType == "Market")
                            order = EnterShort(signal.Quantity, signalName);
                        else if (signal.OrderType == "Limit" && signal.LimitPrice.HasValue)
                            order = EnterShortLimit(signal.Quantity, signal.LimitPrice.Value, signalName);
                        else if (signal.OrderType == "Stop" && signal.StopPrice.HasValue)
                            order = EnterShortStopMarket(signal.Quantity, signal.StopPrice.Value, signalName);
                        break;
                    
                    case "BuyToCover":
                        if (Position.MarketPosition == MarketPosition.Short)
                        {
                            // Closing short position
                            order = ExitShort(signal.Quantity, signalName, "");
                        }
                        break;
                }
                
                if (order != null)
                {
                    _activeOrders[signal.Id] = order;
                    result.Success = true;
                    result.Message = $"Order placed: {order.Name}";
                    result.OrderId = order.OrderId;
                    
                    if (EnableLogging)
                        Print($"Executed {signal.Action} {signal.Quantity} {signal.Symbol}");
                }
                else
                {
                    result.Success = false;
                    result.Message = "Failed to place order";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Execution error: {ex.Message}";
                Print($"Error executing signal: {ex.Message}");
            }
            
            return result;
        }
        
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, 
            int quantity, int filled, double averageFillPrice, OrderState orderState, 
            DateTime time, ErrorCode error, string nativeError)
        {
            if (EnableLogging)
            {
                Print($"Order Update: {order.Name} - {orderState} - Filled: {filled}/{quantity} @ {averageFillPrice}");
            }
        }
        
        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Listen Port", Description = "HTTP port to listen on", 
            Order = 1, GroupName = "Connection")]
        public int ListenPort { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "API Key", Description = "API key for authentication", 
            Order = 2, GroupName = "Connection")]
        public string ApiKey { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Max Position Size", Description = "Maximum contracts per position", 
            Order = 3, GroupName = "Risk Management")]
        public int MaxPositionSize { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Description = "Print debug messages", 
            Order = 4, GroupName = "Settings")]
        public bool EnableLogging { get; set; }
        
        #endregion
    }
    
    #region DTOs
    
    public class TradeSignalDTO
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public string Action { get; set; }
        public string OrderType { get; set; }
        public int Quantity { get; set; }
        public double? LimitPrice { get; set; }
        public double? StopPrice { get; set; }
        public int StopLossTicks { get; set; }
        public int ProfitTargetTicks { get; set; }
    }
    
    public class ExecutionResult
    {
        public bool Success { get; set; }
        public string SignalId { get; set; }
        public string OrderId { get; set; }
        public string Message { get; set; }
    }
    
    #endregion
}
