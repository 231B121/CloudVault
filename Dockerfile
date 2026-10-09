# Multi-stage Dockerfile for CloudVault API & Web UI
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY src/CloudVault.Domain/CloudVault.Domain.csproj src/CloudVault.Domain/
COPY src/CloudVault.Application/CloudVault.Application.csproj src/CloudVault.Application/
COPY src/CloudVault.Infrastructure/CloudVault.Infrastructure.csproj src/CloudVault.Infrastructure/
COPY src/CloudVault.API/CloudVault.API.csproj src/CloudVault.API/

RUN dotnet restore src/CloudVault.API/CloudVault.API.csproj

# Copy all source files and publish
COPY src/CloudVault.Domain/ src/CloudVault.Domain/
COPY src/CloudVault.Application/ src/CloudVault.Application/
COPY src/CloudVault.Infrastructure/ src/CloudVault.Infrastructure/
COPY src/CloudVault.API/ src/CloudVault.API/

WORKDIR /app/src/CloudVault.API
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080 10000
ENV ASPNETCORE_ENVIRONMENT=Production

# Create app_data folder for local file caching
RUN mkdir -p /app/app_data/storage

COPY --from=build /app/publish .

# Run as non-root user for security
RUN chown -R 1000:1000 /app
USER 1000

ENTRYPOINT ["dotnet", "CloudVault.API.dll"]
