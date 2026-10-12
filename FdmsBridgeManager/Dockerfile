# Multi-stage Dockerfile for FDMS Bridge Manager
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["FdmsBridgeManager/FdmsBridgeManager.csproj", "FdmsBridgeManager/"]
RUN dotnet restore "FdmsBridgeManager/FdmsBridgeManager.csproj"

COPY . .
WORKDIR "/src/FdmsBridgeManager"
RUN dotnet build "FdmsBridgeManager.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FdmsBridgeManager.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-chiseled AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5050
EXPOSE 5050
USER 1000
ENTRYPOINT ["dotnet", "FdmsBridgeManager.dll"]
