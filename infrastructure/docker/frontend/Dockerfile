FROM node:24-alpine AS build
WORKDIR /app
COPY src/frontend/package*.json src/frontend/.npmrc ./
RUN npm ci
COPY src/frontend/ .
RUN npm run build

FROM nginxinc/nginx-unprivileged:stable-alpine-slim@sha256:e28dcf0a161ddcbf228c7364b4a14f9bad4763ae8f5317c437b896afa3df4b84 AS final
USER root
RUN apk upgrade --no-cache
USER nginx
COPY --from=build /app/dist /usr/share/nginx/html
COPY infrastructure/docker/frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
