FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Apply published Ubuntu security fixes even when the upstream .NET tag has not
# yet been rebuilt. Keep the upgrade narrow and remove package metadata.
RUN apt-get update \
    && apt-get install --only-upgrade --no-install-recommends -y \
        libssl3t64 \
        openssl \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar os projetos preservando a estrutura usada pelos ProjectReference.
COPY ["src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.API.csproj", "src/backend/ControleAcessoVeiculos.API/"]
COPY ["src/backend/ControleAcessoVeiculos.Application/ControleAcessoVeiculos.Application.csproj", "src/backend/ControleAcessoVeiculos.Application/"]
COPY ["src/backend/ControleAcessoVeiculos.Domain/ControleAcessoVeiculos.Domain.csproj", "src/backend/ControleAcessoVeiculos.Domain/"]
COPY ["src/backend/ControleAcessoVeiculos.Infrastructure/ControleAcessoVeiculos.Infrastructure.csproj", "src/backend/ControleAcessoVeiculos.Infrastructure/"]

# Restore
RUN dotnet restore "src/backend/ControleAcessoVeiculos.API/ControleAcessoVeiculos.API.csproj"

# Copiar o código-fonte para a mesma árvore dos projetos restaurados.
COPY . .

# Build
WORKDIR "/src/src/backend/ControleAcessoVeiculos.API"
RUN dotnet build "ControleAcessoVeiculos.API.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "ControleAcessoVeiculos.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /var/lib/controle-acesso-veiculos/data-protection \
    && chown -R "$APP_UID:$APP_UID" /var/lib/controle-acesso-veiculos
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "ControleAcessoVeiculos.API.dll"]
