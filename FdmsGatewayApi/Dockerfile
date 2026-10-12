# Multi-stage Dockerfile for FDMS Gateway API
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["FdmsGatewayApi/FdmsGatewayApi.csproj", "FdmsGatewayApi/"]
RUN dotnet restore "FdmsGatewayApi/FdmsGatewayApi.csproj"

COPY . .
WORKDIR "/src/FdmsGatewayApi"
RUN dotnet build "FdmsGatewayApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FdmsGatewayApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-chiseled AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER 1000
ENV ASPNETCORE_URLS=http://+:5045
EXPOSE 5045
ENTRYPOINT ["dotnet", "FdmsGatewayApi.dll"]
