# The production replacement environment has demoMode=false and /api URLs.
FROM node:24-alpine AS build
WORKDIR /src
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build -- --configuration production

FROM nginx:stable-alpine AS runtime
COPY deploy/nginx/web.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/web/browser/ /usr/share/nginx/html/
EXPOSE 8080
