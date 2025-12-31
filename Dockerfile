
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
USER $APP_UID
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy csproj files first to leverage layer caching
#COPY src/ExpenseTracker.Domain/ExpenseTracker.Domain.csproj src/ExpenseTracker.Domain/
#COPY src/ExpenseTracker.Data/ExpenseTracker.Data.csproj src/ExpenseTracker.Data/
#COPY src/ExpenseTracker.Services/ExpenseTracker.Services.csproj src/ExpenseTracker.Services/
COPY src/ExpenseTracker.TelegramBot/ExpenseTracker.TelegramBot.csproj src/ExpenseTracker.TelegramBot/

RUN dotnet restore "src/ExpenseTracker.TelegramBot/ExpenseTracker.TelegramBot.csproj"
COPY src/. src/.
RUN dotnet build src/ExpenseTracker.TelegramBot/ExpenseTracker.TelegramBot.csproj -c Release -o /app/build 

FROM build AS publish
RUN dotnet publish src/ExpenseTracker.TelegramBot/ExpenseTracker.TelegramBot.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","ExpenseTracker.TelegramBot.dll"]
