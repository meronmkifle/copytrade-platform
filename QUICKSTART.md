# Quick Start Guide

Get your copy trading platform up and running in 10 minutes!

## Prerequisites

- Docker and Docker Compose installed
- TradeStation developer account (for API access)
- Tradovate account (optional, if using Tradovate)
- NinjaTrader 8 installed (optional, if using NinjaTrader)

## 5-Minute Setup

### 1. Clone and Configure

```bash
git clone <your-repo>
cd CopyTradePlatform

# Copy environment template
cp .env.template .env

# Edit .env file with your credentials
nano .env
```

Required values in `.env`:
```
DB_PASSWORD=secure_password_123
TS_CLIENT_ID=your_client_id
TS_CLIENT_SECRET=your_client_secret
JWT_SECRET=your-32-char-secret-key-here
```

### 2. Launch Platform

```bash
# Start all services
docker-compose up -d

# Check status
docker-compose ps

# View logs
docker-compose logs -f api
```

Platform will be available at: `http://localhost:5000`

### 3. Initialize Database

The database will be automatically initialized on first run. To verify:

```bash
docker-compose exec postgres psql -U copytrade -d CopyTradeDB -c "\dt"
```

## First Time Setup

### Create Admin User

```bash
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@example.com",
    "username": "admin",
    "password": "Admin123!"
  }'
```

### Add TradeStation Account

```bash
# First, login to get JWT token
TOKEN=$(curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "username": "admin",
    "password": "Admin123!"
  }' | jq -r '.token')

# Register TradeStation account
curl -X POST http://localhost:5000/api/account \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "platform": "TradeStation",
    "accountName": "My TS Account",
    "accountId": "YOUR_TS_ACCOUNT_ID",
    "apiUsername": "your_ts_username",
    "apiPassword": "your_ts_password",
    "apiKey": "your_client_id",
    "secretKey": "your_client_secret",
    "accountType": "Source"
  }'
```

### Add Tradovate Account

```bash
curl -X POST http://localhost:5000/api/account \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "platform": "Tradovate",
    "accountName": "My Tradovate",
    "accountId": "YOUR_TRADOVATE_ACCOUNT_ID",
    "apiUsername": "your_tradovate_username",
    "apiPassword": "your_tradovate_password",
    "apiKey": "your_cid",
    "secretKey": "your_secret_key",
    "accountType": "Destination"
  }'
```

## Setting Up NinjaTrader

### 1. Copy Strategy File

```bash
# Windows
copy Services/CopyTradeReceiver.cs "C:\Users\YOUR_USER\Documents\NinjaTrader 8\bin\Custom\Strategies\"

# Or download directly in NinjaTrader folder
```

### 2. Compile Strategy

1. Open NinjaTrader 8
2. Go to **Tools → Edit NinjaScript → Strategy**
3. Open `CopyTradeReceiver`
4. Press **F5** to compile
5. Close the editor

### 3. Add to Chart

1. Open any chart (e.g., ES 5-minute)
2. Right-click chart → **Strategies**
3. Select **CopyTradeReceiver**
4. Configure:
   - **Listen Port**: 8080 (or custom port)
   - **API Key**: Generate from platform dashboard
   - **Max Position Size**: 5
   - **Enable Logging**: ✓
5. Click **OK**

### 4. Test Connection

```bash
# Send test signal
curl -X POST http://localhost:8080/ \
  -H "Authorization: Bearer YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "Id": "test-123",
    "Symbol": "ES 03-25",
    "Action": "Buy",
    "OrderType": "Market",
    "Quantity": 1,
    "StopLossTicks": 10,
    "ProfitTargetTicks": 20
  }'
```

Check NinjaTrader Output window for confirmation.

## Create Copy Configuration

