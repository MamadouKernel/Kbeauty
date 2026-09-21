FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KekeBeauty.slnx .
COPY src/KekeBeauty.Domain/KekeBeauty.Domain.csproj src/KekeBeauty.Domain/
COPY src/KekeBeauty.Application/KekeBeauty.Application.csproj src/KekeBeauty.Application/
COPY src/KekeBeauty.Infrastructure/KekeBeauty.Infrastructure.csproj src/KekeBeauty.Infrastructure/
COPY src/KekeBeauty.Api/KekeBeauty.Api.csproj src/KekeBeauty.Api/
COPY src/KekeBeauty.Web/KekeBeauty.Web.csproj src/KekeBeauty.Web/
COPY tests/KekeBeauty.Api.Tests/KekeBeauty.Api.Tests.csproj tests/KekeBeauty.Api.Tests/
RUN dotnet restore src/KekeBeauty.Web/KekeBeauty.Web.csproj

COPY . .
RUN dotnet publish src/KekeBeauty.Web/KekeBeauty.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/data-protection-keys && chown -R $APP_UID:$APP_UID /app/data-protection-keys
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "KekeBeauty.Web.dll"]
