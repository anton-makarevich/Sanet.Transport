# Use the official .NET 10 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj file and restore dependencies
COPY ["Sanet.Transport.SignalR.Hub.csproj", "."]
RUN dotnet restore "Sanet.Transport.SignalR.Hub.csproj"

# Copy the rest of the source code
COPY . .

# Build the application
RUN dotnet build "Sanet.Transport.SignalR.Hub.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "Sanet.Transport.SignalR.Hub.csproj" -c Release -o /app/publish

# Build the final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Install curl for HEALTHCHECK and create non-root user
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl adduser \
    && rm -rf /var/lib/apt/lists/* \
    && addgroup --system --gid 1001 appgroup \
    && adduser --system --uid 1001 --ingroup appgroup appuser \
    && chown -R appuser:appgroup /app

# Set environment variables for the application
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

USER appuser
ENTRYPOINT ["dotnet", "Sanet.Transport.SignalR.Hub.dll"]
