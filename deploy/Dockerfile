# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY TelegramAssistant.sln .
COPY src/TgAssistant.Core/TgAssistant.Core.csproj src/TgAssistant.Core/
COPY src/TgAssistant.Host/TgAssistant.Host.csproj src/TgAssistant.Host/
COPY src/TgAssistant.Infrastructure/TgAssistant.Infrastructure.csproj src/TgAssistant.Infrastructure/
COPY src/TgAssistant.Intelligence/TgAssistant.Intelligence.csproj src/TgAssistant.Intelligence/
COPY src/TgAssistant.Processing/TgAssistant.Processing.csproj src/TgAssistant.Processing/
COPY src/TgAssistant.Telegram/TgAssistant.Telegram.csproj src/TgAssistant.Telegram/
COPY src/TgAssistant.Web/TgAssistant.Web.csproj src/TgAssistant.Web/

RUN --mount=type=cache,id=tgassistant-nuget,target=/root/.nuget/packages \
    dotnet restore TelegramAssistant.sln

COPY src/ src/
RUN --mount=type=cache,id=tgassistant-nuget,target=/root/.nuget/packages \
    dotnet publish src/TgAssistant.Host/TgAssistant.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /data/media /app/data /app/logs
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TgAssistant.Host.dll"]
