# Build local (aucun CDN) des styles Tailwind : compile wwwroot/css/tailwind.css a partir des
# classes reellement utilisees dans les .razor, remplace l'ancien cdn.tailwindcss.com.
FROM node:20-alpine AS assets
WORKDIR /assets
COPY src/KekeBeauty.Web/package.json src/KekeBeauty.Web/package-lock.json* ./
RUN npm install
COPY src/KekeBeauty.Web/tailwind.config.js ./
COPY src/KekeBeauty.Web/Components ./Components
COPY src/KekeBeauty.Web/wwwroot/css/tailwind-input.css ./wwwroot/css/tailwind-input.css
RUN npm run build:css

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
COPY --from=assets /assets/wwwroot/css/tailwind.css src/KekeBeauty.Web/wwwroot/css/tailwind.css
RUN dotnet publish src/KekeBeauty.Web/KekeBeauty.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/data-protection-keys && chown -R $APP_UID:$APP_UID /app/data-protection-keys
USER $APP_UID

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "KekeBeauty.Web.dll"]
