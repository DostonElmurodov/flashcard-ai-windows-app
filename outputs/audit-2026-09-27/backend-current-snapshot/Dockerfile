# syntax=docker/dockerfile:1

# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer while only source changes). Copy every csproj the Web project
# references so restore can resolve the src/ class libraries.
COPY Mavrylo.csproj ./
COPY src/Mavrylo.Entities/Mavrylo.Entities.csproj src/Mavrylo.Entities/
COPY src/Mavrylo.Data/Mavrylo.Data.csproj         src/Mavrylo.Data/
COPY src/Mavrylo.Services/Mavrylo.Services.csproj src/Mavrylo.Services/
RUN dotnet restore Mavrylo.csproj

# Copy the rest and publish framework-dependent (tests/ excluded via .dockerignore + csproj)
COPY . .
RUN dotnet publish Mavrylo.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Container listens on 8080; nginx on the host proxies 127.0.0.1:5289 -> container:8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
# Bounded defaults for upgrades from deployments predating account AI quotas.
# Explicit container environment settings continue to override these values.
ENV AccountAi__DailyQuota=200
ENV AccountAi__RequestsPerMinute=30
EXPOSE 8080

# Run as the non-root user provided by the aspnet base image
USER $APP_UID

ENTRYPOINT ["dotnet", "Mavrylo.dll"]
