# Cloud Copy Trading Platform

A comprehensive cloud-based platform that allows traders to mirror/copy trades from TradeStation to NinjaTrader and Tradovate in real-time.

## Architecture Overview

```
┌─────────────────┐
│  TradeStation   │
│    Account      │ ────┐
└─────────────────┘     │
                        │ (Monitor trades)
                        ▼
           ┌────────────────────────┐
           │   Cloud Platform       │
           │   (ASP.NET Core API)   │
           │                        │
           │  - Signal Processing   │
           │  - Route Distribution  │
           │  - User Management     │
           │  - Trade History       │
           └────────────────────────┘
                    │        │
      ┌─────────────┘        └─────────────┐
      │ (Execute trades)          (Execute trades)
      ▼                                     ▼
┌──────────────┐                   ┌───────────────┐
│  NinjaTrader │                   │   Tradovate   │
│   Account    │                   │    Account    │
└──────────────┘                   └───────────────┘
```

## Features

- **Real-time Trade Mirroring**: Automatically copy trades from TradeStation to multiple destination accounts
- **Multi-Platform Support**: Execute trades on NinjaTrader and Tradovate
- **Position Sizing**: Adjust trade sizes with multipliers (e.g., 0.5x, 1x, 2x)
- **Risk Management**: 
  - Maximum position size limits
  - Symbol whitelist/blacklist filters
  - Optional stop loss and take profit copying
- **Signal Reversal**: Optionally invert signals (buy becomes sell short)
- **Trade History**: Complete audit trail of all copied trades
- **Cloud-Based**: No need to keep local computers running

## Components

### 1. Core Models (`/Core`)
- `TradeSignal.cs`: Universal trade signal format
- `Models.cs`: User accounts, copy configurations, executed trades

### 2. Services (`/Services`)
- `TradeStationClient.cs`: Monitor TradeStation account for new trades
- `TradovateExecutor.cs`: Execute trades on Tradovate
- `CopyTradeReceiver.cs`: NinjaTrader strategy to receive signals
- `CopyTradingService.cs`: Main orchestration service

### 3. API (`/API`)
- `Controllers.cs`: REST API endpoints for management

## Setup Guide

### Prerequisites

1. **Cloud Server**: 
   - Windows Server or Linux with .NET 8.0
   - Azure, AWS, or Digital Ocean
   - Recommended: 2GB RAM, 1 CPU core

2. **API Credentials**:
   - TradeStation API key and secret
   - Tradovate CID and Secret Key (if using Tradovate)
   - NinjaTrader license (if using NinjaTrader)

### Installation Steps

#### Step 1: Deploy Cloud Platform

```bash
# Install .NET 8.0 SDK
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Clone and build
git clone <your-repo>
cd CopyTradePlatform
dotnet restore
dotnet build --configuration Release
```

#### Step 2: Configure Database

```bash
# Install PostgreSQL or SQL Server
# Create database named "CopyTradeDB"

# Update connection string in appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=CopyTradeDB;Username=postgres;Password=yourpassword"
  }
}

# Run migrations
dotnet ef database update
```

#### Step 3: Deploy API

```bash
# Publish application
dotnet publish -c Release -o ./publish

# Run API (Linux/systemd)
sudo nano /etc/systemd/system/copytrade.service

# Add service configuration:
[Unit]
Description=Copy Trade Platform API
After=network.target

[Service]
WorkingDirectory=/var/www/copytrade
ExecStart=/usr/bin/dotnet /var/www/copytrade/CopyTradePlatform.API.dll
Restart=always
RestartSec=10
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target

# Enable and start
sudo systemctl enable copytrade
sudo systemctl start copytrade
```

#### Step 4: Configure TradeStation Integration

1. **Register Application** at TradeStation Developer Portal
2. **Get API Credentials**: Client ID and Client Secret
3. **Configure Webhook** (optional for push notifications):
   - URL: `https://your-domain.com/api/copytrade/signal`
   - Events: Order Filled, Order Placed

#### Step 5: Setup NinjaTrader (if using)

1. **Copy Strategy to NinjaTrader**:
   ```
   C:\Users\<YourUser>\Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```

2. **Compile in NinjaTrader**:
   - Tools → Edit NinjaScript → Strategy → CopyTradeReceiver
   - Compile (F5)

3. **Add to Chart**:
   - Right-click chart → Strategies → CopyTradeReceiver
   - Set Listen Port (default: 8080)
   - Set API Key (generate from cloud platform)
   - Enable

4. **Configure Firewall**:
   ```bash
   # Allow inbound connection from cloud server
   netsh advfirewall firewall add rule name="CopyTrade" dir=in action=allow protocol=TCP localport=8080
   ```

#### Step 6: Setup Tradovate (if using)

1. **Get API Credentials**:
   - Log in to Tradovate
   - Account → API → Generate CID and Secret Key

