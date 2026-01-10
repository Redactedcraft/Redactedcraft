# EosConfigService

Minimal ASP.NET Core service that returns EOS config JSON for the game.

## Endpoints
- `GET /health` -> `{ "status": "ok" }`
- `GET /eos/config` -> EOS config JSON used by the game

If `EOS_CONFIG_API_KEY` is set on the server, requests must include:
`X-Api-Key: <your key>`

## Required Environment Variables
- `EOS_PRODUCT_ID`
- `EOS_SANDBOX_ID`
- `EOS_DEPLOYMENT_ID`
- `EOS_CLIENT_ID`
- `EOS_CLIENT_SECRET`

Optional:
- `EOS_PRODUCT_NAME` (default: RedactedCraft)
- `EOS_PRODUCT_VERSION` (default: 1.0)
- `EOS_LOGIN_MODE` (default: device)
- `EOS_CONFIG_API_KEY` (enforces API key header)

## Local Run
```
dotnet run
```

## Render Setup
1) Create a new Web Service from this repo.
2) Root Directory: `EosConfigService`
3) Build Command:
```
dotnet publish -c Release -o out
```
4) Start Command:
```
dotnet out/EosConfigService.dll
```
5) Add the env vars listed above in Render.

## Game Client Configuration
Set these on the client:
- `EOS_CONFIG_URL` = `https://<your-render-service>/eos/config`
- `EOS_CONFIG_API_KEY` = same key (only if you set one on the server)
