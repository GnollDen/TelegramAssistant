FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ src/
COPY TelegramAssistant.sln .
RUN dotnet restore
RUN dotnet publish src/TgAssistant.Host/TgAssistant.Host.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /data/media /app/data /app/logs
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TgAssistant.Host.dll"]
