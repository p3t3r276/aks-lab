FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Lab.Api/Lab.Api.csproj src/Lab.Api/
RUN dotnet restore src/Lab.Api/Lab.Api.csproj
COPY . .
RUN dotnet publish src/Lab.Api/Lab.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lab.Api.dll"]
