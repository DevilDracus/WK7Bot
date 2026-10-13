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

ENTRYPOINT ["dotnet", "WK7Bot.dll"]
