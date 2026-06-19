# ============================================================================
# InCleanHome.IamService - Dockerfile (multi-stage)
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

COPY src/InCleanHome.IamService/InCleanHome.IamService.csproj src/InCleanHome.IamService/
RUN dotnet restore "src/InCleanHome.IamService/InCleanHome.IamService.csproj"

COPY . .
RUN dotnet publish "src/InCleanHome.IamService/InCleanHome.IamService.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -u 10001 appuser
USER appuser

COPY --from=build --chown=appuser:appuser /app/publish .

EXPOSE 5001
ENV ASPNETCORE_URLS=http://+:5001

ENTRYPOINT ["dotnet", "InCleanHome.IamService.dll"]
