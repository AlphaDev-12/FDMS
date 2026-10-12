# Multi-stage Dockerfile for FDMS Bridge Admin
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["FdmsBridgeAdmin/FdmsBridgeAdmin.csproj", "FdmsBridgeAdmin/"]
RUN dotnet restore "FdmsBridgeAdmin/FdmsBridgeAdmin.csproj"

COPY . .
WORKDIR "/src/FdmsBridgeAdmin"
RUN dotnet build "FdmsBridgeAdmin.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "FdmsBridgeAdmin.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-chiseled AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:5070
EXPOSE 5070
USER 1000
ENTRYPOINT ["dotnet", "FdmsBridgeAdmin.dll"]
