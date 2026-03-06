# ============================================================
# Multi-stage build for TelegramAssistant
# ============================================================

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY TelegramAssistant.sln .
COPY src/TgAssistant.Core/TgAssistant.Core.csproj src/TgAssistant.Core/
COPY src/TgAssistant.Infrastructure/TgAssistant.Infrastructure.csproj src/TgAssistant.Infrastructure/
COPY src/TgAssistant.Telegram/TgAssistant.Telegram.csproj src/TgAssistant.Telegram/
COPY src/TgAssistant.Processing/TgAssistant.Processing.csproj src/TgAssistant.Processing/
COPY src/TgAssistant.Intelligence/TgAssistant.Intelligence.csproj src/TgAssistant.Intelligence/
COPY src/TgAssistant.Web/TgAssistant.Web.csproj src/TgAssistant.Web/
COPY src/TgAssistant.Host/TgAssistant.Host.csproj src/TgAssistant.Host/

RUN dotnet restore

# Copy everything and build
COPY src/ src/
RUN dotnet publish src/TgAssistant.Host/TgAssistant.Host.csproj \
    -c Release -o /app/publish --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install ffmpeg for audio conversion
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Create directories
RUN mkdir -p /data/media /app/data /app/logs

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TgAssistant.Host.dll"]
