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

FROM build AS migrator-build
RUN dotnet tool install --global dotnet-ef --version 8.0.10
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle -p src/Lab.Api/Lab.Api.csproj \
    -o /app/efbundle --self-contained -r linux-x64

FROM mcr.microsoft.com/dotnet/runtime-deps:8.0 AS migrator
WORKDIR /app
COPY --from=migrator-build /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]