```bash
# Get account IDs from previous steps
SOURCE_ACCOUNT_ID="..."
DEST_ACCOUNT_ID="..."

curl -X POST http://localhost:5000/api/copyconfiguration \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$SOURCE_ACCOUNT_ID'",
    "destinationAccountId": "'$DEST_ACCOUNT_ID'",
    "isActive": true,
    "positionSizeMultiplier": 1.0,
    "copyStopLoss": true,
    "copyTakeProfit": true,
    "reverseSignals": false,
    "symbolWhitelist": ["ES", "NQ"],
    "maxPositionSize": 5
  }'
```

## Monitoring

### View Recent Signals

```bash
curl http://localhost:5000/api/copytrade/signals/recent \
  -H "Authorization: Bearer $TOKEN"
```

### Check Execution Status

```bash
curl http://localhost:5000/api/copytrade/executions?accountId=$DEST_ACCOUNT_ID \
  -H "Authorization: Bearer $TOKEN"
```

### Access Grafana Dashboard (Optional)

1. Open http://localhost:3000
2. Login: admin / admin (or password from .env)
3. Import dashboard: `monitoring/grafana/dashboards/copytrade.json`

## Testing

### Paper Trading Mode

```bash
# Enable paper trading in .env
ENABLE_PAPER_TRADING=true

# Restart services
docker-compose restart api
```

In paper trading mode:
- All trades are simulated
- No real orders placed
- Useful for testing configurations

### Send Test Signal

```bash
curl -X POST http://localhost:5000/api/copytrade/signal \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "TEST_ACCOUNT",
    "symbol": "ESH5",
    "action": "Buy",
    "orderType": "Market",
    "quantity": 1,
    "stopLoss": 4850.00,
    "takeProfit": 4900.00
  }'
```

## Common Issues

### API Not Starting

```bash
# Check logs
docker-compose logs api

# Common fixes:
# 1. Database not ready
docker-compose restart api

# 2. Port already in use
# Change port in docker-compose.yml
ports:
  - "5001:80"  # Changed from 5000
```

### NinjaTrader Not Receiving Signals

1. Check firewall allows port 8080
2. Verify API key matches
3. Check NinjaTrader Output window for errors
4. Ensure strategy is enabled (green circle on chart)

### TradeStation Connection Failed

1. Verify credentials in .env
2. Check API access enabled in TradeStation account
3. View logs: `docker-compose logs api | grep TradeStation`

### Database Connection Issues

```bash
# Check database is running
docker-compose ps postgres

# Connect to database manually
docker-compose exec postgres psql -U copytrade -d CopyTradeDB
```

## Going Live

### 1. Switch to Production APIs

Edit `.env`:
```
TS_USE_SANDBOX=false
TRADOVATE_DEMO=false
ASPNETCORE_ENVIRONMENT=Production
```

### 2. Setup SSL/HTTPS

```bash
# Generate SSL certificate
certbot certonly --standalone -d yourdomain.com

# Update nginx config
# Copy certificates to ./nginx/ssl/
```

### 3. Secure Credentials

```bash
# Use environment variables or secrets manager
# Never commit .env to git!

echo ".env" >> .gitignore
```

### 4. Enable Monitoring

```
SENTRY_DSN=your_sentry_dsn
APPLICATION_INSIGHTS_KEY=your_app_insights_key
```

### 5. Setup Backups

```bash
# Add to crontab
0 2 * * * docker-compose exec -T postgres pg_dump -U copytrade CopyTradeDB | gzip > backup-$(date +\%Y\%m\%d).sql.gz
```

## Updating

```bash
# Pull latest changes
git pull

# Rebuild containers
docker-compose down
docker-compose build --no-cache
docker-compose up -d

# Check everything is working
docker-compose ps
```

## Support

- Check logs: `docker-compose logs -f`
- Restart services: `docker-compose restart`
- Reset everything: `docker-compose down -v` (⚠️ deletes data)

## Next Steps

1. ✅ Platform running
2. ✅ Accounts connected
3. ✅ Copy config created
4. ⬜ Test with paper trading
5. ⬜ Monitor first real trades
6. ⬜ Setup email alerts
7. ⬜ Configure backup strategy

Happy trading! 🚀
