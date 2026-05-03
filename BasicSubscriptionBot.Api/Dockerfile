FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj BasicSubscriptionBot.Api/
RUN dotnet restore BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj

COPY BasicSubscriptionBot.Api/ BasicSubscriptionBot.Api/
RUN dotnet publish BasicSubscriptionBot.Api/BasicSubscriptionBot.Api.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__Sqlite="Data Source=/data/basicsubscriptionbot.db;Cache=Shared"

EXPOSE 5000
ENTRYPOINT ["dotnet", "BasicSubscriptionBot.Api.dll"]
