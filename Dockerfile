FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebsiteBuilder.sln .
COPY src/WebsiteBuilder.Web/WebsiteBuilder.Web.csproj src/WebsiteBuilder.Web/
COPY src/WebsiteBuilder.Core/WebsiteBuilder.Core.csproj src/WebsiteBuilder.Core/
COPY src/WebsiteBuilder.Data/WebsiteBuilder.Data.csproj src/WebsiteBuilder.Data/
RUN dotnet restore src/WebsiteBuilder.Web/WebsiteBuilder.Web.csproj

COPY src/ src/
RUN dotnet publish src/WebsiteBuilder.Web/WebsiteBuilder.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Railway injects PORT at runtime; expand it when the container starts
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet WebsiteBuilder.Web.dll"]
