# Use the official .NET 10 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the SDK image to build the application. Pinned to BUILDPLATFORM so the
# compiler runs natively under multi-arch builds; the target architecture is
# selected via dotnet --arch cross-compilation (no emulation during compile).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy SDK pinning and shared build props so they are honored inside the container
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/Sanet.Transport/Sanet.Transport.csproj", "Sanet.Transport/"]
COPY ["src/Sanet.Transport.Relay.Contracts/Sanet.Transport.Relay.Contracts.csproj", "Sanet.Transport.Relay.Contracts/"]
COPY ["src/Sanet.Transport.SignalR.Client/Sanet.Transport.SignalR.Client.csproj", "Sanet.Transport.SignalR.Client/"]
COPY ["src/Sanet.Transport.SignalR.Hub/Sanet.Transport.SignalR.Hub.csproj", "Sanet.Transport.SignalR.Hub/"]
RUN dotnet restore "Sanet.Transport.SignalR.Hub/Sanet.Transport.SignalR.Hub.csproj" -a $TARGETARCH

# Copy the referenced project sources (Transport, Contracts, Client) and the rest of the Hub source code
COPY ["src/Sanet.Transport/", "./Sanet.Transport"]
COPY ["src/Sanet.Transport.Relay.Contracts/", "./Sanet.Transport.Relay.Contracts"]
COPY ["src/Sanet.Transport.SignalR.Client/", "./Sanet.Transport.SignalR.Client"]
COPY ["src/Sanet.Transport.SignalR.Hub/", "./Sanet.Transport.SignalR.Hub"]

# Build the application
RUN dotnet build "Sanet.Transport.SignalR.Hub/Sanet.Transport.SignalR.Hub.csproj" -c Release -a $TARGETARCH --no-restore -o /app/build

# Publish the application (framework-dependent: the runtime image provides the runtime)
FROM build AS publish
ARG TARGETARCH
RUN dotnet publish "Sanet.Transport.SignalR.Hub/Sanet.Transport.SignalR.Hub.csproj" -c Release -a $TARGETARCH --no-restore --self-contained false -o /app/publish

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