2. **Test Connection**:
   ```bash
   curl -X POST https://demo.tradovateapi.com/v1/auth/accesstokenrequest \
     -H "Content-Type: application/json" \
     -d '{
       "name": "your_username",
       "password": "your_password",
       "appId": "CopyTradePlatform",
       "appVersion": "1.0",
       "cid": "your_cid",
       "sec": "your_secret",
       "deviceId": "unique_device_id"
     }'
   ```

### Configuration Examples

#### Basic Copy Configuration

```json
{
  "sourceAccountId": "TS123456",
  "destinationAccountId": "TDVT789",
  "isActive": true,
  "positionSizeMultiplier": 1.0,
  "copyStopLoss": true,
  "copyTakeProfit": true,
  "reverseSignals": false,
  "symbolWhitelist": ["ES", "NQ", "YM"],
  "maxPositionSize": 5
}
```

#### Advanced Configuration with Filters

```json
{
  "sourceAccountId": "TS123456",
  "destinationAccountId": "NT8080",
  "isActive": true,
  "positionSizeMultiplier": 0.5,
  "copyStopLoss": false,
  "copyTakeProfit": true,
  "reverseSignals": true,
  "symbolWhitelist": ["ES", "NQ"],
  "symbolBlacklist": ["CL"],
  "maxPositionSize": 3,
  "maxDailyLoss": 1000.00
}
```

## API Usage

### Register Trading Account

```bash
POST /api/account
Content-Type: application/json

{
  "platform": "TradeStation",
  "accountName": "My Trading Account",
  "accountId": "TS123456",
  "apiUsername": "username",
  "apiPassword": "password",
  "apiKey": "your_api_key",
  "secretKey": "your_secret",
  "accountType": "Source"
}
```

### Create Copy Configuration

```bash
POST /api/copyconfiguration
Content-Type: application/json

{
  "sourceAccountId": "guid-of-source-account",
  "destinationAccountId": "guid-of-dest-account",
  "isActive": true,
  "positionSizeMultiplier": 1.0,
  "copyStopLoss": true,
  "copyTakeProfit": true
}
```

### Receive Trade Signal (TradeStation Webhook)

```bash
POST /api/copytrade/signal
Content-Type: application/json

{
  "sourceAccountId": "TS123456",
  "symbol": "ESH4",
  "action": "Buy",
  "orderType": "Market",
  "quantity": 2,
  "stopLoss": 4850.00,
  "takeProfit": 4900.00
}
```

## Deployment Options

### Option 1: Azure App Service

```bash
# Create App Service
az webapp create --resource-group CopyTradeRG \
  --plan CopyTradePlan \
  --name copytrade-api \
  --runtime "DOTNET|8.0"

# Deploy
az webapp deployment source config-zip \
  --resource-group CopyTradeRG \
  --name copytrade-api \
  --src ./publish.zip
```

### Option 2: AWS Elastic Beanstalk

```bash
# Install EB CLI
pip install awsebcli

# Initialize
eb init -p "64bit Amazon Linux 2 v2.5.0 running .NET Core" \
  copytrade-api

# Deploy
eb create copytrade-env
eb deploy
```

### Option 3: Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CopyTradePlatform.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CopyTradePlatform.dll"]
```

## Security Considerations

1. **Encrypt API Credentials**: Use Azure Key Vault or AWS Secrets Manager
2. **HTTPS Only**: Force SSL/TLS for all API communications
3. **API Key Authentication**: Generate unique API keys per user
4. **Rate Limiting**: Implement rate limits on API endpoints
5. **IP Whitelist**: Restrict NinjaTrader connections to known IPs
6. **Audit Logging**: Log all trade executions and configuration changes

## Monitoring & Alerts

```csharp
// Add Application Insights or similar
services.AddApplicationInsightsTelemetry();

// Configure alerts
- Failed trade executions
- Account authentication failures
- High error rates
- Unusual trading volumes
```

## Troubleshooting

### TradeStation Connection Issues
- Verify API credentials
- Check token expiration
- Ensure API access is enabled in TradeStation account

### NinjaTrader Not Receiving Signals
- Verify firewall allows port 8080
- Check API key matches
- Ensure strategy is enabled on chart
- Check NinjaTrader output window for errors

### Tradovate Execution Failures
- Verify contract symbols match Tradovate format
- Check account has sufficient buying power
- Ensure API credentials haven't expired
- Review Tradovate API rate limits

## Performance Optimization

1. **Batch Processing**: Group multiple signals if possible
2. **Connection Pooling**: Reuse HTTP connections
3. **Caching**: Cache contract lookups and account info
4. **Async Operations**: Use async/await throughout
5. **Database Indexing**: Index frequently queried fields

## Roadmap

- [ ] Web dashboard for configuration
- [ ] Mobile app notifications
- [ ] Advanced analytics and reporting
- [ ] Paper trading mode
- [ ] Multi-account scaling
- [ ] Integration with Interactive Brokers
- [ ] Machine learning signal filtering

## Disclaimer

This software is for educational purposes. Trading involves risk. Always test with demo accounts first. The authors are not responsible for any financial losses.
