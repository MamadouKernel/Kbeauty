# Debian rather than Alpine for the build: musl's resolver gives up on some home/ISP DNS servers (empty AAAA answers),
# which left `npm ci` waiting forever; glibc resolves them. The served image below stays on Alpine.
FROM node:24-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/security-headers.conf /etc/nginx/snippets/security-headers.conf
# Executed by the nginx entrypoint at startup: writes /config.json from environment variables.
COPY --chmod=755 docker/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
COPY --from=build /app/dist/keke-beauty-web/browser /usr/share/nginx/html
EXPOSE 80
