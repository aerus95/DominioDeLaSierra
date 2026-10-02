FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["src/DominioDeLaSierra.Api/DominioDeLaSierra.Api.csproj", "src/DominioDeLaSierra.Api/"]
COPY ["src/DominioDeLaSierra.Application/DominioDeLaSierra.Application.csproj", "src/DominioDeLaSierra.Application/"]
COPY ["src/DominioDeLaSierra.Domain/DominioDeLaSierra.Domain.csproj", "src/DominioDeLaSierra.Domain/"]
COPY ["src/DominioDeLaSierra.Infrastructure/DominioDeLaSierra.Infrastructure.csproj", "src/DominioDeLaSierra.Infrastructure/"]

RUN dotnet restore "src/DominioDeLaSierra.Api/DominioDeLaSierra.Api.csproj"

COPY . .

RUN dotnet publish \
    "src/DominioDeLaSierra.Api/DominioDeLaSierra.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "DominioDeLaSierra.Api.dll"]
