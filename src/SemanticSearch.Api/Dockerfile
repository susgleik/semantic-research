FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/SemanticSearch.Api/SemanticSearch.Api.csproj",   "SemanticSearch.Api/"]
COPY ["src/SemanticSearch.Core/SemanticSearch.Core.csproj", "SemanticSearch.Core/"]
RUN dotnet restore "SemanticSearch.Api/SemanticSearch.Api.csproj"

COPY src/ .
WORKDIR "/src/SemanticSearch.Api"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SemanticSearch.Api.dll"]
