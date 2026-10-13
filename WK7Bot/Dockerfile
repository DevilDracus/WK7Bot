FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WK7Bot.csproj .
RUN dotnet restore "WK7Bot.csproj"

# Copy all repository files from the wk7bot directory into the container
COPY . .
RUN dotnet publish "WK7Bot.csproj" -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Health check: the /health endpoint is defined in Program.cs and returns "WK7 Bot is healthy."
# A failure here (no response within 5s, or non-2xx status) signals Home Assistant that the
# add-on should be restarted. Interval: every 30s; grace period: 5s for startup; 3 failures
# before marking unhealthy.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fs http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "WK7Bot.dll"]
